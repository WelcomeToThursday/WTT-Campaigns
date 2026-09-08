"""Import only the trader-room dependency closure, remapping scripts and shaders to beta.

Exported dummy scripts/shaders are never copied or compiled. Campaign speech entries,
scene bootstraps, NPC registration, gameplay triggers and playable directors are excluded.
"""
import argparse
import hashlib
import json
import re
import shutil
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SDK = ROOT.parent / 'CJ-SDK'
TARGET = SDK / 'Assets/Mods/SeasonalPerks.Assets/StoryRecovered'
TRADERS = {
    'Fence': '579dc571d53a0658a154fbec', 'Jaeger': '5c0647fdd443bc2504c2d371',
    'Mechanic': '5a7c2eca46aef81a7ca2145d', 'Peacekeeper': '5935c25fb3acc3127c3d8cd9',
    'Prapor': '54cb50c76803fa8b248b4571', 'Ragman': '5ac3b934156ae10c4430e83c',
    'Skier': '58330581ace78e27b8b10cee', 'Therapist': '54cb57776803fa99248b456e',
}
ALLOWED = {
    'EFT.AnimationSequencePlayer.AnimationDictionary', 'EFT.AnimationSequencePlayer.SecondaryAnimationDictionary',
    'EFT.AnimationSequencePlayer.LipSyncDictionary', 'EFT.AnimationSequencePlayer.SequenceReader',
    'uLipSync.uLipSyncBakedDataPlayer', 'uLipSync.uLipSyncBlendShape', 'uLipSync.BakedData', 'uLipSync.Profile',
    'EFT.AnimatorResetter', 'EFT.NPC.NPCAnimationsEventReceiver', 'LightKeeperEyeTargetFollower', 'LightKeeperEyesBlinking',
    'EFT.NPC.AnimationIntRandomizerByTimer', 'EFT.NPC.AnimatorByEventsToggler',
    'AnimationEventSystem.AnimationEventsStateBehaviour', 'AnimationEventSystem.AnimatorControllerStaticData',
    'StaticDeferredDecal', 'StencilShadow', 'ChromaticAberration', 'Undithering', 'VolumetricLight',
}
GENERIC_SPEECH = re.compile(r'^(Greet(?:Pos|Neg|Work)\d+|Greetings(?:_while_work)?_\d+|Goodbye_?\d+|(?:start_)?Trade_?\d+)$', re.I)
GUID = re.compile(r'guid: ([0-9a-f]{32})')
SCRIPT = re.compile(r'm_Script: \{fileID: -?\d+, guid: ([0-9a-f]{32}), type: \d+\}')
HEADER = re.compile(r'^--- !u!(\d+) &(-?\d+).*$', re.M)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--export', type=Path, default=ROOT / 'Research/Story/CompleteTradersExport/ExportedProject')
    parser.add_argument('--scenes-only', action='store_true', help='Refresh scene adapters against the previously verified dependency closure.')
    args = parser.parse_args()
    source = args.export / 'Assets'
    sdk = json.loads((ROOT / 'Research/Story/sdk-assets.json').read_text())
    index = {}
    scripts = {}
    cache = ROOT / 'Research/Story/trader-export-index.json'
    def index_meta(meta):
        asset = meta.with_suffix('')
        if not asset.is_file():
            return None
        guid = GUID.search(meta.read_text(encoding='utf-8-sig')).group(1)
        script = None
        if asset.suffix == '.cs':
            text = asset.read_text(encoding='utf-8-sig')
            ns = re.search(r'namespace ([\w.]+)', text)
            name = re.search(r'\b(?:class|struct) (\w+)', text)
            if name:
                script = (ns.group(1) + '.' if ns else '') + name.group(1)
        return guid, str(asset), script
    cached = json.loads(cache.read_text()) if cache.exists() else {}
    stamp = str(source.resolve()) + ':' + str(source.stat().st_mtime_ns)
    if cached.get('source') == stamp:
        index = {guid: Path(path) for guid, path in cached['assets'].items()}
        scripts = cached['scripts']
    else:
        print('Indexing exported asset references...', flush=True)
        with ThreadPoolExecutor(max_workers=16) as pool:
            for result in pool.map(index_meta, source.rglob('*.meta')):
                if result is None:
                    continue
                guid, asset, script = result
                if guid in index:
                    raise ValueError('Duplicate exported asset GUID: ' + guid)
                index[guid] = Path(asset)
                if script:
                    scripts[guid] = script
        cache.write_text(json.dumps({'source': stamp, 'assets': {g: str(p) for g, p in index.items()}, 'scripts': scripts}))
    print('Indexed', len(index), 'assets. Resolving room dependencies...', flush=True)
    removed = []
    speech = {}
    missing_shaders = set()

    def adapt(text, path):
        if not text.startswith('%YAML'):
            return text
        headers = list(HEADER.finditer(text))
        blocks = []
        dropped = set()
        for number, header in enumerate(headers):
            block = text[header.start():headers[number + 1].start() if number + 1 < len(headers) else len(text)]
            kind, object_id = map(int, header.groups())
            script_match = SCRIPT.search(block)
            if kind == 114 and script_match is None:
                removed.append({'file': str(path.relative_to(source)), 'object': object_id, 'type': 'Missing script'})
                dropped.add(object_id)
                continue
            if script_match:
                name = scripts.get(script_match.group(1), '')
                if name not in ALLOWED or name not in sdk['scripts']:
                    removed.append({'file': str(path.relative_to(source)), 'object': object_id, 'type': name or 'Missing script'})
                    dropped.add(object_id)
                    continue
                script = sdk['scripts'][name]
                block = SCRIPT.sub('m_Script: {fileID: %d, guid: %s, type: 3}' % (script['fileID'], script['guid']), block)
                if name == 'EFT.AnimationSequencePlayer.LipSyncDictionary':
                    # Generic greetings/trading/farewells demonstrate speech. Story-specific recordings
                    # remain outside the backport, together with the live quest and dialogue content.
                    entry = re.search(r'^  entries:\s*\n', block, re.M)
                    if not entry:
                        raise ValueError('Missing recovered lip-sync entries in ' + str(path))
                    chunks = re.split(r'(?=^  - key: )', block[entry.end():], flags=re.M)
                    selected = []
                    keys = []
                    for chunk in chunks:
                        key = re.match(r'  - key: (.+)', chunk)
                        if key and GENERIC_SPEECH.fullmatch(key.group(1).strip().strip('"')):
                            selected.append(re.sub(r'^      info:.*(?:\n        .*)*', '      info:', chunk, flags=re.M))
                            keys.append(key.group(1))
                    speech[str(path.relative_to(source)) + ':' + str(object_id)] = keys
                    block = block[:entry.start()] + ('  entries:\n' + ''.join(selected) if selected else '  entries: []\n')
            # Gameplay and cinematic directors do not belong in a reusable visit-room prefab.
            if kind in (195, 197, 81, 320):
                dropped.add(object_id)
                continue
            if kind == 82:
                # The beta client supplies its own mixer; do not import the live master mixer/plugins.
                block = re.sub(r'((?:m_)?OutputAudioMixerGroup): \{[^}]+\}', r'\1: {fileID: 0}', block)
            if kind == 21:
                # AssetRipper emits material dictionaries as YAML mappings. Unity 2022
                # expects arrays of single-entry mappings and otherwise drops the values.
                block = re.sub(r'^(    m_(?:TexEnvs|Ints|Floats|Colors)): \{\}$', r'\1: []', block, flags=re.M)
                block = re.sub(r'^      ([^\s][^:\n]*):', r'    - \1:', block, flags=re.M)
            blocks.append(block)
        output = text[:headers[0].start()] + ''.join(blocks) if headers else text
        def mixer_reference(match):
            asset = index.get(match.group(1))
            return '{fileID: 0}' if asset and asset.suffix == '.mixer' else match.group(0)
        output = re.sub(r'\{fileID: -?\d+, guid: ([0-9a-f]{32}), type: \d+\}', mixer_reference, output)
        output = re.sub(r'^  - component: \{fileID: (-?\d+)\}\n',
                        lambda m: '' if int(m.group(1)) in dropped else m.group(0), output, flags=re.M)
        output = re.sub(r'\{fileID: (-?\d+)\}',
                        lambda m: '{fileID: 0}' if int(m.group(1)) in dropped else m.group(0), output)
        def shader(match):
            guid = match.group(1)
            asset = index.get(guid)
            if asset is None or asset.suffix != '.shader':
                return match.group(0)
            name = re.search(r'Shader\s+"([^"]+)"', asset.read_text(encoding='utf-8-sig')).group(1)
            target = sdk['shaders'].get(name)
            if target is None:
                missing_shaders.add(name)
                return match.group(0)
            return '{fileID: %d, guid: %s, type: 2}' % (target['fileID'], target['guid'])
        return re.sub(r'\{fileID: -?\d+, guid: ([0-9a-f]{32}), type: \d+\}', shader, output)

    queue = []
    scene_paths = {}
    unavailable = {}
    for name, trader in TRADERS.items():
        if name == 'Peacekeeper':
            unavailable[trader] = 'Live Vendors_Peacekeeper has placeholder animation/speech entries and no visit camera.'
            continue
        scene = source / ('Content/Locations/Venders/Vendors_' + name + '.unity')
        if not scene.is_file():
            raise FileNotFoundError(scene)
        queue.append(scene)
        scene_paths[trader] = str((TARGET / scene.relative_to(source)).relative_to(SDK)).replace('\\', '/')
    processed = {}
    adapted = ROOT / 'Research/Story/AdaptedAssets'
    adapted.mkdir(parents=True, exist_ok=True)
    sdk_guids = {s['guid'] for group in sdk.values() for s in group.values()}
    if args.scenes_only:
        manifest_path = ROOT / 'Research/Story/trader-import.json'
        manifest = json.loads(manifest_path.read_text())
        previous = {a['asset']: a for a in manifest['assets']}
        updates = {}
        for scene in queue:
            data = adapt(scene.read_text(encoding='utf-8-sig'), scene)
            for guid in set(GUID.findall(data)) - sdk_guids:
                if guid.startswith('0000000000000000'):
                    continue
                asset = index[guid]
                dependency = str((TARGET / asset.relative_to(source)).relative_to(SDK)).replace('\\', '/')
                if dependency not in previous:
                    raise ValueError('New dependency requires a full import: ' + dependency)
            output = TARGET / scene.relative_to(source)
            updates[output] = data.encode()
        for output, data in updates.items():
            output.write_bytes(data)
            previous[str(output.relative_to(SDK)).replace('\\', '/')]['sha256'] = hashlib.sha256(data).hexdigest()
        manifest['removed'] = removed
        manifest['speech'] = speech
        manifest_path.write_text(json.dumps(manifest, indent=2))
        print('Refreshed', len(updates), 'scene adapters within the verified closure.')
        return
    while queue:
        path = queue.pop()
        if path in processed:
            continue
        if path.suffix in ('.cs', '.dll', '.shader', '.compute'):
            if path.suffix == '.shader' and missing_shaders:
                processed[path] = path
                continue
            raise ValueError('Unmapped executable/shader dependency: ' + str(path))
        data = path.read_bytes()
        prepared = path
        if data.startswith(b'%YAML'):
            if len(data) > 10_000_000:
                print('Adapting', path.name, len(data), 'bytes...', flush=True)
            data = adapt(data.decode('utf-8-sig'), path).encode('utf-8')
            for guid in set(GUID.findall(data.decode('utf-8'))):
                if guid in sdk_guids or guid.startswith('0000000000000000'):
                    continue
                if guid not in index:
                    raise ValueError('Unresolved asset dependency: ' + guid + ' in ' + str(path))
                queue.append(index[guid])
            prepared = adapted / hashlib.sha256(str(path).encode()).hexdigest()
            prepared.write_bytes(data)
        processed[path] = prepared
        if len(processed) % 500 == 0:
            print('Resolved', len(processed), 'assets...', flush=True)
    if missing_shaders:
        raise ValueError('Beta shader mapping required: ' + ', '.join(sorted(missing_shaders)))
    audit = []
    for path, prepared in processed.items():
        output = TARGET / path.relative_to(source)
        output.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(prepared, output)
        shutil.copyfile(str(path) + '.meta', str(output) + '.meta')
        with output.open('rb') as stream:
            digest = hashlib.file_digest(stream, 'sha256').hexdigest()
        audit.append({'asset': str(output.relative_to(SDK)).replace('\\', '/'), 'sha256': digest})
    manifest = {'scenes': scene_paths, 'unavailable': unavailable, 'assets': audit, 'removed': removed, 'speech': speech}
    (ROOT / 'Research/Story/trader-import.json').write_text(json.dumps(manifest, indent=2))
    from recover_story_fence import recover
    recover()
    print('Imported', len(processed), 'room dependencies; stripped', len(removed), 'unsupported/gameplay components.')


if __name__ == '__main__':
    main()
