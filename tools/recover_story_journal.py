"""Recover presentation-only journal sprites and reference prefabs from local live UI."""
import hashlib
import json
import struct
from pathlib import Path

import UnityPy
from PIL import Image
from UnityPy.classes import PPtr
from recover_ui import DEV, LIVE, Generator


def main():
    data = LIVE / 'EscapeFromTarkov_Data'
    output = DEV / 'SeasonalPerks/UI/Resources/Story'
    audit = DEV / 'SeasonalPerks/Research/Story'
    recovered = DEV / 'CJ-SDK/Assets/Mods/WTT-Campaigns.Assets/StoryArtwork'
    inventory = json.loads((recovered / 'provenance.json').read_text())
    names = {
        'ChaptersBackground': 'journal-rail', 'Allscreens_Borders_Default': 'journal-border',
        'QuestTabChapterImageMask': 'journal-title-mask', 'TitleSeparator': 'journal-title-separator',
        'ChapterUnavailable': 'journal-active', 'ChapterSucceeded': 'journal-complete',
        'ChapterFailed': 'journal-failed', 'ChapterContentBackground': 'journal-content',
        'UnreadWarningIcon': 'journal-unread', 'ExpandIcon': 'journal-expand',
        'ChapterTitleShadow': 'journal-title-shadow', 'ChapterTasksShadow': 'journal-task-shadow',
    }
    manifest = []
    for entry in inventory:
        if entry['name'] not in names:
            continue
        name = names[entry['name']]
        content = (recovered / entry['file']).read_bytes()
        if hashlib.sha256(content).hexdigest() != entry['sha256']:
            raise ValueError('Recovered sprite checksum mismatch: ' + entry['file'])
        (output / (name + '.png')).write_bytes(content)
        manifest.append(dict(entry, resource=name))

    generator = Generator('2022.3.43f2')
    generator.load_il2cpp((LIVE / 'GameAssembly.dll').read_bytes(), (DEV / '1.0 Metadata/global-metadata.dat').read_bytes())
    env = UnityPy.load(str(data / 'sharedassets44.assets'))
    asset = next(f for f in env.files.values() if hasattr(f, 'objects'))

    def visit(go):
        node = {'name': go.read().m_Name, 'components': [], 'children': []}
        for pointer in go.read().m_Component:
            obj = pointer.component.deref()
            if obj.type.name == 'RectTransform':
                node['rect'] = obj.read_typetree()
                node['children'] = [visit(p.read().m_GameObject.deref()) for p in obj.read().m_Children]
                continue
            if obj.type.name != 'MonoBehaviour':
                continue
            fid, pid = struct.unpack_from('<iq', obj.get_raw_data(), 16)
            script = PPtr(m_FileID=fid, m_PathID=pid, assetsfile=asset).read()
            kind = '.'.join(filter(None, [script.m_Namespace, script.m_ClassName]))
            fields = obj.read_typetree(generator.get_nodes_up(script.m_AssemblyName, kind))
            part = {'type': kind, 'fields': fields}
            if kind == 'UnityEngine.UI.Image' and fields['m_Sprite']['m_PathID']:
                sprite_obj = PPtr(**fields['m_Sprite'], assetsfile=asset).deref()
                sprite = sprite_obj.read()
                name = 'journal-' + sprite.m_Name.lower().replace(' ', '-')
                image = Image.new('RGBA', (round(sprite.m_Rect.width), round(sprite.m_Rect.height)))
                offset = sprite.m_RD.textureRectOffset
                raw = sprite.image
                image.paste(raw, (round(offset.x), image.height - round(offset.y) - raw.height))
                image.save(output / (name + '.png'))
                part['resource'] = name
                manifest.append({'resource': name, 'name': sprite.m_Name,
                                 'source': sprite_obj.assets_file.name, 'objectId': sprite_obj.path_id,
                                 'sourceSha256': hashlib.sha256((data / Path(sprite_obj.assets_file.name).name).read_bytes()).hexdigest(),
                                 'sha256': hashlib.sha256((output / (name + '.png')).read_bytes()).hexdigest(),
                                 'border': [sprite.m_Border.x, sprite.m_Border.y, sprite.m_Border.z, sprite.m_Border.w]})
            node['components'].append(part)
        return node

    for name, pid in [('chapter-icon', 13321), ('objective', 13070), ('linked-item', 13354)]:
        obj = asset.objects[pid]
        fid, go_id = struct.unpack_from('<iq', obj.get_raw_data(), 0)
        tree = visit(PPtr(m_FileID=fid, m_PathID=go_id, assetsfile=asset).deref())
        (audit / ('journal-' + name + '-reference.json')).write_text(json.dumps(tree, indent=2), encoding='utf-8')
    unique = {entry['resource']: entry for entry in manifest}
    (output / 'journal-provenance.json').write_text(json.dumps(list(unique.values()), indent=2), encoding='utf-8')
    print('Recovered', len(unique), 'journal sprites and three presentation templates.')


if __name__ == '__main__':
    main()
