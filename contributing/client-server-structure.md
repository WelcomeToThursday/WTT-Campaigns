# Client and server organization

Namespaces match folders beneath each project, such as `WTT.Campaigns.Client.Profiles` and `WTT.Campaigns.Server.Hub`. Keep each top-level type in its own named file and keep partial class files together. The plugin and server entry points remain in their project root namespaces.

## Client

| Folder / namespace suffix | Responsibility |
| --- | --- |
| Project root | `Plugin`: BepInEx startup, current session state, requests and profile reloads |
| `Profiles` | Client snapshot and equipment models, native faction/appearance creation, and character previews |
| `UI` | `SeasonUi` and the native skills-tab adapter |
| `Hub` | Hub presentation and transactions, document tracking, banner sound and video lifecycle |
| `Patches` | Explicit patch registration and hooks grouped by game feature |

The `UI` namespace here contains EFT integration. Game-independent Unity views remain in the separate [UI project](ui-structure.md). Client components are added at runtime; their namespace changes do not require rebuilding the artwork bundle.

## Server

| Folder / namespace suffix | Responsibility |
| --- | --- |
| Project root | `Metadata` and `ServerStartup`: mod identity, initial services and patch discovery |
| `Profiles` | Seasonal profile lifecycle, account links, snapshot/appearance models and native JSON conversion |
| `Routing` | Seasonal snapshot, create, edit and switch endpoints and their request model |
| `Hub` | Hub routes and requests, configuration, gameplay, quest imports, profile transactions and startup |
| `Items` | Seasonal item-template and localization registration |
| `Effects` | Reusable item, secure-container and trading calculations/validation |
| `Patches` | Injectable hooks grouped by game feature |

Keep dependency-injection attributes, lifetimes and load orders on moved services. Server patch discovery is assembly-based. HTTP routes, JSON properties, configuration paths and saved-state keys do not depend on these namespaces and remain unchanged.

## Moving or adding code

Update explicit imports and reflection lookups when moving types, including the full names in `Tests/UiCompatibilityChecks.cs`. Namespace changes alter CLR type names, so external consumers must update their references and rebuild. Preserve patch targets, identifiers and registration order; see [patch organization](patches.md).

Build the solution and run the contract, native compatibility and client hook checks in [CONTRIBUTING](../CONTRIBUTING.md). Use offline contract and assembly checks. Always install validated local updates through MSBuild, and never stop or start servers or clients. See [build and deployment](build-deployment.md).
