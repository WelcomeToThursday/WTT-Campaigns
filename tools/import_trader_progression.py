"""Import progression only from captured PvE responses; never import quest definitions."""
import argparse
import copy
import hashlib
import json
from pathlib import Path

PROJECT = Path(__file__).resolve().parents[1]
SUPPORTED = {'Quest', 'Level', 'TraderLoyalty', 'TraderStanding'}
CONDITION_FIELDS = {'id', 'index', 'conditionType', 'target', 'value', 'compareMethod',
                    'status', 'availableAfter', 'dispersion'}


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')


def latest(root, route):
    return max((root / route / 'response').glob('*.json'), key=lambda p: p.name)


def compile_data(captured, traders, beta, beta_traders):
    output = {'Version': 1, 'Traders': {}, 'Quests': {}}
    audit = {'SkippedQuests': [], 'SkippedTraders': [], 'Adaptations': []}
    for trader in traders:
        tid = trader['_id']
        if tid not in beta_traders:
            audit['SkippedTraders'].append(tid)
            continue
        output['Traders'][tid] = [dict(Level=l['minLevel'], Standing=l['minStanding'])
                                  for l in trader['loyaltyLevels']]
    for quest in captured:
        qid, tid = quest['_id'], quest['traderId']
        if qid not in beta or tid not in beta_traders:
            audit['SkippedQuests'].append(qid)
            continue
        tier = int(quest.get('tierAccessory', 0))
        if beta[qid]['traderId'] != tid:
            audit['Adaptations'].append(dict(QuestId=qid, Action='use captured trader assignment',
                PreviousTrader=beta[qid]['traderId'], TraderId=tid))
        if tier not in range(5):
            continue
        start, reasons = [], []
        for condition in quest['conditions']['AvailableForStart']:
            kind = condition['conditionType']
            target = condition.get('target', [])
            targets = target if isinstance(target, list) else [target]
            missing = kind == 'Quest' and any(t not in beta for t in targets)
            if kind not in SUPPORTED or missing:
                reasons.append({'Condition': condition['id'], 'Type': kind,
                                'Reason': 'missing prerequisite' if missing else 'unsupported live condition'})
                continue
            normalized = {k: copy.deepcopy(v) for k, v in condition.items() if k in CONDITION_FIELDS}
            normalized.update(dynamicLocale=False, visibilityConditions=[], parentId='', globalQuestCounterId='')
            start.append(normalized)
        use_beta = tier == 0 and bool(reasons)
        if tier and not any(c['conditionType'] == 'TraderLoyalty' and c.get('target') == tid
                            and c.get('compareMethod') == '>=' and c.get('value', 0) >= tier for c in start):
            # A display tier is also the minimum unlock tier in this compatibility backport.
            start.append({'id': hashlib.sha256(('wtt-progression:' + qid).encode()).hexdigest()[:24],
                          'conditionType': 'TraderLoyalty', 'target': tid, 'value': tier,
                          'compareMethod': '>=', 'dynamicLocale': False, 'visibilityConditions': [],
                          'parentId': '', 'globalQuestCounterId': '', 'index': len(start)})
        rewards = {}
        for stage, entries in quest['rewards'].items():
            rewards[stage] = [dict(id=r['id'], type='TraderStanding', target=r['target'], value=r['value'])
                              for r in entries if r['type'] == 'TraderStanding' and r['target'] in beta_traders]
        output['Quests'][qid] = dict(TraderId=tid, Tier=tier, UseBetaStart=use_beta,
                                     Start=[] if use_beta else start, Reputation=rewards)
        if reasons:
            audit['Adaptations'].append(dict(QuestId=qid, Tier=tier,
                Action='retain beta start requirements' if use_beta else 'use loyalty tier for unsupported gates',
                Conditions=reasons))
    audit['Counts'] = dict(Captured=len(captured), Beta=len(beta), Applied=len(output['Quests']),
                           Tiered=sum(q['Tier'] > 0 for q in output['Quests'].values()))
    return output, audit


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dump', type=Path, default=PROJECT.parent / '1.0 Dump')
    parser.add_argument('--database', type=Path, default=PROJECT.parents[1] / 'SPT_Runtime/SPT_Data/database')
    parser.add_argument('--output', type=Path, default=PROJECT / 'data/trader-progression.json')
    parser.add_argument('--audit', type=Path, default=PROJECT / 'data/trader-progression-audit.json')
    args = parser.parse_args()
    root = args.dump / 'gw-pve.escapefromtarkov.com/client'
    quests = latest(root, 'quest/list')
    traders = latest(root, 'trading/api/traderSettings')
    beta_path = args.database / 'templates/quests.json'
    beta = read(beta_path)
    beta_traders = {p.parent.name for p in (args.database / 'traders').glob('*/base.json')}
    output, audit = compile_data(read(quests), read(traders), beta, beta_traders)
    output['Sources'] = [{'File': p.name, 'Sha256': hashlib.sha256(p.read_bytes()).hexdigest()}
                         for p in (quests, traders, beta_path)]
    audit['Sources'] = output['Sources']
    write(args.output, output)
    write(args.audit, audit)
    print(json.dumps(audit['Counts']))


if __name__ == '__main__':
    main()
