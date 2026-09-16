"""Recover KORD quest volumes from local EFT assets, without launching either game."""
import hashlib
import json
import math
import re
import struct
from pathlib import Path

import numpy as np
import UnityPy
from recover_ui import Generator

PROJECT = Path(__file__).resolve().parents[1]
SOURCE = Path(r'E:\EscapeFromTarkov')
DATA = SOURCE / 'EscapeFromTarkov_Data'
PATTERN = re.compile(rb'(?:shoreline_kord_(?:house|ges|mts)|kord_breach_wifi_\d+|(?:customs|woods)_destroy_cord_\d+)')


def main():
    scripts = next(iter(UnityPy.load(str(DATA / 'globalgamemanagers.assets')).files.values())).objects
    settings = UnityPy.load(str(DATA / 'globalgamemanagers'))
    scenes = next(o.read_typetree()['scenes'] for o in settings.objects if o.type.name == 'BuildSettings')
    generator = Generator('2022.3.43f2')
    generator.load_il2cpp((SOURCE / 'GameAssembly.dll').read_bytes(), (PROJECT.parent / '1.0 Metadata/global-metadata.dat').read_bytes())
    result, sources = [], []
    for level, location in [(29, 'shoreline'), (395, 'tarkovstreets'), (506, 'sandbox_high'), (514, 'bigmap'), (522, 'woods')]:
        path = DATA / f'level{level}'
        env = UnityPy.load(str(path))
        asset = next(iter(env.files.values()))
        sources.append({'File': str(path), 'Sha256': hashlib.sha256(path.read_bytes()).hexdigest()})

        def world(transform):
            fields = transform.read_typetree()
            x, y, z, w = (fields['m_LocalRotation'][key] for key in 'xyzw')
            matrix = np.eye(4)
            matrix[:3, :3] = [[1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w)],
                             [2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w)],
                             [2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y)]]
            matrix[:3, :3] *= [fields['m_LocalScale'][key] for key in 'xyz']
            matrix[:3, 3] = [fields['m_LocalPosition'][key] for key in 'xyz']
            parent = fields['m_Father']['m_PathID']
            return world(asset.objects[parent]) @ matrix if parent else matrix

        for obj in env.objects:
            if obj.type.name != 'MonoBehaviour' or not PATTERN.search(obj.get_raw_data()):
                continue
            _, sid = struct.unpack_from('<iq', obj.get_raw_data(), 16)
            script = scripts[sid].read()
            name = (script.m_Namespace + '.' if script.m_Namespace else '') + script.m_ClassName
            if script.m_ClassName not in ('ExperienceTrigger', 'PlaceItemTrigger', 'ShootableQuestLocationObject'):
                continue
            fields = obj.read_typetree(generator.get_nodes_up(script.m_AssemblyName, name))
            target = fields.get('_id') or fields.get('Id') or fields.get('_objectId')
            if not target:
                candidates = [value for value in fields.values() if isinstance(value, str) and PATTERN.fullmatch(value.encode())]
                if len(candidates) != 1:
                    raise ValueError((name, fields))
                target = candidates[0]
            go = asset.objects[fields['m_GameObject']['m_PathID']].read_typetree()
            if script.m_ClassName == 'ShootableQuestLocationObject':
                collider = asset.objects[fields['_ballisticCollider']['m_PathID']]
                raw = collider.get_raw_data()
                goid = struct.unpack_from('<iq', raw, 0)[1]
                go = asset.objects[goid].read_typetree()
            objects = [asset.objects[part['component']['m_PathID']] for part in go['m_Component']]
            transform = next(part for part in objects if part.type.name == 'Transform')
            box = next((part.read_typetree() for part in objects if part.type.name == 'BoxCollider'), None)
            if box is None:
                mesh_collider = next(part.read_typetree() for part in objects if part.type.name == 'MeshCollider')
                ptr = mesh_collider['m_Mesh']
                mesh_file = DATA / asset.externals[ptr['m_FileID'] - 1].path
                mesh_asset = next(iter(UnityPy.load(str(mesh_file)).files.values()))
                bounds = mesh_asset.objects[ptr['m_PathID']].read_typetree()['m_LocalAABB']
                box = {'m_Center': bounds['m_Center'], 'm_Size': {key: bounds['m_Extent'][key] * 2 for key in 'xyz'}}
            matrix = world(transform)
            center = matrix @ np.array([box['m_Center'][key] for key in 'xyz'] + [1.0])
            scale = np.linalg.norm(matrix[:3, :3], axis=0)
            rotation = matrix[:3, :3] / scale
            angles = [math.asin(max(-1, min(1, -rotation[1, 2]))), math.atan2(rotation[0, 2], rotation[2, 2]), math.atan2(rotation[1, 0], rotation[1, 1])]
            vector = lambda values: dict(zip('XYZ', [round(float(v), 5) for v in values]))
            result.append({'Target': target, 'Component': name, 'Location': location, 'Scene': Path(scenes[level]).stem,
                           'Position': vector(center[:3]), 'Rotation': vector(np.degrees(angles)),
                           'Size': vector(scale * [box['m_Size'][key] for key in 'xyz'])})
    if len(result) != 13:
        raise ValueError(f'Expected 13 KORD volumes, found {len(result)}')
    (PROJECT / 'data/kord-geometry.json').write_text(json.dumps({'Sources': sources, 'Volumes': result}, indent=2) + '\n', encoding='utf-8')
    print(f'Recovered {len(result)} source quest volumes.')


if __name__ == '__main__':
    main()
