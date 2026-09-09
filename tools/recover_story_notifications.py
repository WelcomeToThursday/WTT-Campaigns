"""Recover the custom live chapter banner's presentation assets from the local reference."""
import hashlib
import json
from pathlib import Path

import UnityPy
from PIL import Image
from UnityPy.classes import PPtr
from recover_ui import DEV, LIVE, Generator


def main():
    root = DEV / 'SeasonalPerks'
    reference = json.loads((root / 'Research/Story/level49-2206.json').read_text())
    env = UnityPy.load(str(LIVE / 'EscapeFromTarkov_Data/level49'))
    asset = next(f for f in env.files.values() if hasattr(f, 'objects'))
    fields = next(c['fields'] for c in reference['components'] if c.get('type') == 'EFT.UI.Quests.MainQuestNotificationView')
    fields['_frameSprite'] = {'m_FileID': 2, 'm_PathID': 311}
    sdk = DEV / 'CJ-SDK/Assets/Mods/SeasonalPerks.Assets'
    output = sdk / 'StoryNotifications/Artwork'
    shared = sdk / 'StoryStatusIcons'
    output.mkdir(parents=True, exist_ok=True)
    shared.mkdir(parents=True, exist_ok=True)
    manifest = []
    for key, name in {
        '_chapterBackgroundSprite': 'notification-chapter',
        '_subtaskBackgroundSprite': 'notification-objective',
        '_checkmarkStartedSprite': 'notification-started',
        '_checkmarkSuccessSprite': 'notification-success',
        '_checkmarkFailSprite': 'notification-failed',
        '_frameSprite': 'notification-frame',
    }.items():
        obj = PPtr(**fields[key], assetsfile=asset).deref()
        sprite = obj.read()
        image = Image.new('RGBA', (round(sprite.m_Rect.width), round(sprite.m_Rect.height)))
        offset = sprite.m_RD.textureRectOffset
        raw = sprite.image
        image.paste(raw, (round(offset.x), image.height - round(offset.y) - raw.height))
        target = (shared if name in ['notification-started', 'notification-success', 'notification-failed'] else output) / (name + '.png')
        image.save(target)
        source = LIVE / 'EscapeFromTarkov_Data' / Path(obj.assets_file.name).name
        manifest.append(dict(resource=name, name=sprite.m_Name, source=source.name, objectId=obj.path_id,
                             sourceSha256=hashlib.sha256(source.read_bytes()).hexdigest(),
                             sha256=hashlib.sha256(target.read_bytes()).hexdigest(), size=image.size,
                             border=[sprite.m_Border.x, sprite.m_Border.y, sprite.m_Border.z, sprite.m_Border.w]))
    for name in ['journal-active', 'journal-complete', 'journal-failed']:
        (shared / (name + '.png')).write_bytes((root / 'UI/Resources/Story' / (name + '.png')).read_bytes())
    audio_output = sdk / 'StoryNotifications/Audio'
    audio_output.mkdir(parents=True, exist_ok=True)
    generator = Generator('2022.3.43f2')
    generator.load_il2cpp((LIVE / 'GameAssembly.dll').read_bytes(), (DEV / '1.0 Metadata/global-metadata.dat').read_bytes())
    resource_path = LIVE / 'EscapeFromTarkov_Data/resources.assets'
    resources = next(f for f in UnityPy.load(str(resource_path)).files.values() if hasattr(f, 'objects'))
    wrapper = resources.objects[46348].read_typetree(generator.get_nodes_up('Assembly-CSharp', 'UISoundsWrapper'))
    for code, status in [(44, 'started'), (45, 'complete'), (46, 'failed')]:
        binding = next(entry for entry in wrapper['_UIAudioClips'] if entry['_soundType'] == code)
        audio_obj = PPtr(**binding['_sound'], assetsfile=resources).deref()
        clip = audio_obj.read()
        samples = next(iter(clip.samples.values()))
        if samples[:4] != b'RIFF':
            raise ValueError('Chapter audio must decode to WAV')
        (audio_output / (status + '.wav')).write_bytes(samples)
        manifest.append(dict(resource=status + '.wav', source='resources.assets', objectId=audio_obj.path_id,
                             soundType=code, name=clip.m_Name, sha256=hashlib.sha256(samples).hexdigest()))
    (root / 'Research/Story/notification-provenance.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    rows = []
    def walk(node, depth=0):
        rect = node.get('rect', {})
        rows.append(dict(name=node['name'], depth=depth, rect={k:v for k,v in rect.items() if k in
                    ['m_AnchorMin','m_AnchorMax','m_Pivot','m_SizeDelta','m_AnchoredPosition']},
                    components=[dict(type=c.get('type'), fields={k:v for k,v in c.get('fields', {}).items() if k in
                    ['m_Enabled','m_text','m_fontSize','m_fontColor','m_Color','m_Sprite','m_Padding','m_Spacing','m_Type',
                     'm_ChildAlignment','m_ChildControlWidth','m_ChildControlHeight','m_ChildForceExpandWidth',
                     'm_ChildForceExpandHeight','m_PreferredWidth','m_PreferredHeight','m_MinWidth','m_MinHeight',
                     'm_HorizontalAlignment','m_VerticalAlignment']}) for c in node['components']]))
        for child in node['children']:
            walk(child, depth+1)
    walk(reference)
    (root / 'artifacts/research/chapter-notification-layout.json').write_text(json.dumps(rows, indent=2))
    print(json.dumps(manifest, indent=2))


if __name__ == '__main__':
    main()
