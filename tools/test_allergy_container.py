"""Allergic and Broken Secure Container tests on synthetic isolated profiles only.

Run prepare with the server stopped, verify while running, then restart after a
server restart. Timed raid effects are covered by contracts, not simulated here.
"""
import argparse, hashlib, json, secrets
from test_integration import PROJECT, SERVER, request, check, checks

STATE = PROJECT/'Testing/allergy-container-state.json'
REPORT = PROJECT/'Research/allergy-container-results.json'
ALLERGIC = '69c3d6a9af28f094100fe128'
BROKEN = '69c3da13eaf97663fb0bb36d'
JUICE = '69c406731d8aec4a2b0551bd'
SAILOR = '69c40ae21d8aec4a2b0551c2'
DIET = '69c40f9f9b5263783d0fe51d'

def read(path): return json.loads(path.read_text(encoding='utf-8-sig'))
def save(path, value): path.write_text(json.dumps(value, indent=2), encoding='utf-8')
def profiles(session): return request('/client/game/profile/list', session=session)
def digest(path): return hashlib.sha256(path.read_bytes()).hexdigest()

def free_space(pmc, templates, tpl):
    inventory = pmc['Inventory']; stash = next(i for i in inventory['items'] if i['_id'] == inventory['stash'])
    grid = templates[stash['_tpl']]['_props']['Grids'][0]['_props']; occupied = set()
    for item in inventory['items']:
        if item.get('parentId') != inventory['stash'] or not isinstance(item.get('location'), dict): continue
        p = item['location']; props = templates[item['_tpl']]['_props']; w, h = props['Width'], props['Height']
        if p.get('r') in [1, 'Vertical']: w, h = h, w
        occupied.update((p['x']+x, p['y']+y) for x in range(w) for y in range(h))
    props = templates[tpl]['_props']; w, h = props['Width'], props['Height']
    return next({'x': x, 'y': y, 'r': 0} for y in reversed(range(grid['cellsV']-h+1)) for x in range(grid['cellsH']-w+1)
                if all((x+dx, y+dy) not in occupied for dx in range(w) for dy in range(h)))

def main():
    parser = argparse.ArgumentParser(); parser.add_argument('phase', choices=['prepare', 'verify', 'restart']); phase = parser.parse_args().phase
    db = SERVER/'SPT_Data/database/templates/items.json'; templates = read(db)
    if phase == 'prepare':
        assert not (SERVER/'test-server.pid').exists(), 'Stop isolated server first'
        prior = read(PROJECT/'Testing/restart-state.json'); state = {k: prior[k] for k in ['root', 'child']}
        state['databaseHash'] = digest(db); state['fixtures'] = {}
        for session in [state['root'], state['child']]:
            path = SERVER/'user/profiles'/f'{session}.json'; full = read(path)
            assert full['info']['username'].startswith('season-test-')
            pmc = full['characters']['pmc']; inv = pmc['Inventory']; fixture = {}
            def add(name, tpl, parent=None, x=0, y=0, count=1):
                item = {'_id': secrets.token_hex(12), '_tpl': tpl, 'parentId': parent or inv['stash'], 'slotId': 'main',
                        'location': {'x':x,'y':y,'r':0} if parent else free_space(pmc, templates, tpl), 'upd': {'StackObjectsCount':count}}
                inv['items'].append(item); fixture[name] = item['_id']; return item['_id']
            box = add('box', '5857a8bc2459772bad15db29')
            add('legacyAmmo', '56dff026d2720bb8668b4567', box, 0, 0, 15)
            add('cashInBox', '5449016a4bdc2d6f028b456f', box, 1, 0, 1000)
            add('medicine', '5755356824597772cb798962')
            add('cash', '5449016a4bdc2d6f028b456f', count=10000)
            add('ammo', '56dff026d2720bb8668b4567', count=30)
            case = add('case', '5c093e3486f77430cb02e593')
            add('nestedMedicine', '5755356824597772cb798962', case)
            add('emptyCase', '619cbf9e0a7c3a1a2731940a')
            state['fixtures'][session] = fixture; save(path, full)
        save(STATE, state); print('Prepared synthetic inventory fixtures'); return

    state = read(STATE); root, child = state['root'], state['child']
    if phase == 'restart':
        snapshot = request('/seasonal-perks/snapshot', session=root)
        check(snapshot['State']['SeasonalPerkEffectParameters'] == state['parameters'], 'Random target roll survives restart')
        check({ALLERGIC, BROKEN, JUICE, SAILOR, DIET} <= set(snapshot['State']['SeasonalPerks']), 'All five selected perks survive restart')
        for session in [root, child]: check([p['Inventory'] for p in profiles(session)] == state['inventories'][session], 'PMC and Scav inventory survive restart '+session)
        check(digest(db) == state['databaseHash'], 'Shared template database unchanged after restart')
        report = read(REPORT); report['restartChecks'] = checks; save(REPORT, report)
        print('Passed', len(checks), 'restart checks'); return

    def select(ids, success=True):
        snapshot = request('/seasonal-perks/snapshot', session=root)
        result = request('/seasonal-perks/edit', {'PerkIds':ids, 'ExpectedRevision':snapshot['State']['Revision']}, root)
        check(bool(result.get('Error')) != success, 'Selection '+str(ids)); return result
    def event(session, action, success=True):
        before = [p['Inventory'] for p in profiles(session)]
        result = request('/client/game/profile/items/moving', {'data':[action], 'tm':0}, session, allow_error=True)
        warnings = (result.get('data') or {}).get('warnings') or result.get('warnings') or []
        rejected = bool(warnings) or bool(result.get('err'))
        check(rejected != success, action['Action']+' '+('allowed' if success else 'rejected')+' '+str(result)[:200])
        after = [p['Inventory'] for p in profiles(session)]
        check(before[1] == after[1], 'PMC action leaves Scav inventory intact')
        if not success: check(before == after, 'Rejection is atomic for '+action['Action'])
        return after[0]
    def pos(parent, x=0, y=1): return {'id':parent, 'container':'main', 'location':{'x':x,'y':y,'r':0}}
    def move(session, item, parent, success=True, x=0, y=1):
        return event(session, {'Action':'Move','item':item,'to':pos(parent,x,y)}, success)

    snapshot = request('/seasonal-perks/snapshot', session=root)
    check(ALLERGIC not in snapshot['Unavailable'] and BROKEN not in snapshot['Unavailable'], 'Both new perks are selectable')
    normal_before = profiles(root)
    selected = select([ALLERGIC, BROKEN, JUICE, SAILOR, DIET])
    parameters = selected['State']['SeasonalPerkEffectParameters']; targets = parameters['allergy'][ALLERGIC]['targetItems']
    def parents(tpl):
        seen = set()
        while tpl in templates and templates[tpl].get('_parent') not in seen:
            tpl = templates[tpl].get('_parent'); seen.add(tpl)
        return seen
    categories = {'5448f3a14bdc2d27728b4569','5448f3a64bdc2d60728b456a','543be6674bdc2df1348b4569'}
    check(len(targets) == len(set(targets)) == 3 and all(t in templates and parents(t)&categories for t in targets), 'Three valid distinct medication/provision targets')
    check(select([ALLERGIC,BROKEN,JUICE,SAILOR,DIET])['State']['SeasonalPerkEffectParameters'] == parameters, 'Repeated edits preserve target roll')
    select([BROKEN]); check(select([ALLERGIC,BROKEN,JUICE,SAILOR,DIET])['State']['SeasonalPerkEffectParameters'] == parameters, 'Deselect/reselect preserves random targets and fixed targets')
    select([ALLERGIC,BROKEN,'6a5789f713792e2c7c0d2a5b'], False)
    check(request('/seasonal-perks/snapshot',session=root)['State']['SeasonalPerkEffectParameters'] == parameters, 'Rejected edit preserves roll')
    check(profiles(root) == normal_before, 'Selections leave complete normal characters unchanged')
    request('/seasonal-perks/switch', {'Mode':'seasonal'}, root)
    f = state['fixtures'][child]; r = state['fixtures'][root]
    move(child, f['medicine'], f['box'], False)
    move(child, f['case'], f['box'], False)
    move(child, f['nestedMedicine'], f['box'], False)
    move(child, f['emptyCase'], f['box'])
    event(child, {'Action':'Transfer','item':f['ammo'],'with':f['legacyAmmo'],'count':2}, False)
    event(child, {'Action':'Merge','item':f['ammo'],'with':f['legacyAmmo']}, False)
    event(child, {'Action':'Split','splitItem':f['ammo'],'newItem':secrets.token_hex(12),'count':2,'container':pos(f['box'],2,0)}, False)
    med = next(i for i in profiles(child)[0]['Inventory']['items'] if i['_id'] == f['medicine'])
    event(child, {'Action':'Swap','item':f['medicine'],'item2':f['cashInBox'], 'to':pos(f['box'],1,0),
                  'to2':{'id':med['parentId'],'container':'main','location':med['location']}}, False)
    event(child, {'Action':'ApplyInventoryChanges','changedItems':[{'_id':f['medicine'],'_tpl':med['_tpl'],'parentId':f['box'],'slotId':'main','location':{'x':2,'y':0,'r':0}}]}, False)
    event(child, {'Action':'Transfer','item':f['cash'],'with':f['cashInBox'],'count':100})
    event(child, {'Action':'Split','splitItem':f['cash'],'newItem':secrets.token_hex(12),'count':100,'container':pos(f['box'],2,0)})
    # Withdrawal is allowed even for pre-existing items now disallowed by the perk.
    pmc = profiles(child)[0]; stash = pmc['Inventory']['stash']; location = free_space(pmc, templates, '56dff026d2720bb8668b4567')
    event(child, {'Action':'Move','item':f['legacyAmmo'],'to':{'id':stash,'container':'main','location':location}})
    check(next(i for i in profiles(child)[0]['Inventory']['items'] if i['_id']==f['legacyAmmo'])['parentId']==stash, 'Pre-existing prohibited item can be withdrawn')
    # Normal profile remains unrestricted even while the seasonal mode is active.
    move(root, r['medicine'], r['box'])
    select([ALLERGIC]); move(child, f['medicine'], f['box'], True, 2, 1)
    selected = select([ALLERGIC,BROKEN,JUICE,SAILOR,DIET])
    check(next(i for i in profiles(child)[0]['Inventory']['items'] if i['_id']==f['medicine'])['parentId']==f['box'], 'Selecting restriction preserves existing contents')
    pmc = profiles(child)[0]; location = free_space(pmc, templates, '5755356824597772cb798962')
    event(child, {'Action':'Move','item':f['medicine'],'to':{'id':stash,'container':'main','location':location}})
    request('/seasonal-perks/switch', {'Mode':'normal'}, root)
    request('/seasonal-perks/switch', {'Mode':'seasonal'}, root)
    check(request('/seasonal-perks/snapshot', session=root)['State']['SeasonalPerkEffectParameters'] == parameters, 'Switching preserves saved targets')
    check(digest(db) == state['databaseHash'], 'Shared templates untouched')
    state['parameters'] = parameters; state['inventories'] = {s:[p['Inventory'] for p in profiles(s)] for s in [root,child]}
    for s in [root,child]: request('/client/game/logout',session=s)
    save(STATE,state); save(REPORT,{'checks':checks,'passed':len(checks)})
    print('Passed',len(checks),'allergy/container server checks')

if __name__ == '__main__': main()
