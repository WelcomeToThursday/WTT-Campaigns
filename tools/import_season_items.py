"""Import the public season item definitions and audit locally supplied asset bundles."""
import argparse
import hashlib
import json
from pathlib import Path
import UnityPy
from import_captures import ROOT, ASSETS, read, save


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--dump', type=Path, required=True)
    parser.add_argument('--live', type=Path, default=Path(r'E:\EscapeFromTarkov'))
    args = parser.parse_args()
    seasonal = args.dump / 'gw-pvp-season.escapefromtarkov.com'
    pve = args.dump / 'gw-pve.escapefromtarkov.com'
    sources = []

    def response(host, endpoint):
        p = sorted((host / endpoint / 'response').glob('*.json'))[-1]
        sources.append({'file': p.relative_to(args.dump).as_posix(), 'sha256': hashlib.sha256(p.read_bytes()).hexdigest()})
        return read(p)

    items = response(seasonal, 'client/items')
    locale = response(pve, 'client/locale/en')
    gameplay = read(ROOT / 'data/hub-gameplay.json')
    ids = {d['itemId'] for d in gameplay['Documents']}
    ids.update(['6a3567f687d90a0deb066c1b', '6a4fa628b4831242f306e8cd'])
    windows = args.live / 'EscapeFromTarkov_Data/StreamingAssets/Windows'
    manifest = read(windows / 'Windows.json')
    assets = {}
    textures = {}
    texture_folder = ASSETS / 'Recovered/SeasonItemTextures'
    texture_folder.mkdir(parents=True, exist_ok=True)
    dependencies = {}
    for tpl in sorted(ids):
        item = items[tpl]
        key = item['_props']['Prefab']['path']
        if key in assets:
            continue
        p = windows / key
        env = UnityPy.load(str(p))
        texture_objects = [o for o in env.objects if o.type.name in ('Texture2D', 'Cubemap')]
        for dependency in manifest[key]['Dependencies']:
            if dependency == 'cubemaps':
                if dependency not in dependencies:
                    dependencies[dependency] = UnityPy.load(str(windows / dependency))
                referenced = {v['m_Texture']['m_PathID'] for o in env.objects if o.type.name == 'Material'
                              for _, v in o.read_typetree()['m_SavedProperties']['m_TexEnvs']}
                texture_objects.extend(o for o in dependencies[dependency].objects if o.path_id in referenced and o.type.name == 'Cubemap')
        for obj in texture_objects:
            texture = obj.read()
            tree = obj.read_typetree()
            raw = texture.get_image_data()
            digest = hashlib.sha256(raw).hexdigest()
            name = tree['m_Name']
            if name in textures and textures[name]['sha256'] != digest:
                raise ValueError('Ambiguous texture name: ' + name)
            path = texture_folder / (digest + '.bytes')
            path.write_bytes(raw)
            textures[name] = {'name': name, 'sha256': digest, 'objectId': obj.path_id, 'type': obj.type.name,
                              'width': tree['m_Width'], 'height': tree['m_Height'], 'format': tree['m_TextureFormat'],
                              'mips': tree['m_MipCount'], 'linear': tree['m_ColorSpace'] == 0,
                              'readable': tree['m_IsReadable'],
                              'settings': tree['m_TextureSettings']}
        scripts = [o.read().m_ClassName for o in env.objects if o.type.name == 'MonoScript']
        if any(name != 'PreviewPivot' for name in scripts):
            raise ValueError('Item needs an explicit SDK component adapter: ' + key + ' ' + str(scripts))
        behaviours = [o.read_typetree() for o in env.objects if o.type.name == 'MonoBehaviour']
        assert len(behaviours) == 1
        pivot = {k: v for k, v in behaviours[0].items() if not k.startswith('m_')}
        pivot['Icon'].pop('overrideIcon', None)
        for flag in ['hasOffset', 'orthographic']:
            pivot['Icon'][flag] = bool(pivot['Icon'][flag])
        assets[key] = {'key': key, 'pivot': pivot, 'dependencies': manifest[key]['Dependencies'], 'sha256': hashlib.sha256(p.read_bytes()).hexdigest(),
                       'objects': [{'id': o.path_id, 'type': o.type.name} for o in env.objects], 'scripts': scripts}
    save(ROOT / 'data/season-items.json', {t: items[t] for t in sorted(ids)})
    save(ROOT / 'data/locales/season-items-en.json', {k: v for k, v in locale.items() if k.split(' ')[0] in ids})
    save(ROOT / 'data/season-items-provenance.json', {'sources': sources, 'assets': list(assets.values()),
         'adaptations': {'documents': 'Native information-item parent; original template IDs, models, dimensions and properties preserved. Prefab bundle keys use the wtt-seasonal/ prefix to avoid content-mod collisions.',
                         'crates': 'Original random-loot-container parent retained. Claim/exchange gated until a verified loot pool is available.'}})
    save(ASSETS / 'Recovered/season-items.json', {'liveWindows': str(windows), 'assets': list(assets.values()), 'textures': textures})
    print('Recovered', len(ids), 'season item definitions and', len(assets), 'unique models.')


if __name__ == '__main__':
    main()
