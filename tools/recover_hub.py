"""Recover the season hub and its local media; never execute live game code."""
import argparse
import hashlib
import json
import shutil
import struct
from pathlib import Path

import UnityPy
from PIL import Image
from recover_ui import DEV, LIVE, Generator

ROOT = DEV / 'CJ-SDK/Assets/Mods/SeasonalPerks.Assets'
DATA = LIVE / 'EscapeFromTarkov_Data'


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--intro', action='store_true', help='Recover the profile-card season introduction separately.')
    args = parser.parse_args()
    out = ROOT / 'HubArtwork'
    out.mkdir(exist_ok=True)
    generator = Generator('2022.3.43f2')
    generator.load_il2cpp((LIVE / 'GameAssembly.dll').read_bytes(), (DEV / '1.0 Metadata/global-metadata.dat').read_bytes())
    scripts = next(iter(UnityPy.load(str(DATA / 'globalgamemanagers.assets')).files.values())).objects
    records = {}
    media = {}
    source_hashes = {}

    def sprite(pointer):
        if not pointer.path_id:
            return None
        obj = pointer.deref()
        name = Path(obj.assets_file.name).stem + '-' + str(obj.path_id)
        if name not in records:
            source_name = Path(obj.assets_file.name).name
            if source_name not in source_hashes:
                source_hashes[source_name] = hashlib.sha256((DATA / source_name).read_bytes()).hexdigest()
            value = obj.read()
            image = value.image
            canvas = Image.new('RGBA', (round(value.m_Rect.width), round(value.m_Rect.height)))
            offset = value.m_RD.textureRectOffset
            canvas.paste(image, (round(offset.x), canvas.height - round(offset.y) - image.height))
            path = out / (name + '.png')
            canvas.save(path)
            records[name] = {'file': path.name, 'name': value.m_Name, 'source': obj.assets_file.name,
                             'sourceSha256': source_hashes[source_name],
                             'objectId': obj.path_id, 'border': [value.m_Border.x, value.m_Border.y, value.m_Border.z, value.m_Border.w],
                             'width': canvas.width, 'height': canvas.height, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}
        return name

    def fields(obj):
        fid, pid = struct.unpack_from('<iq', obj.get_raw_data(), 16)
        assert fid == 1
        script = scripts[pid].read()
        name = (script.m_Namespace + '.' if script.m_Namespace else '') + script.m_ClassName
        return name, obj.read_typetree(generator.get_nodes_up(script.m_AssemblyName, name))

    for level, root_id in ([(48, 8659)] if args.intro else [(48, 8658), (49, 3965), (50, 752)]):
        env = UnityPy.load(str(DATA / f'level{level}'))
        objects = next(f for f in env.files.values() if hasattr(f, 'objects')).objects

        def visit(obj):
            go = obj.read()
            node = {'id': obj.path_id, 'name': go.m_Name, 'active': bool(go.m_IsActive), 'components': [], 'children': []}
            for c in go.m_Component:
                comp = c.component.deref()
                if comp.type.name in ['RectTransform', 'Transform']:
                    node['rect'] = comp.read_typetree()
                    node['children'] = [visit(p.read().m_GameObject.deref()) for p in comp.read().m_Children]
                elif comp.type.name == 'MonoBehaviour':
                    try:
                        name, data = fields(comp)
                        entry = {'id': comp.path_id, 'type': name, 'fields': data}
                        if name == 'UnityEngine.UI.Image':
                            entry['artwork'] = None
                            ptr = data.get('m_Sprite', {})
                            if ptr.get('m_PathID'):
                                from UnityPy.classes import PPtr
                                entry['artwork'] = sprite(PPtr(m_FileID=ptr['m_FileID'], m_PathID=ptr['m_PathID'], assetsfile=comp.assets_file))
                        if name == 'EFT.UI.StreamingVideoPlayer':
                            source = DATA / 'StreamingAssets' / data['_streamingAssetsPath']
                            target = ROOT / 'HubMedia' / source.name
                            target.parent.mkdir(exist_ok=True)
                            shutil.copyfile(source, target)
                            media[source.name] = {'file': source.name, 'source': data['_streamingAssetsPath'], 'sha256': hashlib.sha256(target.read_bytes()).hexdigest()}
                        if name in ['EFT.UI.SeasonsSeasonInfoTab', 'EFT.UI.SeasonsIntroScreen']:
                            from UnityPy.classes import PPtr
                            ptr = data['_carouselData']
                            carousel = PPtr(m_FileID=ptr['m_FileID'], m_PathID=ptr['m_PathID'], assetsfile=comp.assets_file).deref()
                            cn, cd = fields(carousel)
                            for page in cd['_pages']:
                                ptr = page['_background']
                                page['artwork'] = sprite(PPtr(m_FileID=ptr['m_FileID'], m_PathID=ptr['m_PathID'], assetsfile=carousel.assets_file))
                            carousel_file = 'intro-carousel.json' if args.intro else 'hub-carousel.json'
                            (ROOT / 'Recovered' / carousel_file).write_text(json.dumps(cd, indent=2), encoding='utf-8')
                            print('CAROUSEL', carousel.assets_file.name, carousel.path_id, cd)
                        node['components'].append(entry)
                    except Exception as error:
                        node['components'].append({'id': comp.path_id, 'error': str(error)})
                elif comp.type.name == 'CanvasGroup':
                    node['components'].append({'id': comp.path_id, 'type': comp.type.name, 'fields': comp.read_typetree()})
            return node

        tree = visit(objects[root_id])
        target = ROOT / f'Recovered/level{level}-{root_id}.json'
        target.write_text(json.dumps(tree, indent=2), encoding='utf-8')
        print(target)
    from UnityPy.classes import PPtr
    env = UnityPy.load(str(DATA / 'sharedassets48.assets'))
    asset_file = next(f for f in env.files.values() if hasattr(f, 'objects'))
    for object_id in [375, 448]:
        sprite(PPtr(m_FileID=0, m_PathID=object_id, assetsfile=asset_file))
    provenance_file = 'intro-provenance.json' if args.intro else 'provenance.json'
    (out / provenance_file).write_text(json.dumps(list(records.values()), indent=2), encoding='utf-8')
    (ROOT / 'HubMedia' / provenance_file).write_text(json.dumps(list(media.values()), indent=2), encoding='utf-8')
    sources = [DATA / 'level48', DATA / 'level49', DATA / 'level50', LIVE / 'GameAssembly.dll', DEV / '1.0 Metadata/global-metadata.dat', DEV / '1.0 Metadata/GameAssembly.pdb']
    recovery_file = 'intro-provenance.json' if args.intro else 'hub-provenance.json'
    (ROOT / 'Recovered' / recovery_file).write_text(json.dumps([{'source': str(p), 'sha256': hashlib.sha256(p.read_bytes()).hexdigest()} for p in sources], indent=2), encoding='utf-8')
    print('Recovered', len(records), 'sprites and', len(media), 'videos')


if __name__ == '__main__':
    main()
