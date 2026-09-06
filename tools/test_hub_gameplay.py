"""Synthetic, isolated-server claim/exchange fixtures. Never reads or edits installed profiles."""
import argparse
import concurrent.futures
import copy
import ctypes
import hashlib
import json
import secrets
import urllib.error
from collections import Counter
from pathlib import Path
from test_integration import PROJECT, SERVER, request, check, checks

STATE = PROJECT / 'Testing/hub-fixtures.json'
REPORT = PROJECT / 'Research/hub-gameplay-checks.json'


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=True), encoding='utf-8')


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('phase', choices=['prepare', 'verify', 'restart'])
    phase = parser.parse_args().phase
    presentation = read(PROJECT / 'data/hub.json')
    catalogue = read(PROJECT / 'data/hub-gameplay.json')
    rewards = [r for p in presentation['Pages'] for r in p['Rewards']] + presentation['SeasonalRewards']
    key = 'wttSeasonalHub:' + presentation['SeasonId'] + ':' + presentation['Id']
    documents = {d['id']: d['itemId'] for d in catalogue['Documents']}
    if phase == 'prepare':
        assert not (SERVER / 'test-server.pid').exists(), 'Stop the isolated server first'
        previous = read(PROJECT / 'Testing/restart-state.json')
        base = {mode: read(SERVER / 'user/profiles' / (previous[mode] + '.json')) for mode in ['root', 'child']}
        assert all(p['info']['username'].startswith('season-test-') for p in base.values())
        link = read(SERVER / 'user/profileData' / previous['root'] / 'cjSeasonalPerksAccount.json')
        fixtures = []
        previous_cases = read(STATE) if STATE.exists() else []
        for index, reward in enumerate(rewards + [None] * 5):
            ids = {mode: previous_cases[index][mode] if index < len(previous_cases) else secrets.token_hex(12) for mode in ['root', 'child']}
            case = {'reward': reward['Id'] if reward else None, **ids, 'kind': 'claim' if reward else ['exchange', 'classified', 'full', 'save-failure', 'page-gate'][index - len(rewards)]}
            for mode in ['root', 'child']:
                raw = json.dumps(base[mode])
                for name in ids:
                    raw = raw.replace(previous[name], ids[name])
                profile = json.loads(raw)
                profile['info']['username'] = 'season-test-hub-' + ids['root']
                if mode == 'child':
                    pmc = profile['characters']['pmc']
                    pmc['Info']['Level'] = 80
                    pmc['Info']['Experience'] = 100000000
                    stash = pmc['Inventory']['stash']
                    items = pmc['Inventory']['items']
                    removed = {i['_id'] for i in items if i.get('parentId') == stash}
                    while True:
                        children = {i['_id'] for i in items if i.get('parentId') in removed}
                        if children <= removed:
                            break
                        removed.update(children)
                    pmc['Inventory']['items'] = [i for i in items if i['_id'] not in removed]
                    for pos, tpl in enumerate(documents.values()):
                        pmc['Inventory']['items'].append({'_id': secrets.token_hex(12), '_tpl': tpl, 'parentId': stash,
                            'slotId': 'hideout', 'location': {'x': pos % 4 * 2, 'y': pos // 4 * 2, 'r': 0}, 'upd': {'StackObjectsCount': 500}})
                    if case['kind'] == 'full':
                        # Fill the actual installed stash dimensions with native 1x1 rubles.
                        native_items = read(SERVER / 'SPT_Data/database/templates/items.json')
                        stash_tpl = next(i['_tpl'] for i in pmc['Inventory']['items'] if i['_id'] == stash)
                        rows = native_items[stash_tpl]['_props']['Grids'][0]['_props']['cellsV']
                        for y in range(4, rows):
                            for x in range(10):
                                pmc['Inventory']['items'].append({'_id': secrets.token_hex(12), '_tpl': '5449016a4bdc2d6f028b456f',
                                    'parentId': stash, 'slotId': 'hideout', 'location': {'x': x, 'y': y, 'r': 0}, 'upd': {'StackObjectsCount': 500000}})
                        # Widen the document rows with rubles in otherwise unused cells.
                        item_defs = read(PROJECT / 'data/season-items.json')
                        occupied = {(x, y) for i in pmc['Inventory']['items'] if i.get('parentId') == stash
                            for x in range(i['location']['x'], i['location']['x'] + item_defs.get(i['_tpl'], {}).get('_props', {}).get('Width', 1))
                            for y in range(i['location']['y'], i['location']['y'] + item_defs.get(i['_tpl'], {}).get('_props', {}).get('Height', 1))}
                        for y in range(4):
                            for x in range(10):
                                if (x, y) not in occupied:
                                    pmc['Inventory']['items'].append({'_id': secrets.token_hex(12), '_tpl': '5449016a4bdc2d6f028b456f',
                                        'parentId': stash, 'slotId': 'hideout', 'location': {'x': x, 'y': y, 'r': 0}, 'upd': {'StackObjectsCount': 500000}})
                    if case['kind'] == 'classified':
                        for item in pmc['Inventory']['items']:
                            if item['_tpl'] in documents.values():
                                item['upd']['StackObjectsCount'] = 1
                    for quest in read(PROJECT / 'data/hub-quests.json'):
                        pmc['Quests'] = [q for q in pmc['Quests'] if q['qid'] != quest['_id']]
                        pmc['Quests'].append({'qid': quest['_id'], 'status': 4, 'startTime': 1, 'statusTimers': {'4': 1}, 'completedConditions': []})
                    claimed = [r['Id'] for r in rewards if reward and r['Id'] != reward['Id']]
                    pmc[key] = json.dumps({'SeasonId': presentation['SeasonId'], 'BattlePassId': presentation['Id'], 'Claimed': claimed,
                        'Revision': 0, 'Classified': 100 if case['kind'] == 'classified' else 0})
                save(SERVER / 'user/profiles' / (ids[mode] + '.json'), profile)
            fixture_link = {**link, 'SeasonalId': ids['child'], 'Mode': 'seasonal', 'ActiveRaidProfiles': []}
            save(SERVER / 'user/profileData' / ids['root'] / 'cjSeasonalPerksAccount.json', fixture_link)
            fixtures.append(case)
        save(STATE, fixtures)
        print('Prepared', len(fixtures), 'independent synthetic hub cases.')
        return

    fixtures = read(STATE)
    if phase == 'restart':
        for case in fixtures:
            actual = request('/seasonal-perks/hub', session=case['child'])
            check(actual == case['expected'], 'Hub progress survives restart: ' + (case['reward'] or case['kind']))
            if case.get('replay'):
                result = request('/seasonal-perks/hub/' + case['replay']['action'], case['replay']['body'], case['child'])
                check(result.get('Committed') and result['State'] == actual, 'Operation receipt survives restart')
        report = read(REPORT)
        report.update({'restartPassed': len(checks), 'restartChecks': checks})
        save(REPORT, report)
        print('Hub restart:', len(checks), 'checks passed.')
        return

    outcomes = Counter()
    for case in fixtures:
        child = case['child']
        # Finish native load/migration serialization before taking byte-for-byte mutation baselines.
        request('/client/game/logout', session=case['root'])
        request('/client/game/logout', session=child)
        normal_path = SERVER / 'user/profiles' / (case['root'] + '.json')
        normal_hash = digest(normal_path)
        child_path = SERVER / 'user/profiles' / (child + '.json')
        before_profile = read(child_path)
        before_hash = digest(child_path)
        view = request('/seasonal-perks/hub', session=child)
        reward = next((r for p in view['Pages'] for r in p['Rewards'] if r['Id'] == case['reward']), None)
        reward = reward or next((r for r in view['SeasonalRewards'] if r['Id'] == case['reward']), None)
        body = {'OperationId': secrets.token_hex(16), 'ExpectedRevision': view['Revision']}
        action = 'claim'
        if case['kind'] == 'claim':
            body['RewardId'] = reward['Id']
            result = request('/seasonal-perks/hub/claim', body, child)
            if reward['CanClaim']:
                check(result.get('Committed'), 'Claim eligible reward ' + reward['Id'] + ': ' + str(result.get('Error')))
                after = read(child_path)
                progress = json.loads(after['characters']['pmc'][key])
                for grant in catalogue['Rewards'][reward['Id']]['Grants']:
                    kind = grant['type']
                    outcomes[kind] += 1
                    if kind == 'Tarcoin':
                        check(progress['Tarcoins'] == grant['value'], 'Tarcoin adapter grants the exact captured value')
                    elif kind == 'CustomizationDirect':
                        check(any(c['id'] == grant['target'] for c in after['customisationUnlocks']), 'Customization adapter grants the target')
                    elif kind == 'AssortmentUnlock':
                        check(grant['target'] in progress['UnlockedOffers'], 'Trader adapter persists an explicit unlock')
                    elif kind == 'Item':
                        previous_ids = {i['_id'] for i in before_profile['characters']['pmc']['Inventory']['items']}
                        added = [i for i in after['characters']['pmc']['Inventory']['items'] if i['_id'] not in previous_ids]
                        check(all(any(i['_tpl'] == g['_tpl'] for i in added) for g in grant['items']), 'Physical adapter delivers all payload items')
                for cost in reward['Costs']:
                    doc = next(d for d in result['State']['Documents'] if d['Id'] == cost['DocumentId'])
                    check(doc['Count'] == 500 - cost['Count'], 'Exact amended cost consumed')
                check(result['State']['Revision'] == view['Revision'] + 1, 'Multi-payload tile advances one revision')
            else:
                check(result.get('Error') and digest(child_path) == before_hash, 'Unavailable dependency rejects entire claim: ' + reward['Id'])
                outcomes['Unavailable'] += 1
        elif case['kind'] == 'page-gate':
            body['RewardId'] = view['Pages'][1]['Rewards'][-1]['Id']
            result = request('/seasonal-perks/hub/claim', body, child)
            check('previous page' in result.get('Error', '') and digest(child_path) == before_hash, 'Previous-page requirement cannot be bypassed')
        elif case['kind'] == 'classified':
            reward = next(r for r in view['Pages'][0]['Rewards'] if r['Name'].startswith('Tarcoins'))
            body['RewardId'] = reward['Id']
            result = request('/seasonal-perks/hub/claim', body, child)
            check(result.get('Error') and digest(child_path) == before_hash, 'Classified use requires explicit confirmation')
            body['UseClassified'] = True
            result = request('/seasonal-perks/hub/claim', body, child)
            check(result.get('Committed') and result['State']['UniversalCount'] == 100 - reward['UniversalNeeded'], 'Classified covers only the exact shortage')
        else:
            action = 'exchange'
            doc_ids = list(documents)
            body.update({'DocumentId': doc_ids[2], 'Sources': {doc_ids[0]: 3, doc_ids[1]: 2}})
            if case['kind'] == 'save-failure':
                kernel = ctypes.WinDLL('kernel32', use_last_error=True)
                kernel.CreateFileW.restype = ctypes.c_void_p
                kernel.CreateFileW.argtypes = [ctypes.c_wchar_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p]
                kernel.CloseHandle.argtypes = [ctypes.c_void_p]
                handle = kernel.CreateFileW(str(child_path) + '.bak', 0x40000000, 0, None, 4, 0x80, None)
                assert handle != ctypes.c_void_p(-1).value
                try:
                    try:
                        failed = request('/seasonal-perks/hub/exchange', body, child)
                        check(not failed.get('Committed'), 'Failed save never acknowledges a commit')
                    except (urllib.error.URLError, TimeoutError):
                        check(True, 'Failed save returns an uncertain response')
                    except RuntimeError as error:
                        assert str(error).startswith('Empty server response:'), str(error)
                        check(True, 'Failed save returns an empty, uncertain response')
                    check(digest(child_path) == before_hash, 'Save failure leaves durable inventory and progress unchanged')
                    check(request('/seasonal-perks/hub', session=child) == view, 'Save failure restores cached balances and revision')
                finally:
                    kernel.CloseHandle(handle)
            if case['kind'] == 'exchange':
                with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
                    results = list(pool.map(lambda _: request('/seasonal-perks/hub/exchange', body, child), range(2)))
                check(all(r.get('Committed') for r in results) and results[0]['State'] == results[1]['State'], 'Concurrent duplicate exchange commits once')
                result = results[0]
            else:
                result = request('/seasonal-perks/hub/exchange', body, child)
            if case['kind'] == 'full':
                check('room' in result.get('Error', '') and digest(child_path) == before_hash, 'Full stash rejects costs and reward atomically')
            else:
                check(result.get('Committed'), 'Mixed-source exchange commits: ' + str(result.get('Error')))
                balances = {d['Id']: d['Count'] for d in result['State']['Documents']}
                check(balances[doc_ids[0]] == 497 and balances[doc_ids[1]] == 498 and balances[doc_ids[2]] == 501, 'Exchange consumes five ordinary documents and grants one selected type')
        if result.get('Committed'):
            replay = request('/seasonal-perks/hub/' + action, body, child)
            check(replay['State'] == result['State'], 'Retry returns the committed result without a second charge')
            invalid = {**body, 'Crate': not body.get('Crate', False)}
            check(request('/seasonal-perks/hub/' + action, invalid, child).get('Error'), 'Operation identifiers cannot be reused for different inputs')
            stale = {**body, 'OperationId': secrets.token_hex(16)}
            check(request('/seasonal-perks/hub/' + action, stale, child).get('Error'), 'Stale revision rejects a second operation')
            case['replay'] = {'action': action, 'body': body}
        if case['kind'] == 'exchange':
            fresh = request('/seasonal-perks/hub', session=child)
            baseline = digest(child_path)
            for extra in [{'Sources': {doc_ids[0]: 4}}, {'Sources': {'Classified': 5}}, {'DocumentId': 'unknown'}, {'Crate': True, 'Sources': {doc_ids[0]: 10}}]:
                invalid = {**body, 'OperationId': secrets.token_hex(16), 'ExpectedRevision': fresh['Revision'], **extra}
                check(request('/seasonal-perks/hub/exchange', invalid, child).get('Error') and digest(child_path) == baseline, 'Invalid or unavailable exchange is atomic')
            competing = [{**body, 'OperationId': secrets.token_hex(16), 'ExpectedRevision': fresh['Revision']} for _ in range(2)]
            with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
                responses = list(pool.map(lambda b: request('/seasonal-perks/hub/exchange', b, child), competing))
            check(sum(bool(r.get('Committed')) for r in responses) == 1 and sum(bool(r.get('Error')) for r in responses) == 1, 'Different concurrent operations cannot spend the same revision')
        check(digest(normal_path) == normal_hash, 'Normal profile remains byte-for-byte unchanged')
        after_profile = read(child_path)
        check(after_profile['characters']['scav'] == before_profile['characters']['scav'], 'Scav profile remains unchanged')
        before_pmc = {k: v for k, v in before_profile['characters']['pmc'].items() if k not in ('Inventory', key)}
        after_pmc = {k: v for k, v in after_profile['characters']['pmc'].items() if k not in ('Inventory', key)}
        check(before_pmc == after_pmc, 'Unrelated PMC data remains unchanged')
        case['expected'] = request('/seasonal-perks/hub', session=child)
    save(STATE, fixtures)
    save(REPORT, {'passed': len(checks), 'checks': checks, 'payloadOutcomes': outcomes})
    print('Hub gameplay:', len(checks), 'checks passed;', dict(outcomes))


if __name__ == '__main__':
    main()
