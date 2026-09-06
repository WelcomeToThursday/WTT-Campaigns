"""Read-only selection UI checks against the isolated runtime's synthetic account."""
import hashlib
import json
from pathlib import Path

from test_integration import PROJECT, SERVER, request


def main():
    state = json.loads((PROJECT / 'Testing/restart-state.json').read_text())
    profile_path = SERVER / 'user/profiles' / (state['root'] + '.json')
    profile = json.loads(profile_path.read_text(encoding='utf-8-sig'))
    assert profile['info']['username'].startswith('season-test-')
    before = profile_path.read_bytes()
    snapshot = request('/wtt-seasonal/snapshot', session=state['root'])
    assert not snapshot.get('Error'), snapshot.get('Error')
    results = []
    for character in snapshot['Characters']:
        visual = character['Visual']
        assert set(visual) == {'Info', 'Customization', 'Equipment'}, list(visual)
        assert set(visual['Info']) == {'Nickname', 'Level', 'Side'}, list(visual['Info'])
        assert visual['Customization'] and visual['Equipment']['Items']
        equipment = visual['Equipment']['Id']
        items = visual['Equipment']['Items']
        assert any(item['_id'] == equipment for item in items)
        ids = {item['_id'] for item in items}
        assert all(item['_id'] == equipment or item.get('parentId') in ids for item in items)
        assert 'Inventory' not in visual and 'Quests' not in visual
        results.append(character['Mode'] + ': appearance-only contract and equipment tree')
    icons = PROJECT.parent / 'CJ-SDK/Assets/Mods/SeasonalPerks.Assets/Icons'
    catalogue = json.loads((PROJECT / 'data/catalogue.json').read_text())
    for perk in catalogue['common'] + catalogue['personal']:
        file = icons / Path(perk['imageUrl']).name
        content = request('/wtt-seasonal/icons/' + perk['id'] + '.png', raw=True)
        assert hashlib.sha256(content).digest() == hashlib.sha256(file.read_bytes()).digest(), file.name
        results.append('Perk icon served unchanged: ' + perk['id'])
    assert profile_path.read_bytes() == before, 'Read-only UI request modified the profile file'
    results.append('UI reads leave the synthetic normal profile file unchanged')
    (PROJECT / 'Research/UI/selection-server-checks.json').write_text(json.dumps({'passed':len(results),'checks':results}, indent=2))
    print(f'Selection UI server: {len(results)} checks passed; no profile mutations requested.')


if __name__ == '__main__':
    main()
