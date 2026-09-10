"""Legacy-link migration and restart checks, exclusively for test_characters accounts."""
import json
import sys
from test_integration import PROJECT, SERVER, request, check, checks


def main():
    state = json.loads((PROJECT / 'Testing/carousel-test-state.json').read_text())
    root = state['root']
    profile = json.loads((SERVER / 'user/profiles' / (root + '.json')).read_text(encoding='utf-8-sig'))
    assert profile['info']['username'].startswith('carousel-test-')
    link_path = SERVER / 'user/profileData' / root / 'wttCampaignsAccount.json'
    if sys.argv[1] == 'prepare':
        assert not (SERVER / 'test-server.pid').exists(), 'Stop the isolated server first'
        link = json.loads(link_path.read_text())
        children = link['Characters']
        assert {c['ProfileId'] for c in children} == set(state['remaining'])
        # Reproduce the previous version's one-current + one-archived-season contract.
        current, archived = children
        assert current['SeasonId'] != archived['SeasonId']
        old = dict(CurrentSeasonId=current['SeasonId'], SeasonalId=current['ProfileId'], Created=True, Mode='normal',
                   ActiveRaidProfiles=[], Seasons={archived['SeasonId']: dict(ProfileId=archived['ProfileId'], Created=True)})
        (PROJECT / 'Testing/carousel-link-backup.json').write_text(json.dumps(link))
        link_path.write_text(json.dumps(old))
        print('Prepared synthetic legacy account link')
        return
    for _ in range(2):
        snapshot = request('/wtt-campaigns/snapshot', {'ProtocolVersion': 2}, root)
        check(not snapshot.get('Error'), 'Migrated snapshot loads')
        check({c['Id'] for c in snapshot['Characters']} == {root, *state['remaining']}, 'Current and archived season characters migrate once')
        for child in state['remaining']:
            selected = request('/wtt-campaigns/switch', {'ProtocolVersion': 2, 'Mode': 'seasonal', 'CharacterId': child}, root)
            check(selected.get('EffectiveProfileId') == child, 'Migrated character remains selectable by ID')
            hub = request('/wtt-campaigns/hub', {'ProtocolVersion': 2}, child)
            check(hub.get('SeasonId') == selected.get('SeasonId'), 'Migrated character retains its season rewards')
        request('/wtt-campaigns/switch', {'ProtocolVersion': 2, 'Mode': 'normal'}, root)
    saved = json.loads(link_path.read_text())
    check(len(saved['Characters']) == 2, 'Migrated character list is persisted without duplicates')
    print('PASS', len(checks), 'legacy migration/restart checks')


if __name__ == '__main__':
    main()
