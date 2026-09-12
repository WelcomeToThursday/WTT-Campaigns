# Development setup

Use .NET SDK 10. `global.json` accepts stable 10.0 feature bands and does not silently select a different major SDK. Restore the formatter with `dotnet tool restore`; the manifest lives in `.config/dotnet-tools.json`.

## Editor and IDE defaults

The shared settings come from [SP-Tushonka/server-csharp at revision 7d7add5](https://github.com/SP-Tushonka/server-csharp/tree/7d7add556a6f781e9a531fa3b0cf4cc925986e03). `.editorconfig` adopts its formatting, naming, and inspection preferences, including a 140-character line limit, four-space C# indentation, two-space JSON/YAML/XML project indentation, and file-scoped namespaces. The existing UTF-8 default is retained for all text files. CSharpier remains pinned to the same upstream version, 1.3.0.

`WTT-Campaigns.slnx.DotSettings` supplies the upstream Rider/ReSharper inspections, abbreviations, and dictionary. The shared Rider configuration selects project code style, declares the CSharpier plugin dependency, and enables format on save. The Visual Studio v16/v17 CSharpier defaults also enable format on save. IDE directories use this solution's name rather than the upstream name.

Install the CSharpier extension in your IDE and run `dotnet tool restore`. In Rider, verify **Editor / Code Style / Enable EditorConfig Support** and the **Project** scheme. The upstream setup also recommends **Tools / Actions on Save / Reformat and Cleanup Code**, using **Reformat & Apply Syntax Style** on **Changed lines**. Format on save requires the IDE extension; copying settings does not install it.

Only these shared defaults are tracked inside `.idea` and `.vs`. Workspace state, caches, and `*.DotSettings.user` remain local. Format edited files with `dotnet csharpier format <paths>`, or format the whole project with `dotnet csharpier format .`. Verify formatting with `dotnet csharpier check .`.

The **Format** GitHub workflow runs CSharpier on every branch push and commits formatting changes back to that branch when needed. Pull those commits before continuing local work. Repository rules must allow GitHub Actions to push to the branch. Formatting commits use `GITHUB_TOKEN`, so they do not trigger another workflow run; source checks run on the original push.

CSharpier controls layout; it does not convert expression-bodied members or apply inspection fixes. Apply the configured warning-level C# style fixes with `dotnet format style WTT-Campaigns.slnx --severity warn`, then run CSharpier. Verify them with `dotnet format style WTT-Campaigns.slnx --severity warn --verify-no-changes`. These solution-wide commands require the game references described below. Builds also enforce the configured code-style diagnostics.

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

The default layout is `<SPT>/Development/SeasonalPerks` with the companion Unity project in `<SPT>/Development/CJ-SDK`. You need installed SPT 4.1.x / EFT 0.16.9.40743 references, including `BepInEx/DumpedAssemblies/EscapeFromTarkov/Assembly-CSharp.dll`, BepInEx/SPT plugins, and Unity managed assemblies.

The solution and projects are named `WTT-Campaigns`, with matching DLL names. The checkout directory can retain its existing name. MSBuild collects each project's runtime outputs after building, including local output-path overrides, and deploys only the selected components.

Install UnityToolkit 2.0.2 or later into the target SPT installation, including its prepatcher. The Client project references `UniTask.dll`, `ZLinq.dll` and `ZString.dll` from `BepInEx/plugins/UnityToolkit` without copying them into Seasonal. Only the Client project references UnityToolkit's libraries; the other components retain their own dependencies. Game-free tests use ZLinq 1.5.6, matching the existing SPT server package dependency. Client checks isolate the installed Unity build from those server packages, exercise chapter notifications, subtitles, image cancellation and native transpilers offline, and reject remaining standard LINQ calls in the compiled client.

Use explicit `AsValueEnumerable()` for client queries that can remain value enumerables through enumeration or materialization; preserve snapshots before mutating the source. See the [ZLinq documentation](https://github.com/Cysharp/ZLinq). Use scoped `ZString.CreateStringBuilder()` buffers for repeated composition, compare spans before allocating unchanged strings, and use direct TextMeshPro formatting where available. Dispose buffers before any `await`; avoid nested thread-static builders. See the [ZString documentation](https://github.com/Cysharp/ZString). Final collections, sorting buffers, captured delegates and changed strings may still allocate.

Use `JoinToString` to join value queries: passing one to `string.Join` can select an object overload instead of enumerating it. `CopyTo(List<T>)` replaces the destination contents, so use it only for a full refresh; append with a loop when existing entries must survive. Keep native list/dictionary/hash-set operations and required `IEnumerable<T>` API contracts intact. The Client project disables the implicit `System.Linq` import to prevent accidental fallback.

Use UniTask for single-consumer presentation operations and Unity timing. `NextFrame` guarantees a different frame; the character switch loader awaits it twice before reconnecting. Realtime UniTask delays keep navigation and raid polling independent of time scale. Subtitle waits end on media replacement, stop or destruction. Await every operation exactly once, including on native playback failure. Use `UniTaskCompletionSource<T>` for cinematic, continue and handover callbacks. Shared image caches, native `Task` interfaces, network APIs and the appearance model cleanup task retain their existing multi-await or external contracts. Image loading cancels only the caller's wait on a shared download and switches to the main thread before creating textures. See the [UniTask documentation](https://github.com/Cysharp/UniTask). Actual frame timing and rendered UI behavior require user-controlled in-game verification.

For a checkout elsewhere, copy `Directory.Build.local.props.example` to `Directory.Build.local.props` and edit the paths. This local file is ignored. `TarkovDir`, `ManagedDir`, `ServerDir` and `CampaignsAssetsDir` can also be passed as MSBuild properties. Keep trailing directory separators. The same settings drive compilation, validation and installation. Optional research scripts retain their separately documented input paths.

The game-derived UI bundles, story media and icons are local dependencies. The UI builder and asset workspace live in the separate CJ-SDK project. A source checkout alone does not recreate those assets. See [local UI resources](Client/Resources/README.md) for bundle requirements.

```powershell
# Build, validate and install all matching components
dotnet msbuild build.proj

# For changes confined to the UI assembly
dotnet msbuild build.proj -p:DeploymentScope=UI
```

Always install the validated update, with backups and checksum verification. Never stop or start servers or clients, including test instances. If installation is blocked by a locked file, keep the validated build ready and report the file and the application the user needs to close. The user performs any restart needed to load installed assemblies. See [build and deployment](contributing/build-deployment.md) for all targets and compatibility installers.

The isolated test server is retired. Do not stage a runtime or run the historical server fixtures. Keep their synthetic-profile safeguards intact and never redirect them to installed profiles. Existing ignored `Testing/` state is left untouched. Offline contracts, native assembly checks and file-only deployment checks are the automated validation workflow; live acceptance remains user-controlled.

## Changes and review

Follow the [client/server](contributing/client-server-structure.md), [patch](contributing/patches.md), [shared project](contributing/shared.md), and [UI project](contributing/ui-structure.md) organization guides. Format edited C# files with `dotnet csharpier format <paths>`. Source uses UTF-8 and LF line endings; `.gitattributes` also normalizes text when Git adds it.

Keep game binaries, generated bundles, recovered media, raw captures, profiles, credentials and machine-specific settings out of commits. The whole `Research/` tree is ignored; preserve contributor references in `contributing/` and keep `wiki/` focused on players and campaign authors. Keep sanitized catalogue/localization data and fixtures in their existing directories.

The `wiki/` directory contains the documentation home page, guides and authoring example in this repository. See [editing documentation](contributing/wiki-publishing.md) for the normal edit, commit and push workflow.

Before a commit, inspect `git status --short`, `git diff --check` and `git diff --cached`. Before publishing a repository, review `THIRD_PARTY_NOTICES.md`, including the captured data retained in source control. Packaging stages and installs the validated local update; it does not publish it. Never stop or start any server or client.
