"""Compact finalized rooms without re-encoding textures or touching animation/speech.

Writes a separate candidate set. Promote it only after the SDK render checks pass.
BC7 mip tails are copied verbatim; all other object/resource payloads are verified.
"""
import argparse
import copy
import hashlib
import json
import re
from pathlib import Path

import UnityPy
from UnityPy.streams import EndianBinaryReader

ROOT = Path(__file__).resolve().parents[1]
ACTOR = re.compile(r'head|face|prapor|therapist|skier|mechanic|ragman|jaeger|peacekeeper|glukhar|fence|body|pants|hands', re.I)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def mip_tail(tree, data):
    """Select existing BC7 mips, retaining every channel and the remaining chain."""
    limit = 1024 if ACTOR.search(tree['m_Name']) else 512
    if tree['m_TextureFormat'] != 25 or tree['m_ImageCount'] != 1 or tree['m_MipCount'] < 2:
        return data, 0
    width, height = tree['m_Width'], tree['m_Height']
    sizes = []
    for _ in range(tree['m_MipCount']):
        sizes.append(((width + 3) // 4) * ((height + 3) // 4) * 16)
        width, height = max(1, width // 2), max(1, height // 2)
    if sum(sizes) != len(data):
        raise ValueError('Unexpected BC7 mip layout: ' + tree['m_Name'])
    width, height = tree['m_Width'], tree['m_Height']
    count = 0
    while max(width, height) > limit and count < len(sizes) - 1:
        width, height = max(1, width // 2), max(1, height // 2)
        count += 1
    if count:
        data = data[sum(sizes[:count]):]
        tree.update(m_Width=width, m_Height=height, m_MipCount=len(sizes) - count,
                    m_CompleteImageSize=len(data))
    return data, count


def compact(source, destination):
    env = UnityPy.load(str(source))
    resources = {name: value.bytes for name, value in env.file.files.items() if name.endswith('.resS')}
    untouched_resources = {name: digest(value.bytes) for name, value in env.file.files.items()
                           if not hasattr(value, 'objects') and name not in resources}
    streams = {name: bytearray() for name in resources}
    blocks = {name: {} for name in resources}
    expected, payloads, changed = {}, {}, []
    for obj in env.objects:
        key = obj.assets_file.name, obj.path_id
        if obj.type.name not in ('Texture2D', 'Cubemap', 'Mesh'):
            # Refuse to discard an unknown streaming owner.
            raw = obj.get_raw_data()
            if any(name.encode() in raw for name in resources):
                raise ValueError('Unknown resource owner: ' + obj.type.name)
            expected[key] = ('raw', digest(raw))
            continue
        tree = obj.read_typetree()
        stream = tree.get('m_StreamData')
        if not stream or not stream['size']:
            expected[key] = ('raw', digest(obj.get_raw_data()))
            continue
        name = stream['path'].rsplit('/', 1)[-1]
        if name not in resources:
            raise ValueError('External resource: ' + stream['path'])
        data = resources[name][stream['offset']:stream['offset'] + stream['size']]
        if len(data) != stream['size']:
            raise ValueError('Truncated source resource')
        before = len(data)
        stripped = 0
        if obj.type.name == 'Texture2D':
            data, stripped = mip_tail(tree, data)
        sha = digest(data)
        if sha not in blocks[name]:
            target = streams[name]
            target.extend(b'\0' * (-len(target) % 16))
            blocks[name][sha] = len(target)
            target.extend(data)
        stream.update(offset=blocks[name][sha], size=len(data))
        serialized = obj.save_typetree(tree)
        expected[key] = ('tree', digest(serialized))
        payloads[key] = (sha, len(data))
        if stripped:
            changed.append({'name': tree['m_Name'], 'width': tree['m_Width'], 'height': tree['m_Height'],
                            'mipsRemoved': stripped, 'savedBytes': before - len(data)})
    for name, data in streams.items():
        env.file.files[name] = EndianBinaryReader(bytes(data))
        env.file.files[name].flags = 0
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_bytes(env.file.save(packer='lz4'))
    # Read the actual output again, checking object identities and every resource.
    verified = UnityPy.load(str(destination))
    for name, sha in untouched_resources.items():
        if digest(verified.file.files[name].bytes) != sha:
            raise ValueError('Compaction changed an audio/resource payload: ' + name)
    actual = {(o.assets_file.name, o.path_id): o for o in verified.objects}
    if set(actual) != set(expected):
        raise ValueError('Compaction changed object identities')
    for key, (_, sha) in expected.items():
        obj = actual[key]
        if digest(obj.get_raw_data()) != sha:
            raise ValueError('Object failed round-trip: ' + str(key))
        if key in payloads:
            tree = obj.read_typetree()
            stream = tree['m_StreamData']
            name = stream['path'].rsplit('/', 1)[-1]
            data = verified.file.files[name].bytes[stream['offset']:stream['offset'] + stream['size']]
            if (digest(data), len(data)) != payloads[key]:
                raise ValueError('Resource failed round-trip: ' + str(key))
    return {'beforeBytes': source.stat().st_size, 'bytes': destination.stat().st_size,
            'sha256': digest(destination.read_bytes()), 'textures': changed,
            'verifiedObjects': len(expected), 'verifiedResources': len(payloads)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=ROOT / 'Client/Resources/StoryMedia')
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/compact-traders')
    args = parser.parse_args()
    if args.source.resolve() == args.output.resolve():
        raise ValueError('Candidates must not overwrite the validated source set')
    manifest = json.loads((args.source / 'traders.json').read_text())
    audit = []
    for room in manifest['rooms']:
        source = args.source / room['bundle']
        if digest(source.read_bytes()) != room['sha256']:
            raise ValueError('Source bundle differs from its validation manifest')
        print('Compacting', room['trader'], flush=True)
        result = compact(source, args.output / room['bundle'])
        original = copy.deepcopy(room)
        room.update(bytes=result['bytes'], sha256=result['sha256'], textureProfile='compact-v1')
        audit.append({'trader': room['trader'], 'source': original, **result})
        print(f"  {result['beforeBytes'] / 1e6:.1f} -> {result['bytes'] / 1e6:.1f} MB; {len(result['textures'])} mip tails", flush=True)
    (args.output / 'traders.json').write_text(json.dumps(manifest, indent=2))
    (args.output / 'compaction.json').write_text(json.dumps({'rooms': audit}, indent=2))


if __name__ == '__main__':
    main()
