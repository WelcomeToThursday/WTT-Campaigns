"""Build a local-only native container library from this installed game's serialized assets.
Game files are read only. No map or game process is launched.
"""
import argparse, copy, hashlib, json, struct
from pathlib import Path
import UnityPy
from UnityPy.streams import EndianBinaryReader
from recover_ui import Generator
from UnityPy.helpers.Tpk import get_typetree_node
from UnityPy.files.SerializedFile import LocalSerializedObjectIdentifier
from UnityPy.helpers.TypeTreeNode import TypeTreeNode

def referenced_ids(value):
    if isinstance(value, dict):
        if 'm_FileID' in value and 'm_PathID' in value:
            if value['m_PathID'] and not value['m_FileID']:
                yield value['m_PathID']
        else:
            for child in value.values():
                yield from referenced_ids(child)
    elif isinstance(value, (list, tuple)):
        for child in value:
            yield from referenced_ids(child)

def complete_type_tree(node):
    if node.m_Type == 'string' and not node.m_Children:
        node.m_Children = [TypeTreeNode(node.m_Level + 1, 'Array', 'Array', -1, 1,
            [TypeTreeNode(node.m_Level + 2, 'int', 'size', 4, 1),
             TypeTreeNode(node.m_Level + 2, 'char', 'data', 1, 1)], m_MetaFlag=16384)]
    scalar = {'bool': 1, 'char': 1, 'SInt8': 1, 'UInt8': 1, 'short': 2, 'UInt16': 2,
              'SInt16': 2, 'int': 4, 'unsigned int': 4, 'SInt32': 4, 'UInt32': 4,
              'float': 4, 'double': 8, 'SInt64': 8, 'UInt64': 8, 'long long': 8}
    for child in node.m_Children:
        complete_type_tree(child)
    node.m_Version = 1
    if node.m_Type in scalar:
        node.m_ByteSize = scalar[node.m_Type]
    elif node.m_Type in ('Array', 'string') or any(child.m_ByteSize < 0 for child in node.m_Children):
        node.m_ByteSize = -1
    else:
        size = 0
        for child in node.m_Children:
            size += child.m_ByteSize
            if child.m_MetaFlag & 16384:
                size = (size + 3) & ~3
        node.m_ByteSize = size
    if node.m_Level == 0:
        node.m_Type = 'MonoBehaviour'
        node.m_ByteSize = -1
        node.m_MetaFlag |= 32768

class Stream:
    flags = 0

    def __init__(self, data):
        self.data = data

    def save(self):
        return self.data

class Library:

    def __init__(self, game, name="wtt-native-containers", generator=None):
        self.name = name
        self.cab = "CAB-" + name
        self.game = game
        self.data = game / 'EscapeFromTarkov_Data'
        self.files = {}
        self.names = {}
        self.ids = {}
        self.pending = []
        self.stream = bytearray()
        self.gen = generator or Generator('2022.3.43f1')
        if generator is None:
            for p in (self.data / 'Managed').glob('*.dll'):
                self.gen.load_dll(p.read_bytes())
        env = UnityPy.load(str(self.data / 'StreamingAssets/Windows/assets/content/location_objects/lootable/prefab/scontainer_crate.bundle'))
        self.bundle = next(iter(env.files.values()))
        self.asset = next((f for f in self.bundle.files.values() if hasattr(f, 'objects')))
        ab = next((o for o in self.asset.objects.values() if o.type.name == 'AssetBundle'))
        self.abtree = ab.read_typetree()
        ab = copy.copy(ab)
        ab.path_id = 1
        self.asset.objects = {1: ab}
        self.asset.types = [ab.serialized_type]
        ab.type_id = 0
        self.asset.externals = []
        self.asset.script_types = []
        self.asset._enable_type_tree = True
        self.bundle.files = {self.cab: self.asset}
        self.asset.name = self.cab
        self.types = {}
        self.roots = set()
        self.allowed = set()
        self.entries = []
        self.dependencies = {}

    def file(self, name):
        name = Path(name).name
        if name not in self.files:
            env = UnityPy.load(str(self.data / name))
            self.files[name] = next(iter(env.files.values()))
            self.names[id(self.files[name])] = name
        return self.files[name]

    def resolve(self, asset, ptr):
        if not ptr['m_PathID']:
            return None
        owner = self.file(asset.externals[ptr['m_FileID'] - 1].path) if ptr['m_FileID'] else asset
        return owner.objects[ptr['m_PathID']]

    def script_name(self, obj):
        if obj.type.name != 'MonoBehaviour':
            return ''
        fid, pid = struct.unpack_from('<iq', obj.get_raw_data(), 16)
        script = self.resolve(obj.assets_file, {'m_FileID': fid, 'm_PathID': pid}).read()
        return '.'.join(filter(None, [script.m_Namespace, script.m_ClassName]))

    def tree(self, obj):
        if obj.type.name != 'MonoBehaviour':
            return (obj.read_typetree(), None)
        fid, pid = struct.unpack_from('<iq', obj.get_raw_data(), 16)
        script = self.resolve(obj.assets_file, {'m_FileID': fid, 'm_PathID': pid}).read()
        name = '.'.join(filter(None, [script.m_Namespace, script.m_ClassName]))
        node = self.gen.get_nodes_up(script.m_AssemblyName, name)
        # The metadata generator supplies fields/alignment for decoding, but leaves
        # byte sizes and versions at zero. Unity requires real sizes to deserialize
        # missing/editor-only scripts safely from a type-tree-enabled bundle.
        complete_type_tree(node)
        try:
            return (obj.read_typetree(node), node)
        except Exception as e:
            raise ValueError(name + ': ' + str(e)) from e

    def key(self, obj):
        return (self.names[id(obj.assets_file)], obj.path_id)

    def add(self, obj):
        if obj is None:
            return 0
        key = self.key(obj)
        if key not in self.ids:
            self.ids[key] = len(self.ids) + 2
            self.pending.append(obj)
        return self.ids[key]

    def hierarchy(self, go):
        result = []
        pending = [go]
        while pending:
            obj = pending.pop()
            result.append(obj)
            if len(result) > 256:
                raise ValueError('Container root includes unrelated map hierarchy')
            for c in obj.read_typetree()['m_Component']:
                component = self.resolve(obj.assets_file, c['component'])
                if component.type.name == 'Transform':
                    pending.extend((self.resolve(component.assets_file, self.resolve(component.assets_file, p).read_typetree()['m_GameObject']) for p in component.read_typetree()['m_Children']))
        return result

    def select(self, scene, pid):
        source = self.file(scene)
        interaction = source.objects[pid]
        fields, _ = self.tree(interaction)
        go = self.resolve(source, fields['m_GameObject'])
        candidates = [go] + [self.resolve(source, p) for p in fields.get('GameObjectsToDestroy', []) if p['m_PathID']]
        selected = go
        for candidate in candidates:
            if candidate.type.name == 'GameObject':
                children = self.hierarchy(candidate)
                if any((self.key(c) == self.key(go) for c in children)):
                    selected = candidate
        # The interaction commonly lives on the lid; the immediate parent is the
        # native body/lid assembly. Do not absorb a multi-inventory cabinet or room.
        if self.key(selected) == self.key(go):
            transform = next(self.resolve(source, c['component']) for c in go.read_typetree()['m_Component']
                             if self.resolve(source, c['component']).type.name == 'Transform')
            parent = self.resolve(source, transform.read_typetree()['m_Father'])
            if parent is not None:
                candidate = self.resolve(source, parent.read_typetree()['m_GameObject'])
                try:
                    siblings = self.hierarchy(candidate)
                    native_count = sum(self.script_name(self.resolve(source, c['component'])) == 'EFT.Interactive.LootableContainer'
                                       for child in siblings for c in child.read_typetree()['m_Component'])
                    if native_count == 1:
                        selected = candidate
                except ValueError:
                    pass
        children = self.hierarchy(selected)
        components = []
        for child in children:
            self.allowed.add(self.key(child))
            for c in child.read_typetree()['m_Component']:
                comp = self.resolve(source, c['component'])
                self.allowed.add(self.key(comp))
                components.append(comp)
        interactions = []
        for c in components:
            if c.type.name == 'MonoBehaviour' and self.script_name(c) == 'EFT.Interactive.LootableContainer':
                t, _ = self.tree(c)
                if 'Template' in t and 'OpenPosition' in t:
                    interactions.append(c)
        if len(interactions) != 1:
            raise ValueError('Container model contains multiple native inventories')
        transform = next((c for c in selected.read_typetree()['m_Component'] if self.resolve(source, c['component']).type.name == 'Transform'))
        self.roots.add(self.key(self.resolve(source, transform['component'])))
        root = self.add(selected)
        return (root, selected.read().m_Name, fields['Template'])

    def remap(self, value, owner):
        if isinstance(value, dict):
            if 'm_FileID' in value and 'm_PathID' in value:
                obj = self.resolve(owner, value)
                if obj is not None and obj.type.name in ('GameObject', 'Transform', 'MonoBehaviour', 'MeshRenderer', 'BoxCollider', 'MeshCollider', 'LODGroup') and (self.key(obj) not in self.allowed):
                    raise ValueError('Container links to map-owned object ' + str(self.key(obj)))
                return {'m_FileID': 0, 'm_PathID': self.add(obj)}
            return {k: self.remap(v, owner) for k, v in value.items()}
        if isinstance(value, list):
            return [self.remap(v, owner) for v in value]
        if isinstance(value, tuple):
            return tuple((self.remap(v, owner) for v in value))
        return value

    def streams(self, t):
        for field in ('m_StreamData', 'm_Resource'):
            if field not in t:
                continue
            v = t[field]
            pathkey = 'path' if 'path' in v else 'm_Source'
            sizekey = 'size' if 'size' in v else 'm_Size'
            offsetkey = 'offset' if 'offset' in v else 'm_Offset'
            if not v[sizekey]:
                continue
            path = self.data / Path(v[pathkey]).name
            with path.open('rb') as f:
                f.seek(v[offsetkey])
                data = f.read(v[sizekey])
            if len(data) != v[sizekey]:
                raise ValueError('Truncated resource ' + str(path))
            v[offsetkey] = len(self.stream)
            v[pathkey] = f'archive:/{self.cab}/{self.cab}.resS'
            self.stream.extend(data)

    def build(self):
        index = 0
        while index < len(self.pending):
            obj = self.pending[index]
            index += 1
            t, node = self.tree(obj)
            kind = obj.type.name
            if kind == 'Transform' and self.key(obj) in self.roots:
                t['m_Father'] = {'m_FileID': 0, 'm_PathID': 0}
                t['m_LocalPosition'] = {'x': 0.0, 'y': 0.0, 'z': 0.0}
                t['m_LocalRotation'] = {'x': 0.0, 'y': 0.0, 'z': 0.0, 'w': 1.0}
            if kind == 'GameObject':
                t['m_StaticEditorFlags'] = 0
                t['m_Component'] = [c for c in t['m_Component'] if self.script_name(self.resolve(obj.assets_file, c['component'])) != 'EFT.Interactive.LootPoint']
            if kind == 'MonoBehaviour' and 'Template' in t and ('OpenPosition' in t):
                for k in ('TriggersMap', '_mboitRenderers', 'GameObjectsToDestroy'):
                    t[k] = []
                go = self.resolve(obj.assets_file, t['m_GameObject'])
                transform = next((self.resolve(go.assets_file, c['component']) for c in go.read_typetree()['m_Component'] if self.resolve(go.assets_file, c['component']).type.name == 'Transform'))
                if self.key(transform) in self.roots:
                    t['OpenPosition'] = {axis: t['OpenPosition'][axis] - t['ClosedPosition'][axis] for axis in ('x', 'y', 'z')}
                    t['ClosedPosition'] = {'x': 0.0, 'y': 0.0, 'z': 0.0}
                t['Id'] = 'wtt-library-' + t['Template']
                t['LootableContainersGroupId'] = ''
                t['IsAlwaysSpawn'] = 1
                t['SpawnChance'] = 100
            if kind == 'MeshRenderer':
                if t.get('m_StaticBatchInfo', {}).get('subMeshCount', 0):
                    raise ValueError('Source container uses combined static geometry')
                for k in ('m_StaticBatchRoot', 'm_ProbeAnchor', 'm_LightProbeVolumeOverride'):
                    if k in t:
                        t[k] = {'m_FileID': 0, 'm_PathID': 0}
                for k in ('m_LightmapIndex', 'm_LightmapIndexDynamic'):
                    if k in t:
                        t[k] = 65535
            self.streams(t)
            t = self.remap(t, obj.assets_file)
            self.dependencies[self.ids[self.key(obj)]] = list(referenced_ids(t))
            clone = copy.copy(obj)
            clone.assets_file = self.asset
            clone.path_id = self.ids[self.key(obj)]
            typekey = (self.names[id(obj.assets_file)], obj.type_id)
            if typekey not in self.types:
                typ = copy.copy(obj.serialized_type)
                typ.script_type_index = -1
                typ.type_dependencies = []
                typ.node = node or get_typetree_node(obj.class_id, obj.version)
                for ni, n in enumerate(typ.node.traverse()):
                    if n.m_TypeFlags is None:
                        n.m_TypeFlags = 1 if n.m_Type == 'Array' else 0
                    if n.m_Index is None:
                        n.m_Index = ni
                    if n.m_MetaFlag is None:
                        n.m_MetaFlag = 0
                    if n.m_RefTypeHash is None:
                        n.m_RefTypeHash = 0
                if kind == 'MonoBehaviour':
                    ref = object.__new__(LocalSerializedObjectIdentifier)
                    ref.local_serialized_file_index = 0
                    ref.local_identifier_in_file = t['m_Script']['m_PathID']
                    typ.script_type_index = len(self.asset.script_types)
                    self.asset.script_types.append(ref)
                self.types[typekey] = len(self.asset.types)
                self.asset.types.append(typ)
            clone.type_id = self.types[typekey]
            clone.serialized_type = self.asset.types[clone.type_id]
            clone.save_typetree(t, node)
            self.asset.objects[clone.path_id] = clone
        tree = self.abtree
        tree['m_Name'] = tree['m_AssetBundleName'] = self.name
        tree['m_Dependencies'] = []
        tree['m_SceneHashes'] = []
        tree['m_IsStreamedSceneAssetBundle'] = False
        tree['m_PreloadTable'] = []
        tree['m_Container'] = []
        for entry in self.entries:
            root = entry.pop('Root')
            seen, ordered = set(), []
            def visit(pid):
                if not pid or pid in seen:
                    return
                seen.add(pid)
                for dependency in self.dependencies.get(pid, []):
                    visit(dependency)
                ordered.append(pid)
            visit(root)
            offset = len(tree['m_PreloadTable'])
            tree['m_PreloadTable'].extend({'m_FileID': 0, 'm_PathID': pid} for pid in ordered)
            tree['m_Container'].append((entry['Asset'], {'preloadIndex': offset, 'preloadSize': len(ordered),
                'asset': {'m_FileID': 0, 'm_PathID': root}}))
        tree['m_MainAsset'] = {'preloadIndex': 0, 'preloadSize': 0, 'asset': {'m_FileID': 0, 'm_PathID': 0}}
        self.asset.objects[1].save_typetree(tree)
        self.bundle.files[self.cab + '.resS'] = Stream(bytes(self.stream))
        return self.bundle.save(packer='lz4')

def main():
    p = argparse.ArgumentParser()
    p.add_argument('--game', type=Path, default=Path(__file__).resolve().parents[3])
    p.add_argument('--inventory', type=Path)
    p.add_argument('--output', type=Path, required=True)
    p.add_argument('--limit', type=int, default=0)
    p.add_argument('--verify', action='store_true')
    a = p.parse_args()
    if a.verify:
        verify_library(a.output, a.game)
        return
    lib = Library(a.game)
    seen = set()
    locale = json.loads((a.game / 'SPT_Runtime/SPT_Data/database/locales/global/en.json').read_text(encoding='utf-8-sig'))
    templates = json.loads((a.game / 'SPT_Runtime/SPT_Data/database/templates/items.json').read_text(encoding='utf-8-sig'))
    inventory = json.loads(a.inventory.read_text()) if a.inventory else scan_inventory(a.game)
    for scene, pid, name, template in inventory:
        if template in seen:
            continue
        root, label, template = lib.select(scene, pid)
        seen.add(template)
        lib.entries.append({'Root': root, 'Name': locale.get(template + ' Name', templates.get(template, {}).get('_name', label)), 'Template': template, 'Asset': 'containers/' + template + '.prefab', 'Source': scene})
        if a.limit and len(seen) >= a.limit:
            break
    raw = lib.build()
    a.output.mkdir(parents=True, exist_ok=True)
    (a.output / 'native-containers.bundle').write_bytes(raw)
    manifest = {'Schema': 1, 'NativeAssemblySha256': hashlib.sha256((a.game / 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll').read_bytes()).hexdigest().upper(), 'Sha256': hashlib.sha256(raw).hexdigest().upper(), 'Entries': lib.entries}
    (a.output / 'catalog.json').write_text(json.dumps(manifest, indent=2))
    print('Exported', len(lib.entries), 'containers,', len(raw), 'bytes')

def scan_inventory(game):
    data = game / 'EscapeFromTarkov_Data'
    scripts = next(iter(UnityPy.load(str(data / 'globalgamemanagers.assets')).files.values())).objects
    script_ids = {pid for pid, obj in scripts.items() if obj.type.name == 'MonoScript' and obj.read().m_Namespace == 'EFT.Interactive' and (obj.read().m_ClassName == 'LootableContainer')}
    generator = Generator('2022.3.43f1')
    for path in (data / 'Managed').glob('*.dll'):
        generator.load_dll(path.read_bytes())
    node = generator.get_nodes_up('Assembly-CSharp.dll', 'EFT.Interactive.LootableContainer')
    inventory = []
    for path in sorted(data.glob('level*')):
        if path.suffix or not path.is_file():
            continue
        raw = path.read_bytes()
        if not any((struct.pack('<q', pid) in raw for pid in script_ids)):
            continue
        asset = next(iter(UnityPy.load(str(path)).files.values()))
        for obj in list(asset.objects.values()):
            if obj.type.name != 'MonoBehaviour':
                continue
            file_id, script_id = struct.unpack_from('<iq', obj.get_raw_data(), 16)
            if script_id not in script_ids or file_id < 1 or Path(asset.externals[file_id - 1].path).name != 'globalgamemanagers.assets':
                continue
            fields = obj.read_typetree(node)
            go = asset.objects[fields['m_GameObject']['m_PathID']].read()
            inventory.append((path.name, obj.path_id, go.m_Name, fields['Template']))
    return inventory

def verify_library(directory, game):
    manifest = json.loads((directory / 'catalog.json').read_text(encoding='utf-8'))
    bundle_path = directory / 'native-containers.bundle'
    assert manifest['Schema'] == 1
    assert hashlib.sha256(bundle_path.read_bytes()).hexdigest().upper() == manifest['Sha256'], 'Library hash mismatch'
    native = game / 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'
    assert hashlib.sha256(native.read_bytes()).hexdigest().upper() == manifest['NativeAssemblySha256'], 'Native assembly changed; rebuild the library'
    env = UnityPy.load(str(bundle_path))
    objects = {o.path_id: o for o in env.objects}

    def pointers(value):
        if isinstance(value, dict):
            if 'm_FileID' in value and 'm_PathID' in value:
                assert value['m_FileID'] == 0, 'External map dependency in container library'
                assert not value['m_PathID'] or value['m_PathID'] in objects, 'Missing container dependency'
            else:
                for child in value.values():
                    pointers(child)
        elif isinstance(value, (list, tuple)):
            for child in value:
                pointers(child)
    containers = []
    for obj in objects.values():
        tree = obj.read_typetree()
        pointers(tree)
        if obj.type.name == 'MonoBehaviour' and 'Template' in tree and ('OpenPosition' in tree):
            assert tree['TriggersMap'] == [] and tree['GameObjectsToDestroy'] == [], 'Map conversion ownership leaked into container prefab'
            containers.append(tree['Template'])
    assert sorted(containers) == sorted((e['Template'] for e in manifest['Entries'])), 'Catalog and native containers differ'
    assert len(set((e['Asset'] for e in manifest['Entries']))) == len(manifest['Entries'])
    assert set(env.container) == {e['Asset'] for e in manifest['Entries']}, 'Bundle asset names differ from catalog'
    print('PASS native container library:', len(containers), 'templates,', len(objects), 'self-contained serialized objects')
if __name__ == '__main__':
    main()
