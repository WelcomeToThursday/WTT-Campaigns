"""Fixed consumable selection/targets/persistence on isolated synthetic accounts.

Run prepare with the isolated server stopped, verify while running, restart the
server and run restart. Requires the existing test_integration.py fixture.
Timed raid buffs require client validation; this script makes no raid claim.
"""
import argparse, hashlib, json, secrets
from test_integration import PROJECT, SERVER, request, check, checks
from test_experience_flea import free_cell

STATE = PROJECT/'Testing/consumable-perks-state.json'
REPORT = PROJECT/'Research/consumable-perks-results.json'
JUICE = '69c406731d8aec4a2b0551bd'
SAILOR = '69c40ae21d8aec4a2b0551c2'
ALLERGIC = '69c3d6a9af28f094100fe128'
DIET = '69c40f9f9b5263783d0fe51d'

def read(path): return json.loads(path.read_text(encoding='utf-8-sig'))
def save(path, value): path.write_text(json.dumps(value, indent=2), encoding='utf-8')
def profiles(session): return request('/client/game/profile/list', session=session)
def digest(path): return hashlib.sha256(path.read_bytes()).hexdigest()

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('phase', choices=['prepare', 'verify', 'restart'])
    phase = parser.parse_args().phase
    if phase == 'prepare':
        assert not (SERVER/'test-server.pid').exists(), 'Stop isolated server first'
        previous = read(PROJECT/'Testing/restart-state.json')
        state = {k: previous[k] for k in ['root', 'child']}
        catalogue = read(PROJECT/'data/catalogue.json')
        state['targets'] = {p['id']: [r['value'] for r in p['effects'][0]['itemFilter']['include']] for p in catalogue['personal'] if p['id'] in [JUICE, SAILOR]}
        items_path = SERVER/'SPT_Data/database/templates/items.json'
        items = read(items_path)
        state['databaseHash'] = digest(items_path)
        state['items'] = {}
        for session in [state['root'], state['child']]:
            path = SERVER/'user/profiles'/f'{session}.json'
            full = read(path)
            assert full['info']['username'].startswith('season-test-'), 'Synthetic accounts only'
            pmc = full['characters']['pmc']
            pmc['Health']['Energy']['Current'] = 10
            pmc['Health']['Hydration']['Current'] = 10
            state['items'][session] = []
            for targets in state['targets'].values():
                for tpl in targets:
                    assert tpl in items, 'All captured targets must exist in SPT'
                    resource = items[tpl]['_props']['MaxResource']
                    item_id = secrets.token_hex(12)
                    state['items'][session].append({'id': item_id, 'tpl': tpl, 'resource': resource})
                    pmc['Inventory']['items'].append({'_id': item_id, '_tpl': tpl,
                        'parentId': pmc['Inventory']['stash'], 'slotId': 'hideout',
                        'location': free_cell(pmc, items), 'upd': {'FoodDrink': {'HpPercent': resource}}})
            save(path, full)
        save(STATE, state)
        print('Prepared all eight consumables on synthetic profiles.')
        return
    state = read(STATE)
    root, child = state['root'], state['child']
    if phase == 'restart':
        snapshot = request('/wtt-campaigns/snapshot', session=child)
        check(snapshot['State']['SeasonalPerkEffectParameters'] == state['parameters'], 'Fixed target parameters survive restart')
        check({JUICE, SAILOR, DIET} <= set(snapshot['State']['SeasonalPerks']), 'Consumable selections survive restart')
        for session in [root, child]:
            current = profiles(session)
            check([p['Inventory'] for p in current] == state['inventories'][session], 'Resource consumption survives restart: '+session)
        check(digest(SERVER/'SPT_Data/database/templates/items.json') == state['databaseHash'], 'Shared item database remains unchanged after restart')
        report = read(REPORT); report.update({'restartChecks': checks, 'restartPassed': len(checks)}); save(REPORT, report)
        print('Passed', len(checks), 'consumable restart checks.')
        return

    def select(ids, should_succeed=True):
        snapshot = request('/wtt-campaigns/snapshot', session=root)
        result = request('/wtt-campaigns/edit', {'PerkIds': ids, 'ExpectedRevision': snapshot['State']['Revision']}, root)
        check(bool(result.get('Error')) != should_succeed, 'Selection outcome '+str(ids))
        return result

    snapshot = request('/wtt-campaigns/snapshot', session=root)
    check(JUICE not in snapshot['Unavailable'] and SAILOR not in snapshot['Unavailable'], 'Both fixed consumable perks are available')
    check(ALLERGIC not in snapshot['Unavailable'], 'Random Allergic is available with its own implementation')
    select([JUICE], False)
    select([SAILOR], False)
    # Captured drawbacks provide budget without changing food resources.
    funding = []
    budget = 0
    for p in snapshot['Catalogue']['personal']:
        if p['points'] > 0 and p['id'] not in snapshot['Unavailable']:
            funding.append(p['id']); budget += p['points']
            if budget >= 6: break
    # Selection uses positive points as penalties that fund negative-cost benefits.
    saved = select(funding + [JUICE, SAILOR])
    check(saved['State']['SeasonalPerkEffectParameters']['allergy'] == {k: {'targetItems': v} for k, v in state['targets'].items()}, 'Selection saves all four targets for each perk')
    parameters = saved['State']['SeasonalPerkEffectParameters']
    repeated = select(funding + [JUICE, SAILOR])
    check(repeated['State']['SeasonalPerkEffectParameters'] == parameters, 'Repeated save does not reroll')
    removed = select(funding + [SAILOR])
    check(JUICE not in removed['State']['SeasonalPerkEffectParameters']['allergy'] and SAILOR in removed['State']['SeasonalPerkEffectParameters']['allergy'], 'Removal affects only one parameter entry')
    restored = select(funding + [JUICE, SAILOR])
    check(restored['State']['SeasonalPerkEffectParameters'] == parameters, 'Reselection restores the same targets')
    select(funding + ['6a5789f713792e2c7c0d2a5b'], False)
    after_rejected = request('/wtt-campaigns/snapshot', session=root)
    check(after_rejected['State'] == restored['State'], 'Unsupported selection leaves saved targets/state unchanged')
    request('/wtt-campaigns/switch', {'Mode': 'seasonal'}, root)
    for session in [root, child]:
        before = profiles(session)
        for item in state['items'][session]:
            resource = item['resource']
            count = 1 if resource == 1 else min(5, resource)
            result = request('/client/game/profile/items/moving', {'data': [{'Action': 'Eat', 'item': item['id'], 'count': count, 'time': 1}]}, session, allow_error=True)
            check(result['err'] == 0, 'Native stash consumption succeeds '+item['tpl']+' '+session)
            remaining = next((i for i in profiles(session)[0]['Inventory']['items'] if i['_id'] == item['id']), None)
            check(remaining is None if resource == 1 else remaining['upd']['FoodDrink']['HpPercent'] == resource - count, 'New perks do not change native resource use '+item['tpl']+' '+session)
        after = profiles(session)
        check(after[1]['Inventory'] == before[1]['Inventory'], 'Scav inventory unchanged by PMC consumption '+session)
        check(after[0]['Health']['BodyParts'] == before[0]['Health']['BodyParts'], 'Timed raid buffs are not converted into instant stash healing '+session)
    selected = select(funding + [JUICE, SAILOR, DIET])
    check({JUICE, SAILOR, DIET} <= set(selected['State']['SeasonalPerks']), 'Both perks coexist with Diet')
    request('/wtt-campaigns/switch', {'Mode': 'normal'}, root)
    check(request('/wtt-campaigns/snapshot', session=root)['ActiveMode'] == 'normal', 'Switching back to normal succeeds')
    request('/wtt-campaigns/switch', {'Mode': 'seasonal'}, root)
    check(request('/wtt-campaigns/snapshot', session=root)['State']['SeasonalPerkEffectParameters'] == parameters, 'Switching preserves deterministic target data')
    check(digest(SERVER/'SPT_Data/database/templates/items.json') == state['databaseHash'], 'Shared item templates remain unchanged')
    state['parameters'] = parameters
    state['inventories'] = {s: [p['Inventory'] for p in profiles(s)] for s in [root, child]}
    for s in [root, child]: request('/client/game/logout', session=s)
    save(STATE, state); save(REPORT, {'checks': checks, 'passed': len(checks)})
    print('Passed', len(checks), 'consumable server checks.')

if __name__ == '__main__': main()
