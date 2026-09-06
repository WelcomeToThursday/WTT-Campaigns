"""Exercise real local-raid routes using the original synthetic integration account."""
import copy
import json
import secrets
from collections import Counter
from test_integration import PROJECT, SERVER, request, check, checks
from test_hub_gameplay import read, save


def main():
    account = read(PROJECT / 'Testing/restart-state.json')
    child, root = account['child'], account['root']
    docs = {d['itemId'] for d in read(PROJECT / 'data/hub-gameplay.json')['Documents']}
    request('/client/game/start', session=child)
    request('/seasonal-perks/switch', {'Mode': 'seasonal'}, root)
    templates = request('/client/items', session=child)
    if isinstance(templates, list):
        templates = {t['_id']: t for t in templates}
    for number in range(5):
        before = request('/seasonal-perks/hub', session=child)
        profile = request('/client/game/profile/list', session=child)[0]
        start = request('/client/match/local/start', {'location': 'bigmap', 'playerSide': 'pmc', 'mode': 'regular', 'timeVariant': 'CURR', 'transitionType': 0}, child)
        containers = [(loot, item) for loot in start['locationLoot']['Loot'] for item in loot.get('Items', []) if item['_tpl'] in docs]
        check(len(containers) == min(8, before['RemainingDocuments']), 'Raid spawn count respects remaining allowance')
        check(len({loot['Root'] for loot, _ in containers}) == len(containers), 'Documents use distinct containers')
        for loot, item in containers:
            host = next(i for i in loot['Items'] if i['_id'] == item['parentId'])
            name = templates[host['_tpl']]['_name'].lower()
            check(any(n in name for n in ['jacket', 'drawer', 'safe', 'duffle', 'duffel', 'sportbag']), 'Document uses an eligible container')
            check(item['upd']['StackObjectsCount'] == 1, 'One ordinary document per selected container')
        check(request('/seasonal-perks/hub/claim', {'OperationId': secrets.token_hex(16), 'ExpectedRevision': before['Revision'], 'RewardId': before['Pages'][0]['Rewards'][1]['Id']}, child).get('Error'), 'Transactions are blocked during raids')
        for _, item in containers:
            event = {'OperationId': secrets.token_hex(16), 'ItemId': item['_id'], 'PickedUp': True}
            check(request('/seasonal-perks/hub/raid-document', event, child).get('Committed'), 'First pickup acknowledged')
            picked = request('/seasonal-perks/hub', session=child)
            request('/seasonal-perks/hub/raid-document', event, child)
            check(request('/seasonal-perks/hub', session=child) == picked, 'Reconnect retry cannot count a pickup twice')
        after_pickups = request('/seasonal-perks/hub', session=child)
        check(after_pickups['RemainingDocuments'] == before['RemainingDocuments'] - len(containers), 'Each new unit consumes one allowance')
        extracted = []
        if containers:
            item = copy.deepcopy(containers[0][1])
            target = secrets.token_hex(12)
            split = {'OperationId': secrets.token_hex(16), 'ItemId': item['_id'], 'TargetId': target, 'Count': 1, 'Split': True, 'PickedUp': True}
            check(request('/seasonal-perks/hub/raid-document', split, child).get('Committed'), 'Split conserves a picked-up identity')
            request('/seasonal-perks/hub/raid-document', split, child)
            check(request('/seasonal-perks/hub', session=child)['RemainingDocuments'] == after_pickups['RemainingDocuments'], 'Duplicate split cannot consume another allowance')
            item['_id'] = target
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
        request('/client/match/local/end', end, child)
        after = request('/seasonal-perks/hub', session=child)
        check(after['RemainingDocuments'] == after_pickups['RemainingDocuments'], 'Extraction does not recount pickups')
        check(0 <= after['UniversalCount'] - before['UniversalCount'] <= len(extracted), 'Server awards at most one bonus per new extracted document')
        inventory = request('/client/game/profile/list', session=child)[0]['Inventory']
        check(all(any(i['_id'] == identity for i in inventory['items']) for identity in extracted), 'Ordinary documents remain after extraction')
        request('/client/match/local/end', end, child)
        check(request('/seasonal-perks/hub', session=child) == after, 'Repeated raid-end response cannot reroll or restore rejected documents')
    check(after['RemainingDocuments'] == 0, 'Thirty first pickups exhaust the rolling window')
    for mode, side in [('seasonal', 'savage'), ('normal', 'pmc')]:
        request('/seasonal-perks/switch', {'Mode': mode}, root)
        session = child if mode == 'seasonal' else root
        start = request('/client/match/local/start', {'location': 'bigmap', 'playerSide': side, 'mode': 'regular', 'timeVariant': 'CURR', 'transitionType': 0}, session)
        items = [i for loot in start['locationLoot']['Loot'] for i in loot.get('Items', []) if i['_tpl'] in docs]
        check(not items, 'No seasonal document injection for ' + mode + '/' + side)
        request('/client/game/start', session=session)
    save(PROJECT / 'Research/hub-raid-checks.json', {'passed': len(checks), 'checks': checks})
    print('Hub raids:', len(checks), 'checks passed.')


if __name__ == '__main__':
    main()
