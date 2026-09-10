"""No-FiR upgrade acceptance and persistence using isolated synthetic accounts only.

Run prepare with the isolated server stopped, then verify while running, restart,
run restart, stop, and run restore-config. Requires test_integration.py fixtures.
"""
import argparse, hashlib, json, secrets
from test_integration import PROJECT, SERVER, request, check, checks

STATE = PROJECT/'Testing/hideout-fir-state.json'
CONFIG = SERVER/'user/mods/WTT-Campaigns/config.json'
FIR = '69ce5eb3e4b79de94a0d78c8'
BUSH = '69c405a9d7a7b2ca660e0c56'

def profile(session):
    return request('/client/game/profile/list', session=session)

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('phase', choices=['prepare', 'verify', 'restart', 'restore-config'])
    phase = parser.parse_args().phase
    if phase in ['prepare', 'restore-config']:
        assert not (SERVER/'test-server.pid').exists(), 'Stop isolated server first'
    if phase == 'restore-config':
        state = json.loads(STATE.read_text(encoding='utf-8'))
        CONFIG.write_text(state['originalConfig'], encoding='utf-8')
        print('Restored isolated server common-perk configuration.')
        return
    if phase == 'prepare':
        previous = json.loads((PROJECT/'Testing/restart-state.json').read_text(encoding='utf-8'))
        state = {k: previous[k] for k in ['root', 'child']}
        state['originalConfig'] = CONFIG.read_text(encoding='utf-8-sig')
        config = json.loads(state['originalConfig'])
        config['EnabledCommonIds'] = list(dict.fromkeys(config['EnabledCommonIds'] + [FIR]))
        path = SERVER/'user/profiles'/f"{state['child']}.json"
        data = json.loads(path.read_text(encoding='utf-8'))
        assert data['info']['username'].startswith('season-test-'), 'Synthetic accounts only'
        pmc = data['characters']['pmc']
        areas_path = SERVER/'SPT_Data/database/hideout/areas.json'
        state['databaseHash'] = hashlib.sha256(areas_path.read_bytes()).hexdigest()
        area = next(a for a in json.loads(areas_path.read_text(encoding='utf-8')) if a['type'] == 6)
        stage = area['stages']['1']
        assert stage['constructionTime'] > 0
        state['items'] = []
        for requirement in stage['requirements']:
            if requirement['type'] == 'Area':
                next(a for a in pmc['Hideout']['Areas'] if a['type'] == requirement['areaType'])['level'] = requirement['requiredLevel']
            elif requirement['type'] == 'Item':
                assert requirement['isSpawnedInSession'], 'Fixture must exercise actual FiR requirements'
                for _ in range(int(requirement['count'])):
                    item_id = secrets.token_hex(12)
                    state['items'].append(item_id)
                    pmc['Inventory']['items'].append({'_id': item_id, '_tpl': requirement['templateId'],
                        'parentId': pmc['Inventory']['stash'], 'slotId': 'hideout',
                        'location': {'x': len(state['items']) % 8, 'y': 35 + len(state['items']) // 8, 'r': 0},
                        'upd': {'SpawnedInSession': False}})
        target = next(a for a in pmc['Hideout']['Areas'] if a['type'] == 6)
        target.update({'level': 0, 'constructing': False, 'completeTime': 0})
        path.write_text(json.dumps(data), encoding='utf-8')
        CONFIG.write_text(json.dumps(config, indent=2), encoding='utf-8')
        STATE.write_text(json.dumps(state), encoding='utf-8')
        print('Prepared non-FiR materials for a level-one water collector on the synthetic seasonal profile.')
        return
    state = json.loads(STATE.read_text(encoding='utf-8'))
    root, child = state['root'], state['child']
    if phase == 'verify':
        snapshot = request('/wtt-campaigns/snapshot', session=root)
        check(FIR not in snapshot['Unavailable'] and BUSH not in snapshot['Unavailable'], 'Both new perks advertised as supported')
        edited = request('/wtt-campaigns/edit', {'PerkIds': [BUSH, '69c3ce913ffdba4e68086bdb'], 'ExpectedRevision': snapshot['State']['Revision']}, root)
        check(not edited.get('Error'), 'Bushborne selectable with five-point budget')
        check(FIR in edited['State']['SeasonalPerks'], 'Configured common No-FiR perk included on save')
        request('/wtt-campaigns/switch', {'Mode': 'seasonal'}, root)
        before = profile(child)[0]
        check(all(not i.get('upd', {}).get('SpawnedInSession', False) for i in before['Inventory']['items'] if i['_id'] in state['items']), 'All submitted upgrade materials are non-FiR')
        normal = profile(root)
        event = {'Action': 'HideoutUpgrade', 'areaType': 6, 'timestamp': 0,
                 'items': [{'id': item_id, 'count': 1} for item_id in state['items']]}
        result = request('/client/game/profile/items/moving', {'data': [event]}, child, allow_error=True)
        check(result['err'] == 0, 'Native server accepts non-FiR upgrade: ' + str(result.get('errmsg')))
        pmc = profile(child)[0]
        check(not set(state['items']).intersection(i['_id'] for i in pmc['Inventory']['items']), 'Upgrade consumes every submitted material')
        area = next(a for a in pmc['Hideout']['Areas'] if a['type'] == 6)
        check(area['constructing'] and area['completeTime'] > 0, 'Upgrade starts the native construction timer')
        after = profile(root)
        check([p['Inventory'] for p in after] == [p['Inventory'] for p in normal], 'Normal PMC and Scav inventories unchanged')
        remaining = {i['_id']: i for i in pmc['Inventory']['items']}
        check(all(remaining[i['_id']].get('upd', {}).get('SpawnedInSession', False) == i.get('upd', {}).get('SpawnedInSession', False) for i in before['Inventory']['items'] if i['_id'] not in state['items']), 'Remaining inventory FiR flags unchanged')
        check(hashlib.sha256((SERVER/'SPT_Data/database/hideout/areas.json').read_bytes()).hexdigest() == state['databaseHash'], 'Shared hideout database unchanged')
        state['expectedArea'] = area
        for session in [root, child]: request('/client/game/logout', session=session)
        STATE.write_text(json.dumps(state), encoding='utf-8')
        (PROJECT/'Research/hideout-fir-results.json').write_text(json.dumps({'checks': checks, 'passed': len(checks)}, indent=2), encoding='utf-8')
    else:
        pmc = profile(child)[0]
        check(not set(state['items']).intersection(i['_id'] for i in pmc['Inventory']['items']), 'Non-FiR material consumption survives restart')
        check(next(a for a in pmc['Hideout']['Areas'] if a['type'] == 6) == state['expectedArea'], 'Construction state survives restart')
        snapshot = request('/wtt-campaigns/snapshot', session=child)
        check({FIR, BUSH} <= set(snapshot['State']['SeasonalPerks']), 'Both new perk selections survive restart')
        report_path = PROJECT/'Research/hideout-fir-results.json'
        report = json.loads(report_path.read_text(encoding='utf-8'))
        report.update({'restartChecks': checks, 'restartPassed': len(checks)})
        report_path.write_text(json.dumps(report, indent=2), encoding='utf-8')
    print('Passed', len(checks), 'hideout checks.')

if __name__ == '__main__': main()
