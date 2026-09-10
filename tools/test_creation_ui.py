"""Verify appearance selections on fresh synthetic profiles in the isolated SPT runtime."""
import json
import time

from test_integration import PROJECT, SERVER, request

HEAD = '5cc085e214c02e000c6bea67'
VOICE = '5fc100cf95572123ae738483'


def main():
    customization = json.loads((SERVER / 'SPT_Data/database/templates/customization.json').read_text())
    results = []

    def choice(side, parent):
        return sorted(key for key, value in customization.items()
                      if value.get('_parent') == parent and value['_props'].get('AvailableAsDefault')
                      and side in value['_props'].get('Side', []))[-1]

    for side in ['Bear', 'Usec']:
        username = 'season-test-creation-' + side.lower() + '-' + str(time.time_ns())
        registered = request('/launcher/v2/register', {'username': username, 'edition': 'Standard'})
        root = next(profile['profileId'] for profile in registered['Profiles'] if profile['username'] == username)
        request('/client/game/profile/create', {'side': 'Usec', 'nickname': 'NormalTest',
                'headId': choice('Usec', HEAD), 'voiceId': choice('Usec', VOICE)}, root)
        request('/wtt-campaigns/snapshot', session=root)
        normal_before = request('/client/game/profile/list', session=root)
        profile_files = set((SERVER / 'user/profiles').glob('*.json'))
        payload = {'Side': side, 'Nickname': 'SeasonTest', 'PerkIds': [], 'ExpectedRevision': 0,
                   'HeadId': choice(side, HEAD), 'VoiceId': choice(side, VOICE)}
        other = 'Usec' if side == 'Bear' else 'Bear'
        for field, value in [('HeadId', 'not-an-item'), ('HeadId', choice(other, HEAD)),
                             ('VoiceId', choice(other, VOICE)), ('VoiceId', choice(side, HEAD))]:
            rejected = request('/wtt-campaigns/create', {**payload, field: value}, root)
            assert rejected.get('Error'), f'{side}: {field}={value} was unexpectedly accepted'
            assert set((SERVER / 'user/profiles').glob('*.json')) == profile_files
            results.append(side + ': rejected invalid/faction-mismatched ' + field + ' before creating a profile')
        created = request('/wtt-campaigns/create', payload, root)
        assert not created.get('Error'), created.get('Error')
        assert created['ActiveMode'] == 'normal'
        normal_after = request('/client/game/profile/list', session=root)
        def differences(a, b, path=''):
            if type(a) != type(b):
                return [path]
            if isinstance(a, dict):
                return [p for key in a.keys() | b.keys() for p in differences(a.get(key), b.get(key), path + '/' + key)]
            if isinstance(a, list):
                return [path] if len(a) != len(b) else [p for i, (x, y) in enumerate(zip(a, b)) for p in differences(x, y, path + '/' + str(i))]
            return [] if a == b else [path]
        changed = differences(normal_before, normal_after)
        assert not changed, changed
        results.append(side + ': creating with explicit appearance leaves normal character unchanged')
        switched = request('/wtt-campaigns/switch', {'Mode': 'seasonal'}, root)
        seasonal = request('/client/game/profile/list', session=switched['EffectiveProfileId'])[0]
        assert seasonal['Info']['Side'] == side
        assert seasonal['Customization']['Head'] == payload['HeadId']
        assert seasonal['Customization']['Voice'] == payload['VoiceId']
        results.append(side + ': selected head and voice persist in the independent seasonal profile')
        duplicate = request('/wtt-campaigns/create', payload, root)
        assert duplicate.get('Error')
        assert len(set((SERVER / 'user/profiles').glob('*.json')) - profile_files) == 1
        results.append(side + ': duplicate confirmation cannot create a second seasonal profile')
    (PROJECT / 'Research/UI/creation-server-checks.json').write_text(json.dumps({'passed': len(results), 'checks': results}, indent=2))
    print(f'Creation UI server: {len(results)} checks passed on synthetic BEAR and USEC profiles.')


if __name__ == '__main__':
    main()
