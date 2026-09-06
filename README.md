# Seasonal Perks

Development backport for **SPT 4.1.3 / EFT 0.16.9.40743**. This is a test build, not a completed parity release. See [compatibility and remaining gates](docs/compatibility.md).

The project imports the captured 39-entry catalogue and English localization, serves all perk icons from the local SPT server, creates an independent seasonal PMC/Scav profile, persists selections and grant receipts, and supplies a client selection screen with recovered perk cards and a confirmation window. Thirty-three catalogue entries currently have implementations; six remain unavailable in selection. Actual in-game switching and gameplay still need validation.

Build **0.1.23** adds Allergic (three persistent random medication/provision targets and three symptoms per use) and Broken Secure Container (the captured recursive item allow-list, enforced on client and server). See [behavior and validation](docs/allergy-container.md). It retains the [consumable perks](docs/consumables.md), [experience/flea perks](docs/experience-flea.md) and [trader-price perks](docs/trader-prices.md). Install both client and server from the full gameplay package produced by `tools/package.ps1`.

For a fresh checkout, start with [development setup](CONTRIBUTING.md). This repository contains mod source, reviewed documentation and sanitized data. Game binaries, recovered assets, generated bundles, research output, test profiles and releases remain local. Original code is MIT-licensed; see [third-party notices](THIRD_PARTY_NOTICES.md) for captured data and external dependencies.

## Project layout

UI build **0.1.7** includes the seasonal creation sequence and a native reconnect adapter for character switching. See [UI changes and validation limits](docs/ui.md).

Open `SeasonalPerks.sln` in this directory. Each C# project has its own directory:

- `Client`: BepInEx plugin, SPT `ModulePatch` hooks and UI controllers.
- `UI`: Unity views shared by the client and CJ-SDK preview, including character selection, perk editing and confirmation.
- `Server`: SPT server mod, SPT `AbstractPatch` hooks and local routes. Uses `SPTarkov.Server.Core` and `SPTarkov.Reflection` NuGets at 4.1.0, matching the existing projects; it is built and integration-tested against the installed 4.1.3 runtime. Host assemblies are excluded from the mod output.
- `Shared`: captured contracts, selection validation and effect aggregation.
- `Tests`: contract tests and read-only assembly compatibility checks.
- `tools`, `data`, `docs`, `Research`: import/build tools, sanitized data and investigation evidence.

Patches are grouped by feature with matching namespaces. Client registration lives in `Client/Patches/PatchRegistration.cs`; server patches are discovered through SPT dependency injection. See [patch organization and extension guide](docs/patches.md).

Asset sources, all 39 PNGs, recovered layout data and the Unity editor builder are in `../CJ-SDK/Assets/Mods/SeasonalPerks.Assets`. The UI bundle contains layout prefabs, all 26 decorative artwork sprites, fonts and materials. **Perk icons remain outside the bundle.** Icons are fetched lazily by perk ID from `/seasonal-perks/icons/{id}.png`. Installed operation does not use the live backend or CDN.

Formatting follows the four-space indentation, file-scoped namespaces, expanded braces and patch folders used by Use Items Anywhere and Skills Extended. Restore the formatter from `.config/dotnet-tools.json` with `dotnet tool restore`, then run `dotnet csharpier format Client UI Server Shared Tests`. Text files use LF line endings.

## Build and stage

The projects use .NET SDK 10 and the local SPT references configured in `Directory.Build.props`. Restore/build with `dotnet build SeasonalPerks.sln`. Client references require the supplied dumped Assembly-CSharp and installed BepInEx/SPT assemblies.

Use Unity 2022.3.43f1 to open CJ-SDK and run **SDK / Seasonal Perks / Build recovered UI**. The builder writes `Client/Resources/seasonalperks_ui.bundle` and checks that no icon dependencies are present. It does not rebuild unrelated mod assets.

Run `tools/package.ps1` to build Release, run contract/assembly checks, and create a timestamped `release` directory. It stages files only; it does not install into the running SPT environment. The package uses this installation's `SPT_Runtime/user/mods/SeasonalPerks` server location and `BepInEx/plugins/SeasonalPerks` client location. Copy the server folder into the active server's `user/mods` if using another installation layout. Install both parts together.

For UI update 0.1.4, use `tools/package_ui.ps1`. It builds the client and the server's perk-icon/appearance adapter without adding gameplay behavior. Close the game and installed server, then run `tools/install_ui.ps1` to back up and install the update. See [UI usage, previews and validation](docs/ui.md).

## Configuration and use

On first server startup, the mod writes `config.json` beside its server DLL. Defaults: zero starting points, budget enforcement on, editing allowed outside raids, and the currently supported common rules enabled. `EnabledCommonIds` contains catalogue IDs; unsupported IDs are rejected. Changes to common rules are applied to a seasonal character on its next successful selection save.

Open **CHARACTERS** from the menu or press **F8** outside a raid. The native **PERKS** tab beside Skills and Mastery shows saved active perks and opens the editor. Pick a faction/nickname for creation, balance beneficial perks with detrimental perks, and save through the confirmation window. Creation leaves the normal PMC active; the character buttons switch profiles. Existing progress is never cloned. Skill presets grant a minimum level once on first selection; removing/reselecting a perk never refills that grant. Skill caps can reduce progress when selected.

The linked seasonal profile has independent inventory, quests, traders, hideout, mail, insurance and Scav data, because it is a separate full SPT profile. The root account's mod data contains the link and selected mode. Do not delete either linked profile while using character switching.

## Reproduce validation

`dotnet run --project Tests -- "../../BepInEx/DumpedAssemblies/EscapeFromTarkov/Assembly-CSharp.dll"` checks contracts and the actual client hook shapes.

`tools/start_test_server.ps1` starts a separate SPT runtime under `Testing/Server`, on port 6975. It copies runtime/database files, never installed user profiles. `tools/test_integration.py` creates synthetic test accounts and runs real routes. Use `tools/stop_test_server.ps1` to stop only that runtime.

For restart coverage: run integration tests, stop the isolated server, run `tools/test_restart.py prepare`, start the isolated server, then run `tools/test_restart.py verify`. The preparation phase modifies only synthetic test profiles. It simulates an interrupted solo raid and previously consumed creation grants.

Importer: `tools/import_captures.py --download-icons` uses the supplied packet-log location by default. Existing icons can be imported offline without that flag. `tools/recover_ui.py` uses the supplied live files and extracted metadata. Python dependencies are recorded in `tools/requirements.txt`; use a local virtual environment. Raw packet logs, account IDs and credentials are never included in the package.
