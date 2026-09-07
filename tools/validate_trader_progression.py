"""Validate capture provenance and estimate progression reachability without importing missing tasks."""
from copy import deepcopy
from collections import Counter
from import_trader_progression import PROJECT, read, write, latest, compile_data

DATABASE = PROJECT.parents[1] / 'SPT_Runtime/SPT_Data/database'
DUMP = PROJECT.parent / '1.0 Dump/gw-pve.escapefromtarkov.com/client'


def main():
    beta = read(DATABASE / 'templates/quests.json')
    live = read(latest(DUMP, 'quest/list'))
    trader_ids = {p.parent.name for p in (DATABASE / 'traders').glob('*/base.json')}
    expected, audit = compile_data(live, read(latest(DUMP, 'trading/api/traderSettings')), beta, trader_ids)
    packaged = read(PROJECT / 'data/trader-progression.json')
    assert {k: v for k,v in packaged.items() if k != 'Sources'} == expected, 'Packaged overlay must reproduce exactly'
    assert read(PROJECT / 'data/trader-progression-audit.json')['Adaptations'] == audit['Adaptations']
    overlaid = deepcopy(beta)
    for qid, spec in expected['Quests'].items():
        q = overlaid[qid]
        q['traderId'] = spec['TraderId']
        if not spec['UseBetaStart']:
            q['conditions']['AvailableForStart'] = spec['Start']
        for stage in q['rewards'].keys() | spec['Reputation'].keys():
            q['rewards'][stage] = [r for r in q['rewards'].get(stage, []) if r['type'] != 'TraderStanding'] + spec['Reputation'].get(stage, [])
        assert q['conditions']['AvailableForFinish'] == beta[qid]['conditions']['AvailableForFinish']
        assert q['conditions'].get('Fail') == beta[qid]['conditions'].get('Fail')
        for stage in q['rewards']:
            assert [r for r in q['rewards'][stage] if r['type'] != 'TraderStanding'] == [r for r in beta[qid]['rewards'].get(stage, []) if r['type'] != 'TraderStanding']
    assert overlaid.keys() == beta.keys()
    assert all(overlaid[i] == beta[i] for i in beta.keys() - expected['Quests'].keys())

    # Optimistic reachability: unlimited player level, all traders unlocked, objectives completable,
    # no optional negative rep, no time delays, and either terminal state allowed for branching chains.
    # A shortfall even under these assumptions is meaningful; success is not a playthrough guarantee.
    standing = {tid: 0.0 for tid in expected['Traders']}
    done = set()
    def loyalty(tid):
        return max([1] + [i+1 for i,t in enumerate(expected['Traders'].get(tid, [])) if standing.get(tid, 0) + 1e-8 >= t['Standing']])
    def satisfied(c):
        target = c.get('target', [])
        targets = target if isinstance(target, list) else [target]
        kind = c['conditionType']
        if kind == 'Level': return c.get('compareMethod') in ('>=', '>') and c.get('value', 0) <= 79
        if kind == 'Quest': return any(t in done for t in targets)
        if kind in ('TraderStanding', 'TraderLoyalty'):
            value = standing.get(targets[0], 0) if kind == 'TraderStanding' else loyalty(targets[0])
            return {'>=': value + 1e-8 >= c.get('value', 0), '>': value > c.get('value', 0),
                    '<': value < c.get('value', 0), '<=': value <= c.get('value', 0),
                    '=': abs(value-c.get('value', 0)) < 1e-8}.get(c.get('compareMethod'), False)
        return False
    while True:
        unlocked = [i for i,q in overlaid.items() if i not in done and all(satisfied(c) for c in q['conditions']['AvailableForStart'])]
        if not unlocked: break
        for qid in unlocked:
            done.add(qid)
            for stage in ('Started', 'Success'):
                for reward in overlaid[qid]['rewards'].get(stage, []):
                    if reward['type'] == 'TraderStanding' and reward['target'] in standing:
                        standing[reward['target']] += max(0, float(reward['value']))
    locale = read(DATABASE / 'locales/global/en.json')
    report = dict(Assumptions='Optimistic static bound: level 79, zero initial reputation, all traders unlocked, no repeatables, '
        'objectives assumed completable, branch prerequisites treated as terminal, optional penalties ignored. Not a playthrough guarantee.',
        InstalledTasks=len(beta), CapturedTasks=len(live), AppliedTasks=len(expected['Quests']),
        SkippedLiveTasks=len(audit['SkippedQuests']), OptimisticallyReachableTasks=len(done), Traders=[])
    for tid, thresholds in expected['Traders'].items():
        missing_rep = sum(max(0, float(r['value'])) for q in live if q['_id'] not in beta
                          for r in q['rewards'].get('Success', []) if r['type'] == 'TraderStanding' and r['target'] == tid)
        report['Traders'].append(dict(Id=tid, Name=locale.get(tid + ' Nickname', tid), HighestLoyalty=loyalty(tid),
            MaximumLoyalty=len(thresholds), OptimisticStanding=round(standing[tid], 4),
            MaximumRequired=thresholds[-1]['Standing'], MissingLiveSuccessReputation=round(missing_rep, 4),
            Shortfall=round(max(0, thresholds[-1]['Standing'] - standing[tid]), 4)))
    write(PROJECT / 'data/trader-progression-reachability.json', report)
    print('Capture and invariant validation passed for', len(expected['Quests']), 'tasks.')
    print('Optimistic reachability shortfalls:', [(t['Name'],t['Shortfall']) for t in report['Traders'] if t['Shortfall']])


if __name__ == '__main__':
    main()
