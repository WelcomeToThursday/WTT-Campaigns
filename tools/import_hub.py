"""Import public season presentation data and offline images. Never read profile responses or headers."""
import argparse
import hashlib
import json
import re
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from urllib.request import Request, urlopen

from import_captures import ROOT, ASSETS, DEFAULT_LOGS, read, save


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--logs', type=Path, default=DEFAULT_LOGS)
    parser.add_argument('--download-images', action='store_true')
    parser.add_argument('--mode-dump', type=Path, help='Multi-host capture; Seasonal is authoritative, PvE supplies missing public definitions.')
    args = parser.parse_args()
    requests = args.mode_dump / 'gw-pvp-season.escapefromtarkov.com' if args.mode_dump else args.logs / 'requests'
    sources = []

    def response(name):
        paths = sorted(requests.rglob('resp.' + name + '_*.json'))
        if not paths and args.mode_dump:
            paths = sorted((args.mode_dump / 'gw-pve.escapefromtarkov.com').rglob('resp.' + name + '_*.json'))
        path = paths[-1]
        sources.append({'file': str(path.relative_to(args.mode_dump or args.logs)).replace('\\', '/'), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()})
        return read(path)

    battle = response('client.battle-pass.active')['battlePasses'][0]
    season = response('client.season.active')['season']
    locale = response('client.locale.en')
    items = response('client.items')
    customization = response('client.customization')
    universal = response('client.globals')['config']['BattlePassUniversalDocument']
    image_paths = set()

    def image(path):
        if not path:
            return ''
        assert re.fullmatch(r'/files/(battle-pass|documents|seasonal)/[a-f0-9]+\.png', path), path
        image_paths.add(path)
        return Path(path).stem

    def localized(target, suffix, fallback=''):
        return locale.get(target + ' ' + suffix, fallback)

    def reward(value):
        content = value['rewards'][0]
        target = content.get('target', '')
        if content.get('items'):
            target = content['items'][0]['_tpl']
        template = customization.get(target, items.get(target, {}))
        props = template.get('_props', {})
        name = localized(target, 'Name', content['type'])
        kind = content['type']
        if kind == 'Tarcoin':
            name = 'Tarcoins (' + str(content.get('value', content.get('amount', 50))) + ')'
        side = [s.upper() for s in props.get('Side', []) if s in ['Bear', 'Usec']]
        if len(side) != 1:
            side = []
        category = customization.get(template.get('_parent'), {}).get('_name', '')
        display_kind = {'CustomizationDirect': 'CUSTOMIZATION', 'AssortmentUnlock': 'TRADE OFFER', 'Item': 'ITEM', 'Tarcoin': 'CURRENCY'}.get(kind, kind.upper())
        if kind == 'CustomizationDirect':
            display_kind = {'DogTags': 'DOGTAG', 'Upper': 'UPPER CLOTHING', 'Lower': 'LOWER CLOTHING', 'Head': 'HEAD', 'Voice': 'VOICE',
                            'Ceiling': 'HIDEOUT CUSTOMIZATION', 'Floor': 'HIDEOUT CUSTOMIZATION', 'Wall': 'HIDEOUT CUSTOMIZATION',
                            'ShootingRangeMark': 'HIDEOUT CUSTOMIZATION', 'MannequinPose': 'HIDEOUT CUSTOMIZATION', 'EnvironmentUI': 'MENU BACKGROUND'}.get(category, display_kind)
        requirements = []
        for condition in value.get('conditions', []):
            if condition['conditionType'] == 'Level':
                requirements.append('Reach level ' + str(condition['value']))
            else:
                quest = condition.get('target', '')
                requirements.append('Complete the task: ' + locale.get(quest + ' name', locale.get(quest + ' Name', 'Seasonal task')))
        loc = value['location']
        return {'Id': value['id'], 'Name': name, 'Description': localized(target, 'Description'),
                'Kind': display_kind,
                'Side': ' / '.join(side), 'Image': image(value['imageUrl']), 'BigImage': image(value['bigImageUrl']),
                'X': loc['x'], 'Y': loc['y'], 'Width': loc['w'], 'Height': loc['h'],
                'Costs': [{'DocumentId': k, 'Count': v} for k, v in value.get('cost', {}).items()],
                'Requirements': requirements, 'Claimed': False}

    data = {'Id': battle['id'], 'SeasonId': season['id'], 'Pages': [], 'SeasonalRewards': [reward(v) for v in season['seasonalRewards']],
            'Documents': [], 'Slides': [], 'DocumentLimit': 30, 'ClaimedRewards': 0, 'PreviewOnly': True}
    for page in battle['pages']:
        data['Pages'].append({'PreviousRequirement': page['prevPageItemsRequirement'], 'Rewards': [reward(v) for v in page['rewards']]})
    for document in battle['documents']:
        data['Documents'].append({'Id': document['id'], 'Name': localized(document['itemId'], 'Name', 'Document'),
                                  'Image': image(document['imageUrl']), 'UnavailableImage': image(document['unavailableImageUrl']), 'Count': 0})
    data['UniversalImage'] = image(universal['Image'])
    data['UniversalUnavailableImage'] = image(universal['UnavailableImage'])
    data['UniversalCount'] = 0
    for page in read(ASSETS / 'Recovered/hub-carousel.json')['_pages']:
        data['Slides'].append({'Image': page['artwork'], 'Text': re.sub('<[^>]+>', '', locale[page['_text']])})
    assert len(data['Pages']) == 12 and sum(len(p['Rewards']) for p in data['Pages']) == 53
    assert len(data['Documents']) == 8 and len(data['SeasonalRewards']) == 5 and len(data['Slides']) == 5
    save(ROOT / 'data/hub.json', data)
    save(ROOT / 'data/hub-provenance.json', {'sources': sources, 'battlePassId': battle['id'], 'seasonId': season['id']})
    save(ROOT / 'data/locales/hub-en.json', {k: v for k, v in locale.items() if k.startswith(('Seasons/', 'Season/', 'Seasonal/')) or ('BattlePass' in k and not k.startswith('Arena'))})
    folder = ASSETS / 'HubImages'
    folder.mkdir(exist_ok=True)

    def fetch(path):
        dest = folder / Path(path).name
        url = 'https://s3-prod.escapefromtarkov.com/pvp-season' + path
        if not dest.exists():
            copies = sorted(requests.rglob('resp.*' + Path(path).name + '_*.png'))
            if copies:
                dest.write_bytes(copies[-1].read_bytes())
            elif args.download_images:
                with urlopen(Request(url, headers={'User-Agent': 'Campaigns-AssetImport/1.0'}), timeout=40) as response:
                    raw = response.read()
                assert raw.startswith(b'\x89PNG\r\n\x1a\n'), path
                dest.write_bytes(raw)
            else:
                raise FileNotFoundError('Missing hub image: ' + path + '; use --download-images')
        raw = dest.read_bytes()
        assert raw.startswith(b'\x89PNG\r\n\x1a\n'), dest
        return {'id': dest.stem, 'file': dest.name, 'sourceUrl': url, 'sha256': hashlib.sha256(raw).hexdigest()}

    with ThreadPoolExecutor(max_workers=6) as executor:
        manifest = list(executor.map(fetch, sorted(image_paths)))
    save(folder / 'provenance.json', manifest)
    save(ROOT / 'data/hub-images.json', [{'Id': v['id'], 'Sha256': v['sha256']} for v in manifest])
    print('Imported 53 Battle Pass rewards, 5 seasonal rewards, 5 slides and', len(manifest), 'offline images.')


if __name__ == '__main__':
    main()
