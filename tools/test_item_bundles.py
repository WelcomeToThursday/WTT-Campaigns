"""Read-only verification of Seasonal item data and Backport bundle ownership."""
import argparse
import json
from pathlib import Path

import test_integration as server


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--port', type=int, default=6987)
    args = parser.parse_args()
    server.BASE = f'https://127.0.0.1:{args.port}'
    items = server.request('/client/items')
    bundles = server.request('/singleplayer/bundles')
    expected = json.loads((Path(__file__).resolve().parents[1] / 'data/season-items.json').read_text())
    keys = set()
    for identity, source in expected.items():
        item = items[identity]
        key = source['_props']['Prefab']['path']
        keys.add(key)
        assert item['_props']['Prefab']['path'] == key, identity + ': shared prefab path'
        for field in ['Width', 'Height', 'StackMaxSize', 'Weight', 'QuestItem']:
            assert item['_props'].get(field) == source['_props'].get(field), identity + ': ' + field
        parent = source['_parent']
        if parent == '6a28212a0368f4438b0d0a45':
            parent = '5448ecbe4bdc2d60728b4568'
        assert item['_parent'] == parent, identity + ': native parent'
        matches = [b for b in bundles if b['FileName'] == key]
        assert len(matches) == 1, identity + ': one bundle registration'
        assert matches[0]['ModPath'].endswith('/WTT-ContentBackport'), identity + ': Backport owns the bundle'
        assert matches[0]['Size'] > 0 and matches[0]['Dependencies'], identity + ': bundle and dependencies loaded'
    assert not any(b['FileName'] == 'wtt-campaigns/' + key for key in keys for b in bundles), 'Duplicate Seasonal bundles'
    print(f'PASS: {len(expected)} item definitions retain their data and use {len(keys)} Backport models; no duplicate bundles.')


if __name__ == '__main__':
    main()
