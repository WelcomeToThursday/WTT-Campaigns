"""Trader pricing routes and persistence on synthetic accounts in isolated port 6975."""
import argparse, concurrent.futures, copy, json, math, secrets
from decimal import Decimal
from test_integration import PROJECT, SERVER, request, check, checks

STATE = PROJECT/'Testing/trader-price-state.json'
REPORT = PROJECT/'Research/trader-price-results.json'
THERAPIST = '54cb57776803fa99248b456e'
PRAPOR = '54cb50c76803fa8b248b4571'
PEACEKEEPER = '5935c25fb3acc3127c3d8cd9'
SKIER = '58330581ace78e27b8b10cee'
VACUUM = '69c3d43030f896ebef0ed357'
THIRD = '69ce62886e199f4bbe0ab19b'
RUB = '5449016a4bdc2d6f028b456f'
USD = '5696686a4bdc2da3298b456a'
EUR = '569668774bdc2da2298b4568'
MONEY = [RUB, USD, EUR]

def read(path): return json.loads(path.read_text(encoding='utf-8'))
def save(path, data): path.write_text(json.dumps(data, indent=2), encoding='utf-8')
def profile(session): return request('/client/game/profile/list', session=session)[0]
def assort(session, trader): return request('/client/trading/api/getTraderAssort/'+trader, session=session)
def total(pmc, tpl): return sum(i.get('upd', {}).get('StackObjectsCount', 1) for i in pmc['Inventory']['items'] if i['_tpl'] == tpl)
def scale(value, multiplier): return float(Decimal(str(value)) * Decimal(str(multiplier)))
def search(session, tpl, **options):
    payload = {'page': 0, 'limit': 100, 'sortType': 5, 'sortDirection': 0, 'currency': 0,
               'priceFrom': 0, 'priceTo': 0, 'quantityFrom': 0, 'quantityTo': 0,
               'conditionFrom': 0, 'conditionTo': 100, 'oneHourExpiration': False,
               'removeBartering': False, 'offerOwnerType': 0, 'onlyFunctional': False,
               'updateOfferCount': False, 'handbookId': tpl, 'buildCount': 0, 'buildItems': {}}
    payload.update(options)
    return request('/client/ragfair/find', payload, session)['offers']

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('phase', choices=['prepare', 'verify', 'restart'])
    phase = parser.parse_args().phase
    if phase == 'prepare':
        assert not (SERVER/'test-server.pid').exists(), 'Stop isolated server first'
        previous = read(PROJECT/'Testing/restart-state.json')
        state = {k: previous[k] for k in ['root', 'child']}
        db = read(SERVER/f'SPT_Data/database/traders/{THERAPIST}/assort.json')
        barter_id, variant = next((k, v[0]) for k, v in db['barter_scheme'].items()
            if db['loyal_level_items'][k] == 1 and v and v[0] and all(r['_tpl'] not in MONEY and r['count'] <= 5 and not r.get('level') and not r.get('side') and not r.get('onlyFunctional') for r in v[0]))
        state['barter'] = barter_id
        for session in [state['root'], state['child']]:
            path = SERVER/'user/profiles'/f'{session}.json'
            data = read(path)
            assert data['info']['username'].startswith('season-test-'), 'Synthetic accounts only'
            pmc = data['characters']['pmc']
            pmc['Info'].update({'Level': 55, 'Experience': 10000000})
            for trader in pmc['TradersInfo'].values():
                trader.update({'loyaltyLevel': 4, 'salesSum': 100000000, 'standing': 10, 'unlocked': True})
            specs = [(RUB, 500000), (RUB, 500000), (USD, 10000), (EUR, 10000)]
            specs += [(r['_tpl'], 1) for r in variant for _ in range(20)]
            for index, (tpl, count) in enumerate(specs):
                pmc['Inventory']['items'].append({'_id': secrets.token_hex(12), '_tpl': tpl,
                    'parentId': pmc['Inventory']['stash'], 'slotId': 'hideout',
                    'location': {'x': index % 8, 'y': 35 + index // 8, 'r': 0},
                    'upd': {'StackObjectsCount': count}})
            save(path, data)
        save(STATE, state)
        print('Prepared synthetic trader fixtures.')
        return
    state = read(STATE)
    root, child = state['root'], state['child']
    if phase == 'restart':
        for session, expected in state['expected'].items():
            pmc = profile(session)
            check(pmc['Inventory'] == expected, 'Trader inventory and payment survive restart: '+('seasonal' if session == child else 'normal'))
        snapshot = request('/seasonal-perks/snapshot', session=child)
        check(VACUUM in snapshot['State']['SeasonalPerks'] and THIRD not in snapshot['State']['SeasonalPerks'], 'Saved trader selection survives restart')
        prices = assort(child, THERAPIST)['barter_scheme']
        check(prices[state['offer']][0][0]['count'] == state['unitPrice'], 'Seasonal price survives restart without compounding')
        report = read(REPORT); report.update({'restartPassed': len(checks), 'restartChecks': checks}); save(REPORT, report)
        print('Passed', len(checks), 'trader restart checks.')
        return

    def select(ids):
        snapshot = request('/seasonal-perks/snapshot', session=root)
        result = request('/seasonal-perks/edit', {'PerkIds': ids, 'ExpectedRevision': snapshot['State']['Revision']}, root)
        check(not result.get('Error'), 'Save trader selection '+str(ids))
        return result
    snapshot = select([])
    check(VACUUM not in snapshot['Unavailable'] and THIRD not in snapshot['Unavailable'], 'Both trader perks selectable')
    request('/seasonal-perks/switch', {'Mode': 'seasonal'}, root)
    traders = [THERAPIST, PRAPOR, PEACEKEEPER, SKIER, '579dc571d53a0658a154fbec', '5ac3b934156ae10c4430e83c', '5a7c2eca46aef81a7ca2145d', '5c0647fdd443bc2504c2d371']
    baseline = {trader: assort(child, trader) for trader in traders}
    normal_prices = {trader: assort(root, trader)['barter_scheme'] for trader in traders}
    normal_before = profile(root)['Inventory']
    for ids, therapist_factor, other_factor in [([VACUUM], 1.2, 1.2), ([THIRD], 0.95, 1)]:
        select(ids)
        for trader in traders:
            actual = assort(child, trader)['barter_scheme']
            factor = therapist_factor if trader == THERAPIST else other_factor
            check(all(math.isclose(r['count'], scale(baseline[trader]['barter_scheme'][k][vi][ri]['count'], factor), abs_tol=1e-8)
                for k, variants in actual.items() for vi, variant in enumerate(variants) for ri, r in enumerate(variant)), 'All monetary/barter requirements scale for '+trader+' '+str(ids))
            check(assort(child, trader)['barter_scheme'] == actual, 'Repeated assortment request does not compound '+trader)
            check(assort(root, trader)['barter_scheme'] == normal_prices[trader], 'Normal assortment unchanged '+trader)

    def choose(trader, currency):
        a = assort(child, trader)
        items = {i['_id']: i for i in a['items']}
        return next((k, v[0][0], items[k]['_tpl']) for k, v in a['barter_scheme'].items()
            if k in items and len(v[0]) == 1 and v[0][0]['_tpl'] == currency and items[k]['_tpl'] not in MONEY
            and 0 < v[0][0]['count'] < (10000 if currency == RUB else 300)
            and items[k].get('upd', {}).get('StackObjectsCount', 0) > 20
            and items[k].get('upd', {}).get('BuyRestrictionMax', 1000) >= 5)

    def payment(pmc, requirements, quantity):
        items = []
        for r in requirements:
            remaining = math.ceil(r['count'] * quantity)
            for i in pmc['Inventory']['items']:
                if i['_tpl'] != r['_tpl']: continue
                count = min(remaining, i.get('upd', {}).get('StackObjectsCount', 1))
                if count: items.append({'id': i['_id'], 'count': count}); remaining -= count
                if not remaining: break
            assert not remaining, 'Fixture has enough payment items'
        return items

    def buy(trader, offer_id, quantity=1, change=0, flea_offer=None):
        before = profile(child)
        requirements = assort(child, trader)['barter_scheme'][offer_id][0]
        payments = payment(before, requirements, quantity)
        payments[0]['count'] += change
        event = {'Action': 'TradingConfirm', 'type': 'buy_from_trader', 'tid': trader,
                 'item_id': offer_id, 'count': quantity, 'scheme_id': 0, 'scheme_items': payments}
        if flea_offer:
            event = {'Action': 'RagFairBuyOffer', 'offers': [{'id': flea_offer['_id'], 'count': quantity, 'items': payments}]}
        stock_before = next(i for i in assort(root, trader)['items'] if i['_id'] == offer_id)['upd']['StackObjectsCount']
        result = request('/client/game/profile/items/moving', {'data': [event]}, child, allow_error=True)
        after = profile(child)
        if change:
            check(result['err'] != 0, 'Stale '+('flea' if flea_offer else 'shop')+' price rejected '+str(change))
            check(after['Inventory'] == before['Inventory'], 'Rejected payment leaves inventory unchanged')
            stock_after = next(i for i in assort(root, trader)['items'] if i['_id'] == offer_id)['upd']['StackObjectsCount']
            check(stock_after == stock_before, 'Rejected payment leaves shared stock unchanged')
        else:
            check(result['err'] == 0, 'Purchase accepted '+trader+' '+str(result.get('errmsg')))
            for r in requirements:
                check(total(before, r['_tpl']) - total(after, r['_tpl']) == math.ceil(r['count'] * quantity), 'Exact scaled payment '+r['_tpl'])
        return after

    discount_offer, _, _ = choose(THERAPIST, RUB)
    buy(THERAPIST, discount_offer)
    snapshot = request('/seasonal-perks/snapshot', session=root)
    conflict = request('/seasonal-perks/edit', {'PerkIds': [VACUUM, THIRD], 'ExpectedRevision': snapshot['State']['Revision']}, root)
    check(bool(conflict.get('Error')), 'Captured mutual exclusion between both trader perks is enforced')
    select([VACUUM])
    offer, requirement, tpl = choose(THERAPIST, RUB)
    state['offer'] = offer; state['unitPrice'] = requirement['count']
    def reject_payment(label, selected_offer, count, payments):
        before = profile(child)['Inventory']
        stock = next(i for i in assort(root, THERAPIST)['items'] if i['_id'] == selected_offer)['upd']['StackObjectsCount']
        event = {'Action': 'TradingConfirm', 'type': 'buy_from_trader', 'tid': THERAPIST,
                 'item_id': selected_offer, 'count': count, 'scheme_id': 0, 'scheme_items': payments}
        response = request('/client/game/profile/items/moving', {'data': [event]}, child, allow_error=True)
        check(response['err'] != 0, label+' rejected')
        check(profile(child)['Inventory'] == before, label+' leaves inventory unchanged')
        check(next(i for i in assort(root, THERAPIST)['items'] if i['_id'] == selected_offer)['upd']['StackObjectsCount'] == stock, label+' leaves stock unchanged')
    reject_payment('Missing payment', offer, 1, [])
    reject_payment('Insufficient funds', offer, 1000000, [{'id': RUB, 'count': math.ceil(requirement['count'] * 1000000)}])
    barter_requirement = assort(child, THERAPIST)['barter_scheme'][state['barter']][0][0]
    single_stack = next(i for i in profile(child)['Inventory']['items'] if i['_tpl'] == barter_requirement['_tpl'])
    reject_payment('Repeated barter stack overdraft', state['barter'], 2, [{'id': single_stack['_id'], 'count': 1}] * math.ceil(barter_requirement['count'] * 2))
    buy(THERAPIST, offer, change=-1)
    buy(THERAPIST, offer, change=1)
    buy(THERAPIST, offer, quantity=3)
    for trader, currency in [(PEACEKEEPER, USD), (SKIER, EUR)]:
        money_offer, _, _ = choose(trader, currency)
        buy(trader, money_offer)
    buy(THERAPIST, state['barter'], quantity=2)

    normal_flea = search(root, tpl)
    seasonal_flea = search(child, tpl)
    original = {o['_id']: o for o in normal_flea}
    affected = [o for o in seasonal_flea if o['user']['id'] == THERAPIST and o['root'] == offer]
    check(bool(affected), 'Therapist offer appears on flea')
    flea = affected[0]; base = original[flea['_id']]
    check(flea['requirements'][0]['count'] == requirement['count'], 'Flea and shop quote identical seasonal price')
    check(math.isclose(flea['requirementsCost'], scale(base['requirementsCost'], 1.2)), 'Flea sorting cost includes seasonal multiplier')
    bots = [o for o in seasonal_flea if o['user'].get('memberType') != 4 and o['_id'] in original]
    check(bool(bots) and all(o['requirements'] == original[o['_id']]['requirements'] for o in bots), 'Non-trader flea offers stay unchanged')
    lookup = request('/client/ragfair/offer/findbyid', {'id': flea['intId']}, child)
    check(lookup['requirements'] == flea['requirements'], 'Direct flea lookup uses same price')
    midpoint = int((base['requirementsCost'] + flea['requirementsCost']) / 2)
    check(flea['_id'] not in {o['_id'] for o in search(child, tpl, priceTo=midpoint)}, 'Flea upper price filter uses seasonal price before filtering')
    check(flea['_id'] in {o['_id'] for o in search(root, tpl, priceTo=midpoint)}, 'Normal price filter keeps cheaper normal offer')
    build = search(child, tpl, buildCount=1, buildItems={tpl: 1}, offerOwnerType=1)
    check(any(o['user']['id'] == THERAPIST and o['requirements'][0]['count'] == requirement['count'] for o in build), 'Weapon-build offer path uses seasonal trader prices')
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        results = list(pool.map(lambda session: search(session, tpl), [root, child, root, child]))
    check(all(next(o for o in offers if o['_id'] == flea['_id'])['requirements'][0]['count'] == (base['requirements'][0]['count'] if index % 2 == 0 else requirement['count']) for index, offers in enumerate(results)), 'Concurrent normal and seasonal flea searches remain isolated')
    buy(THERAPIST, offer, change=-1, flea_offer=flea)
    buy(THERAPIST, offer, flea_offer=flea)
    check(profile(root)['Inventory'] == normal_before, 'All seasonal purchases leave normal inventory unchanged')
    select([])
    check(assort(child, THERAPIST)['barter_scheme'] == baseline[THERAPIST]['barter_scheme'], 'Removing perks restores original shop requirements')
    removed = next(o for o in search(child, tpl) if o['_id'] == flea['_id'])
    check(removed['requirements'] == base['requirements'], 'Removing perks restores original flea price')
    select([VACUUM])
    state['expected'] = {}
    for session in [root, child]:
        state['expected'][session] = profile(session)['Inventory']
        request('/client/game/logout', session=session)
    save(STATE, state); save(REPORT, {'passed': len(checks), 'checks': checks})
    print('Passed', len(checks), 'trader integration checks.')

if __name__ == '__main__': main()
