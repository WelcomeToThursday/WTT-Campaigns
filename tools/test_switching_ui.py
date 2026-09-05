"""Exercise both switching directions on the isolated runtime's synthetic account."""
import json

from test_integration import PROJECT, SERVER, request


def main():
    state = json.loads((PROJECT / 'Testing/restart-state.json').read_text())
    root, child = state['root'], state['child']
    profile = json.loads((SERVER / 'user/profiles' / (root + '.json')).read_text(encoding='utf-8-sig'))
    assert profile['info']['username'].startswith('season-test-')
    results = []
    baselines = {}

    def progression(profiles):
        return [{key: value.get(key) for key in ['_id', 'Customization', 'Inventory', 'Quests', 'Skills', 'TradersInfo']}
                for value in profiles]

    for mode, expected in [('normal', root), ('seasonal', child)]:
        snapshot = request('/seasonal-perks/switch', {'Mode': mode}, root)
        assert not snapshot.get('Error'), snapshot.get('Error')
        assert snapshot['EffectiveProfileId'] == expected
        baselines[mode] = progression(request('/client/game/profile/list', session=expected))
    for caller in [child, root, child]:
        for mode, expected in [('normal', root), ('seasonal', child), ('normal', root)]:
            snapshot = request('/seasonal-perks/switch', {'Mode': mode}, caller)
            assert not snapshot.get('Error'), snapshot.get('Error')
            assert snapshot['ActiveMode'] == mode and snapshot['EffectiveProfileId'] == expected
            profiles = request('/client/game/profile/list', session=expected)
            assert profiles[0]['_id'] == expected
            assert progression(profiles) == baselines[mode]
            results.append(mode + ': correct PMC/Scav and unchanged inventory, quests, skills, traders and appearance')
    (PROJECT / 'Research/UI/switching-server-checks.json').write_text(json.dumps({'passed': len(results), 'checks': results}, indent=2))
    print(f'Switching: {len(results)} server round trips passed using synthetic profiles.')


if __name__ == '__main__':
    main()
