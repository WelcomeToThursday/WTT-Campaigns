"""Restore metadata for lip-sync data referenced by the eight trader rooms."""
import hashlib
import json
import os
import struct
import UnityPy
from UnityPy.helpers.Tpk import get_typetree_node
from recover_ui import DEV, LIVE, Generator
from prepare_story_scene import dictionary_nodes


def main():
    source = LIVE / 'EscapeFromTarkov_Data'
    target = DEV / 'SeasonalPerks/Research/Story/TypedInput'
    scripts = next(iter(UnityPy.load(str(source / 'globalgamemanagers.assets')).files.values())).objects
    generator = Generator('2022.3.43f2')
    generator.load_il2cpp((LIVE / 'GameAssembly.dll').read_bytes(), (DEV / '1.0 Metadata/global-metadata.dat').read_bytes())
    audit = []
    sdk = json.loads((target.parent / 'sdk-assets.json').read_text())['scripts']
    failures = set()
    for path in sorted(source.glob('sharedassets*.assets')):
        env = UnityPy.load(str(path))
        asset = next(f for f in env.files.values() if hasattr(f, 'objects'))
        script_files = {i + 1 for i, external in enumerate(asset.externals) if external.path.lower().endswith('globalgamemanagers.assets')}
        restored = {}
        for obj in asset.objects.values():
            if obj.type.name != 'MonoBehaviour':
                continue
            fid, pid = struct.unpack_from('<iq', obj.get_raw_data(), 16)
            if fid not in script_files or pid not in scripts:
                continue
            script = scripts[pid].read()
            name = '.'.join(filter(None, [script.m_Namespace, script.m_ClassName]))
            relevant = name.startswith(('uLipSync.', 'EFT.AnimationSequencePlayer.', 'AnimationEventSystem.', 'EFT.NPC')) or name in ('EFT.AnimatorResetter', 'EFT.AudioSequence', 'LightKeeperEyeTargetFollower', 'LightKeeperEyesBlinking')
            if not relevant or name not in sdk or name in failures:
                continue
            try:
                node = dictionary_nodes(generator, script.m_AssemblyName, name)
                tree = obj.read_typetree(node)
            except Exception as error:
                failures.add(name)
                audit.append({'source': path.name, 'type': name, 'error': str(error)})
                continue
            restored[id(obj.serialized_type)] = node
            audit.append({'source': path.name, 'object': obj.path_id, 'type': name, 'name': tree.get('m_Name')})
        if not restored:
            continue
        for serialized in asset.types:
            obj = next(o for o in asset.objects.values() if o.serialized_type is serialized)
            node = restored.get(id(serialized)) or get_typetree_node(serialized.class_id, obj.version)
            serialized.node = node
            for index, child in enumerate(node.traverse()):
                if child.m_TypeFlags is None:
                    child.m_TypeFlags = 1 if child.m_Type == 'Array' else 0
                if child.m_Index is None:
                    child.m_Index = index
                if child.m_MetaFlag is None:
                    child.m_MetaFlag = 0
                if child.m_RefTypeHash is None:
                    child.m_RefTypeHash = 0
            serialized.type_dependencies = []
        output = target / path.name
        if os.path.samefile(output, path):
            raise ValueError('Refusing to write to the live asset file.')
        asset._enable_type_tree = True
        output.write_bytes(asset.save())
        print(path.name, len(restored), 'lip-sync layouts restored', flush=True)
    (target.parent / 'typed-dependencies.json').write_text(json.dumps(audit, indent=2))


if __name__ == '__main__':
    main()
