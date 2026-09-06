# Third-party material

The MIT license applies to the original Seasonal Perks mod code, consistent with the license declared in `Server/Metadata.cs`. It does not grant rights to Escape from Tarkov, SPT, Unity, their binaries, or their assets.

- Game assemblies, extracted assets, UI bundles, icons, fonts, audio, videos, decompiled sources and raw network captures are local development inputs/outputs and are excluded from this repository.
- `data/catalogue.json` and `data/locales/en.json` retain captured game configuration and localization. `data/provenance.json` records source filenames and hashes. These files are not original MIT-licensed mod code; their original ownership remains unchanged.
- `data/hub.json`, `data/locales/hub-en.json`, `data/hub-images.json` and `data/hub-provenance.json` retain sanitized seasonal catalogue, localization and asset provenance under the same ownership conditions. Account progress, profiles and request headers are excluded.
- `Tests/fixtures/captured-perk-state.json` contains only perk/template identifiers and effect parameters. Full profiles and request headers are excluded.
- NuGet and Python dependencies retain their own licenses. Their versions are declared in the project files, tool manifest and `tools/requirements.txt`; dependency binaries are not vendored.
- The companion CJ-SDK Unity project and its recovered asset workspace are maintained separately and are not included here.

Seasonal document/crate definitions, quest definitions, reward payloads and associated localization are recovered game data. Source hashes and object identifiers are retained with the import provenance. Rebuilt item bundles, compressed textures, models, shaders and native reference audits remain local and ignored, and are included only in locally staged runtime packages.
