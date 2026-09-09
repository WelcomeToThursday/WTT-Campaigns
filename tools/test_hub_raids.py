"""Exercise real local-raid routes using the original synthetic integration account."""
import sys
import copy
import json
import secrets
from collections import Counter
from test_integration import PROJECT, SERVER, request, check, checks
from test_hub_gameplay import read, save


def main(account=None):
    creator = '--creator' in sys.argv
    account = account or read(PROJECT / ('Testing/creator-acceptance-state.json' if creator else 'Testing/restart-state.json'))
    fixture = read(SERVER / 'user/mods/SeasonalPerks/creator/acceptance-fixture.json') if creator else None
    def call(path, data=None, session=None):
        if creator and path.startswith('/wtt-seasonal/'):
            data = {**(data or {}), 'ProtocolVersion': 2, 'SeasonId': fixture['SeasonId']}
        return request(path, data, session)
    child, root = account['child'], account['root']
    docs = {fixture['DocumentTemplate']} if creator else {d['itemId'] for d in read(PROJECT / 'data/hub-gameplay.json')['Documents']}
    call('/client/game/start', session=child)
    call('/wtt-seasonal/switch', {'Mode': 'seasonal'}, root)
    templates = call('/client/items', session=child)
    if isinstance(templates, list):
        templates = {t['_id']: t for t in templates}
    for number in range(5):
        before = call('/wtt-seasonal/hub', session=child)
        profile = call('/client/game/profile/list', session=child)[0]
        start = call('/client/match/local/start', {'location': 'bigmap', 'playerSide': 'pmc', 'mode': 'regular', 'timeVariant': 'CURR', 'transitionType': 0}, child)
        spawns = [(loot, item) for loot in start['locationLoot']['Loot'] for item in loot.get('Items', []) if item['_tpl'] in docs]
        check(len(spawns) == min(8, before['RemainingDocuments']), 'Raid spawn count respects remaining allowance')
        check(len({loot['Root'] for loot, _ in spawns}) == len(spawns), 'Documents use distinct loose-loot points')
        allowed = {cap['TemplateId'] for cap in read(PROJECT / 'data/hub-gameplay.json')['CapturedMapCaps']['bigmap']}
        for loot, item in spawns:
            check(loot.get('IsContainer') is False and len(loot['Items']) == 1, 'Document uses a standalone loose-loot point')
            check(loot['Root'] == item['_id'] and not item.get('parentId'), 'Loose document is the spawn root without a container parent')
            check(creator or item['_tpl'] in allowed, 'Document type is allowed on Customs')
            check(item['upd']['StackObjectsCount'] == 1, 'One ordinary document per selected loose-loot point')
        check(call('/wtt-seasonal/hub/claim', {'OperationId': secrets.token_hex(16), 'ExpectedRevision': before['Revision'], 'RewardId': before['Pages'][0]['Rewards'][1]['Id']}, child).get('Error'), 'Transactions are blocked during raids')
        for _, item in spawns:
            event = {'OperationId': secrets.token_hex(16), 'ItemId': item['_id'], 'PickedUp': True}
            check(call('/wtt-seasonal/hub/raid-document', event, child).get('Committed'), 'First pickup acknowledged')
            picked = call('/wtt-seasonal/hub', session=child)
            call('/wtt-seasonal/hub/raid-document', event, child)
            check(call('/wtt-seasonal/hub', session=child) == picked, 'Reconnect retry cannot count a pickup twice')
        after_pickups = call('/wtt-seasonal/hub', session=child)
        check(after_pickups['RemainingDocuments'] == before['RemainingDocuments'] - len(spawns), 'Each new unit consumes one allowance')
        extracted = []
        if spawns:
            item = copy.deepcopy(spawns[0][1])
            target = secrets.token_hex(12)
            split = {'OperationId': secrets.token_hex(16), 'ItemId': item['_id'], 'TargetId': target, 'Count': 1, 'Split': True, 'PickedUp': True}
            check(call('/wtt-seasonal/hub/raid-document', split, child).get('Committed'), 'Split conserves a picked-up identity')
            call('/wtt-seasonal/hub/raid-document', split, child)
            check(call('/wtt-seasonal/hub', session=child)['RemainingDocuments'] == after_pickups['RemainingDocuments'], 'Duplicate split cannot consume another allowance')
            merge = {'OperationId': secrets.token_hex(16), 'ItemId': target, 'TargetId': item['_id'], 'Count': 1, 'Split': False, 'PickedUp': True}
            check(call('/wtt-seasonal/hub/raid-document', merge, child).get('Committed'), 'Merge preserves picked-up identity')
            call('/wtt-seasonal/hub/raid-document', merge, child)
            check(call('/wtt-seasonal/hub', session=child)['RemainingDocuments'] == after_pickups['RemainingDocuments'], 'Repeated merge cannot consume allowance')
            target = item['_id']
            host = next(i for i in profile['Inventory']['items'] if i.get('slotId') == 'Backpack')
            host_grid = templates[host['_tpl']]['_props']['Grids'][0]
            item.update({'parentId': host['_id'], 'slotId': host_grid['_name'], 'location': {'x': 0, 'y': 0, 'r': 0}})
            # The fixture drops the backpack's previous contents before extracting one new document.
            dropped = {i['_id'] for i in profile['Inventory']['items'] if i.get('parentId') == host['_id']}
            while True:
                children = {i['_id'] for i in profile['Inventory']['items'] if i.get('parentId') in dropped}
                if children <= dropped:
                    break
                dropped.update(children)
            profile['Inventory']['items'] = [i for i in profile['Inventory']['items'] if i['_id'] not in dropped]
            profile['Inventory']['items'].append(item)
            extracted.append(target)
        outcome = 'Survived' if number % 2 == 0 else 'Runner'
        end = {'serverId': start['serverId'], 'results': {'profile': profile, 'result': outcome, 'exitName': 'Crossroads', 'inSession': False, 'favorite': False, 'playTime': 600}, 'lostInsuredItems': [], 'transferItems': {}}
        call('/client/match/local/end', end, child)
        after = call('/wtt-seasonal/hub', session=child)
        check(after['RemainingDocuments'] == after_pickups['RemainingDocuments'], 'Extraction does not recount pickups')
        check(0 <= after['UniversalCount'] - before['UniversalCount'] <= len(extracted), 'Server awards at most one bonus per new extracted document')
        inventory = call('/client/game/profile/list', session=child)[0]['Inventory']
        check(all(any(i['_id'] == identity for i in inventory['items']) for identity in extracted), 'Ordinary documents remain after extraction')
        call('/client/match/local/end', end, child)
        check(call('/wtt-seasonal/hub', session=child) == after, 'Repeated raid-end response cannot reroll or restore rejected documents')
    check(after['RemainingDocuments'] == 0, 'Thirty first pickups exhaust the rolling window')
    for mode, side in [('seasonal', 'savage'), ('normal', 'pmc')]:
        call('/wtt-seasonal/switch', {'Mode': mode}, root)
        session = child if mode == 'seasonal' else root
        start = call('/client/match/local/start', {'location': 'bigmap', 'playerSide': side, 'mode': 'regular', 'timeVariant': 'CURR', 'transitionType': 0}, session)
        items = [i for loot in start['locationLoot']['Loot'] for i in loot.get('Items', []) if i['_tpl'] in docs]
        check(not items, 'No seasonal document injection for ' + mode + '/' + side)
        call('/client/game/start', session=session)
    if creator:
        account['inventory'] = call('/client/game/profile/list', session=child)[0]['Inventory']['items']
        save(PROJECT / 'Testing/creator-acceptance-state.json', account)
    save(PROJECT / ('Research/creator-raid-checks.json' if creator else 'Research/hub-raid-checks.json'), {'passed': len(checks), 'checks': checks})
    print('Hub raids:', len(checks), 'checks passed.')


if __name__ == '__main__':
    main()
