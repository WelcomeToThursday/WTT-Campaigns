# WTT-Seasonal

Development backport for **SPT 4.1.3 / EFT 0.16.9.40743**. This is a test build, not a completed parity release. See [compatibility and remaining gates](docs/compatibility.md).

Build **0.5.1** exposes the Story tab and trader visits to Seasonal characters even when the season has no authored story content. Existing season content and progression stay unchanged.

Build **0.5.0** is the [story-system implementation candidate](docs/story-system.md): Seasonal journals, dialogue/quest authority, eight trader visit rooms (including a custom Peacekeeper office), raid bindings, media and [pack authoring tools](docs/story-authoring.md). No live campaign is imported. Native beta gameplay/visual acceptance remains outstanding; see the support matrix and validation gates before authoring a season.

Build **0.4.0** adds [live trader and task progression](docs/trader-progression.md) for normal and seasonal characters: captured loyalty requirements without spending gates, reputation changes for 381 existing tasks, and a grouped native task list. Missing live tasks are excluded. Existing reputation and task progress are preserved; loyalty may decrease under the new thresholds. Install both client and server. In-game visual acceptance remains a release gate.

Build **0.3.0** adds the [Season Creator](docs/season-creator.md), hosted in SPT’s administrator web interface at `/wtt-seasonal/creator`. Create and duplicate drafts, configure supported content, validate and export packs, and select a default season for the next restart. The [character selector](docs/characters.md) supports several characters per season, simultaneous playable seasons, a wrapping carousel, and confirmed deletion or achievement-preserving wipes. Install the client and server together. Browser interaction and installed-game visual acceptance remain release gates.

Build **0.2.0** adds [Battle Pass gameplay](docs/battle-pass-gameplay.md) to the Seasonal hub: document acquisition, local claims, exchanges and persistent progress, using the amended capture. WTT-Seasonal includes the eight document items and two season crate definitions. Missing quest, customization and crate-content dependencies remain explicitly locked; online purchases stay disabled. Install both client and server, plus **WTT-ContentBackport 2.0.1 or later** and its dependencies. Seasonal supplies the item JSON and localization; Backport supplies the document and crate bundles.

The project imports the captured 39-entry catalogue and English localization, serves all perk icons from the local SPT server, creates an independent seasonal PMC/Scav profile, persists selections and grant receipts, and supplies a client selection screen with recovered perk cards and a confirmation window. Thirty-three catalogue entries currently have implementations; six remain unavailable in selection. Actual in-game switching and gameplay still need validation.

Build **0.1.23** adds Allergic (three persistent random medication/provision targets and three symptoms per use) and Broken Secure Container (the captured recursive item allow-list, enforced on client and server). See [behavior and validation](docs/allergy-container.md). It retains the [consumable perks](docs/consumables.md), [experience/flea perks](docs/experience-flea.md) and [trader-price perks](docs/trader-prices.md). Install both client and server from the full gameplay package produced by `tools/package.ps1`.

For a fresh checkout, start with [development setup](CONTRIBUTING.md). This repository contains mod source, reviewed documentation and sanitized data. Game binaries, recovered assets, generated bundles, research output, test profiles and releases remain local. Original code is MIT-licensed; see [third-party notices](THIRD_PARTY_NOTICES.md) for captured data and external dependencies.

Seasonal character files are stored separately from launcher accounts in `SPT_Runtime/user/seasonal/profiles`. Existing character files migrate automatically with verified backups. See [typed models and profile storage](docs/typed-models-and-profile-storage.md) for compatibility details and regression checks.

## Project layout

UI build **0.1.7** includes the seasonal creation sequence and a native reconnect adapter for character switching. See [UI changes and validation limits](docs/ui.md).

Open `WTT-Seasonal.slnx` in this directory. Each C# project has its own directory:

Projects and built assemblies use the `WTT-Seasonal` prefix (for example, `WTT-Seasonal.Client.csproj` produces `WTT-Seasonal.Client.dll`). C# namespaces remain under `SeasonalPerks` for compatibility with the companion Unity preview sources.

- `Client`: BepInEx plugin, SPT `ModulePatch` hooks and UI controllers.
- `UI`: Unity views shared by the client and CJ-SDK preview, organized into [screens, models, creation, profiles, modifiers, controls and audio](docs/ui-structure.md).
- `Server`: SPT server mod, SPT `AbstractPatch` hooks and local routes. Uses `SPTarkov.Server.Core` and `SPTarkov.Reflection` NuGets at 4.1.0, matching the existing projects; it is built and integration-tested against the installed 4.1.3 runtime. Host assemblies are excluded from the mod output.
- `Shared`: contracts, configuration, perks, profile state, gameplay effects and serialization, with [namespaces matching their folders](docs/shared.md).
- `Tests`: contract tests and read-only assembly compatibility checks.
- `tools`, `data`, `docs`, `Research`: import/build tools, sanitized data and investigation evidence.

Client and server code use [folders with matching feature namespaces](docs/client-server-structure.md). Client registration lives in `Client/Patches/PatchRegistration.cs`; server patches are discovered through SPT dependency injection. See [patch organization and extension guide](docs/patches.md).

Asset sources, all 39 PNGs, recovered layout data and the Unity editor builder are in `../CJ-SDK/Assets/Mods/SeasonalPerks.Assets`. The UI bundle contains layout prefabs, all 26 decorative artwork sprites, fonts and materials. **Perk icons remain outside the bundle.** Icons are fetched lazily by perk ID from `/wtt-seasonal/icons/{id}.png`. Installed operation does not use the live backend or CDN.

Client image downloads share a session cache across the hub, banner, story journal and perk selector. Requests for the same server, image, season and pack revision share one download; reopening screens reuses the encoded bytes while each screen releases its own textures. The cache retains up to 64 MiB / 256 images, evicts least recently used entries, and limits downloads to six at a time. Failed, empty or undecodable responses can be retried. Pack revision changes use fresh entries; restarting the game clears the cache. No images are cached on disk.

Formatting and shared Rider/Visual Studio defaults follow SP-Tushonka/server-csharp. See [editor setup](CONTRIBUTING.md#editor-and-ide-defaults). Restore the formatter from `.config/dotnet-tools.json` with `dotnet tool restore`, then run `dotnet csharpier format Client UI Server Shared Tests`. Text files use UTF-8 and LF line endings.

## Build and stage

The projects use .NET SDK 10 and the local SPT references configured in `Directory.Build.props`. Restore/build with `dotnet build WTT-Seasonal.slnx`. Client references require the supplied dumped Assembly-CSharp and installed BepInEx/SPT assemblies.

Use Unity 2022.3.43f1 to open CJ-SDK and run **SDK / Seasonal Perks / Build recovered UI**. The builder writes `Client/Resources/seasonalperks_ui.bundle` and checks that no icon dependencies are present. It does not rebuild unrelated mod assets.

Run `tools/package.ps1` to build Release, run contract/assembly checks, and create a timestamped `release` directory. It stages files only; it does not install into the running SPT environment. The package uses this installation's `SPT_Runtime/user/mods/SeasonalPerks` server location and `BepInEx/plugins/SeasonalPerks` client location. Copy the server folder into the active server's `user/mods` if using another installation layout. Install both parts together.

Release folders are named `WTT-Seasonal-<version>-<timestamp>` and `WTT-Seasonal-UI-<version>-<timestamp>`. Both packaging scripts return only the package directory on the PowerShell success stream, so it can be captured with `$package = & ./tools/package_ui.ps1` and passed to `./tools/install_ui.ps1 -Package $package`. Build and check messages remain visible in the console.

When upgrading an older installation manually, remove the old `SeasonalPerks.Client.dll`, `SeasonalPerks.UI.dll`, `SeasonalPerks.Shared.dll`, `SeasonalPerks.Server.dll` and corresponding PDB/deps files from the two mod directories before copying in the renamed assemblies. Preserve `config.json` and profile data. The UI installer and isolated-server staging script back up the old assemblies automatically. Existing mod directory names, asset paths and saved-state keys remain stable.

For UI update 0.1.4, use `tools/package_ui.ps1`. It builds the client and the server's perk-icon/appearance adapter without adding gameplay behavior. Close the game and installed server, then run `tools/install_ui.ps1` to back up and install the update. See [UI usage, previews and validation](docs/ui.md).

## Configuration and use

On the first 0.3.0 startup, existing catalogue, item, quest, hub, `config.json` and `hub-config.json` values are imported into `creator/legacy.json`. Later restarts use that saved definition. Use the browser creator for new seasons. Defaults retain zero starting points, budget enforcement, editing outside raids, and supported common rules. Once a season has a character, gameplay changes require duplication into a new season; artwork and text can be revised. Preserve the entire server mod’s `creator` directory when upgrading.

Open **CHARACTERS** from the menu or press **F8** outside a raid. The native **PERKS** tab beside Skills and Mastery shows saved active perks and opens the editor. Pick a faction/nickname for creation, balance beneficial perks with detrimental perks, and save through the confirmation window. Creation leaves the normal PMC active; the character buttons switch profiles. Existing progress is never cloned. Skill presets grant a minimum level once on first selection; removing/reselecting a perk never refills that grant. Skill caps can reduce progress when selected.

The linked seasonal profile has independent inventory, quests, traders, hideout, mail, insurance and Scav data, because it is a separate full SPT profile. The root account's mod data contains the link and selected mode. Do not delete either linked profile while using character switching.

## Reproduce validation

`dotnet run --project Tests -- "../../BepInEx/DumpedAssemblies/EscapeFromTarkov/Assembly-CSharp.dll"` checks contracts and the actual client hook shapes.

`tools/start_test_server.ps1` starts a separate SPT runtime under `Testing/Server`, on port 6975. It copies runtime/database files, never installed user profiles. `tools/test_integration.py` creates synthetic test accounts and runs real routes. Use `tools/stop_test_server.ps1` to stop only that runtime.

For restart coverage: run integration tests, stop the isolated server, run `tools/test_restart.py prepare`, start the isolated server, then run `tools/test_restart.py verify`. The preparation phase modifies only synthetic test profiles. It simulates an interrupted solo raid and previously consumed creation grants.

Importer: `tools/import_captures.py --download-icons` uses the supplied packet-log location by default. Existing icons can be imported offline without that flag. `tools/recover_ui.py` uses the supplied live files and extracted metadata. Python dependencies are recorded in `tools/requirements.txt`; use a local virtual environment. Raw packet logs, account IDs and credentials are never included in the package.

Story protocol 2 (0.5.2) adds complete automatic dialogue pacing, staged native handovers, live raid observations, scene restrictions, assigned custom trader rooms and ordinary raid-event media. Install matching client, UI, shared and server components together. See [story behavior](docs/story-system.md#protocol-2-client-completion) and [authoring](docs/story-authoring.md). Existing story saves and format-1 packs remain compatible.
