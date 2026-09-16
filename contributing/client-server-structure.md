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
| `Authoring` | Editor entry points, session and transport state, diagnostics, and the `RaidEditor` coordinator |
| `Authoring.Views` | Toolkit documents, controls, windows, layout, tree models, and editor screens |
| `Authoring.Scenes` | Scene discovery, selection, model ownership, navigation, and route overlays |
| `Authoring.Rendering` | Editor camera environment, weather, visibility, HUD suppression, and render guards |
| `Authoring.Preview` | Item previews, equipment preview players, and item identity helpers |
| `Encounters` | Authored AI spawning, movement, patrols, and native compatibility |
| `Missions` | Mission startup, raid state, loot, and mission HUD |
| `Story` | Story state, interactions, trader presentation, cinematics, and media |
| `Spatial` | Runtime zone integration |
| `Progression` | Progression state and native task-list presentation |
| `Customization` | Native appearance views and customization-tab integration |

The `UI` namespace here contains EFT integration. Game-independent Unity views remain in the separate [UI project](ui-structure.md). Client components are added at runtime; their namespace changes do not require rebuilding the artwork bundle.

Keep the authoring root for coordination. Put new helpers in the area that owns their behavior and import that namespace explicitly; avoid project-wide global imports. All `RaidEditor` partial declarations stay in the root because they are one type, even when an individual file handles scene or view operations. Helper types that are not partial declarations belong in their own area. Keep namespace depth shallow and introduce another area only when it has a distinct responsibility shared by several types.

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
