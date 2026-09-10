"""Exercise the local route namespace using a fresh account in Testing/Server only."""
import json
import secrets
from test_integration import PROJECT, SERVER, request, check, checks


def main():
    name = 'route-test-' + secrets.token_hex(5)
    registered = request('/launcher/v2/register', {'username': name, 'edition': 'Standard'})
    root = next(p['profileId'] for p in registered['Profiles'] if p['username'] == name)
    customization = json.loads((SERVER / 'SPT_Data/database/templates/customization.json').read_text())

    def cosmetic(parent):
        return next(k for k, v in customization.items() if v.get('_parent') == parent
                    and v['_props'].get('AvailableAsDefault') and 'Usec' in v['_props'].get('Side', []))

    request('/client/game/profile/create', {
        'side': 'Usec', 'nickname': 'RouteTest',
        'headId': cosmetic('5cc085e214c02e000c6bea67'),
        'voiceId': cosmetic('5fc100cf95572123ae738483'),
    }, root)
    snapshot = request('/wtt-campaigns/snapshot', {'ProtocolVersion': 2}, root)
    check(not snapshot.get('Error') and snapshot['ActiveMode'] == 'normal', 'Snapshot route serves the new account')
    identity = {'ProtocolVersion': 2, 'SeasonId': snapshot['SeasonId']}

    def seasonal(path, payload=None, session=root):
        return request('/wtt-campaigns/' + path, {**identity, **(payload or {})}, session)

    for perk in snapshot['Catalogue']['common'] + snapshot['Catalogue']['personal']:
        check(perk['imageUrl'] == '/wtt-campaigns/icons/' + perk['id'] + '.png', 'Catalogue publishes the new icon route')
        check(request(perk['imageUrl'], raw=True).startswith(b'\x89PNG\r\n\x1a\n'), 'New icon route serves PNG bytes')
    created = seasonal('create', {'PerkIds': [], 'Nickname': 'RouteSeason', 'Side': 'Usec', 'ExpectedRevision': 0})
    check(not created.get('Error'), 'Create route: ' + str(created.get('Error')))
    edited = seasonal('edit', {'PerkIds': [], 'ExpectedRevision': created['State']['Revision']})
    check(not edited.get('Error') and edited['State']['Revision'] > created['State']['Revision'], 'Edit route saves a revision')
    switched = seasonal('switch', {'Mode': 'seasonal'})
    check(not switched.get('Error') and switched['ActiveMode'] == 'seasonal', 'Switch route selects the seasonal profile')
    child = switched['EffectiveProfileId']
    hub = seasonal('hub', session=child)
    check(not hub.get('Error') and hub['SeasonId'] == snapshot['SeasonId'], 'Hub route serves the active season')
    asset = hub['Documents'][0]['Image']
    check(request('/wtt-campaigns/hub-images/' + asset + '.png', raw=True).startswith(b'\x89PNG\r\n\x1a\n'),
          'New hub image route serves PNG bytes')
    invalid = {'OperationId': secrets.token_hex(16), 'ExpectedRevision': hub['Revision'], 'RewardId': '0' * 24}
    check(bool(seasonal('hub/claim', invalid, child).get('Error')), 'Claim route returns a validation error for an unknown reward')
    invalid = {'OperationId': secrets.token_hex(16), 'ExpectedRevision': hub['Revision'], 'Sources': {}}
    check(bool(seasonal('hub/exchange', invalid, child).get('Error')), 'Exchange route rejects an empty source selection')
    pickup = seasonal('hub/raid-document', {'OperationId': secrets.token_hex(16), 'ItemId': '0' * 24}, child)
    check(pickup.get('Ignored') or pickup.get('Error'), 'Raid document route handles a non-raid item')
    check(seasonal('hub', session=child)['Revision'] == hub['Revision'], 'Rejected operations leave the hub revision unchanged')
    page = request('/wtt-campaigns/creator', raw=True).decode('utf-8')
    check('Season Creator' in page and '/wtt-campaigns/creator/packs/' in page, 'Creator page renders new export links')
    check('/seasonal-perks/' not in page, 'Creator page contains no old local routes')
    check(request('/wtt-campaigns/creator/assets/' + asset + '.png', raw=True).startswith(b'\x89PNG\r\n\x1a\n'),
          'Creator asset route serves PNG bytes')
    selection = json.loads((SERVER / 'user/mods/WTT-Campaigns/creator/selection.json').read_text(encoding='utf-8-sig'))
    if selection['Active']:
        pack = request('/wtt-campaigns/creator/packs/' + selection['Active'] + '/download', raw=True)
        check(pack.startswith(b'PK\x03\x04'), 'Creator download route exports the active pack')
    check(seasonal('switch', {'Mode': 'normal'})['ActiveMode'] == 'normal', 'Switch route returns to Normal')
    (PROJECT / 'Research/route-checks.json').write_text(json.dumps({'passed': len(checks), 'checks': checks}, indent=2))
    print('Passed', len(checks), 'isolated-server route checks.')


if __name__ == '__main__':
    main()
