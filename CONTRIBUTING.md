# Development setup

Use .NET SDK 10. `global.json` accepts stable 10.0 feature bands and does not silently select a different major SDK. Restore the formatter with `dotnet tool restore`; the manifest lives in `.config/dotnet-tools.json`.

## Checks without a game installation

From a clean checkout:

```powershell
dotnet tool restore
dotnet run --project Tests -c Release
dotnet build Server -c Release
```

These commands validate shared contracts and compile the backend. Without icons, the server output is not a runnable mod package. CI intentionally runs these checks without any game files or credentials.

## Full local build

The default layout is `<SPT>/Development/SeasonalPerks` with the companion Unity project in `<SPT>/Development/CJ-SDK`. You need the installed SPT 4.1.3 / EFT 0.16.9.40743 references, including `BepInEx/DumpedAssemblies/EscapeFromTarkov/Assembly-CSharp.dll`, BepInEx/SPT plugins, and Unity managed assemblies.

For a checkout elsewhere, copy `Directory.Build.local.props.example` to `Directory.Build.local.props` and edit the paths. This local file is ignored. `TarkovDir`, `ManagedDir`, `ServerDir` and `SeasonalAssetsDir` can also be passed as MSBuild properties. Keep trailing directory separators. The MSBuild overrides configure compilation; deployment, isolated-server and research scripts still expect the documented companion-directory layout unless they expose an explicit path argument.

The game-derived UI bundle and 39 icons are local dependencies. The UI builder and asset workspace live in the separate CJ-SDK project. A source checkout alone does not recreate those assets. See the README for the Unity build sequence.

```powershell
dotnet build SeasonalPerks.sln -c Release
dotnet run --project Tests -c Release -- "<SPT>/BepInEx/DumpedAssemblies/EscapeFromTarkov/Assembly-CSharp.dll"
dotnet run --project Tests -c Release -- --resource-hooks "<SPT>" "Client/bin/Release/netstandard2.1/SeasonalPerks.Client.dll"
dotnet run --project Tests -c Release -- --ui "<SPT>/BepInEx/DumpedAssemblies/EscapeFromTarkov/Assembly-CSharp.dll" "Client/bin/Release/netstandard2.1/SeasonalPerks.Client.dll"
```

Use the isolated-server scripts for backend integration tests. `Testing/` is disposable local state and must never contain an installed player's profile. Research scripts are optional, require separately supplied captures/game metadata, and write ignored local outputs.

## Changes and review

Follow the [patch](docs/patches.md), [shared project](docs/shared.md), and [UI project](docs/ui-structure.md) organization guides. Format edited C# files with `dotnet csharpier format <paths>`. Source uses UTF-8 and LF line endings; `.gitattributes` also normalizes text when Git adds it.

Keep game binaries, generated bundles, recovered media, raw captures, profiles, credentials and machine-specific settings out of commits. The whole `Research/` tree is ignored; preserve useful conclusions in reviewed `docs/` files. Keep sanitized catalogue/localization data and fixtures in their existing directories.

Before a commit, inspect `git status --short`, `git diff --check` and `git diff --cached`. Before publishing a repository, review `THIRD_PARTY_NOTICES.md`, including the captured data retained in source control. Packaging stages a local release and does not install or publish it.
