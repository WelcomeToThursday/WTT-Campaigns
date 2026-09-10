"""Map placement and pickup/extraction regression checks on the isolated bundle runtime."""
import argparse
import json
import time
from collections import Counter

import test_integration as api
import test_hub_raids


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--port', type=int, default=6987)
    args = parser.parse_args()
    runtime = api.PROJECT / 'Testing/BundleBackportServer'
    config = json.loads((runtime / 'SPT_Data/configs/http.json').read_text(encoding='utf-8-sig'))
    assert args.port == config['port'] and args.port != 6969, 'Only the isolated runtime is supported'
    api.BASE = f'https://127.0.0.1:{args.port}'
    call, check = api.request, api.check
    name = 'document-loot-' + str(time.time_ns())
    registration = call('/launcher/v2/register', {'username': name, 'edition': 'Standard'})
    root = next(p['profileId'] for p in registration['Profiles'] if p['username'] == name)
    customization = json.loads((runtime / 'SPT_Data/database/templates/customization.json').read_text())

    def cosmetic(parent):
        return next(k for k, v in customization.items() if v.get('_parent') == parent and v['_props'].get('AvailableAsDefault') and 'Usec' in v['_props'].get('Side', []))

    call('/client/game/profile/create', {'side': 'Usec', 'nickname': 'LootTest', 'headId': cosmetic('5cc085e214c02e000c6bea67'), 'voiceId': cosmetic('5fc100cf95572123ae738483')}, root)
    created = call('/wtt-campaigns/create', {'PerkIds': [], 'Nickname': 'LooseLootTest', 'Side': 'Usec', 'ExpectedRevision': 0}, root)
    check(not created.get('Error'), 'Create fresh synthetic seasonal character')
    switched = call('/wtt-campaigns/switch', {'Mode': 'seasonal'}, root)
    child = switched['EffectiveProfileId']
    call('/client/game/start', session=child)
    catalogue = json.loads((api.PROJECT / 'data/hub-gameplay.json').read_text())
    documents = {d['itemId'] for d in catalogue['Documents']}
    report = []
    for name, caps in catalogue['CapturedMapCaps'].items():
        location = name.lower()
        if not (runtime / 'SPT_Data/database/locations' / location / 'looseLoot.json').exists():
            continue
        allowed = {cap['TemplateId']: cap['Value'] for cap in caps}
        raid = call('/client/match/local/start', {'location': location, 'playerSide': 'pmc', 'mode': 'regular', 'timeVariant': 'CURR', 'transitionType': 0}, child)
        spawns = [(p, i) for p in raid['locationLoot']['Loot'] for i in p.get('Items', []) if i['_tpl'] in documents]
        check(len(spawns) == 8, location + ': eight loose documents with full allowance')
        counts = Counter(i['_tpl'] for _, i in spawns)
        check(all(t in allowed and count <= allowed[t] for t, count in counts.items()), location + ': only assigned types within captured caps')
        for point, item in spawns:
            check(point['IsContainer'] is False and len(point['Items']) == 1 and point['Root'] == item['_id'] and not item.get('parentId'), location + ': standalone loose root')
            check(point.get('IsAlwaysSpawn') is not True, location + ': mandatory spawn excluded')
        profile = call('/client/game/profile/list', session=child)[0]
        call('/client/match/local/end', {'serverId': raid['serverId'], 'results': {'profile': profile, 'result': 'Runner', 'exitName': '', 'inSession': False, 'favorite': False, 'playTime': 60}, 'lostInsuredItems': [], 'transferItems': {}}, child)
        check(call('/wtt-campaigns/hub', session=child)['RemainingDocuments'] == 30, location + ': uncollected spawns do not consume allowance')
        report.append({'map': location, 'documents': dict(counts)})
        print(location, dict(counts), flush=True)
    # Exercise conserved pickup identities, split/merge, extraction, exhausted allowance,
    # duplicate requests, and Normal/Scav exclusions using this same fresh account.
    test_hub_raids.main({'root': root, 'child': child})
    (api.PROJECT / 'Research/document-loot-checks.json').write_text(json.dumps({'passed': len(api.checks), 'maps': report, 'checks': api.checks}, indent=2))
    print('Document loot:', len(api.checks), 'real-route checks passed.')


if __name__ == '__main__':
    main()
