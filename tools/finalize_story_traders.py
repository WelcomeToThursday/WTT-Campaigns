"""Remap validated SDK room bundles to beta assemblies and audit distributable output."""
import hashlib
import json
import argparse
from collections import Counter
from pathlib import Path
import UnityPy
from story_compiled_shaders import CompiledShaders

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--trader', action='append', default=[], help='Finalize only these rebuilt rooms, retaining other verified room records.')
    args = parser.parse_args()
    native_path = ROOT.parent.parent / 'EscapeFromTarkov_Data/globalgamemanagers.assets'
    native = UnityPy.load(str(native_path))
    scripts = {}
    for obj in native.objects:
        if obj.type.name == 'MonoScript':
            tree = obj.read_typetree()
            scripts[(tree['m_Namespace'], tree['m_ClassName'])] = tree
    audit = []
    compiled = CompiledShaders()
    built = json.loads((ROOT / 'Research/Story/trader-build.json').read_text())
    output = ROOT / 'Client/Resources/StoryMedia/traders'
    output.mkdir(parents=True, exist_ok=True)
    previous_path = output.parent / 'traders.json'
    previous = {r['trader']: r for r in json.loads(previous_path.read_text())['rooms']} if previous_path.exists() else {}
    if set(args.trader) - {r['trader'] for r in built}:
        raise ValueError('The selected trader has no validated room build.')
    for room in built:
        if args.trader and room['trader'] not in args.trader:
            retained = previous[room['trader']]
            with (output.parent / retained['bundle']).open('rb') as stream:
                if hashlib.file_digest(stream, 'sha256').hexdigest() != retained['sha256']:
                    raise ValueError('An unselected room changed after validation: ' + room['trader'])
            audit.append(retained)
            continue
        print('Finalizing', room['trader'], flush=True)
        source = ROOT / 'Research/Story/Bundles/traders' / (room['trader'] + '.bundle')
        env = UnityPy.load(str(source))
        compiled.restore(env)
        preview = ROOT / 'Research/Story/PreviewBundles' / source.name
        preview.parent.mkdir(parents=True, exist_ok=True)
        preview.write_bytes(env.file.save(packer='lz4'))
        mapped = []
        shader_names = []
        for obj in env.objects:
            if obj.type.name == 'MonoScript':
                tree = obj.read_typetree()
                key = tree['m_Namespace'], tree['m_ClassName']
                if key in scripts:
                    target = scripts[key]
                    if tree['m_AssemblyName'].removesuffix('.dll') not in (target['m_AssemblyName'].removesuffix('.dll'), 'Tarkov.Assembly'):
                        raise ValueError('Unexpected assembly for ' + str(key) + ': ' + tree['m_AssemblyName'])
                    tree['m_AssemblyName'] = target['m_AssemblyName']
                    tree['m_PropertiesHash'] = target['m_PropertiesHash']
                elif key == ('SeasonalPerks.UI.Media', 'StoryRoomLighting'):
                    tree['m_AssemblyName'] = 'WTT-Seasonal.UI.dll'
                else:
                    raise ValueError('Unreviewed room script: ' + str(key))
                obj.save_typetree(tree)
                mapped.append({ 'type': '.'.join(filter(None, key)), 'assembly': tree['m_AssemblyName'] })
            elif obj.type.name == 'AssetBundle':
                tree = obj.read_typetree()
                if tree['m_Dependencies']:
                    raise ValueError('Room requires explicit dependency packaging: ' + str(tree['m_Dependencies']))
            elif obj.type.name == 'Shader':
                shader = obj.read()
                shader_names.append(shader.m_ParsedForm.m_Name)
                if not shader.compressedBlob:
                    raise ValueError('Room contains a shader without compiled programs: ' + shader.m_ParsedForm.m_Name)
            elif obj.type.name == 'Material':
                if not obj.read().m_Shader.m_PathID:
                    raise ValueError('Room material lost its shader: ' + obj.read().m_Name)
        destination = output / source.name
        destination.write_bytes(env.file.save(packer='lz4'))
        verified = UnityPy.load(str(destination))
        types = Counter(o.type.name for o in verified.objects)
        if not all(types[t] > 0 for t in ('GameObject', 'Mesh', 'Texture2D', 'Animator', 'AnimationClip')):
            raise ValueError('Incomplete animated room: ' + room['trader'])
        if types['Camera'] != 1:
            raise ValueError('A room must have exactly one visit camera.')
        if not room.get('custom') and not types['AudioClip']:
            raise ValueError('The recovered room lost its generic visit recordings: ' + room['trader'])
        for obj in verified.objects:
            if obj.type.name == 'MonoScript' and obj.read().m_AssemblyName == 'Tarkov.Assembly.dll':
                raise ValueError('SDK assembly reference survived finalization.')
        audit.append({'trader': room['trader'], 'bundle': 'traders/' + source.name,
                      'sha256': hashlib.sha256(destination.read_bytes()).hexdigest(),
                      'bytes': destination.stat().st_size, 'objects': dict(types), 'scripts': mapped,
                      'shaders': sorted(set(shader_names))})
    (output.parent / 'traders.json').write_text(json.dumps({'formatVersion': 1, 'rooms': audit}, indent=2))
    (ROOT / 'Research/Story/trader-bundle-audit.json').write_text(json.dumps({
        'nativeSha256': hashlib.sha256(native_path.read_bytes()).hexdigest(), 'rooms': audit}, indent=2))
    print('Finalized and verified', len(audit), 'beta trader bundles.')


if __name__ == '__main__':
    main()
