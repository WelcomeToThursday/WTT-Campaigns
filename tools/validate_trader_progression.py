"""Read-only reproduction and native quest invariants; never creates a server or profile."""
from copy import deepcopy
from import_trader_progression import PROJECT, read, compile_package
from balance_quest_reputation import overlay_quests


def main():
    database = PROJECT.parents[1] / 'SPT_Runtime/SPT_Data/database'
    output, audit, report = compile_package(PROJECT.parent / '1.0 Dump', database)
    assert read(PROJECT / 'data/trader-progression.json') == output
    assert read(PROJECT / 'data/trader-progression-audit.json') == audit
    assert read(PROJECT / 'data/trader-progression-reachability.json') == report
    native = read(database / 'templates/quests.json')
    source = deepcopy(native)
    for entry in read(PROJECT / 'data/quest-backports.json')['Quests']:
        source[entry['Quest']['_id']] = entry['Quest']
    overlaid = overlay_quests(source, output)
    for qid, q in source.items():
        actual = overlaid[qid]
        assert q['conditions']['AvailableForFinish'] == actual['conditions']['AvailableForFinish']
        assert q['conditions'].get('Fail') == actual['conditions'].get('Fail')
        original = q['conditions']['AvailableForStart']
        assert actual['conditions']['AvailableForStart'][:len(original)] == original
        for stage in q['rewards']:
            assert [r for r in q['rewards'][stage] if r['type'] != 'TraderStanding'] == [r for r in actual['rewards'][stage] if r['type'] != 'TraderStanding']
    assert set(overlaid) == set(source)
    for route in report['After']:
        seen = set()
        for step in route['Steps']:
            key = (step['QuestId'], step['Stage'])
            assert key not in seen, 'Repeated quest reward stage'
            seen.add(key)
        assert all(t['Loyalty'] == 4 for t in route['Traders'].values())
    print('Reproduced progression, rewards and separate USEC/BEAR routes; native conditions and non-reputation rewards preserved.')


if __name__ == '__main__':
    main()
