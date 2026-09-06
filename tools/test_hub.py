"""Offline catalogue and isolated-server read-only hub checks."""
import hashlib
import json
from pathlib import Path

from test_integration import PROJECT, SERVER, request


def main():
    expected = json.loads((PROJECT / 'data/hub.json').read_text(encoding='utf-8'))
    account = json.loads((PROJECT / 'Testing/restart-state.json').read_text())
    request('/seasonal-perks/switch', {'Mode': 'seasonal'}, account['root'])
    paths = list((SERVER / 'user/profiles').glob('*.json'))
    before = {p: hashlib.sha256(p.read_bytes()).hexdigest() for p in paths}
    results = []
    def check(value, description):
        assert value, description
        results.append(description)
    data = request('/seasonal-perks/hub', session=account['child'])
    check([r['Id'] for p in data['Pages'] for r in p['Rewards']] == [r['Id'] for p in expected['Pages'] for r in p['Rewards']], 'Local hub response preserves the sanitized catalogue ordering')
    check(len(data['Pages']) == 12 and sum(len(p['Rewards']) for p in data['Pages']) == 53, 'All 12 pages and 53 rewards')
    check(len(data['SeasonalRewards']) == 5 and len(data['Slides']) == 5, 'Five seasonal rewards and five carousel slides')
    check(not data['PreviewOnly'] and data['ClaimedRewards'] == 0 and all(d['Count'] == 0 for d in data['Documents']) and data['UniversalCount'] == 0 and data['NextResetTime'] == 0, 'New gameplay state is zero with no captured progress or countdown')
    docs = {d['Id'] for d in data['Documents']}
    for index, page in enumerate(data['Pages']):
        cells = set()
        for reward in page['Rewards']:
            check(all(c['DocumentId'] in docs and c['Count'] > 0 for c in reward['Costs']), reward['Id'] + ': document requirements resolve')
            for x in range(reward['X'], reward['X'] + reward['Width']):
                for y in range(reward['Y'], reward['Y'] + reward['Height']):
                    assert 0 <= x < 2 and 0 <= y < 3 and (x, y) not in cells
                    cells.add((x, y))
        check(True, 'Page ' + str(index + 1) + ': non-overlapping captured tile spans')
    for image in json.loads((PROJECT / 'data/hub-images.json').read_text()):
        raw = request('/seasonal-perks/hub-images/' + image['Id'] + '.png', raw=True)
        check(hashlib.sha256(raw).hexdigest() == image['Sha256'], 'Local image ' + image['Id'])
    check(all(hashlib.sha256(p.read_bytes()).hexdigest() == digest for p, digest in before.items()), 'All isolated profile files unchanged')
    check(set(paths) == set((SERVER / 'user/profiles').glob('*.json')), 'No profiles created')
    out = PROJECT / 'Research/UI/hub-server-checks.json'
    out.write_text(json.dumps({'passed': len(results), 'checks': results}, indent=2))
    print('Season hub:', len(results), 'checks passed.')


if __name__ == '__main__':
    main()
