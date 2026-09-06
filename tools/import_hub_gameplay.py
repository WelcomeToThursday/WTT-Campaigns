"""Recover public gameplay definitions only; never read profiles, headers, or request bodies."""
import argparse
import hashlib
import json
from pathlib import Path
from import_captures import ROOT, read, save


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--dump', type=Path, required=True)
    args = parser.parse_args()
    seasonal = args.dump / 'gw-pvp-season.escapefromtarkov.com'
    pve = args.dump / 'gw-pve.escapefromtarkov.com'
    sources = []

    def response(host, endpoint):
        path = sorted((host / endpoint / 'response').glob('*.json'))[-1]
        sources.append({'file': path.relative_to(args.dump).as_posix(), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()})
        return read(path)

    battle = response(seasonal, 'client/battle-pass/active')['battlePasses'][0]
    season = response(seasonal, 'client/season/active')['season']
    quests = {q['_id']: q for q in response(pve, 'client/quest/list')}
    quests.update({q['_id']: q for q in response(seasonal, 'client/quest/list')})
    tiles = [r for p in battle['pages'] for r in p['rewards']] + season['seasonalRewards']
    seen = set()

    def nodes(value):
        if isinstance(value, dict):
            yield value
            for v in value.values():
                yield from nodes(v)
        elif isinstance(value, list):
            for v in value:
                yield from nodes(v)

    def visit(qid):
        if qid in seen:
            return
        seen.add(qid)
        for c in nodes(quests.get(qid, {}).get('conditions', {})):
            if c.get('conditionType') == 'Quest':
                for target in [c['target']] if isinstance(c['target'], str) else c['target']:
                    visit(target)

    for tile in tiles:
        for c in tile.get('conditions', []):
            if c['conditionType'] == 'Quest':
                visit(c['target'])
    recovered = []
    for qid in sorted(seen & quests.keys()):
        q = dict(quests[qid])
        q.pop('status', None)
        recovered.append(q)

    offers = []
    unlocks = [r for t in tiles for r in t['rewards'] if r['type'] == 'AssortmentUnlock']
    for trader in sorted({r['traderId'] for r in unlocks}):
        assort = response(seasonal, 'client/trading/api/getTraderAssort/' + trader)
        for reward in [r for r in unlocks if r['traderId'] == trader]:
            tpl = next(i['_tpl'] for i in reward['items'] if i['_id'] == reward['target'])
            roots = [i for i in assort['items'] if i['_tpl'] == tpl and i['_id'] in assort['barter_scheme']]
            if len(roots) != 1:
                continue
            root = roots[0]
            scheme = assort['barter_scheme'][root['_id']]
            # Currency offers must use installed pricing; captured prices can contain account discounts.
            if any(c['_tpl'] in ['5449016a4bdc2d6f028b456f', '5696686a4bdc2da3298b456a', '569668774bdc2da2298b4568'] for row in scheme for c in row):
                continue
            offers.append({'Target': reward['target'], 'TraderId': trader, 'Barter': scheme, 'Loyalty': reward['loyaltyLevel']})
    locations = response(seasonal, 'client/locations')['locations']
    doc_ids = {d['itemId'] for d in battle['documents']}
    caps = {v['Id']: [r for r in v.get('maxItemCountInLocation', []) if r['TemplateId'] in doc_ids] for v in locations.values()}
    data = {'Id': battle['id'], 'SeasonId': season['id'], 'ExchangeRate': battle['exchangeRate'],
            'ItemExchange': battle['itemExchangeSettings'], 'Documents': battle['documents'],
            'Rewards': {t['id']: {'Grants': t['rewards'], 'Conditions': t.get('conditions', [])} for t in tiles},
            'Offers': offers, 'CapturedMapCaps': {k: v for k, v in caps.items() if v}}
    assert len(tiles) == 58 and sum(len(t['rewards']) for t in tiles[:53]) == 58
    assert sum(sum(t.get('cost', {}).values()) for t in tiles) == 501
    save(ROOT / 'data/hub-gameplay.json', data)
    save(ROOT / 'data/hub-quests.json', recovered)
    save(ROOT / 'data/hub-gameplay-provenance.json', {'sources': sources, 'missingQuests': sorted(seen - quests.keys())})
    print('Imported 58 tiles, 63 grants,', len(recovered), 'quest definitions and', len(offers), 'undiscounted barter fallbacks.')


if __name__ == '__main__':
    main()
