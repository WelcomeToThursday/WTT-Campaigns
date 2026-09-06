# Development setup

Use .NET SDK 10. `global.json` accepts stable 10.0 feature bands and does not silently select a different major SDK. Restore the formatter with `dotnet tool restore`; the manifest lives in `.config/dotnet-tools.json`.

## Editor and IDE defaults

The shared settings come from [SP-Tushonka/server-csharp at revision 7d7add5](https://github.com/SP-Tushonka/server-csharp/tree/7d7add556a6f781e9a531fa3b0cf4cc925986e03). `.editorconfig` adopts its formatting, naming, and inspection preferences, including a 140-character line limit, four-space C# indentation, two-space JSON/YAML/XML project indentation, and file-scoped namespaces. The existing UTF-8 default is retained for all text files. CSharpier remains pinned to the same upstream version, 1.3.0.

`WTT-Seasonal.sln.DotSettings` supplies the upstream Rider/ReSharper inspections, abbreviations, and dictionary. The shared Rider configuration selects project code style, declares the CSharpier plugin dependency, and enables format on save. The Visual Studio v16/v17 CSharpier defaults also enable format on save. IDE directories use this solution's name rather than the upstream name.

Install the CSharpier extension in your IDE and run `dotnet tool restore`. In Rider, verify **Editor / Code Style / Enable EditorConfig Support** and the **Project** scheme. The upstream setup also recommends **Tools / Actions on Save / Reformat and Cleanup Code**, using **Reformat & Apply Syntax Style** on **Changed lines**. Format on save requires the IDE extension; copying settings does not install it.

Only these shared defaults are tracked inside `.idea` and `.vs`. Workspace state, caches, and `*.DotSettings.user` remain local. Format edited files with `dotnet csharpier format <paths>`, or format the whole project with `dotnet csharpier format .`. Verify formatting with `dotnet csharpier check .`.

The **Format** GitHub workflow runs CSharpier on every branch push and commits formatting changes back to that branch when needed. Pull those commits before continuing local work. Repository rules must allow GitHub Actions to push to the branch. Formatting commits use `GITHUB_TOKEN`, so they do not trigger another workflow run; source checks run on the original push.

CSharpier controls layout; it does not convert expression-bodied members or apply inspection fixes. Apply the configured warning-level C# style fixes with `dotnet format style WTT-Seasonal.sln --severity warn`, then run CSharpier. Verify them with `dotnet format style WTT-Seasonal.sln --severity warn --verify-no-changes`. These solution-wide commands require the game references described below. Builds also enforce the configured code-style diagnostics.

Server patch callbacks use `UsedImplicitly` to identify methods invoked through SPT reflection. Keep those annotations alongside the patch attributes. UI sources use explicit imports because the same files compile in Unity's preview assemblies without SDK-generated global usings.

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

The solution and projects are named `WTT-Seasonal`, with matching DLL names. The checkout directory can retain its existing name. Packaging scripts resolve build output directories through MSBuild, including local output-path overrides, and stage only the required runtime files. The isolated-server script builds only the server project.

For a checkout elsewhere, copy `Directory.Build.local.props.example` to `Directory.Build.local.props` and edit the paths. This local file is ignored. `TarkovDir`, `ManagedDir`, `ServerDir` and `SeasonalAssetsDir` can also be passed as MSBuild properties. Keep trailing directory separators. The MSBuild overrides configure compilation; deployment, isolated-server and research scripts still expect the documented companion-directory layout unless they expose an explicit path argument.

The game-derived UI bundle and 39 icons are local dependencies. The UI builder and asset workspace live in the separate CJ-SDK project. A source checkout alone does not recreate those assets. See the README for the Unity build sequence.

```powershell
dotnet build WTT-Seasonal.sln -c Release
dotnet run --project Tests -c Release -- "<SPT>/BepInEx/DumpedAssemblies/EscapeFromTarkov/Assembly-CSharp.dll"
dotnet run --project Tests -c Release -- --resource-hooks "<SPT>" "Client/bin/Release/netstandard2.1/WTT-Seasonal.Client.dll"
dotnet run --project Tests -c Release -- --ui "<SPT>/BepInEx/DumpedAssemblies/EscapeFromTarkov/Assembly-CSharp.dll" "Client/bin/Release/netstandard2.1/WTT-Seasonal.Client.dll"
```

Use the isolated-server scripts for backend integration tests. `Testing/` is disposable local state and must never contain an installed player's profile. Research scripts are optional, require separately supplied captures/game metadata, and write ignored local outputs.

## Changes and review

Follow the [patch](docs/patches.md), [shared project](docs/shared.md), and [UI project](docs/ui-structure.md) organization guides. Format edited C# files with `dotnet csharpier format <paths>`. Source uses UTF-8 and LF line endings; `.gitattributes` also normalizes text when Git adds it.

Keep game binaries, generated bundles, recovered media, raw captures, profiles, credentials and machine-specific settings out of commits. The whole `Research/` tree is ignored; preserve useful conclusions in reviewed `docs/` files. Keep sanitized catalogue/localization data and fixtures in their existing directories.

Before a commit, inspect `git status --short`, `git diff --check` and `git diff --cached`. Before publishing a repository, review `THIRD_PARTY_NOTICES.md`, including the captured data retained in source control. Packaging stages a local release and does not install or publish it.
