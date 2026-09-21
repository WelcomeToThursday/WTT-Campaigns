"""Read-only scenery discovery in installed Unity level files. No Unity scene is loaded."""
import argparse
import collections
import gc
import hashlib
import json
import re
import struct
from pathlib import Path

import numpy as np
import UnityPy

SCHEMA = 1
COMPONENTS = {'Transform', 'MeshFilter', 'MeshRenderer', 'LODGroup', 'BoxCollider',
              'SphereCollider', 'CapsuleCollider', 'MeshCollider'}
SCRIPTS = {'EFT.Ballistics.BallisticCollider', 'PreviewPivot'}
NULL = {'m_FileID': 0, 'm_PathID': 0}


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(',', ':')).encode()).hexdigest()


def source_stamp(game):
    data = game / 'EscapeFromTarkov_Data'
    paths = sorted(p for p in data.iterdir() if p.is_file() and
                   (re.fullmatch(r'level\d+(?:\.resS)?', p.name) or p.suffix in ('.assets', '.resS', '.resource') or p.name == 'globalgamemanagers'))
    # SPT can rewrite the same patched assembly at each client startup. Its content,
    # not that rewrite's timestamp, determines serialized native component types.
    with (data / 'Managed/Assembly-CSharp.dll').open('rb') as native:
        assembly_hash = hashlib.file_digest(native, 'sha256').hexdigest()
    return digest([[(p.name, p.stat().st_size, p.stat().st_mtime_ns) for p in paths], assembly_hash])


def clean_name(name):
    return re.sub(r'(?:\s*\(\d+\))+$', '', name).strip() or 'Scenery'


def clean_tree(kind, tree):
    if kind == 'MeshRenderer':
        for name in ('m_StaticBatchRoot', 'm_ProbeAnchor', 'm_LightProbeVolumeOverride'):
            if name in tree:
                tree[name] = dict(NULL)
        for name in ('m_LightmapIndex', 'm_LightmapIndexDynamic'):
            if name in tree:
                tree[name] = 65535
        for name in ('m_LightmapTilingOffset', 'm_LightmapTilingOffsetDynamic'):
            if name in tree:
                tree[name] = {'x': 1.0, 'y': 1.0, 'z': 0.0, 'w': 0.0}
    if kind == 'GameObject':
        tree['m_StaticEditorFlags'] = 0
    return tree


def pointer_key(asset, pointer):
    pid = pointer['m_PathID']
    if not pid:
        return None
    owner = Path(asset.externals[pointer['m_FileID'] - 1].path).name if pointer['m_FileID'] else asset.name
    return owner, pid


def quaternion_matrix(q):
    x, y, z, w = (q[k] for k in 'xyzw')
    return np.array([[1-2*y*y-2*z*z, 2*x*y-2*z*w, 2*x*z+2*y*w],
                     [2*x*y+2*z*w, 1-2*x*x-2*z*z, 2*y*z-2*x*w],
                     [2*x*z-2*y*w, 2*y*z+2*x*w, 1-2*x*x-2*y*y]])


def multiply_quaternion(a, b):
    x, y, z, w = (a[k] for k in 'xyzw')
    X, Y, Z, W = (b[k] for k in 'xyzw')
    return dict(zip('xyzw', (w*X+x*W+y*Z-z*Y, w*Y-x*Z+y*W+z*X,
                            w*Z+x*Y-y*X+z*W, w*W-x*X-y*Y-z*Z)))


def world_transform(chain):
    rotation = dict(zip('xyzw', (0., 0., 0., 1.)))
    basis = np.eye(3)
    for tree in reversed(chain):
        rotation = multiply_quaternion(rotation, tree['m_LocalRotation'])
        basis = basis @ quaternion_matrix(tree['m_LocalRotation']) @ np.diag([tree['m_LocalScale'][k] for k in 'xyz'])
    scale_matrix = quaternion_matrix(rotation).T @ basis
    scale = np.diag(scale_matrix)
    if not np.isfinite(basis).all() or np.max(np.abs(scale_matrix - np.diag(scale))) > 0.001:
        raise ValueError('Source transform contains shear and cannot be copied independently.')
    if min(abs(v) for v in scale) < 1e-6:
        raise ValueError('Source geometry has a zero scale.')
    return rotation, dict(zip('xyz', (float(v) for v in scale)))


class Level:
    def __init__(self, path, scripts):
        self.asset = next(iter(UnityPy.load(str(path)).files.values()))
        self.asset.name = path.name
        self.objects = self.asset.objects
        self.scripts = scripts
        self.trees = {}
        self.components = {}
        self.transforms = {}
        self.groups = set()
        self.renderers = []
        for pid, obj in self.objects.items():
            if obj.type.name in ('Transform', 'MeshRenderer', 'LODGroup'):
                tree = self.tree(pid)
                go = tree['m_GameObject']['m_PathID']
                if obj.type.name == 'Transform':
                    self.transforms[go] = pid
                elif obj.type.name == 'LODGroup':
                    self.groups.add(go)
                else:
                    self.renderers.append(go)

    def tree(self, pid):
        if pid not in self.trees:
            self.trees[pid] = self.objects[pid].read_typetree()
        return self.trees[pid]

    def parts(self, go):
        if go not in self.components:
            self.components[go] = [p['component']['m_PathID'] for p in self.tree(go)['m_Component']]
        return self.components[go]

    def script(self, pid):
        file_id, script_id = struct.unpack_from('<iq', self.objects[pid].get_raw_data(), 16)
        key = pointer_key(self.asset, {'m_FileID': file_id, 'm_PathID': script_id})
        return self.scripts.get(key, 'Unknown script')

    def ancestors(self, go):
        seen = set()
        pid = self.transforms[go]
        while pid:
            if pid in seen or len(seen) > 512:
                raise ValueError('Invalid source hierarchy.')
            seen.add(pid)
            tree = self.tree(pid)
            yield tree
            pid = tree['m_Father']['m_PathID']

    def root(self, go):
        root = go
        for tree in self.ancestors(go):
            parent = tree['m_GameObject']['m_PathID']
            if parent in self.groups:
                root = parent
                break
        return root

    def candidate(self, root):
        chain = list(self.ancestors(root))
        for ancestor in chain:
            for pid in self.parts(ancestor['m_GameObject']['m_PathID']):
                kind = self.objects[pid].type.name
                if kind in ('Animator', 'Animation', 'Rigidbody', 'SkinnedMeshRenderer'):
                    raise ValueError('This geometry belongs to an animated or dynamic object.')
                if kind == 'MonoBehaviour':
                    script = self.script(pid)
                    if script.startswith(('EFT.Interactive.', 'EFT.Player', 'EFT.InventoryLogic.')):
                        raise ValueError('This geometry belongs to a native gameplay object: ' + script)
        rotation, scale = world_transform(chain)
        pending, nodes, parts = [root], [], []
        while pending:
            go = pending.pop()
            if go in nodes or len(nodes) >= 256:
                raise ValueError('The source hierarchy is too large to treat as one prop.')
            nodes.append(go)
            for pid in self.parts(go):
                if pid not in self.objects:
                    raise ValueError('A source component is missing.')
                kind = self.objects[pid].type.name
                if kind not in COMPONENTS and not (kind == 'MonoBehaviour' and self.script(pid) in SCRIPTS):
                    name = self.script(pid) if kind == 'MonoBehaviour' else kind
                    raise ValueError('This component needs a movement adapter: ' + name)
                parts.append(pid)
                if kind == 'Transform':
                    for child in reversed(self.tree(pid)['m_Children']):
                        pending.append(self.tree(child['m_PathID'])['m_GameObject']['m_PathID'])
                elif kind == 'MeshRenderer':
                    tree = self.tree(pid)
                    if tree.get('m_StaticBatchInfo', {}).get('subMeshCount', 0):
                        raise ValueError('Combined map geometry cannot be placed independently.')
                    if not tree['m_Materials'] or any(not p['m_PathID'] for p in tree['m_Materials']):
                        raise ValueError('A required material is missing.')
                elif kind == 'MeshFilter' and not self.tree(pid)['m_Mesh']['m_PathID']:
                    raise ValueError('A required mesh is missing.')
        if not any(self.objects[p].type.name == 'MeshFilter' for p in parts):
            raise ValueError('No independently placeable mesh.')
        allowed = nodes + parts
        identities = {pid: i for i, pid in enumerate(allowed)}
        def normalize(value):
            if isinstance(value, dict):
                if 'm_FileID' in value and 'm_PathID' in value:
                    key = pointer_key(self.asset, value)
                    if key and key[0] == self.asset.name and key[1] in identities:
                        return ['local', identities[key[1]]]
                    if key and key[0] == self.asset.name and self.objects[key[1]].type.name in COMPONENTS | {'GameObject', 'MonoBehaviour'}:
                        raise ValueError('The prop links to an object outside its hierarchy.')
                    return key
                return {k: normalize(v) for k, v in value.items()}
            if isinstance(value, (list, tuple)):
                return [normalize(v) for v in value]
            return round(value, 5) if isinstance(value, float) else value
        shape = []
        for pid in allowed:
            obj = self.objects[pid]
            if obj.type.name == 'MonoBehaviour':
                # Full script references are verified again by the exporter; raw IDs are not geometry identity.
                shape.append([obj.type.name, self.script(pid), obj.get_raw_data()[28:].hex()])
                continue
            tree = clean_tree(obj.type.name, dict(self.tree(pid)))
            if obj.type.name == 'GameObject':
                tree.pop('m_Name', None)
            if pid == self.transforms[root]:
                tree['m_Father'] = dict(NULL)
                tree['m_LocalPosition'] = dict(zip('xyz', (0., 0., 0.)))
                tree['m_LocalRotation'] = dict(zip('xyzw', (0., 0., 0., 1.)))
                tree['m_LocalScale'] = scale
            shape.append([obj.type.name, normalize(tree)])
        return {'Id': digest(shape), 'Root': root, 'Transform': self.transforms[root],
                'Allowed': allowed, 'Rotation': rotation, 'Scale': scale}


def scan(game, output):
    data = game / 'EscapeFromTarkov_Data'
    settings = next(o for o in UnityPy.load(str(data / 'globalgamemanagers')).objects if o.type.name == 'BuildSettings').read_typetree()
    scripts_file = next(iter(UnityPy.load(str(data / 'globalgamemanagers.assets')).files.values()))
    scripts = {}
    for pid, obj in scripts_file.objects.items():
        if obj.type.name == 'MonoScript':
            script = obj.read()
            scripts[('globalgamemanagers.assets', pid)] = '.'.join(filter(None, (script.m_Namespace, script.m_ClassName)))
    del scripts_file
    entries = {}
    counts = collections.Counter()
    sources = []
    levels = sorted((p for p in data.glob('level*') if re.fullmatch(r'level\d+', p.name)), key=lambda p: int(p.name[5:]))
    for number, path in enumerate(levels):
        level = Level(path, scripts)
        index = int(path.name[5:])
        scene = settings['scenes'][index] if index < len(settings['scenes']) else path.name
        sources.append({'File': path.name, 'Scene': scene, 'Objects': len(level.objects)})
        seen = set()
        for go in level.renderers:
            root = level.root(go)
            if root in seen:
                continue
            seen.add(root)
            name = clean_name(level.tree(root)['m_Name'])
            counts['Candidates'] += 1
            try:
                entry = level.candidate(root)
                error = ''
                counts['SupportedInstances'] += 1
            except (ValueError, KeyError) as e:
                error = str(e)
                entry = {'Id': digest([name, error])}
                counts['UnavailableInstances'] += 1
            if entry['Id'] not in entries:
                entry.update(Name=name, Source=path.name, Scene=scene, Error=error, Sources=[], Aliases=[])
                entries[entry['Id']] = entry
            entry = entries[entry['Id']]
            if scene not in entry['Sources']:
                entry['Sources'].append(scene)
            if name not in entry['Aliases']:
                entry['Aliases'].append(name)
        del level
        if number % 10 == 0 or number == len(levels)-1:
            print(f'Level index {number+1}/{len(levels)}: {len(entries)} unique entries; {counts["SupportedInstances"]} supported instances', flush=True)
            gc.collect()
    result = {'Schema': SCHEMA, 'SourceStamp': source_stamp(game), 'Levels': sources, 'Counts': dict(counts), 'Entries': list(entries.values())}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, separators=(',', ':')), encoding='utf-8')
    print(f'Indexed {len(levels)} levels: {len(entries)} entries', flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    scan(args.game, args.output)
