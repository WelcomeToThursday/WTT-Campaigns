"""Resource-perk routes/persistence on synthetic accounts in the isolated 6975 server."""
import argparse, json, secrets
from test_integration import PROJECT, SERVER, request, check, checks

STATE = PROJECT/'Testing/resource-state.json'
MED = '590c678286f77426c9660122'
WATER = '5448fee04bdc2dbc018b4567'
SINGLE = '5448ff904bdc2d6f028b456e'

def profile(session):
    return request('/client/game/profile/list', session=session)[0]

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('phase', choices=['prepare', 'verify', 'restart'])
    phase = parser.parse_args().phase
    if phase == 'prepare':
        assert not (SERVER/'test-server.pid').exists(), 'Stop isolated server first'
        previous = json.loads((PROJECT/'Testing/restart-state.json').read_text(encoding='utf-8'))
        state = {k: previous[k] for k in ['root', 'child']}
        state['items'] = {}
        specs = {'med': (MED, 100), 'med_low': (MED, 5), 'med_bleed': (MED, 100),
                 'med_exhaust': (MED, 10), 'med_fresh': (MED, None),
                 'water': (WATER, 60), 'water_fresh': (WATER, None),
                 'single': (SINGLE, None), 'water_odd': (WATER, 60),
                 'water_exhaust': (WATER, 4)}
        for session in [state['root'], state['child']]:
            path = SERVER/'user/profiles'/f'{session}.json'
            data = json.loads(path.read_text(encoding='utf-8'))
            assert data['info']['username'].startswith('season-test-'), 'Synthetic accounts only'
            pmc = data['characters']['pmc']
            ids = {}
            for index, (name, (tpl, resource)) in enumerate(specs.items()):
                item_id = secrets.token_hex(12)
                ids[name] = item_id
                item = {'_id': item_id, '_tpl': tpl, 'parentId': pmc['Inventory']['stash'],
                        'slotId': 'hideout', 'location': {'x': index % 5, 'y': 30 + index // 5, 'r': 0}}
                if resource is not None:
                    item['upd'] = {'MedKit': {'HpResource': resource}} if tpl == MED else {'FoodDrink': {'HpPercent': resource}}
                pmc['Inventory']['items'].append(item)
            for part in pmc['Health']['BodyParts'].values():
                part['Health']['Current'] = 1
                part.pop('Effects', None)
            pmc['Health']['BodyParts']['RightArm']['Effects'] = {'LightBleeding': {'Time': 100}}
            pmc['Health']['BodyParts']['LeftArm']['Effects'] = {'HeavyBleeding': {'Time': 100}}
            for key in ['Energy', 'Hydration']:
                pmc['Health'][key]['Current'] = 10
            path.write_text(json.dumps(data), encoding='utf-8')
            state['items'][session] = ids
        STATE.write_text(json.dumps(state), encoding='utf-8')
        print('Prepared synthetic resource fixtures.')
        return

    state = json.loads(STATE.read_text(encoding='utf-8'))
    root, child = state['root'], state['child']
    if phase == 'restart':
        for session, expected in state['expected'].items():
            pmc = profile(session)
            actual = {i['_id']: i.get('upd') for i in pmc['Inventory']['items'] if i['_id'] in state['items'][session].values()}
            check(actual == expected, 'Resource amounts and exhausted-item removal survive restart: ' + ('seasonal' if session == child else 'normal'))
        results = PROJECT/'Research/item-resource-results.json'
        report = json.loads(results.read_text(encoding='utf-8'))
        report['restartPassed'] = len(checks)
        report['restartChecks'] = checks
        results.write_text(json.dumps(report, indent=2), encoding='utf-8')
        print('Passed', len(checks), 'resource persistence checks.')
        return

    snapshot = request('/wtt-campaigns/snapshot', session=root)
    perks = ['69c3d036042c81ad9209eeae', '69c40f9f9b5263783d0fe51d']
    edited = request('/wtt-campaigns/edit', {'PerkIds': perks, 'ExpectedRevision': snapshot['State']['Revision']}, root)
    check(not edited.get('Error'), 'Both resource perks selectable together within budget')
    request('/wtt-campaigns/switch', {'Mode': 'seasonal'}, root)
    normal_before = profile(root)
    def use(session, name, count, part=None):
        item_id = state['items'][session][name]
        event = {'Action': 'Eat' if part is None else 'Heal', 'item': item_id, 'count': count}
        if part: event['part'] = part
        response = request('/client/game/profile/items/moving', {'data': [event]}, session, allow_error=True)
        check(response['err'] == 0, 'Use ' + name + ': ' + str(response.get('errmsg')))
        pmc = profile(session)
        item = next((i for i in pmc['Inventory']['items'] if i['_id'] == item_id), None)
        return pmc, item
    pmc, item = use(child, 'med', 20, 'Chest')
    check(item['upd']['MedKit']['HpResource'] == 75, '20 HP costs 25 medical resource')
    check(pmc['Health']['BodyParts']['Chest']['Health']['Current'] == 21, 'Medical multiplier preserves healing')
    pmc, item = use(child, 'med_low', 4, 'LeftArm')
    check(item is None, 'Last five resource heal four HP and remove item')
    check('HeavyBleeding' in pmc['Health']['BodyParts']['LeftArm']['Effects'], 'Unaffordable bleed stays active')
    pmc, item = use(child, 'med_bleed', 40, 'RightArm')
    check(item['upd']['MedKit']['HpResource'] == 50, 'Treatment plus healing both pay 1.25 multiplier')
    check('LightBleeding' not in pmc['Health']['BodyParts']['RightArm'].get('Effects', {}), 'Affordable injury treated')
    check(pmc['Health']['BodyParts']['RightArm']['Health']['Current'] == 11, 'Treatment cost does not become HP')
    _, item = use(child, 'med_fresh', 8, 'Stomach')
    check(item['upd']['MedKit']['HpResource'] == 290, 'Medical item without upd uses template maximum')
    pmc, item = use(child, 'water', 20)
    check(item['upd']['FoodDrink']['HpPercent'] == 50, '20 food units cost 10 resource')
    check(pmc['Health']['Hydration']['Current'] == 30, 'Diet preserves vanilla stash hydration benefit')
    _, item = use(child, 'water_fresh', 10)
    check(item['upd']['FoodDrink']['HpPercent'] == 55, 'Food without upd uses template maximum')
    _, item = use(child, 'water_odd', 3)
    check(item['upd']['FoodDrink']['HpPercent'] == 58, 'Odd stash amount follows native round-to-even')
    _, item = use(child, 'single', 1)
    check(item is not None and item['upd']['FoodDrink']['HpPercent'] == 1, 'Single-resource stash food follows native rounded half-use')
    _, item = use(child, 'water_exhaust', 8)
    check(item is None, 'Exhausted food is removed')
    check(profile(root)['Inventory'] == normal_before['Inventory'], 'Seasonal use leaves normal inventory unchanged')

    request('/wtt-campaigns/switch', {'Mode': 'normal'}, child)
    _, item = use(root, 'med', 20, 'Chest')
    check(item['upd']['MedKit']['HpResource'] == 80, 'Normal medical use stays vanilla')
    _, item = use(root, 'water', 20)
    check(item['upd']['FoodDrink']['HpPercent'] == 40, 'Normal food use stays vanilla')
    snapshot = request('/wtt-campaigns/snapshot', session=root)
    request('/wtt-campaigns/edit', {'PerkIds': [], 'ExpectedRevision': snapshot['State']['Revision']}, root)
    request('/wtt-campaigns/switch', {'Mode': 'seasonal'}, root)
    _, item = use(child, 'med_exhaust', 10, 'RightLeg')
    check(item is None, 'Removing medical perk restores one-for-one exhaustion')
    _, item = use(child, 'water', 10)
    check(item['upd']['FoodDrink']['HpPercent'] == 40, 'Removing Diet restores vanilla consumption')
    state['expected'] = {}
    for session in [root, child]:
        state['expected'][session] = {i['_id']: i.get('upd') for i in profile(session)['Inventory']['items'] if i['_id'] in state['items'][session].values()}
        request('/client/game/logout', session=session)
    STATE.write_text(json.dumps(state), encoding='utf-8')
    (PROJECT/'Research/item-resource-results.json').write_text(json.dumps({'passed': len(checks), 'checks': checks}, indent=2), encoding='utf-8')
    print('Passed', len(checks), 'resource integration checks.')

if __name__ == '__main__': main()
