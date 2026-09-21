"""Build local scenery prefabs and shared resource bundles from every installed level.

Only supported hierarchies are exported. Source files are never modified. Texture/mesh
resources are shared across props, so repeated map instances do not duplicate their data.
"""
import argparse
import collections
import copy
import gc
import hashlib
import json
import re
import inspect
import os
from concurrent.futures import ProcessPoolExecutor
from pathlib import Path

import UnityPy
from UnityPy.files.SerializedFile import FileIdentifier
from export_container_library import Library, referenced_ids
from level_prop_index import SCHEMA, COMPONENTS, NULL, clean_tree, digest, pointer_key, scan, source_stamp as index_stamp
from recover_ui import Generator

RESOURCES = {'Mesh', 'Texture2D', 'Cubemap', 'Texture2DArray', 'CubemapArray', 'Texture3D', 'Shader'}
LOCAL = COMPONENTS | {'GameObject', 'MonoBehaviour', 'MonoScript', 'Material', 'PhysicMaterial'}


def sha(path):
    with path.open('rb') as source:
        return hashlib.file_digest(source, 'sha256').hexdigest().upper()


def source_stamp(game):
    builtins = game / 'EscapeFromTarkov_Data/Resources'
    return digest([index_stamp(game), [(p.name, p.stat().st_size, p.stat().st_mtime_ns)
                                     for p in sorted(builtins.iterdir()) if p.is_file()]])


class Sources:
    def __init__(self, game):
        self.game = game
        self.data = game / 'EscapeFromTarkov_Data'
        self.files = collections.OrderedDict()
        self.generator = Generator('2022.3.43f1')
        for path in (self.data / 'Managed').glob('*.dll'):
            self.generator.load_dll(path.read_bytes())

    def file(self, name):
        name = Path(name).name
        if name not in self.files:
            path = self.data / name
            if not path.is_file():
                path = self.data / 'Resources' / name
            if not path.is_file():
                raise ValueError('Missing serialized resource file: ' + name)
            asset = next(iter(UnityPy.load(str(path)).files.values()), None)
            if asset is None or not hasattr(asset, 'objects'):
                raise ValueError('Unsupported serialized resource file: ' + name)
            asset.name = name
            self.files[name] = asset
            while len(self.files) > 12:
                self.files.popitem(last=False)
        self.files.move_to_end(name)
        return self.files[name]

    def get(self, key):
        return self.file(key[0]).objects[key[1]]


class Resources:
    def __init__(self, sources):
        self.sources = sources
        self.groups = {}
        self.keys = {}

    def add(self, obj):
        key = (obj.assets_file.name, obj.path_id)
        if key not in self.keys:
            kind = obj.type.name
            if kind not in RESOURCES:
                raise ValueError('Unsupported resource: ' + kind)
            # All shaders share one archive because native shaders may reference each other.
            group = 'shaders' if kind == 'Shader' else digest([key[0], kind, key[1] % 32])[:24]
            name = 'resources-' + group
            pid = int(digest(key)[:15], 16) + 2
            self.keys[key] = (name, pid)
            group_keys = self.groups.setdefault(name, {})
            if pid in group_keys and group_keys[pid] != key:
                raise ValueError('Resource identity collision')
            group_keys[pid] = key
        return self.keys[key]


class PropBundle(Library):
    def __init__(self, sources, resources, name):
        super().__init__(sources.game, name, sources.generator)
        self.sources = sources
        self.resources = resources
        self.entry = None
        self.scopes = {}
        self.scoped_objects = {}
        self.current_object = 0
        self.external_bundles = {}
        self.external_pointers = collections.defaultdict(set)

    def scoped(self, entry, obj):
        if obj is None or obj.type.name in RESOURCES or entry is None:
            return obj
        key = entry['Id'], obj.assets_file.name, obj.path_id
        if key not in self.scoped_objects:
            clone = copy.copy(obj)
            self.scoped_objects[key] = clone
            self.scopes[id(clone)] = entry
        return self.scoped_objects[key]

    def add_entry(self, entry):
        self.allowed.update((entry['Id'] + ':' + entry['Source'], pid) for pid in entry['Allowed'])
        obj = self.scoped(entry, self.file(entry['Source']).objects[entry['Root']])
        self.entries.append({'Root': self.add(obj), 'Asset': 'props/' + entry['Id'] + '.prefab'})

    def resolve(self, asset, pointer):
        return self.scoped(self.entry, super().resolve(asset, pointer))

    def file(self, name):
        return self.sources.file(name)

    def key(self, obj):
        self.names[id(obj.assets_file)] = obj.assets_file.name
        entry = self.scopes.get(id(obj))
        return (entry['Id'] + ':' if entry else '') + obj.assets_file.name, obj.path_id

    def tree(self, obj):
        self.entry = self.scopes.get(id(obj))
        self.current_object = self.ids[self.key(obj)]
        tree, node = super().tree(obj)
        kind = obj.type.name
        if kind not in LOCAL | RESOURCES:
            raise ValueError('Unsupported referenced object: ' + kind)
        if self.entry and obj.assets_file.name == self.entry['Source'] and obj.path_id == self.entry['Transform']:
            tree['m_Father'] = dict(NULL)
            tree['m_LocalPosition'] = dict(zip('xyz', (0., 0., 0.)))
            tree['m_LocalRotation'] = self.entry['Rotation']
            tree['m_LocalScale'] = self.entry['Scale']
        if kind == 'GameObject' and self.entry and obj.path_id == self.entry['Root']:
            tree['m_IsActive'] = True
        return clean_tree(kind, tree), node

    def external(self, name):
        if name not in self.external_bundles:
            cab = 'CAB-' + name
            ref = object.__new__(FileIdentifier)
            ref.path = f'archive:/{cab}/{cab}'
            ref.temp_empty = ''
            ref.guid = bytes(16)
            ref.type = 0
            self.asset.externals.append(ref)
            self.external_bundles[name] = len(self.asset.externals)
        return self.external_bundles[name]

    def remap(self, value, owner):
        if isinstance(value, dict):
            if 'm_FileID' in value and 'm_PathID' in value:
                obj = self.resolve(owner, value)
                if obj is None:
                    return dict(NULL)
                if obj.type.name in RESOURCES:
                    name, pid = self.resources.add(obj)
                    if name != self.name:
                        file_id = self.external(name)
                        self.external_pointers[self.current_object].add((file_id, pid))
                        return {'m_FileID': file_id, 'm_PathID': pid}
                if obj.type.name in COMPONENTS | {'GameObject', 'MonoBehaviour'} and self.key(obj) not in self.allowed:
                    raise ValueError('Prop links to a map-owned object: ' + str(self.key(obj)))
                return {'m_FileID': 0, 'm_PathID': self.add(obj)}
            return {k: self.remap(v, owner) for k, v in value.items()}
        if isinstance(value, list):
            return [self.remap(v, owner) for v in value]
        if isinstance(value, tuple):
            return tuple(self.remap(v, owner) for v in value)
        return value

    def add(self, obj):
        if obj is None:
            return 0
        if obj.type.name in RESOURCES and self.resources.add(obj)[0] == self.name:
            key = self.key(obj)
            if key not in self.ids:
                self.ids[key] = self.resources.add(obj)[1]
                self.pending.append(obj)
            return self.ids[key]
        return super().add(obj)

    def finish(self, output):
        raw = self.build()
        asset_dependencies = {}
        if self.entries and self.external_pointers:
            tree = self.abtree
            old = tree['m_PreloadTable']
            tree['m_PreloadTable'] = []
            by_file = {fid: name for name, fid in self.external_bundles.items()}
            for asset_name, info in tree['m_Container']:
                local = old[info['preloadIndex']:info['preloadIndex'] + info['preloadSize']]
                external = set()
                for pointer in local:
                    external.update(self.external_pointers[pointer['m_PathID']])
                info['preloadIndex'] = len(tree['m_PreloadTable'])
                tree['m_PreloadTable'].extend(local)
                tree['m_PreloadTable'].extend({'m_FileID': fid, 'm_PathID': pid} for fid, pid in sorted(external))
                info['preloadSize'] = len(local) + len(external)
                asset_dependencies[asset_name] = sorted({by_file[fid] for fid, _ in external})
            self.asset.objects[1].save_typetree(tree)
            raw = self.bundle.save(packer='lz4')
        # The base serializer stores concrete cross-file PPtrs; the manifest owns load ordering.
        path = output / (self.name + '.bundle')
        path.write_bytes(raw)
        return {'File': path.name, 'Sha256': sha(path), 'Dependencies': sorted(self.external_bundles), 'Assets': asset_dependencies,
                'Objects': len(self.asset.objects)}


def write_catalog(output, catalog):
    temporary = output / 'catalog.json.tmp'
    temporary.write_text(json.dumps(catalog, separators=(',', ':')), encoding='utf-8')
    temporary.replace(output / 'catalog.json')


_worker_sources = None


def worker_start(game):
    global _worker_sources
    _worker_sources = Sources(Path(game))


def pack_group(work):
    name, items, directory = work
    output = Path(directory)
    resources = Resources(_worker_sources)
    bundles = {}
    def pack(name, items):
        try:
            bundle = PropBundle(_worker_sources, resources, name)
            for source, entry in items:
                bundle.add_entry(source)
            bundles[name] = bundle.finish(output)
        except Exception as error:
            if len(items) == 1:
                items[0][1]['Error'] = 'Cannot build an independent prop: ' + str(error)
            else:
                middle = len(items) // 2
                for suffix, subset in (('a', items[:middle]), ('b', items[middle:])):
                    child = name + suffix
                    for _, entry in subset:
                        entry['Bundle'] = child
                    pack(child, subset)
    pack(name, items)
    return name, bundles, [(e['Id'], e['Bundle'], e['Error']) for _, e in items], list(resources.keys.items())


def pack_resource(work):
    name, keys, directory = work
    resources = Resources(_worker_sources)
    bundle = PropBundle(_worker_sources, resources, name)
    for key in keys:
        bundle.add(_worker_sources.get(key))
    info = bundle.finish(Path(directory))
    return name, info, list(resources.keys.items()), len(resources.groups[name])


def build(game, output, index_path, rebuild=False):
    stamp = source_stamp(game)
    output.mkdir(parents=True, exist_ok=True)
    path = output / 'catalog.json'
    if not rebuild and path.exists():
        old = json.loads(path.read_text())
        if old.get('Schema') == SCHEMA and old.get('SourceStamp') == stamp and old.get('ToolStamp') == tool_stamp():
            print('Level prop library is current.', flush=True)
            return
    index = json.loads(index_path.read_text()) if index_path.exists() else {}
    if index.get('Schema') != SCHEMA or index.get('SourceStamp') != index_stamp(game) or rebuild:
        scan(game, index_path)
        index = json.loads(index_path.read_text())
    sources = Sources(game)
    resources = Resources(sources)
    catalog = {'Schema': SCHEMA, 'SourceStamp': stamp, 'ToolStamp': tool_stamp(), 'Levels': index['Levels'], 'Counts': index['Counts'],
               'NativeAssemblySha256': sha(sources.data / 'Managed/Assembly-CSharp.dll'), 'Bundles': {}, 'Entries': []}
    groups = collections.defaultdict(list)
    for source in index['Entries']:
        entry = {k: source[k] for k in ('Id', 'Name', 'Source', 'Scene', 'Sources', 'Aliases', 'Error')}
        entry['Bundle'] = 'props-' + source['Source'] + '-' + entry['Id'][0]
        entry['Asset'] = 'props/' + entry['Id'] + '.prefab'
        if not entry['Error']:
            groups[entry['Bundle']].append((source, entry))
        catalog['Entries'].append(entry)
    by_id = {entry['Id']: entry for entry in catalog['Entries']}
    completed, changes, built = set(), [], {}
    checkpoint = output / 'work-state.json'
    def merge_resources(pairs):
        for key, identity in pairs:
            key, identity = tuple(key), tuple(identity)
            if key in resources.keys and resources.keys[key] != identity:
                raise ValueError('Inconsistent shared resource identity')
            resources.keys[key] = identity
            resources.groups.setdefault(identity[0], {})[identity[1]] = key
    def save_work():
        work = {'SourceStamp': stamp, 'ToolStamp': catalog['ToolStamp'], 'Completed': sorted(completed),
                'Bundles': catalog['Bundles'], 'Changes': changes, 'Resources': list(resources.keys.items()), 'ResourceCounts': built}
        temp = checkpoint.with_suffix('.tmp')
        temp.write_text(json.dumps(work, separators=(',', ':')))
        temp.replace(checkpoint)
    if checkpoint.exists() and not rebuild:
        work = json.loads(checkpoint.read_text())
        if work['SourceStamp'] == stamp and work['ToolStamp'] == catalog['ToolStamp']:
            if all((output / info['File']).exists() and sha(output / info['File']) == info['Sha256'] for info in work['Bundles'].values()):
                completed.update(work['Completed'])
                catalog['Bundles'].update(work['Bundles'])
                changes.extend(work['Changes'])
                merge_resources(work['Resources'])
                built.update(work.get('ResourceCounts', {}))
                for identity, bundle, error in changes:
                    by_id[identity].update(Bundle=bundle, Error=error)
    tasks = [(name, items, str(output)) for name, items in sorted(groups.items()) if name not in completed]
    # File-only workers own separate serialized objects and output files. Unity access stays in the client.
    with ProcessPoolExecutor(max_workers=min(8, max(1, (os.cpu_count() or 2) // 2)), initializer=worker_start, initargs=(str(game),)) as pool:
        for name, bundles, updates, pairs in pool.map(pack_group, tasks, chunksize=4):
            catalog['Bundles'].update(bundles)
            merge_resources(pairs)
            changes.extend(updates)
            for identity, bundle, error in updates:
                by_id[identity].update(Bundle=bundle, Error=error)
            completed.add(name)
            if len(completed) % 25 == 0:
                print(f'Prop groups {len(completed)}/{len(groups)}: {len(catalog["Bundles"])} bundles; {len(resources.keys)} shared resources', flush=True)
            if len(completed) % 250 == 0:
                save_work()
    save_work()
    # Shader dependency traversal can add resources; finish all discovered groups to a fixed point.
    with ProcessPoolExecutor(max_workers=min(8, max(1, (os.cpu_count() or 2) // 2)), initializer=worker_start, initargs=(str(game),)) as pool:
        while any(built.get(name) != len(keys) for name, keys in resources.groups.items()):
            tasks = [(name, list(resources.groups[name].values()), str(output))
                     for name in sorted(resources.groups, key=lambda n: next(iter(resources.groups[n].values())))
                     if built.get(name) != len(resources.groups[name])]
            for name, info, pairs, count in pool.map(pack_resource, tasks, chunksize=4):
                catalog['Bundles'][name] = info
                merge_resources(pairs)
                built[name] = count
                if len(built) % 100 == 0:
                    print(f'Shared resource bundles {len(built)}/{len(resources.groups)}', flush=True)
                if len(built) % 500 == 0:
                    save_work()
    if source_stamp(game) != stamp:
        raise ValueError('Game source files changed during export; rebuild the level library.')
    write_catalog(output, catalog)
    for path in output.glob('*.bundle'):
        if path.stem not in catalog['Bundles']:
            path.unlink()
    print(f'Exported {sum(not e["Error"] for e in catalog["Entries"])} placeable level props from {len(catalog["Levels"])} levels.', flush=True)
    checkpoint.unlink(missing_ok=True)


def pointers(value):
    if isinstance(value, dict):
        if 'm_FileID' in value and 'm_PathID' in value:
            if value['m_PathID']:
                yield value
        else:
            for child in value.values():
                yield from pointers(child)
    elif isinstance(value, (list, tuple)):
        for child in value:
            yield from pointers(child)


def tool_stamp():
    return digest([inspect.getsource(value) for value in (Sources, Resources, PropBundle, pack_group, build)] +
                  [sha(Path(__file__).with_name('level_prop_index.py')), sha(Path(__file__).with_name('export_container_library.py'))])


def verify(output, game):
    catalog = json.loads((output / 'catalog.json').read_text())
    assert catalog['Schema'] == SCHEMA and catalog['SourceStamp'] == source_stamp(game), 'Level library needs rebuilding'
    assert catalog['NativeAssemblySha256'] == sha(game / 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll')
    identities = set()
    assert any(not entry['Error'] for entry in catalog['Entries']), 'No supported level props were exported'
    for entry in catalog['Entries']:
        assert entry['Id'] not in identities, 'Duplicate level prop identity'
        identities.add(entry['Id'])
        assert entry['Error'] or entry['Bundle'] in catalog['Bundles'], 'Missing generated prop'
    object_ids = {}
    references = []
    expected = collections.defaultdict(set)
    for entry in catalog['Entries']:
        if not entry['Error']:
            expected[entry['Bundle']].add(entry['Asset'])
    for number, (name, info) in enumerate(catalog['Bundles'].items()):
        path = output / info['File']
        assert sha(path) == info['Sha256'], 'Level prop hash mismatch: ' + name
        env = UnityPy.load(str(path))
        asset = next(f for b in env.files.values() for f in b.files.values() if hasattr(f, 'objects'))
        object_ids[name] = set(asset.objects)
        scene_trees = {}
        bundle_tree = None
        for obj in asset.objects.values():
            tree = obj.read_typetree()
            if obj.type.name in COMPONENTS | {'GameObject', 'MonoBehaviour', 'MonoScript'}:
                scene_trees[obj.path_id] = tree
            for ref in pointers(tree):
                if ref['m_FileID']:
                    target = Path(asset.externals[ref['m_FileID'] - 1].path).name.removeprefix('CAB-')
                    assert target in info['Dependencies'], 'Unleased generated dependency'
                    references.append((target, ref['m_PathID']))
                else:
                    assert ref['m_PathID'] in asset.objects, 'Dangling local prop reference'
            if obj.type.name == 'MeshRenderer':
                assert not tree.get('m_StaticBatchInfo', {}).get('subMeshCount'), 'Combined map geometry leaked into prop'
            if obj.type.name == 'AssetBundle':
                assert not tree['m_IsStreamedSceneAssetBundle'], 'Generated prop must not load a scene'
                bundle_tree = tree
        assert bundle_tree is not None
        assert {key for key, _ in bundle_tree['m_Container']} == expected[name], 'Generated prefab paths differ from catalog'
        for asset_name, info_entry in bundle_tree['m_Container']:
            start, count = info_entry['preloadIndex'], info_entry['preloadSize']
            assert start >= 0 and count > 0 and start + count <= len(bundle_tree['m_PreloadTable']), 'Invalid prefab preload range'
            required = set()
            for ref in bundle_tree['m_PreloadTable'][start:start + count]:
                if ref['m_FileID']:
                    required.add(Path(asset.externals[ref['m_FileID'] - 1].path).name.removeprefix('CAB-'))
            assert required == set(info['Assets'][asset_name]), 'Prefab resources must have their own exact leases'
            root = info_entry['asset']['m_PathID']
            assert asset.objects[root].type.name == 'GameObject'
            pending, owned = [root], set()
            transforms = []
            while pending:
                go = pending.pop()
                assert go not in owned, 'Cyclic or shared prefab hierarchy'
                owned.add(go)
                for component in scene_trees[go]['m_Component']:
                    pointer = component['component']
                    assert not pointer['m_FileID'], 'Prefab components must be owned locally'
                    pid = pointer['m_PathID']
                    owned.add(pid)
                    if asset.objects[pid].type.name == 'Transform':
                        tr = scene_trees[pid]
                        transforms.append(pid)
                        if go == root:
                            assert not tr['m_Father']['m_PathID'], 'Prefab retained its original map parent'
                            assert all(abs(v) < 1e-6 for v in tr['m_LocalPosition'].values()), 'Prefab retained a map position'
                        for child in tr['m_Children']:
                            assert not child['m_FileID'], 'Prefab children must be owned locally'
                            pending.append(scene_trees[child['m_PathID']]['m_GameObject']['m_PathID'])
            for pid in owned:
                kind = asset.objects[pid].type.name
                if kind in COMPONENTS | {'GameObject', 'MonoBehaviour'}:
                    for ref in pointers(scene_trees[pid]):
                        if not ref['m_FileID'] and asset.objects[ref['m_PathID']].type.name in COMPONENTS | {'GameObject', 'MonoBehaviour'}:
                            assert ref['m_PathID'] in owned, 'Prefab references another prop or a map-owned object'
                if kind == 'MonoBehaviour':
                    script = scene_trees[scene_trees[pid]['m_Script']['m_PathID']]
                    script_name = '.'.join(filter(None, (script['m_Namespace'], script['m_ClassName'])))
                    assert script_name in ('EFT.Ballistics.BallisticCollider', 'PreviewPivot'), 'Unsupported native behavior in generated prop'
        if number % 500 == 0:
            print(f'Verified level bundles {number+1}/{len(catalog["Bundles"])}', flush=True)
    assert all(pid in object_ids[name] for name, pid in references), 'Dangling shared prop reference'
    visiting, done = set(), set()
    def visit(name):
        assert name not in visiting, 'Generated dependency cycle'
        if name in done:
            return
        visiting.add(name)
        for dependency in catalog['Bundles'][name]['Dependencies']:
            visit(dependency)
        visiting.remove(name)
        done.add(name)
    for name in catalog['Bundles']:
        visit(name)
    print(f'Level prop library verified: {len(catalog["Levels"])} levels, {sum(not e["Error"] for e in catalog["Entries"])} placeable props, {len(catalog["Bundles"])} bundles.', flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--index', type=Path)
    parser.add_argument('--rebuild', action='store_true')
    parser.add_argument('--verify', action='store_true')
    args = parser.parse_args()
    if args.verify:
        verify(args.output, args.game)
    else:
        build(args.game, args.output, args.index or args.output / 'index.json', args.rebuild)
