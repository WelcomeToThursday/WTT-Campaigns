"""Progression integration checks. Only synthetic accounts on isolated port 6975."""
import argparse
import copy
import json
import time
from test_integration import PROJECT, SERVER, request, check, checks

STATE = PROJECT / 'Testing/progression-state.json'
REPORT = PROJECT / 'Research/progression-results.json'
PRAPOR = '54cb50c76803fa8b248b4571'
DATA = json.loads((PROJECT / 'data/trader-progression.json').read_text(encoding="utf-8-sig"))
DB = json.loads((SERVER / 'SPT_Data/database/templates/quests.json').read_text(encoding="utf-8-sig"))


def save(path, data):
    path.write_text(json.dumps(data, indent=2) + '\n', encoding='utf-8')


def profile(session):
    return request('/client/game/profile/list', session=session)[0]


def quests(session):
    return {q['_id']: q for q in request('/client/quest/list', session=session)}


def action(session, kind, qid):
    return request('/client/game/profile/items/moving', {'data': [{'Action': kind, 'qid': qid, 'type': 'Quest'}]},
                   session, allow_error=True)


def fixture():
    username = 'season-test-progression-' + str(time.time_ns())
    registered = request('/launcher/v2/register', {'username': username, 'edition': 'Standard'})
    root = next(p['profileId'] for p in registered['Profiles'] if p['username'] == username)
    customization = json.loads((SERVER / 'SPT_Data/database/templates/customization.json').read_text(encoding="utf-8-sig"))
    def cosmetic(parent):
        return sorted(k for k,v in customization.items() if v.get('_parent') == parent
            and v['_props'].get('AvailableAsDefault') and 'Usec' in v['_props'].get('Side', []))[0]
    request('/client/game/profile/create', {'side': 'Usec', 'nickname': 'Progression',
        'headId': cosmetic('5cc085e214c02e000c6bea67'), 'voiceId': cosmetic('5fc100cf95572123ae738483')}, root)
    snapshot = request('/wtt-campaigns/snapshot', {'ProtocolVersion': 2}, root)
    created = request('/wtt-campaigns/create', {'ProtocolVersion': 2, 'PerkIds': [], 'Nickname': 'ProgressionSeason',
        'Side': 'Usec', 'ExpectedRevision': 0, 'SeasonId': snapshot['SeasonId']}, root)
    assert not created.get('Error'), created.get('Error')
    switched = request('/wtt-campaigns/switch', {'ProtocolVersion': 2, 'Mode': 'seasonal'}, root)
    assert not switched.get('Error'), switched.get('Error')
    child = switched['EffectiveProfileId']
    candidates = {i:q for i,q in DATA['Quests'].items() if q['TraderId'] == PRAPOR}
    target = next(i for i,q in candidates.items() if q['Tier'] == 2 and all(c['conditionType'] == 'TraderLoyalty' for c in q['Start'])
                  and not DB[i].get('secretQuest'))
    reward = next(i for i,q in candidates.items() if q['Tier'] == 1 and any(r['target'] == PRAPOR and r['value'] > 0
        for r in q['Reputation'].get('Success', [])) and not DB[i].get('secretQuest'))
    active = next(i for i,q in candidates.items() if q['Tier'] == 3 and not DB[i].get('secretQuest'))
    save(STATE, dict(root=root, child=child, target=target, reward=reward, active=active))
    print('Created independent normal and seasonal synthetic profiles.')


def prepare(phase):
    assert not (SERVER / 'test-server.pid').exists(), 'Stop the isolated server before editing synthetic fixtures.'
    state = json.loads(STATE.read_text(encoding="utf-8-sig"))
    for session in [state['root'], state['child']]:
        path = SERVER / 'user/profiles' / (session + '.json')
        data = json.loads(path.read_text(encoding="utf-8-sig"))
        assert data['info']['username'].startswith('season-test-'), 'Synthetic accounts only'
        pmc = data['characters']['pmc']
        if phase == 'prepare':
            xp = json.loads((SERVER / 'SPT_Data/database/globals.json').read_text(encoding="utf-8-sig"))['config']['exp']['level']['exp_table']
            pmc['Info'].update(Level=6, Experience=sum(r['exp'] for r in xp[:6]))
            for tid, trader in pmc['TradersInfo'].items():
                trader.update(loyaltyLevel=4, salesSum=0, standing=0, unlocked=True, disabled=False)
            pmc['TradersInfo'][PRAPOR]['standing'] = .7 if session == state['root'] else .69
            pmc['Quests'] = [dict(qid=state['active'], startTime=1, status=2, statusTimers={'2': 1}, completedConditions=[])]
            # Ready-to-turn-in fixture tests native reward delivery without altering objectives.
            if session == state['child']:
                pmc['Quests'].append(dict(qid=state['reward'], startTime=1, status=3,
                    statusTimers={'2': 1, '3': 2}, completedConditions=[]))
        elif phase == 'prepare-loss':
            pmc['TradersInfo'][PRAPOR]['standing'] = .1
            pmc['TradersInfo'][PRAPOR]['loyaltyLevel'] = 4
        elif phase == 'prepare-inspect':
            xp = json.loads((SERVER / 'SPT_Data/database/globals.json').read_text(encoding='utf-8-sig'))['config']['exp']['level']['exp_table']
            pmc['Info'].update(Level=79, Experience=sum(r['exp'] for r in xp[:79]))
            for trader in pmc['TradersInfo'].values():
                trader.update(loyaltyLevel=4, salesSum=0, standing=100, unlocked=True, disabled=False)
            pmc['Quests'] = [dict(qid=i, startTime=1, status=4, statusTimers={'2': 1, '4': 2}, completedConditions=[]) for i in DB]
        elif phase == 'prepare-restrictions':
            for trader in pmc['TradersInfo'].values():
                trader.update(loyaltyLevel=4, salesSum=0, standing=100, unlocked=True, disabled=False)
            pmc['TradersInfo']['58330581ace78e27b8b10cee']['unlocked'] = False
            pmc['TradersInfo']['54cb57776803fa99248b456e']['disabled'] = True
            pmc['Quests'] = [dict(qid=state['target'], startTime=1, status=9, statusTimers={'9':1},
                availableAfter=int(time.time())+3600 if session == state['root'] else 1, completedConditions=[])]
        state.setdefault('before', {})[session] = copy.deepcopy(pmc)
        save(path, data)
    save(STATE, state)
    print('Prepared', phase, 'fixtures.')


def verify(phase):
    state = json.loads(STATE.read_text(encoding="utf-8-sig"))
    root, child, target = state['root'], state['child'], state['target']
    metadata = request('/wtt-campaigns/progression', session=root)
    check(metadata['Version'] == 1 and set(metadata['Quests']) == set(DATA['Quests']), 'All 381 existing quest mappings served')
    check(request('/wtt-campaigns/progression', session=child) == metadata, 'Normal and seasonal use the same metadata')
    for session in [root, child]:
        pmc = profile(session)
        check(pmc['TradersInfo'][PRAPOR]['salesSum'] == 0, 'Zero spending preserved ' + session)
        check(pmc['Quests'] == state['before'][session]['Quests'] if phase != 'restart' else
              pmc['Quests'] == state['after'][session]['Quests'], 'Quest progress preserved ' + session)
        check(pmc['TradersInfo'][PRAPOR]['standing'] == (state['after'][session] if phase == 'restart'
              else state['before'][session])['TradersInfo'][PRAPOR]['standing'], 'No retroactive reputation ' + session)
        listing = quests(session)
        check(listing[state['active']]['sptStatus'] == 2, 'Accepted LL3 task remains active below LL3 ' + session)
        check(all(i in DB or i not in DATA['Quests'] for i in listing), 'Overlay only references installed quests')
        check(set(q['id'] for q in listing[target]['conditions']['AvailableForFinish']) ==
              set(q['id'] for q in DB[target]['conditions']['AvailableForFinish']), 'Objective IDs unchanged')
        check(any(c['conditionType'] == 'TraderLoyalty' and c['value'] == 2
                  for c in listing[target]['conditions']['AvailableForStart']), 'Tier requirement remains on wire')
    if phase == 'verify':
        check(profile(root)['TradersInfo'][PRAPOR]['loyaltyLevel'] == 2, 'Exact level and reputation threshold unlocks LL2 without spending')
        check(profile(child)['TradersInfo'][PRAPOR]['loyaltyLevel'] == 1, 'Below reputation threshold remains LL1')
        check(quests(root)[target]['sptStatus'] == 1, 'Normal character receives available LL2 group')
        check(quests(child)[target]['sptStatus'] == 0, 'Seasonal character receives locked LL2 previews')
        before = copy.deepcopy(profile(child))
        denied = action(child, 'QuestAccept', target)
        check(bool(denied.get('err')) or bool(denied.get('data', {}).get('warnings')), 'Server rejects premature acceptance')
        check(profile(child)['Quests'] == before['Quests'], 'Rejected acceptance does not alter quest progress')
        accepted = action(root, 'QuestAccept', target)
        check(accepted.get('err', 0) == 0 and not accepted.get('data', {}).get('warnings'), 'Eligible task acceptance succeeds')
        delta = sum(r['value'] for r in DATA['Quests'][state['reward']]['Reputation']['Success'] if r['target'] == PRAPOR)
        completed = action(child, 'QuestComplete', state['reward'])
        check(completed.get('err', 0) == 0 and not completed.get('data', {}).get('warnings'), 'Native task completion succeeds')
        check(abs(profile(child)['TradersInfo'][PRAPOR]['standing'] - (.69 + delta)) < 1e-7, 'Captured reputation reward applied exactly once')
        check(quests(child)[target]['sptStatus'] == 1, 'Reward crossing threshold unlocks seasonal LL2 group')
        duplicate = action(root, 'QuestAccept', target)
        check(bool(duplicate.get('err')) or bool(duplicate.get('data', {}).get('warnings')), 'Duplicate acceptance rejected without resetting objectives')
    elif phase == 'verify-loss':
        for session in [root, child]:
            check(profile(session)['TradersInfo'][PRAPOR]['loyaltyLevel'] == 1, 'Loyalty decreases after rep loss')
        check(quests(root)[target]['sptStatus'] == 2, 'Accepted task survives rep loss')
        check(quests(child)[target]['sptStatus'] == 0, 'Unaccepted task relocks after rep loss')
    state['after'] = {s: profile(s) for s in [root, child]}
    save(STATE, state)
    if phase == 'verify':
        # SPT batches profile writes on a 60-second timer. Never kill the test server before it flushes.
        deadline = time.monotonic() + 80
        while True:
            persisted = [json.loads((SERVER / 'user/profiles' / (s + '.json')).read_text(encoding='utf-8-sig'))['characters']['pmc']
                         for s in [root, child]]
            if all(p['Quests'] == state['after'][s]['Quests'] and p['TradersInfo'][PRAPOR]['standing'] ==
                   state['after'][s]['TradersInfo'][PRAPOR]['standing'] for s,p in zip([root,child], persisted)):
                break
            assert time.monotonic() < deadline, 'SPT did not flush synthetic quest/reputation changes'
            time.sleep(.5)
        check(True, 'Native autosave persisted quest acceptance and completion before shutdown')
    report = json.loads(REPORT.read_text(encoding="utf-8-sig")) if REPORT.exists() else {}
    report[phase] = {'Passed': len(checks), 'Checks': checks}
    save(REPORT, report)
    print('Passed', len(checks), phase, 'checks.')


def inspect():
    state = json.loads(STATE.read_text(encoding='utf-8-sig'))
    listing = quests(state['root'])
    expanded_weapon_lists = []
    def contains(actual, expected, path=''):
        if isinstance(expected, dict):
            return isinstance(actual, dict) and all(contains(actual.get(k), v, path + '/' + k) for k,v in expected.items())
        if isinstance(expected, list):
            # This isolated runtime also loads WTT-ContentBackport, which appends new weapon IDs.
            if path.rsplit('/',1)[-1] in ('weapon', 'target', 'equipmentExclusive', 'equipmentInclusive', 'weaponModsInclusive', 'weaponModsExclusive') and isinstance(actual, list) and actual[:len(expected)] == expected and len(actual) > len(expected):
                expanded_weapon_lists.append(path)
                return True
            return isinstance(actual, list) and len(actual) == len(expected) and all(contains(a,b,path + '/' + str(i)) for i,(a,b) in enumerate(zip(actual,expected)))
        return actual == expected
    for qid, spec in DATA['Quests'].items():
        q = listing[qid]
        check(q['traderId'] == spec['TraderId'], qid + ' captured trader assignment')
        check(contains(q['conditions']['AvailableForFinish'], DB[qid]['conditions']['AvailableForFinish']), qid + ' objectives unchanged on wire')
        for stage, original in DB[qid]['rewards'].items():
            # Edition-specific rewards are filtered by the native server before transmission.
            expected = [r for r in original if r['type'] != 'TraderStanding' and not r.get('availableInGameEditions')]
            actual = {r['id']: r for r in q['rewards'][stage]}
            check(all(r['id'] in actual and contains(actual[r['id']], r) for r in expected), qid + ' non-reputation ' + stage + ' rewards unchanged')
            standing = [r for r in q['rewards'][stage] if r['type'] == 'TraderStanding']
            check(len(standing) == len(spec['Reputation'].get(stage, [])) and all(any(contains(a,e) for a in standing)
                  for e in spec['Reputation'].get(stage, [])), qid + ' captured ' + stage + ' reputation')
        if spec['UseBetaStart']:
            check(contains(q['conditions']['AvailableForStart'], DB[qid]['conditions']['AvailableForStart']), qid + ' beta Essential prerequisites preserved')
    settings = {t['_id']:t for t in request('/client/trading/api/traderSettings', session=state['root'])}
    for tid, levels in DATA['Traders'].items():
        original = json.loads((SERVER / 'SPT_Data/database/traders' / tid / 'base.json').read_text(encoding='utf-8-sig'))
        for i, threshold in enumerate(levels):
            actual = settings[tid]['loyaltyLevels'][i]
            check(actual['minLevel'] == threshold['Level'] and actual['minStanding'] == threshold['Standing'] and actual['minSalesSum'] == 0,
                  tid + ' live LL' + str(i+1) + ' requirements')
            check(all(str(actual[k]) == str(v) or float(actual[k]) == float(v) for k,v in original['loyaltyLevels'][i].items()
                      if k not in ('minLevel','minStanding','minSalesSum')), tid + ' prices and service coefficients preserved')
        check(profile(state['root'])['TradersInfo'][tid]['loyaltyLevel'] == len(levels), tid + ' maximum loyalty including Fence')
    report = json.loads(REPORT.read_text(encoding='utf-8-sig'))
    report['inspect'] = dict(Passed=len(checks), Checks=checks, ContentBackportExpandedWeaponLists=expanded_weapon_lists)
    save(REPORT, report)
    print('Passed', len(checks), 'wire-data and maximum-loyalty checks.')


def restrictions():
    state = json.loads(STATE.read_text(encoding='utf-8-sig'))
    root, child, target = state['root'], state['child'], state['target']
    for session in [root,child]:
        listing = quests(session)
        for tid in ['58330581ace78e27b8b10cee', '54cb57776803fa99248b456e']:
            candidates = [i for i,q in DATA['Quests'].items() if q['TraderId'] == tid and q['Tier'] > 0]
            check(not any(i in listing for i in candidates), 'Locked/disabled trader has no previews ' + tid)
            denied = action(session, 'QuestAccept', candidates[0])
            check(bool(denied.get('err')) or bool(denied.get('data', {}).get('warnings')), 'Locked/disabled trader rejects acceptance ' + tid)
        essential = next(i for i,q in DATA['Quests'].items() if not q['UseBetaStart'] and q['TraderId'] == PRAPOR
            and any(c['conditionType'] == 'Quest' for c in q['Start']))
        denied = action(session, 'QuestAccept', essential)
        check(bool(denied.get('err')) or bool(denied.get('data', {}).get('warnings')), 'Essential prerequisite cannot be bypassed')
    denied = action(root, 'QuestAccept', target)
    check(bool(denied.get('err')) or bool(denied.get('data', {}).get('warnings')), 'Existing future AvailableAfter timer is honored')
    accepted = action(child, 'QuestAccept', target)
    check(accepted.get('err',0) == 0 and not accepted.get('data',{}).get('warnings'), 'Expired AvailableAfter task can be accepted')
    check(next(q for q in profile(child)['Quests'] if q['qid']==target)['status']==2, 'Expired delayed task becomes Started')
    before = {s: profile(s)['Quests'] for s in [root,child]}
    for mode, session in [('normal',root),('seasonal',child),('normal',root)]:
        switched = request('/wtt-campaigns/switch', {'ProtocolVersion':2, 'Mode':mode}, root)
        check(switched['EffectiveProfileId']==session, 'Switch to ' + mode + ' resolves correct profile')
        check(profile(session)['Quests']==before[session], 'Switch to ' + mode + ' preserves independent tasks')
    report = json.loads(REPORT.read_text(encoding='utf-8-sig'))
    report['restrictions'] = dict(Passed=len(checks), Checks=checks)
    save(REPORT, report)
    print('Passed',len(checks),'restriction, delay and switching checks.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('phase', choices=['create', 'prepare', 'verify', 'prepare-loss', 'verify-loss', 'restart', 'prepare-inspect', 'inspect', 'prepare-restrictions', 'restrictions'])
    phase = parser.parse_args().phase
    if phase == 'create': fixture()
    elif phase == 'inspect': inspect()
    elif phase == 'restrictions': restrictions()
    elif phase.startswith('prepare'): prepare(phase)
    else: verify(phase)
