"""Prepare a bounded, source-diverse Unity SDK check of a generated local prop library."""
import argparse
import json
from pathlib import Path


def prepare(directory, index_path, output, count):
    catalog = json.loads((directory / 'catalog.json').read_text())
    index = {entry['Id']: entry for entry in json.loads(index_path.read_text())['Entries']}
    assets, order, seen, sources = [], [], set(), set()
    def visit(name):
        if name in seen:
            return
        seen.add(name)
        for dependency in catalog['Bundles'][name]['Dependencies']:
            visit(dependency)
        order.append(name)
    # First cover different source levels, then add another prop sharing a prefab archive.
    selected = []
    for entry in catalog['Entries']:
        if not entry['Error'] and entry['Source'] not in sources:
            selected.append(entry)
            sources.add(entry['Source'])
            if len(selected) >= count:
                break
    selected_bundles = {entry['Bundle'] for entry in selected}
    selected_ids = {entry['Id'] for entry in selected}
    for entry in catalog['Entries']:
        if not entry['Error'] and entry['Id'] not in selected_ids and entry['Bundle'] in selected_bundles:
            selected.append(entry)
            if len(selected) >= count + 4:
                break
    for entry in selected:
        # Match the runtime's per-prefab resource leases, not every sibling prefab's resources.
        for dependency in catalog['Bundles'][entry['Bundle']]['Assets'][entry['Asset']]:
            visit(dependency)
        if entry['Bundle'] not in seen:
            seen.add(entry['Bundle'])
            order.append(entry['Bundle'])
        source = index[entry['Id']]
        assets.append({'bundle': entry['Bundle'], 'asset': entry['Asset'],
                       'rotation': source['Rotation'], 'scale': source['Scale']})
    assert assets, 'No available assets selected for SDK validation'
    output.write_text(json.dumps({'directory': str(directory.resolve()), 'bundles': order, 'assets': assets}), encoding='utf-8')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--directory', type=Path, required=True)
    parser.add_argument('--index', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--count', type=int, default=16)
    args = parser.parse_args()
    prepare(args.directory, args.index, args.output, args.count)
