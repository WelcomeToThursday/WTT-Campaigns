"""Import only public catalogue/localization plus anonymized perk state. Never import headers."""
import argparse
import hashlib
import json
import struct
from pathlib import Path
from urllib.request import Request, urlopen

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_LOGS = Path(r"F:\Git Repos\PacketSniffer\SPTarkov.PacketSniffer\bin\Debug\net9.0\logs")
ASSETS = ROOT.parent / "CJ-SDK/Assets/Mods/WTT-Campaigns.Assets"

def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))

def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--logs", type=Path, default=DEFAULT_LOGS)
    parser.add_argument("--download-icons", action="store_true")
    args = parser.parse_args()
    requests = args.logs / "requests"
    # These are upstream EFT capture filenames, not the mod's local route prefix.
    captures = sorted(requests.rglob("resp.client.seasonal-perks.list*.json"))
    if not captures:
        raise SystemExit("No seasonal perk catalogue response found")
    catalogue = read(captures[-1])
    perks = catalogue["common"] + catalogue["personal"]
    ids = {p["id"] for p in perks}
    assert len(ids) == len(perks), "Duplicate perk IDs"
    for perk in perks:
        assert all(x in ids for x in perk["mutuallyExclusiveSeasonalPerkIds"])
        assert all(perk['id'] in next(p for p in perks if p['id']==other)['mutuallyExclusiveSeasonalPerkIds'] for other in perk['mutuallyExclusiveSeasonalPerkIds']), 'Nonreciprocal conflict'
    locale_path = sorted(requests.rglob("resp.client.locale.en*.json"))[-1]
    full_locale = read(locale_path)
    keys = {p["id"] + suffix for p in perks for suffix in (" name", " description")}
    keys.update(k for k in full_locale if k.startswith(("Perks/", "CharacterSelection/", "CharacterSelectionScreen/", "CharacterSlotView/", "ECharacterSelectionSeasonStat/")))
    assert keys <= full_locale.keys(), "Missing perk localization"
    save(ROOT / "data/locales/en.json", {k: full_locale[k] for k in sorted(keys)})
    fixtures = []
    profile_sources = sorted(requests.rglob("resp.client.game.profile.list*.json"))
    for source in profile_sources:
        profiles = read(source)
        for profile in profiles if isinstance(profiles, list) else [profiles]:
            fixtures.append({k: profile[k] for k in ("SeasonalPerks", "SeasonalPerkEffectParameters") if k in profile})
    save(ROOT / "Tests/fixtures/captured-perk-state.json", fixtures)
    sources = [{"file": str(p.relative_to(args.logs)).replace("\\", "/"), "sha256": hashlib.sha256(p.read_bytes()).hexdigest()} for p in captures + [locale_path] + profile_sources]
    save(ROOT / "data/provenance.json", {"sources": sources, "commonCount": len(catalogue["common"]), "personalCount": len(catalogue["personal"]), "effectIds": sorted({e["effectId"] for p in perks for e in p["effects"]})})
    manifest = []
    for perk in perks:
        # Keep the real upstream URL for downloads/provenance; publish a local URL below.
        image = perk["imageUrl"]
        assert image.startswith("/files/seasonal-perks/") and ".." not in image
        url = "https://s3-prod.escapefromtarkov.com/pvp-season" + image
        path = ASSETS / "Icons" / Path(image).name
        if args.download_icons and not path.exists():
            with urlopen(Request(url, headers={"User-Agent": "Campaigns-AssetImport/1.0"}), timeout=30) as response:
                raw = response.read()
            assert raw[:8] == b"\x89PNG\r\n\x1a\n", f"Not a PNG: {url}"
            assert struct.unpack(">II", raw[16:24]) == (272, 272), f"Unexpected icon size: {url}"
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(raw)
        if path.exists():
            raw = path.read_bytes()
            assert raw[:8] == b"\x89PNG\r\n\x1a\n"
            assert struct.unpack('>II', raw[16:24]) == (272, 272), f'Unexpected existing icon size: {path}'
            manifest.append({"perkId": perk["id"], "asset": "Icons/" + path.name, "sourceUrl": url, "sha256": hashlib.sha256(raw).hexdigest(), "size": len(raw)})
        perk["imageUrl"] = "/wtt-campaigns/icons/" + perk["id"] + ".png"
    save(ROOT / "data/catalogue.json", catalogue)
    save(ASSETS / "provenance.json", {"icons": manifest})
    print(f"Imported {len(perks)} perks, {len(keys)} locale entries, {len(manifest)} icons, {len(fixtures)} sanitized profile fixtures.")

if __name__ == "__main__":
    main()
