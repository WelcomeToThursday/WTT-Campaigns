# Build, validate and install

Run from the checkout with .NET SDK 10:

```powershell
dotnet msbuild build.proj
```

The default target builds Release, runs offline contract and native assembly checks, validates bundled assets, and installs the matching client/UI/shared/server files. Always install validated local updates. Never stop or start any server or client as part of development, validation, packaging or deployment. Application restarts are performed by the user.

On Windows, MSBuild runs asset validation with the full path to the built-in Windows PowerShell 5.1 executable; `pwsh` does not need to be installed or on `PATH`. Other platforms default to `pwsh`. Override `AssetValidationPowerShell` with an executable path if needed. The optional packaging scripts still require PowerShell 7.

The default layout is `<SPT>/Development/SeasonalPerks`. `Directory.Build.props` resolves the game through `../../`, the server through `<SPT>/SPT_Runtime`, and the companion Unity assets through `../CJ-SDK`. `Directory.Build.local.props` or command-line MSBuild properties can override `TarkovDir`, `ManagedDir`, `ServerDir` and `CampaignsAssetsDir`. Use directory paths with trailing separators. These same properties drive compilation, validation and installation; a separate server directory is supported. Project output overrides are respected through MSBuild target outputs.

## Scope

```powershell
# Full matching update (default)
dotnet msbuild build.proj

# UI assembly only; does not deploy client/server work in progress
dotnet msbuild build.proj -p:DeploymentScope=UI

# Client, UI, shared and media, or server runtime files
dotnet msbuild build.proj -p:DeploymentScope=Client
dotnet msbuild build.proj -p:DeploymentScope=Server

# Build and validate while preparing an update; installation is still required
dotnet msbuild build.proj -t:Validate
```

Client-only and server-only updates require the built shared assembly to match the installed opposite component. Use the default matching update when shared contracts change. UI-only deployment is appropriate only for changes contained within the UI assembly; it compiles that project and runs offline contracts/native compatibility checks without building the client or server.

`dotnet build WTT-Campaigns.slnx` remains a compilation command for IDE/CI use. It does not complete a local update: follow it with `dotnet msbuild build.proj`. CI without game assets uses the existing standalone Tests and Server projects and does not deploy.

## Installation behavior

MSBuild collects an explicit list of shipped runtime assemblies, dependencies, bundles, story media, data, icons and web assets. It never mirrors or clears an installation directory. Configuration files, creator content and profiles are excluded from the runtime manifest.

Install folders are `BepInEx/plugins/WTT-Campaigns` and `SPT_Runtime/user/mods/WTT-Campaigns`. When the previous `SeasonalPerks` or `WTT-Seasonal` folders exist, installation requires all matching components. It checks old files for locks before changing either component, archives both old folders under the installation backup with verified SHA-256 hashes, and retains configuration, creator drafts and other non-shipped content in the new folders. The BepInEx configuration is copied to `com.wtt.campaigns.cfg` for the new plugin GUID. Existing Campaigns configuration wins if both versions are present; the old copy remains in the backup. Failed installation restores the old folders. This prevents duplicate plugin loading.

This development rename changes namespaces, assembly identities, routes and mod-owned profile extension keys. Existing profile files remain untouched; old progression keys are not migrated. Native seasonal gameplay fields and the seasonal character storage directory retain their existing names.

Archival copies and verifies the old files before removing them from the plugin search paths. Empty legacy directories can remain if an editor or file watcher holds them open; they contain no runtime assemblies and do not trigger another migration. The backup remains available after rollback as well as successful installation.

The deployment targets use MSBuild [Copy](https://learn.microsoft.com/en-us/visualstudio/msbuild/copy-task?view=visualstudio) and [hash tasks](https://learn.microsoft.com/en-us/visualstudio/msbuild/getfilehash-task?view=visualstudio), with a preflight task that checks destination paths and file availability. Identical files are skipped. Every changed existing file is backed up and verified before replacement, and every installed file is verified with SHA-256. Backups and the installation record are under `artifacts/install-backups/<UTC timestamp>/`; override `CampaignsBackupDir` to move them. Copy or verification failure triggers restoration of the backed-up files.

If any changed destination is locked, installation fails before replacement and identifies the file. Keep the validated build, report that installation is blocked, and let the user close the locking application. Then rerun the same command. Never kill a process, launch a runtime, or perform startup checks. New client/UI assemblies take effect after the user manually restarts the game; new server assemblies take effect after the user manually restarts the server.

## Packages and existing entry points

`tools/package.ps1` uses MSBuild to build, validate, stage and install a complete matching update, then writes a SHA-256 package manifest. It returns the timestamped `release/WTT-Campaigns-<version>-<timestamp>` directory. `tools/package_ui.ps1` is a compatibility alias for the full matching package; use `DeploymentScope=UI` for a genuinely UI-only update.

`tools/install_matching.ps1 -Package <path>` and `tools/install_ui.ps1 -Package <path>` translate an existing package manifest into MSBuild deployment items. Both install only the package's runtime files, validate its checksums and matching shared contracts, and use the same backups and verification. `install_story_ui.ps1` retains its narrow two-assembly repair for an already validated build with matching installed shared contracts. `install_web_editor.ps1` retains its two-file web repair and `-CheckOnly` option. None of these entry points controls applications.

## Retired server tests

The isolated server lifecycle scripts and profile-storage server runner have been removed. Do not create or use an isolated SPT runtime for this workflow. Existing `tools/test_*.py` and `tools/test_*.cjs` server fixtures remain historical references, with their synthetic-profile safeguards intact; they are not current validation commands and must never be redirected to installed profiles. Existing ignored `Testing/` state is left untouched. Historical route and restart counts in feature documents describe previous runs, not checks executed by the current workflow.

Use offline Tests, client assembly compatibility checks, `pwsh -File tools/test_deployment.ps1` and `pwsh -File tools/test_rename_deployment.ps1` for file-only deployment regression checks. These temporary files are under `artifacts/deployment-tests/` and `artifacts/rename-deployment-tests/`; no runtime is copied or launched. In-game visual or live behavior acceptance remains a user-controlled activity and must be reported separately from offline verification.
