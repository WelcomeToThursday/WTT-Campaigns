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
        if spec['Tier']:
            # Every native condition must survive unchanged, including branch
            # statuses, time delays and fields the importer does not interpret.
            native = beta[qid]['conditions']['AvailableForStart']
            assert spec['Start'][:len(native)] == native, qid + ' lost native start requirements'
        else:
            assert spec['UseBetaStart'] and not spec['Start'], qid + ' must use native essential requirements'
            assert q['conditions']['AvailableForStart'] == beta[qid]['conditions']['AvailableForStart']
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
    def satisfied(c, level=79):
        target = c.get('target', [])
        targets = target if isinstance(target, list) else [target]
        kind = c['conditionType']
        if kind == 'Level': return c.get('compareMethod') in ('>=', '>') and c.get('value', 0) <= level
        if kind == 'Quest': return any(t in done for t in targets)
        if kind in ('TraderStanding', 'TraderLoyalty'):
            value = standing.get(targets[0], 0) if kind == 'TraderStanding' else loyalty(targets[0])
            return {'>=': value + 1e-8 >= c.get('value', 0), '>': value > c.get('value', 0),
                    '<': value < c.get('value', 0), '<=': value <= c.get('value', 0),
                    '=': abs(value-c.get('value', 0)) < 1e-8}.get(c.get('compareMethod'), False)
        return False

    # Fresh Standard and EoD-like standings must not bypass the native chain.
    # This only evaluates in-memory database copies; no character is created.
    for initial in (0.0, 0.2):
        standing = {tid: initial for tid in expected['Traders']}
        fresh = {qid for qid, q in overlaid.items()
                 if all(satisfied(c, level=1) for c in q['conditions']['AvailableForStart'])}
        assert '657315df034d76585f032e01' in fresh, 'Shooting Cans must remain a starter'
        assert fresh.isdisjoint({'5936d90786f7742b1420ba5b', '5936da9e86f7742d65037edf',
                                 '59674cd986f7744ab26e32f2', '5fd9fad9c1ce6b1a3b486d00',
                                 '59c512ad86f7741f0d09de9b'}), 'Fresh PMC bypassed a quest chain'
        done.add('657315df034d76585f032e01')
        assert all(satisfied(c, level=1) for c in overlaid['5936d90786f7742b1420ba5b']['conditions']['AvailableForStart']), 'Debut must unlock after Shooting Cans'
        done.add('5936d90786f7742b1420ba5b')
        search = overlaid['5fd9fad9c1ce6b1a3b486d00']['conditions']['AvailableForStart']
        assert not all(satisfied(c, level=4) for c in search), 'Search Mission must retain its level gate'
        assert all(satisfied(c, level=5) for c in search), 'Search Mission must unlock at level 5 after Debut'
        done.clear()
    standing = {tid: 0.0 for tid in expected['Traders']}

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
