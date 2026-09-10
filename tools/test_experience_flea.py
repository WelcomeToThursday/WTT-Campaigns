"""Seasoned PMCs / No Flea Market on isolated synthetic accounts (port 6975).

Run prepare while stopped, verify while running, restart the server, run restart,
then stop and restore-config. Uses the test_integration.py account fixture.
"""
import argparse, concurrent.futures, copy, json, math, secrets
from test_integration import PROJECT, SERVER, request, check, checks
from test_trader_prices import search, profile, total, RUB, THERAPIST

STATE = PROJECT/'Testing/experience-flea-state.json'
REPORT = PROJECT/'Research/experience-flea-results.json'
CONFIG = SERVER/'user/mods/WTT-Campaigns/config.json'
XP = '69c41adf883efd5e3b09ccae'
FLEA = '69c3da8fc0e4deb02605f3c9'
QUEST = '5936d90786f7742b1420ba5b'
TPL = '544fb25a4bdc2dfb738b4567'

def read(path): return json.loads(path.read_text(encoding='utf-8-sig'))
def save(path, data): path.write_text(json.dumps(data, indent=2), encoding='utf-8')
def event(session, data): return request('/client/game/profile/items/moving', {'data': [data]}, session, allow_error=True)
def trader(offer): return offer['user'].get('memberType') == 4

def free_cell(pmc, templates):
    inventory = pmc['Inventory']
    stash = next(i for i in inventory['items'] if i['_id'] == inventory['stash'])
    grid = templates[stash['_tpl']]['_props']['Grids'][0]['_props']
    occupied = set()
    for item in inventory['items']:
        if item.get('parentId') != inventory['stash'] or not isinstance(item.get('location'), dict): continue
        position = item['location']
        props = templates[item['_tpl']]['_props']
        width, height = props.get('Width', 1), props.get('Height', 1)
        if position.get('r') in [1, 'Vertical']: width, height = height, width
        occupied.update((position['x'] + x, position['y'] + y) for x in range(width) for y in range(height))
    return next({'x': x, 'y': y, 'r': 0} for y in reversed(range(grid['cellsV'])) for x in range(grid['cellsH']) if (x, y) not in occupied)

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('phase', choices=['prepare', 'verify', 'restart', 'restore-config'])
    phase = parser.parse_args().phase
    if phase in ['prepare', 'restore-config']:
        assert not (SERVER/'test-server.pid').exists(), 'Stop isolated server first'
    if phase == 'restore-config':
        CONFIG.write_text(read(STATE)['originalConfig'], encoding='utf-8')
        return
    if phase == 'prepare':
        previous = read(PROJECT/'Testing/restart-state.json')
        state = {k: previous[k] for k in ['root', 'child']}
        state['originalConfig'] = CONFIG.read_text(encoding='utf-8-sig')
        config = json.loads(state['originalConfig'])
        config['EnabledCommonIds'] = list(dict.fromkeys(config['EnabledCommonIds'] + [XP]))
        save(CONFIG, config)
        items = read(SERVER/'SPT_Data/database/templates/items.json')
        state['examine'] = next(k for k, v in items.items() if v.get('_type') == 'Item' and v.get('_props', {}).get('ExamineExperience', 0) >= 4 and not v['_props'].get('QuestItem'))
        state['examineXp'] = items[state['examine']]['_props']['ExamineExperience']
        state['listing'] = {}
        for session in [state['root'], state['child']]:
            path = SERVER/'user/profiles'/f'{session}.json'
            data = read(path)
            assert data['info']['username'].startswith('season-test-'), 'Synthetic profiles only'
            pmc = data['characters']['pmc']
            pmc['Info'].update({'Level': 55, 'Experience': 10000000})
            pmc['Quests'] = [q for q in pmc['Quests'] if q['qid'] != QUEST]
            pmc['Quests'].append({'qid': QUEST, 'status': 3, 'startTime': 1, 'statusTimers': {'2': 1, '3': 2}, 'completedConditions': []})
            for p in [pmc, data['characters']['scav']]:
                p['Encyclopedia'].pop(state['examine'], None)
            for t in pmc['TradersInfo'].values():
                t.update({'loyaltyLevel': 4, 'salesSum': 100000000, 'standing': 10, 'unlocked': True})
            for tpl, count in [(RUB, 500000), (RUB, 500000), (TPL, 1)]:
                item_id = secrets.token_hex(12)
                pmc['Inventory']['items'].append({'_id': item_id, '_tpl': tpl,
                    'parentId': pmc['Inventory']['stash'], 'slotId': 'hideout',
                    'location': free_cell(pmc, items),
                    'upd': {'StackObjectsCount': count, 'SpawnedInSession': True}})
                if tpl == TPL: state['listing'][session] = item_id
            save(path, data)
        save(STATE, state)
        print('Prepared synthetic XP and flea fixtures.')
        return
    state = read(STATE)
    root, child = state['root'], state['child']
    if phase == 'restart':
        for session, expected in state['expected'].items():
            p = profile(session)
            check(p['Info']['Experience'] == expected['xp'], 'XP survives restart: '+session)
            check(p['Inventory'] == expected['inventory'], 'Inventory survives restart: '+session)
        snapshot = request('/wtt-campaigns/snapshot', session=child)
        check({XP, FLEA} <= set(snapshot['State']['SeasonalPerks']), 'Both selections survive restart')
        offers = search(child, TPL)
        check(offers and all(map(trader, offers)), 'Trader-only search survives restart')
        report = read(REPORT); report.update({'restartChecks': checks, 'restartPassed': len(checks)}); save(REPORT, report)
        print('Passed', len(checks), 'restart checks.')
        return

    def select(restricted):
        snapshot = request('/wtt-campaigns/snapshot', session=root)
        ids = []
        if restricted:
            ids = [FLEA]
            budget = 10
            for p in snapshot['Catalogue']['personal']:
                if p['points'] < 0 and p['id'] not in snapshot['Unavailable']:
                    ids.append(p['id']); budget += p['points']
                    if budget <= 0: break
        result = request('/wtt-campaigns/edit', {'PerkIds': ids, 'ExpectedRevision': snapshot['State']['Revision']}, root)
        check(not result.get('Error'), 'Save restriction '+str(restricted)+': '+str(result.get('Error')))
        return result

    snapshot = select(False)
    check(XP not in snapshot['Unavailable'] and FLEA not in snapshot['Unavailable'], 'Both perks advertised as implemented')
    check(XP in snapshot['State']['SeasonalPerks'], 'Configured Seasoned PMCs applied')
    request('/wtt-campaigns/switch', {'Mode': 'seasonal'}, root)
    for session, factor in [(root, 1), (child, 1.25)]:
        before = request('/client/game/profile/list', session=session)
        result = event(session, {'Action': 'Examine', 'item': state['examine']})
        check(result['err'] == 0, 'Examination accepted '+str(factor))
        after = request('/client/game/profile/list', session=session)
        check(after[0]['Info']['Experience'] - before[0]['Info']['Experience'] == math.floor(state['examineXp'] * factor), 'Correct PMC examination XP '+str(factor))
        check(after[1]['Info']['Experience'] - before[1]['Info']['Experience'] == state['examineXp'], 'Scav examination XP remains native '+str(factor))
        before_xp = after[0]['Info']['Experience']
        result = event(session, {'Action': 'QuestComplete', 'qid': QUEST})
        check(result['err'] == 0, 'Quest completion accepted '+str(factor)+': '+str(result.get('errmsg')))
        check(profile(session)['Info']['Experience'] - before_xp == int(1700 * factor), 'Correct quest XP '+str(factor))
        stable = profile(session)['Info']['Experience']
        request('/wtt-campaigns/snapshot', session=session)
        check(profile(session)['Info']['Experience'] == stable, 'Snapshot and reload do not multiply existing XP')

    baseline = search(root, TPL)
    bot = next(o for o in baseline if not trader(o) and len(o['requirements']) == 1 and o['requirements'][0]['_tpl'] == RUB)
    trader_offer = next(o for o in baseline if trader(o) and not o.get('locked') and len(o['requirements']) == 1 and o['requirements'][0]['_tpl'] == RUB)
    select(True)
    paged_request = {'page': 0, 'limit': 1, 'sortType': 5, 'sortDirection': 0, 'currency': 0,
        'priceFrom': 0, 'priceTo': 0, 'quantityFrom': 0, 'quantityTo': 0, 'conditionFrom': 0,
        'conditionTo': 100, 'oneHourExpiration': False, 'removeBartering': False,
        'offerOwnerType': 0, 'onlyFunctional': False, 'updateOfferCount': True,
        'handbookId': TPL, 'buildCount': 0, 'buildItems': {}}
    first_page = request('/client/ragfair/find', paged_request, child)
    full = search(child, TPL)
    check(first_page['offersCount'] == len(full) and len(first_page['offers']) == 1, 'Pagination counts only visible trader offers')
    comparison = request('/client/ragfair/find', dict(paged_request, offerOwnerType=1), root)
    check(first_page['categories'] == comparison['categories'], 'Category counts match native trader-only search')
    for options in [{}, {'offerOwnerType': 1}, {'offerOwnerType': 2}, {'buildCount': 1, 'buildItems': {TPL: 1}}, {'linkedSearchId': '5447a9cd4bdc2dbd208b4567'}, {'neededSearchId': TPL}]:
        offers = search(child, TPL, **options)
        check(all(map(trader, offers)), 'Only traders for search '+str(options))
        if not options: check(bool(offers), 'Trader offers remain visible')
    lookup = request('/client/ragfair/offer/findbyid', {'id': bot['intId']}, child)
    check(lookup is None or trader(lookup), 'Internal-id lookup cannot return a non-trader offer')
    with concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool:
        sessions = [root, child] * 6
        results = list(pool.map(lambda s: search(s, TPL), sessions))
    check(all(all(map(trader, offers)) if s == child else any(not trader(o) for o in offers) for s, offers in zip(sessions, results)), 'Concurrent seasonal/normal searches stay isolated')

    def payment(offer, session):
        amount = math.ceil(offer['requirements'][0]['count'])
        stack = next(i for i in profile(session)['Inventory']['items'] if i['_tpl'] == RUB and i.get('upd', {}).get('StackObjectsCount', 0) >= amount)
        return {'id': offer['_id'], 'count': 1, 'items': [{'id': stack['_id'], 'count': amount}]}
    for offers in [[bot], [trader_offer, bot]]:
        before = profile(child)
        result = event(child, {'Action': 'RagFairBuyOffer', 'offers': [payment(o, child) for o in offers]})
        check(result['err'] != 0 and 'No Flea Market' in result.get('errmsg', ''), 'Cached/mixed non-trader purchase rejected')
        check(profile(child)['Inventory'] == before['Inventory'], 'Rejected basket leaves all money/items intact')
        current = {o['_id']: o for o in search(root, TPL)}
        check(all(current[o['_id']]['items'][0]['upd']['StackObjectsCount'] == o['items'][0]['upd']['StackObjectsCount'] for o in offers), 'Rejected basket leaves trader and dynamic stock intact')
    before = profile(child)
    result = event(child, {'Action': 'RagFairBuyOffer', 'offers': [payment(trader_offer, child)]})
    check(result['err'] == 0, 'Trader-only purchase succeeds: '+str(result.get('errmsg')))
    check(total(before, RUB) - total(profile(child), RUB) == math.ceil(trader_offer['requirements'][0]['count']), 'Trader purchase charges exact native price')

    def listing(session):
        return {'Action': 'RagFairAddOffer', 'items': [state['listing'][session]], 'requirements': [{'_tpl': RUB, 'count': 5000}], 'sellInOnePiece': False}
    before = profile(child)
    for action in [listing(child), {'Action': 'RagFairRenewOffer', 'offerId': bot['_id'], 'renewalTime': 1}]:
        result = event(child, action)
        check(result['err'] != 0 and 'No Flea Market' in result.get('errmsg', ''), 'Listing/extension rejected before charging fees')
        after = profile(child)
        check(after['Inventory'] == before['Inventory'] and after['RagfairInfo'] == before['RagfairInfo'], 'Rejected listing/extension changes no inventory or profile offers')
    normal = request('/client/game/profile/list', session=root)
    select(False)
    check(any(not trader(o) for o in search(child, TPL)), 'Removing perk restores non-trader offers')
    result = event(child, {'Action': 'RagFairBuyOffer', 'offers': [payment(bot, child)]})
    check(result['err'] == 0, 'Removing perk restores dynamic purchases: '+str(result.get('errmsg')))
    result = event(child, listing(child))
    check(result['err'] == 0, 'Removing perk restores listings: '+str(result.get('errmsg')))
    after_normal = request('/client/game/profile/list', session=root)
    check([p['Inventory'] for p in after_normal] == [p['Inventory'] for p in normal], 'Seasonal transactions leave normal PMC/Scav inventories unchanged')
    select(True)
    state['expected'] = {s: {'xp': profile(s)['Info']['Experience'], 'inventory': profile(s)['Inventory']} for s in [root, child]}
    for s in [root, child]: request('/client/game/logout', session=s)
    save(STATE, state); save(REPORT, {'checks': checks, 'passed': len(checks)})
    print('Passed', len(checks), 'XP/flea checks.')

if __name__ == '__main__': main()
