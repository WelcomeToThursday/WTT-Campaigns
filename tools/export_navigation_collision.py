"""Read-only extraction of native collision settings unavailable in the runtime API.

This pilot catalogue covers Interchange only. Regenerate deliberately after changing
the installed game; --verify never rewrites either game files or the catalogue.
"""
import argparse
import hashlib
import json
from pathlib import Path

import UnityPy


def extract(game):
    data = game / "EscapeFromTarkov_Data"
    settings = next(o for o in UnityPy.load(str(data / "globalgamemanagers")).objects if o.type.name == "BuildSettings").read_typetree()
    scene_path = "Assets/Content/Locations/Shopping_Mall/Shopping_Mall_Terrain.unity"
    index = settings["scenes"].index(scene_path)
    level = "level" + str(index)
    asset = next(iter(UnityPy.load(str(data / level)).files.values()))
    objects = asset.objects
    transforms = {o.read_typetree()["m_GameObject"]["m_PathID"]: o for o in objects.values() if o.type.name == "Transform"}
    files = {"globalgamemanagers", level, "Managed/UnityEngine.TerrainPhysicsModule.dll"}
    external_cache = {}
    entries = []
    for obj in objects.values():
        if obj.type.name != "TerrainCollider":
            continue
        fields = obj.read_typetree()
        # A missing setting is unknown, never an implicit false.
        enabled = fields["m_EnableTreeColliders"]
        go_id = fields["m_GameObject"]["m_PathID"]
        go = objects[go_id].read_typetree()
        if sum(objects[c["component"]["m_PathID"]].type.name == "TerrainCollider" for c in go["m_Component"]) != 1:
            raise ValueError("Ambiguous TerrainCollider owner")
        hierarchy = []
        at = transforms[go_id]
        while at:
            tr = at.read_typetree()
            name = objects[tr["m_GameObject"]["m_PathID"]].read_typetree()["m_Name"]
            parent_id = tr["m_Father"]["m_PathID"]
            parent = objects[parent_id] if parent_id else None
            sibling = next((i for i, child in enumerate(parent.read_typetree()["m_Children"]) if child["m_PathID"] == at.path_id), -1) if parent else -1
            hierarchy.insert(0, {"Name": name, "Sibling": sibling,
                                 "Position": list(tr["m_LocalPosition"].values()),
                                 "Rotation": list(tr["m_LocalRotation"].values()),
                                 "Scale": list(tr["m_LocalScale"].values())})
            at = parent
        pointer = fields["m_TerrainData"]
        file_name = Path(asset.externals[pointer["m_FileID"] - 1].path).name
        files.add(file_name)
        if file_name not in external_cache:
            external_cache[file_name] = next(iter(UnityPy.load(str(data / file_name)).files.values()))
        terrain = external_cache[file_name].objects[pointer["m_PathID"]].read_typetree()
        heightmap = terrain["m_Heightmap"]
        scale = heightmap["m_Scale"]
        resolution = heightmap["m_Resolution"]
        entries.append({"ScenePath": scene_path, "BuildIndex": index, "Hierarchy": hierarchy,
                        "EnableTreeColliders": enabled, "DataName": terrain["m_Name"], "DataFile": file_name,
                        "Resolution": resolution, "TreeCount": len(terrain["m_DetailDatabase"]["m_TreeInstances"]),
                        "Size": [scale["x"] * (resolution - 1), scale["y"], scale["z"] * (resolution - 1)]})
    if len(entries) != 4:
        raise ValueError("Pilot scene no longer contains exactly four terrain colliders; review the adapter")
    return {"Schema": 1, "Files": [{"Path": name, "Sha256": hashlib.sha256((data / name).read_bytes()).hexdigest().upper()}
                                     for name in sorted(files)], "Terrains": entries}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--verify", action="store_true")
    args = parser.parse_args()
    result = extract(args.game)
    if args.verify:
        if json.loads(args.output.read_text(encoding="utf-8")) != result:
            raise SystemExit("Native collision catalogue differs from installed scene; regenerate and review before deployment")
        print("Native collision catalogue: four terrain settings and installed source hashes verified offline.")
    else:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
        print("Exported", len(result["Terrains"]), "native terrain collision settings")


if __name__ == "__main__":
    main()
