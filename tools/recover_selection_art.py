"""Extract original selection-screen artwork, with source IDs and hashes.

UI sprites are bundled by the SDK builder; perk icons remain server-served.
"""
import hashlib
import json
from pathlib import Path

import UnityPy
from PIL import Image

DEV = Path(__file__).resolve().parents[2]
DATA = Path('E:/EscapeFromTarkov/EscapeFromTarkov_Data')
OUT = DEV / 'CJ-SDK/Assets/Mods/SeasonalPerks.Assets/SelectionArtwork'
SPRITES = {
    'sharedassets47.assets': {
        44: 'background', 40: 'normal-glow', 69: 'normal-hover',
        66: 'normal-card', 70: 'seasonal-card', 36: 'bear', 65: 'usec',
        59: 'normal-badge', 60: 'seasonal-empty', 71: 'seasonal-empty-hover',
        38: 'normal-empty', 81: 'normal-empty-hover', 68: 'info-gradient',
        58: 'footer-gradient',
        76: 'seasonal-glow-1', 78: 'seasonal-glow-2', 62: 'seasonal-glow-3',
        77: 'seasonal-hover-1', 79: 'seasonal-hover-2', 63: 'seasonal-hover-3',
        53: 'season-stats-divider', 74: 'season-stat-rewards', 75: 'season-stat-story',
        64: 'season-stat-kd', 37: 'season-stat-survivals',
    },
    'sharedassets45.assets': {6: 'top-glow-seasonal'},
    'sharedassets44.assets': {
        499: 'seasonal-badge', 643: 'season-banner', 677: 'info-icon', 784: 'footer-glow',
        630: 'modifier-gradient', 676: 'modifier-tint', 908: 'modifier-grid',
        749: 'modifier-shadow',
        511: 'modifiers-tab-normal', 492: 'modifiers-tab-selected',
    },
    'resources.assets': {5032: 'profile-icon', 5972: 'confirmation-border'},
}


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    records = []
    for filename, sprites in SPRITES.items():
        source = DATA / filename
        env = UnityPy.load(str(source))
        objects = next(f for f in env.files.values() if hasattr(f, 'objects')).objects
        source_hash = hashlib.sha256(source.read_bytes()).hexdigest()
        for object_id, name in sprites.items():
            sprite = objects[object_id].read()
            image = sprite.image
            # Restore atlas trimming so the original RectTransform geometry remains valid.
            if name.startswith(('seasonal-glow-', 'seasonal-hover-', 'top-glow-')) or name in ['info-gradient', 'normal-glow', 'normal-hover']:
                canvas = Image.new('RGBA', (round(sprite.m_Rect.width), round(sprite.m_Rect.height)))
                offset = sprite.m_RD.textureRectOffset
                canvas.paste(image, (round(offset.x), canvas.height - round(offset.y) - image.height))
                image = canvas
            output = OUT / (name + '.png')
            image.save(output)
            records.append({
                'file': output.name, 'source': filename, 'sourceSha256': source_hash,
                'objectId': object_id, 'name': sprite.m_Name,
                'width': image.width, 'height': image.height,
                'border': [sprite.m_Border.x, sprite.m_Border.y, sprite.m_Border.z, sprite.m_Border.w],
                'sha256': hashlib.sha256(output.read_bytes()).hexdigest(),
            })
            print(name, image.size)
    (OUT / 'provenance.json').write_text(json.dumps(records, indent=2), encoding='utf-8')
    env = UnityPy.load(str(DATA / 'resources.assets'))
    objects = next(f for f in env.files.values() if hasattr(f, 'objects')).objects
    fonts = OUT.parent / 'Fonts'
    fonts.mkdir(exist_ok=True)
    font = fonts / 'Bender.ttf'
    font.write_bytes(bytes(objects[4379].read().m_FontData))
    (fonts / 'provenance.json').write_text(json.dumps({
        'source': 'resources.assets', 'objectId': 4379,
        'sha256': hashlib.sha256(font.read_bytes()).hexdigest(),
    }, indent=2))
    env = UnityPy.load(str(DATA / 'level47'))
    objects = next(f for f in env.files.values() if hasattr(f, 'objects')).objects
    def rig(transform_id):
        transform = objects[transform_id].read()
        go = transform.m_GameObject.read()
        return {
            'name': go.m_Name, 'transform': objects[transform_id].read_typetree(),
            'components': [{'type': c.component.deref().type.name, 'fields': c.component.deref().read_typetree()}
                           for c in go.m_Component if c.component.deref().type.name in ['Camera', 'Light']],
            'children': [rig(c.path_id) for c in transform.m_Children],
        }
    (OUT.parent / 'Recovered/selection-camera.json').write_text(json.dumps(rig(782), indent=2))


if __name__ == '__main__':
    main()
