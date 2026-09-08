"""Supply serialized type trees to the scene exporter without loading live game code.

Only the selected scene is rewritten. Its original dependencies are linked read-only
by convention in an intermediate directory; neither this script nor the exporter
writes to those dependency files. Output must never be installed in the game.
"""
import argparse
import hashlib
import json
import os
import shutil
import struct
from pathlib import Path

import UnityPy
from UnityPy.helpers.Tpk import get_typetree_node
from UnityPy.helpers.UnityVersion import UnityVersion
from UnityPy.helpers.TypeTreeNode import TypeTreeNode
from recover_ui import DEV, LIVE, Generator


def dictionary_nodes(generator, assembly, name):
    """Resolve the serialized generic entry fields using their concrete value type."""
    values = {
        "EFT.AnimationSequencePlayer.AnimationDictionary": "EFT.AnimationSequencePlayer.AnimationElement",
        "EFT.AnimationSequencePlayer.SecondaryAnimationDictionary": "EFT.AnimationSequencePlayer.AnimationElement",
        "EFT.AnimationSequencePlayer.LipSyncDictionary": "EFT.AnimationSequencePlayer.LipSyncElement",
    }
    value = values.get(name)
    if value is None:
        return generator.get_nodes_up(assembly, name)
    source = generator.get_nodes(assembly.removesuffix('.dll'), value)
    nodes = []
    def add(level, kind, field, flags=0):
        nodes.append(TypeTreeNode(level, kind, field, 0, 0, m_MetaFlag=flags))
    # The generator prepends the MonoBehaviour header even for a serializable value.
    start = next(i for i, n in enumerate(source) if n.m_Level == 1 and n.m_Name == 'info')
    for n in source[:start]:
        add(n.m_Level, 'MonoBehaviour' if n.m_Level == 0 else n.m_Type, n.m_Name,
            n.m_MetaFlag | (16384 if n.m_Name == 'm_Enabled' else 0))
    add(1, 'vector', 'entries')
    add(2, 'Array', 'Array', 16384)
    add(3, 'int', 'size')
    add(3, 'SerializableKeyValuePair', 'data')
    add(4, 'string', 'key')
    add(5, 'Array', 'Array', 16384)
    add(6, 'int', 'size')
    add(6, 'char', 'data')
    add(4, value.rsplit('.', 1)[1], 'value')
    for n in source[start:]:
        add(n.m_Level + 4, n.m_Type.replace('$', ''), n.m_Name, n.m_MetaFlag)
    return TypeTreeNode.from_list(nodes)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--level", type=int, default=642)
    args = parser.parse_args()
    if args.level not in range(638, 646):
        raise ValueError("Only the eight trader-room scenes are accepted.")
    source = LIVE / "EscapeFromTarkov_Data"
    target = DEV / "SeasonalPerks/Research/Story/TypedInput"
    target.mkdir(parents=True, exist_ok=True)
    scene_name = f"level{args.level}"
    for path in source.iterdir():
        if not path.is_file() or path.name.startswith("level") and path.name != scene_name + ".resS":
            continue
        link = target / path.name
        if not link.exists():
            try:
                os.symlink(path, link)
            except OSError:
                shutil.copyfile(path, link)
    scripts = next(iter(UnityPy.load(str(source / "globalgamemanagers.assets")).files.values())).objects
    generator = Generator("2022.3.43f2")
    generator.load_il2cpp((LIVE / "GameAssembly.dll").read_bytes(), (DEV / "1.0 Metadata/global-metadata.dat").read_bytes())
    env = UnityPy.load(str(source / scene_name))
    asset = next(f for f in env.files.values() if hasattr(f, "objects"))
    audit = []
    for serialized in asset.types:
        obj = next((o for o in asset.objects.values() if o.serialized_type is serialized), None)
        node = get_typetree_node(serialized.class_id, obj.version if obj is not None else UnityVersion.from_str(asset.unity_version))
        if obj is not None and obj.type.name == "MonoBehaviour":
            fid, pid = struct.unpack_from("<iq", obj.get_raw_data(), 16)
            if fid == 1 and pid:
                script = scripts[pid].read()
                name = ".".join(filter(None, [script.m_Namespace, script.m_ClassName]))
                try:
                    candidate = dictionary_nodes(generator, script.m_AssemblyName, name)
                    obj.read_typetree(candidate)
                    node = candidate
                    audit.append({"id": obj.path_id, "type": name, "restored": True})
                except Exception as error:
                    audit.append({"id": obj.path_id, "type": name, "restored": False, "error": str(error)})
            else:
                audit.append({"id": obj.path_id, "type": "Missing script", "restored": False})
        serialized.node = node
        for index, child in enumerate(node.traverse()):
            if child.m_TypeFlags is None:
                child.m_TypeFlags = 1 if child.m_Type == "Array" else 0
            if child.m_Index is None:
                child.m_Index = index
            if child.m_MetaFlag is None:
                child.m_MetaFlag = 0
            if child.m_RefTypeHash is None:
                child.m_RefTypeHash = 0
        serialized.type_dependencies = []
    asset._enable_type_tree = True
    output = target / scene_name
    if output.exists() and os.path.samefile(output, source / scene_name):
        raise ValueError("Refusing to rewrite a linked source scene.")
    output.write_bytes(asset.save())
    (target.parent / f"typed-{scene_name}.json").write_text(json.dumps({"source": str(source / scene_name),
        "sourceSha256": hashlib.sha256((source / scene_name).read_bytes()).hexdigest(),
        "outputSha256": hashlib.sha256(output.read_bytes()).hexdigest(), "types": audit}, indent=2))
    print("Restored", sum(a["restored"] for a in audit), "of", len(audit), "script layouts in", output)


if __name__ == "__main__":
    main()
