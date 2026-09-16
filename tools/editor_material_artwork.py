"""Regenerate PNGs offline; --refresh vendors the latest Iconify Material Symbols.

Requires Pillow and resvg-py==0.5.0. Normal builds use the checked-in PNGs.
"""
from pathlib import Path
import argparse
import hashlib
import json
import re
import urllib.request
from PIL import Image, ImageDraw
import resvg_py

ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / 'UI/Resources/Editor'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--refresh', action='store_true')
    args = parser.parse_args()
    names = sorted(set(re.findall(r'\["[^"]+"\] = "([^"]+)"', (ROOT / 'UI/Controls/EditorToolkitIcons.cs').read_text())) | {'dock-to-right-rounded'})
    DEST.mkdir(parents=True, exist_ok=True)
    if args.refresh:
        request = urllib.request.Request('https://api.github.com/repos/iconify/icon-sets/commits?path=json/material-symbols.json&per_page=1', headers={'User-Agent': 'WTT-Campaigns-artwork'})
        commit = json.load(urllib.request.urlopen(request))[0]['sha']
        source = f'https://raw.githubusercontent.com/iconify/icon-sets/{commit}/json/material-symbols.json'
        raw = urllib.request.urlopen(source).read()
        collection = json.loads(raw)
        manifest = {'source': source, 'sha256': hashlib.sha256(raw).hexdigest(), 'author': collection['info']['author'], 'license': collection['info']['license'], 'icons': names, 'changes': 'White fill; rasterized to transparent 72px PNGs. Original SVG geometry retained.'}
        for name in names:
            resolved = name
            while resolved in collection.get('aliases', {}):
                alias = collection['aliases'][resolved]
                if set(alias) != {'parent'}:
                    raise ValueError(f'{name}: alias transform needs explicit handling: {alias}')
                resolved = alias['parent']
            icon = collection['icons'][resolved]
            w, h = icon.get('width', collection.get('width', 24)), icon.get('height', collection.get('height', 24))
            svg = f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="0 0 {w} {h}">{icon["body"].replace("currentColor", "#ffffff")}</svg>\n'
            (DEST / f'{name}.svg').write_text(svg, encoding='utf-8')
        (DEST / 'provenance.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
        (DEST / 'LICENSE').write_bytes(urllib.request.urlopen('https://raw.githubusercontent.com/google/material-design-icons/master/LICENSE').read())
    for name in names:
        (DEST / f'{name}.png').write_bytes(resvg_py.svg_to_bytes(svg_path=str(DEST / f'{name}.svg'), width=72, height=72))
    surface = Image.new('RGBA', (128, 128))
    ImageDraw.Draw(surface).rounded_rectangle((0, 0, 127, 127), radius=24, fill='white')
    surface.resize((32, 32), Image.Resampling.LANCZOS).save(DEST / 'surface.png')
    for name in names + ['surface']:
        image = Image.open(DEST / f'{name}.png')
        assert image.mode == 'RGBA' and image.getbbox(), name
        assert image.getextrema()[3][0] == 0, f'{name}: expected transparent background'
    print(f'Verified {len(names)} Material Symbols and the rounded surface.')


if __name__ == '__main__':
    main()
