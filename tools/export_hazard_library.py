"""Export the installed game's native razor wire, without loading a map or client."""
import argparse
import hashlib
import json
from pathlib import Path
import UnityPy
from export_container_library import Library, referenced_ids


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def verify(output, game):
    catalog = json.loads((output / 'catalog.json').read_text())
    assert catalog['Schema'] == 1
    assert catalog['Sha256'] == sha(output / 'native-hazards.bundle')
    assert catalog['NativeAssemblySha256'] == sha(game / 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll')
    env = UnityPy.load(str(output / 'native-hazards.bundle'))
    expected = {'hazards/wire.mesh': 'Mesh', 'hazards/wire.mat': 'Material', 'hazards/wire.sound': 'MonoBehaviour'}
    assert {name: obj.type.name for name, obj in env.container.items()} == expected
    for obj in env.objects:
        assert not obj.assets_file.externals, 'Hazard library must be independent of loaded maps'
        tree = obj.read_typetree()
        assert all(pid in obj.assets_file.objects for pid in referenced_ids(tree)), 'Broken asset reference'
        if obj.type.name == 'AudioClip':
            assert obj.read().samples, 'Contact audio must contain decodable samples'
    assert env.container['hazards/wire.mesh'].read_typetree()['m_VertexData']['m_VertexCount'] > 0
    windows = game / 'EscapeFromTarkov_Data/StreamingAssets/Windows'
    manifest = json.loads((windows / 'Windows.json').read_text())
    key = 'assets/content/audio/banks/mosin.bundle'
    assert key in manifest, 'Native dependency graph must expose the sniper sound bundle'
    sounds = UnityPy.load(str(windows / key))
    for dependency in manifest[key]['Dependencies']:
        assert dependency in manifest and (windows / dependency).is_file(), 'Missing native sound dependency'
    for suffix in ('', '_silenced'):
        name = 'Assets/Content/Audio/Banks/mosin/mosin_main' + suffix + '.asset'
        bank = sounds.container[name].read_typetree()
        assert bank['Rolloff'] > 0 and bank['BaseVolume'] > 0
        for environment in bank['Environments']:
            assert any(distance['Clips'] for distance in environment['Clips']), 'Empty native rifle bank'
            for distance in environment['Clips']:
                for clip in distance['Clips']:
                    assert clip['m_FileID'] == 0, 'Re-audit changed rifle audio dependencies'
                    audio = sounds.container[name].assetsfile.objects[clip['m_PathID']]
                    assert audio.type.name == 'AudioClip' and audio.read().samples, 'Rifle audio must be decodable'
    print('Native hazard library: mesh, material, contact audio, both rifle banks, dependencies and hashes verified.')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--verify', action='store_true')
    args = parser.parse_args()
    if not args.verify:
        lib = Library(args.game, 'wtt-native-hazards')
        source = lib.file('sharedassets2.assets')
        # Verified unbatched spiral_bruno used by level54's native BarbedWire.
        for pid, asset, expected in [(665, 'wire.mesh', 'spiral_bruno'), (89, 'wire.mat', None), (1771, 'wire.sound', 'BarbedWire')]:
            obj = source.objects[pid]
            tree, _ = lib.tree(obj)
            if expected:
                assert tree['m_Name'] == expected, 'Installed game assets changed; re-audit native wire sources'
            lib.allowed.add(lib.key(obj))
            lib.entries.append({'Root': lib.add(obj), 'Asset': 'hazards/' + asset, 'Source': 'sharedassets2.assets', 'PathId': pid})
        raw = lib.build()
        args.output.mkdir(parents=True, exist_ok=True)
        bundle = args.output / 'native-hazards.bundle'
        bundle.write_bytes(raw)
        catalog = {'Schema': 1, 'Sha256': sha(bundle), 'NativeAssemblySha256': sha(lib.data / 'Managed/Assembly-CSharp.dll'), 'Entries': lib.entries}
        (args.output / 'catalog.json').write_text(json.dumps(catalog, indent=2))
    verify(args.output, args.game)


if __name__ == '__main__':
    main()
