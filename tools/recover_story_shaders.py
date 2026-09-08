"""Recover compiled room-only shader YAML from the already loaded AssetRipper session."""
import hashlib
import argparse
import html
import json
import re
import urllib.parse
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
TARGET = ROOT.parent / 'CJ-SDK/Assets/Mods/SeasonalPerks.Assets/StoryLiveShaders'
NAMES = ['ANGRYMESH/PBR Rocks/PBR BlendTopDetail (Legacy)', 'Characters/TraiderHair',
         'Cloth/ClothShader', 'Custom/Billboard_FogSheet_Simple', 'Particles/VolumetricSmoke']


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--beta', action='store_true', help='Read the beta shader bundle currently loaded in AssetRipper.')
    args = parser.parse_args()
    TARGET.mkdir(parents=True, exist_ok=True)
    sdk_path = ROOT / 'Research/Story/sdk-assets.json'
    sdk = json.loads(sdk_path.read_text())
    names = list(NAMES)
    beta_ids = {}
    beta_collection = None
    if args.beta:
        import UnityPy
        env = UnityPy.load(str(ROOT.parent.parent / 'EscapeFromTarkov_Data/StreamingAssets/Windows/shaders'))
        beta_ids = {o.read().m_ParsedForm.m_Name: o.path_id for o in env.objects if o.type.name == 'Shader'}
        page = urllib.request.urlopen('http://127.0.0.1:6976/Search/View?q=shadow').read().decode()
        links = re.findall(r'<a href="(/Assets/View\?[^\"]+)"[^>]*>([^<]+)</a>', page)
        url = next(html.unescape(u) for u, n in links if html.unescape(n) == 'shadow')
        beta_collection = json.loads(urllib.parse.parse_qs(urllib.parse.urlsplit(url).query)['Path'][0])['C']
        assets = ROOT.parent / 'CJ-SDK/Assets/Mods/SeasonalPerks.Assets/StoryRecovered'
        used = set()
        for extension in ('*.mat', '*.unity'):
            for path in assets.rglob(extension):
                used.update(re.findall(r'guid: ([0-9a-f]{32})', path.read_text(encoding='utf-8-sig')))
        names = [n for n, s in sdk['shaders'].items() if s['guid'] in used and '/StoryNativeShaders/' in s['path']]
        print('Restoring compiled beta programs for', len(names), 'room shaders.', flush=True)
    for name in ([] if args.beta else names):
        digest = hashlib.sha256(('Seasonal story shader:' + name).encode()).hexdigest()
        sdk['shaders'][name] = {'guid': digest[:32], 'fileID': 4800000,
                                'path': str((TARGET / (hashlib.sha256(name.encode()).hexdigest() + '.asset')).relative_to(ROOT.parent / 'CJ-SDK')).replace('\\', '/')}
    audit = []
    for name in names:
        if args.beta:
            url = '/Assets/View?Path=' + urllib.parse.quote(json.dumps({'C': beta_collection, 'D': beta_ids[name]}))
        else:
            page = urllib.request.urlopen('http://127.0.0.1:6976/Search/View?q=' + urllib.parse.quote(name)).read().decode()
            links = [(html.unescape(url), html.unescape(label)) for url, label in
                     re.findall(r'<a href="(/Assets/View\?[^\"]+)"[^>]*>([^<]+)</a>', page)]
            url, _ = next(pair for pair in links if pair[1] == name)
        data = urllib.request.urlopen('http://127.0.0.1:6976' + url.replace('/Assets/View', '/Assets/Yaml')).read()
        view = urllib.request.urlopen('http://127.0.0.1:6976' + url).read().decode()
        dependencies = {}
        for dep_url, label in re.findall(r'<a href="(/Assets/View\?[^\"]+)"[^>]*>([^<]+)</a>', view):
            dep_url = html.unescape(dep_url)
            pointer = json.loads(urllib.parse.parse_qs(urllib.parse.urlsplit(dep_url).query)['Path'][0])
            dependency_name = html.unescape(label)
            if dependency_name not in sdk['shaders']:
                dependency_yaml = urllib.request.urlopen('http://127.0.0.1:6976' + dep_url.replace('/Assets/View', '/Assets/Yaml')).read().decode()
                candidates = re.findall(r'^  (?:  )?m_Name: (.+)$', dependency_yaml, re.M)
                dependency_name = next((n for n in candidates if n in sdk['shaders']), dependency_name)
            dependencies.setdefault(pointer['D'], set()).add(dependency_name)
        def reference(match):
            file_id, path_id = map(int, match.groups())
            if not path_id:
                return '{fileID: 0}'
            candidates = dependencies.get(path_id, set())
            if len(candidates) != 1:
                raise ValueError('Ambiguous shader dependency: ' + name + ' ' + match.group(0))
            dependency = next(iter(candidates))
            target = sdk['shaders'].get(dependency)
            if not target:
                raise ValueError('Unmapped shader dependency: ' + dependency)
            if args.beta and '/StoryNativeShaders/' in target['path'] and dependency not in names:
                names.append(dependency)
            return '{fileID: %d, guid: %s, type: 2}' % (target['fileID'], target['guid'])
        data = re.sub(r'\{m_FileID: (\d+), m_PathID: (-?\d+), m_TargetClassID: \d+\}', reference, data.decode()).encode()
        output = ROOT.parent / 'CJ-SDK' / sdk['shaders'][name]['path']
        output.write_bytes(data)
        Path(str(output) + '.meta').write_text('fileFormatVersion: 2\nguid: ' + sdk['shaders'][name]['guid'] + '\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 4800000\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n')
        audit.append({'name': name, 'asset': str(output), 'source': url, 'sha256': hashlib.sha256(data).hexdigest()})
        print(name, len(data), 'bytes', flush=True)
    (ROOT / 'Research/Story' / ('beta-shaders.json' if args.beta else 'live-shaders.json')).write_text(json.dumps(audit, indent=2))
    sdk_path.write_text(json.dumps(sdk, indent=2))


if __name__ == '__main__':
    main()
