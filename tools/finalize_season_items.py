"""Map the SDK PreviewPivot reference to the installed native type and audit rebuilt bundles."""
import argparse
import hashlib
import json
from pathlib import Path
import UnityPy
from import_captures import ROOT, ASSETS, read, save


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--spt', type=Path, default=ROOT.parent.parent)
    args = parser.parse_args()
    native_path = args.spt / 'EscapeFromTarkov_Data/globalgamemanagers.assets'
    native = UnityPy.load(str(native_path))
    pivots = [o for o in native.objects if o.type.name == 'MonoScript' and o.read().m_ClassName == 'PreviewPivot']
    assert len(pivots) == 1
    target = pivots[0].read_typetree()
    output = ROOT / 'Research/SeasonItems'
    manifest = read(output / 'bundles.json')
    audit = []
    textures = read(ASSETS / 'Recovered/season-items.json')['textures']
    for entry in manifest['manifest']:
        path = output / entry['key']
        env = UnityPy.load(str(path))
        for obj in env.objects:
            if obj.type.name not in ('Texture2D', 'Cubemap'):
                continue
            digest = hashlib.sha256(obj.read().get_image_data()).hexdigest()
            candidates = [t for t in textures.values() if t['sha256'] == digest and t['type'] == obj.type.name]
            assert candidates, 'Rebuilt texture data differs from the recovered original: ' + str(obj.path_id)
            source = candidates[0]
            assert all(t['linear'] == source['linear'] for t in candidates)
            tree = obj.read_typetree()
            assert (tree['m_Width'], tree['m_Height'], tree['m_MipCount'], tree['m_TextureFormat']) == (source['width'], source['height'], source['mips'], source['format'])
            # Unity's Cubemap constructor defaults to linear; restore the captured sampling metadata without recompressing any pixels.
            tree['m_ColorSpace'] = 0 if source['linear'] else 1
            tree['m_Name'] = source['name']
            tree['m_IsReadable'] = source['readable']
            tree['m_TextureSettings'] = source['settings']
            obj.save_typetree(tree)
        scripts = [o for o in env.objects if o.type.name == 'MonoScript']
        assert len(scripts) == 1, entry['key']
        for obj in scripts:
            tree = obj.read_typetree()
            assert tree['m_ClassName'] == 'PreviewPivot' and tree['m_Namespace'] == ''
            assert tree['m_AssemblyName'] in ('Tarkov.Assembly.dll', target['m_AssemblyName'])
            tree['m_AssemblyName'] = target['m_AssemblyName']
            tree['m_PropertiesHash'] = target['m_PropertiesHash']
            obj.save_typetree(tree)
        path.write_bytes(env.file.save(packer='lz4'))
        reloaded = UnityPy.load(str(path))
        types = [o.type.name for o in reloaded.objects]
        assert types.count('GameObject') > 0 and types.count('Mesh') > 0 and types.count('Texture2D') > 0
        assert types.count('MonoBehaviour') == 1
        for obj in reloaded.objects:
            if obj.type.name == 'MonoScript':
                assert obj.read().m_AssemblyName == target['m_AssemblyName']
            if obj.type.name == 'Material':
                assert obj.read_typetree()['m_Shader']['m_PathID'] != 0
        audit.append({'key': entry['key'], 'sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
                      'objects': [{'id': o.path_id, 'type': o.type.name} for o in reloaded.objects]})
    save(output / 'audit.json', {'nativeSha256': hashlib.sha256(native_path.read_bytes()).hexdigest(),
                                 'previewPivotObjectId': pivots[0].path_id, 'bundles': audit})
    print('Validated', len(audit), 'rebuilt item bundles; only the native PreviewPivot script reference is present.')


if __name__ == '__main__':
    main()
