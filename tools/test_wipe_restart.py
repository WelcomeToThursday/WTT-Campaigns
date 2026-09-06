"""Achievement preservation when recreation is cancelled and the test server restarts."""
import json
import secrets
import sys
from test_integration import PROJECT, SERVER, request, check, checks


def main():
    state = json.loads((PROJECT / 'Testing/carousel-test-state.json').read_text())
    root, child = state['root'], state['remaining'][0]
    normal = json.loads((SERVER / 'user/profiles' / (root + '.json')).read_text(encoding='utf-8-sig'))
    assert normal['info']['username'].startswith('carousel-test-')
    receipt = PROJECT / 'Testing/wipe-restart.json'

    def call(action, **data):
        result = request('/wtt-seasonal/' + action, {'ProtocolVersion': 2, **data}, root)
        check(not result.get('Error'), action + ': ' + str(result.get('Error')))
        return result

    if sys.argv[1] == 'prepare':
        call('switch', Mode='normal')
        profile = request('/client/game/profile/list', session=child)[0]
        snapshot = call('wipe', CharacterId=child, OperationId=secrets.token_hex(16))
        slot = next(c for c in snapshot['Characters'] if c['Id'] == child)
        check(slot['Wiped'] and not slot['Exists'], 'Wiped card is available for later recreation')
        receipt.write_text(json.dumps({'Achievements': profile['Achievements'], 'SeasonId': slot['SeasonId']}))
    else:
        saved = json.loads(receipt.read_text())
        snapshot = call('snapshot')
        slot = next(c for c in snapshot['Characters'] if c['Id'] == child)
        check(slot['Wiped'] and not slot['Exists'], 'Wiped card survives server restart')
        call('create', CharacterId=child, SeasonId=saved['SeasonId'], OperationId=secrets.token_hex(16), Side='Usec', Nickname='Restored', PerkIds=[])
        profile = request('/client/game/profile/list', session=child)[0]
        check(profile['Achievements'] == saved['Achievements'], 'Achievements survive delayed recreation and restart')
        stored = json.loads((SERVER / 'user/profiles' / (child + '.json')).read_text(encoding='utf-8-sig'))
        check(stored['info']['edition'] == normal['info']['edition'], 'Recreation uses the current root account edition')
        call('switch', Mode='seasonal', CharacterId=child)
        hub = request('/wtt-seasonal/hub', {'ProtocolVersion': 2}, child)
        check(hub['ClaimedRewards'] == 0 and hub['Revision'] == 0, 'Delayed recreation has fresh seasonal rewards')
        call('switch', Mode='normal')
    print('PASS', len(checks), 'wipe restart checks (' + sys.argv[1] + ')')


if __name__ == '__main__':
    main()
