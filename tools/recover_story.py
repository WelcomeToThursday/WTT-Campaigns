"""Recover reusable story UI and audit trader scene dependencies from the local live build.

No quest definitions, dialogue text, placements, or executable assemblies are exported.
The output is an intermediate recovery inventory, not an installable asset bundle.
"""
import hashlib
import json
import struct
from pathlib import Path

import UnityPy
from PIL import Image
from UnityPy.classes import PPtr
from recover_ui import DEV, LIVE, Generator

DATA = LIVE / "EscapeFromTarkov_Data"
OUTPUT = DEV / "CJ-SDK/Assets/Mods/SeasonalPerks.Assets/StoryArtwork"
AUDIT = DEV / "SeasonalPerks/Research/Story"
ROOTS = {
    44: [3857, 7225, 6704, 4478, 6048, 502, 6551, 3663, 2044, 5810, 6025],
    48: [49, 5486, 9865, 1669],
    49: [2206, 1912],
}
TRADERS = {638: "Fence", 639: "Jaeger", 640: "Mechanic", 641: "Peacekeeper",
           642: "Prapor", 643: "Ragman", 644: "Skier", 645: "Therapist"}


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    AUDIT.mkdir(parents=True, exist_ok=True)
    generator = Generator("2022.3.43f2")
    generator.load_il2cpp((LIVE / "GameAssembly.dll").read_bytes(),
                         (DEV / "1.0 Metadata/global-metadata.dat").read_bytes())
    scripts = next(iter(UnityPy.load(str(DATA / "globalgamemanagers.assets")).files.values())).objects
    art, sources = {}, {}

    def fingerprint(path):
        path = Path(path)
        key = str(path)
        if key not in sources:
            sources[key] = hashlib.sha256(path.read_bytes()).hexdigest()
        return sources[key]

    def fields(obj):
        fid, pid = struct.unpack_from("<iq", obj.get_raw_data(), 16)
        if fid != 1:
            raise ValueError(f"Unexpected script reference {fid}:{pid}")
        script = scripts[pid].read()
        name = ".".join(filter(None, [script.m_Namespace, script.m_ClassName]))
        return name, obj.read_typetree(generator.get_nodes_up(script.m_AssemblyName, name))

    def sprite(pointer):
        obj = pointer.deref()
        key = Path(obj.assets_file.name).stem + "-" + str(obj.path_id)
        if key not in art:
            value = obj.read()
            canvas = Image.new("RGBA", (round(value.m_Rect.width), round(value.m_Rect.height)))
            offset = value.m_RD.textureRectOffset
            canvas.paste(value.image, (round(offset.x), canvas.height - round(offset.y) - value.image.height))
            path = OUTPUT / (key + ".png")
            canvas.save(path)
            art[key] = {"file": path.name, "name": value.m_Name, "source": obj.assets_file.name,
                        "objectId": obj.path_id, "sourceSha256": fingerprint(DATA / Path(obj.assets_file.name).name),
                        "width": canvas.width, "height": canvas.height,
                        "border": [value.m_Border.x, value.m_Border.y, value.m_Border.z, value.m_Border.w],
                        "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
        return key

    def visit(obj):
        go = obj.read()
        node = {"id": obj.path_id, "name": go.m_Name, "active": bool(go.m_IsActive), "components": [], "children": []}
        for component in go.m_Component:
            value = component.component.deref()
            if value.type.name in ["Transform", "RectTransform"]:
                node["rect"] = value.read_typetree()
                node["children"] = [visit(p.read().m_GameObject.deref()) for p in value.read().m_Children]
                continue
            entry = {"id": value.path_id, "type": value.type.name}
            try:
                if value.type.name == "MonoBehaviour":
                    entry["type"], entry["fields"] = fields(value)
                    if entry["type"] == "UnityEngine.UI.Image":
                        ptr = entry["fields"].get("m_Sprite", {})
                        if ptr.get("m_PathID"):
                            entry["artwork"] = sprite(PPtr(m_FileID=ptr["m_FileID"], m_PathID=ptr["m_PathID"], assetsfile=value.assets_file))
                else:
                    entry["fields"] = value.read_typetree()
            except Exception as error:
                entry["error"] = str(error)
            node["components"].append(entry)
        return node

    for level, roots in ROOTS.items():
        path = DATA / f"level{level}"
        env = UnityPy.load(str(path))
        objects = next(f for f in env.files.values() if hasattr(f, "objects")).objects
        fingerprint(path)
        for root in roots:
            tree = visit(objects[root])
            (AUDIT / f"level{level}-{root}.json").write_text(json.dumps(tree, indent=2), encoding="utf-8")
            print(level, root, tree["name"], flush=True)
    for level, name in TRADERS.items():
        path = DATA / f"level{level}"
        env = UnityPy.load(str(path))
        asset = next(f for f in env.files.values() if hasattr(f, "objects"))
        inventory = {"name": name, "source": str(path), "sha256": fingerprint(path),
                     "dependencies": [e.path for e in asset.externals], "types": {}, "scripts": {}, "npcs": []}
        for obj in asset.objects.values():
            kind = obj.type.name
            inventory["types"][kind] = inventory["types"].get(kind, 0) + 1
            if kind != "MonoBehaviour":
                continue
            try:
                script, data = fields(obj)
                inventory["scripts"][script] = inventory["scripts"].get(script, 0) + 1
                if "NPCObject" in script or "SequenceReader" in script or "AnimationController" in script:
                    inventory["npcs"].append({"id": obj.path_id, "type": script, "fields": data})
            except Exception as error:
                inventory.setdefault("errors", []).append({"id": obj.path_id, "error": str(error)})
        (AUDIT / f"trader-{name.lower()}.json").write_text(json.dumps(inventory, indent=2), encoding="utf-8")
        print(name, inventory["types"].get("SkinnedMeshRenderer", 0), "skinned renderers", flush=True)
    for path in [LIVE / "GameAssembly.dll", DEV / "1.0 Metadata/global-metadata.dat"]:
        fingerprint(path)
    (OUTPUT / "provenance.json").write_text(json.dumps(list(art.values()), indent=2), encoding="utf-8")
    (AUDIT / "sources.json").write_text(json.dumps(sources, indent=2), encoding="utf-8")
    print("Recovered", len(art), "sprites; trader inventories require conversion before deployment.")


if __name__ == "__main__":
    main()
