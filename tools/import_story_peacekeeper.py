"""Import the bounded beta head/accessory exports used by the custom Peacekeeper office."""
import hashlib
import json
import re
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def main():
    sdk = ROOT.parent / 'CJ-SDK'
    destination = sdk / 'Assets/Mods/SeasonalPerks.Assets/StoryPeacekeeper'
    shaders = json.loads((ROOT / 'Research/Story/sdk-assets.json').read_text())['shaders']
    shader = shaders['p0/Reflective/Bumped Specular SMap']
    audit = []
    for export, subfolder in [('PeacekeeperHead', ''), ('PeacekeeperAccessories', 'Accessories')]:
        source = ROOT / 'Research/Story' / export / 'ExportedProject/Assets'
        for folder in ('Mesh', 'Texture2D', 'Material'):
            for asset in (source / folder).iterdir():
                output = destination / subfolder / folder / asset.name
                output.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(asset, output)
                if asset.suffix == '.mat':
                    data = asset.read_text()
                    data = re.sub(r'm_Shader: \{[^}]+\}', 'm_Shader: {fileID: 4800000, guid: ' + shader['guid'] + ', type: 2}', data)
                    # Native inventory cubemaps are deliberately omitted in this room;
                    # its lighting owns reflections. All local texture maps are retained.
                    data = re.sub(r'\{fileID: -?\d+, guid: 0000000deadbeef15deadf00d0000000, type: \d+\}', '{fileID: 0}', data)
                    data = re.sub(r'^(    m_(?:TexEnvs|Ints|Floats|Colors)): \{\}$', r'\1: []', data, flags=re.M)
                    data = re.sub(r'^      ([^\s][^:\n]*):', r'    - \1:', data, flags=re.M)
                    output.write_text(data)
                audit.append({'source': str(asset.relative_to(ROOT)), 'asset': str(output.relative_to(sdk)),
                              'sha256': hashlib.sha256(output.read_bytes()).hexdigest()})
    (ROOT / 'Research/Story/peacekeeper-import.json').write_text(json.dumps(audit, indent=2))
    print('Imported', len(audit), 'reviewed head and accessory assets; no source scripts or prefab lifecycle.')


if __name__ == '__main__':
    main()
