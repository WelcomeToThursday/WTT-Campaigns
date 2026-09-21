"""Offline regressions for level scenery discovery, transforms and resource ownership."""
import copy
import os
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace

from level_prop_index import Level, clean_tree, multiply_quaternion, pointer_key, world_transform, source_stamp
from export_level_props import PropBundle, Resources
from export_container_library import referenced_ids


def transform(rotation=None, scale=None):
    return {'m_LocalRotation': rotation or dict(zip('xyzw', (0., 0., 0., 1.))),
            'm_LocalScale': scale or dict(zip('xyz', (1., 1., 1.)))}


class LevelPropTests(unittest.TestCase):
    def test_native_timestamp_rewrite_does_not_rebuild_library(self):
        with tempfile.TemporaryDirectory() as directory:
            game = Path(directory)
            data = game / 'EscapeFromTarkov_Data'
            (data / 'Managed').mkdir(parents=True)
            native = data / 'Managed/Assembly-CSharp.dll'
            native.write_bytes(b'unchanged native types')
            scene = data / 'level1'
            scene.write_bytes(b'scene data')
            stamp = source_stamp(game)
            os.utime(native, ns=(native.stat().st_atime_ns, native.stat().st_mtime_ns + 1000000000))
            self.assertEqual(source_stamp(game), stamp)
            native.write_bytes(b'changed native types')
            self.assertNotEqual(source_stamp(game), stamp)
            stamp = source_stamp(game)
            scene.write_bytes(b'changed scene data')
            self.assertNotEqual(source_stamp(game), stamp)

    def test_world_orientation_and_scale(self):
        q = dict(zip('xyzw', (0., 0., 2**-0.5, 2**-0.5)))
        rotation, scale = world_transform([transform(q), transform(scale=dict(zip('xyz', (2., 2., 2.))))])
        self.assertAlmostEqual(rotation['z'], q['z'])
        self.assertAlmostEqual(rotation['w'], q['w'])
        for value in scale.values():
            self.assertAlmostEqual(value, 2.)

    def test_mirrored_transform_is_preserved(self):
        _, scale = world_transform([transform(scale=dict(zip('xyz', (-1., 2., 3.))))])
        self.assertEqual(scale, {'x': -1., 'y': 2., 'z': 3.})

    def test_shear_and_zero_scale_fail_closed(self):
        q = dict(zip('xyzw', (0., 0., 0.3826834324, 0.9238795325)))
        with self.assertRaisesRegex(ValueError, 'shear'):
            world_transform([transform(q), transform(scale=dict(zip('xyz', (2., 1., 1.))))])
        with self.assertRaisesRegex(ValueError, 'zero scale'):
            world_transform([transform(scale=dict(zip('xyz', (0., 1., 1.))))])

    def test_resource_identity_uses_source_file_and_path_id(self):
        asset = SimpleNamespace(name='level54', externals=[SimpleNamespace(path='sharedassets2.assets')])
        self.assertEqual(pointer_key(asset, {'m_FileID': 1, 'm_PathID': 77}), ('sharedassets2.assets', 77))
        self.assertEqual(pointer_key(asset, {'m_FileID': 0, 'm_PathID': 77}), ('level54', 77))
        self.assertIsNone(pointer_key(asset, {'m_FileID': 0, 'm_PathID': 0}))

    def test_shared_resources_are_deduplicated_and_deterministic(self):
        obj = SimpleNamespace(assets_file=SimpleNamespace(name='sharedassets2.assets'), path_id=77,
                              type=SimpleNamespace(name='Mesh'))
        first, second = Resources(None), Resources(None)
        identity = first.add(obj)
        self.assertEqual(first.add(copy.copy(obj)), identity)
        self.assertEqual(second.add(obj), identity)
        self.assertEqual(len(first.keys), 1)
        obj.path_id = 78
        self.assertNotEqual(first.add(obj), identity)

    def test_prefab_roots_do_not_share_mutable_hierarchy_objects(self):
        bundle = object.__new__(PropBundle)
        bundle.scoped_objects, bundle.scopes, bundle.names = {}, {}, {}
        source = SimpleNamespace(assets_file=SimpleNamespace(name='level54'), path_id=17,
                                 type=SimpleNamespace(name='Transform'))
        a = bundle.scoped({'Id': 'a'}, source)
        b = bundle.scoped({'Id': 'b'}, source)
        self.assertIsNot(a, b)
        self.assertEqual(bundle.key(a), ('a:level54', 17))
        self.assertEqual(bundle.key(b), ('b:level54', 17))
        self.assertIs(bundle.scoped({'Id': 'a'}, source), a)

    def test_map_lighting_links_are_removed_without_changing_materials(self):
        materials = [{'m_FileID': 2, 'm_PathID': 30}]
        tree = clean_tree('MeshRenderer', {'m_ProbeAnchor': {'m_FileID': 0, 'm_PathID': 9},
                                          'm_LightmapIndex': 3, 'm_Materials': materials})
        self.assertEqual(tree['m_ProbeAnchor']['m_PathID'], 0)
        self.assertEqual(tree['m_LightmapIndex'], 65535)
        self.assertEqual(tree['m_Materials'], materials)

    def test_external_preloads_are_not_rewritten_as_local_ids(self):
        tree = [{'m_FileID': 0, 'm_PathID': 12}, {'m_FileID': 1, 'm_PathID': 999}, {'m_FileID': 0, 'm_PathID': 0}]
        self.assertEqual(list(referenced_ids(tree)), [12])

    def test_inactive_source_is_discovered_and_unsupported_components_are_rejected(self):
        level = object.__new__(Level)
        level.asset = SimpleNamespace(name='level1', externals=[SimpleNamespace(path='sharedassets2.assets')])
        ptr = lambda pid, fid=0: {'m_FileID': fid, 'm_PathID': pid}
        level.trees = {
            1: {'m_Name': 'inactive concrete', 'm_IsActive': False, 'm_Component': [{'component': ptr(p)} for p in (2, 3, 4)]},
            2: dict(transform(), m_GameObject=ptr(1), m_LocalPosition={'x': 90., 'y': 0., 'z': 0.}, m_Father=ptr(0), m_Children=[]),
            3: {'m_GameObject': ptr(1), 'm_Mesh': ptr(50, 1)},
            4: {'m_GameObject': ptr(1), 'm_Materials': [ptr(51, 1)]},
        }
        level.objects = {p: SimpleNamespace(type=SimpleNamespace(name=n)) for p, n in enumerate(
            ('GameObject', 'Transform', 'MeshFilter', 'MeshRenderer'), start=1)}
        level.components, level.transforms = {}, {1: 2}
        entry = level.candidate(1)
        level.trees[2]['m_LocalPosition']['x'] = 190.
        self.assertEqual(level.candidate(1)['Id'], entry['Id'], 'World position must not defeat geometry deduplication')
        level.objects[4].type.name = 'Animator'
        with self.assertRaisesRegex(ValueError, 'animated'):
            level.candidate(1)


if __name__ == '__main__':
    unittest.main()
