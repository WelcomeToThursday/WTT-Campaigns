# UI project organization

`WTT-Seasonal.UI` contains Unity presentation code without EFT or SPT dependencies. Namespaces match folders beneath `UI`, for example `SeasonalPerks.UI.Screens`.

| Folder / namespace suffix | Responsibility |
| --- | --- |
| `Screens` | `SeasonalScreen`, its page/dialog/creation partial files, and `ScreenPage` navigation |
| `Models` | Presentation data: `ScreenState`, `CharacterEntry`, and `PerkEntry` |
| `Creation` | The character draft and `ICreationIdentity` adapter contract |
| `Profiles` | Profile selection layout and profile-card hover behavior |
| `Modifiers` | Modifier-card hover and pointer/keyboard focus components |
| `Controls` | Reusable UI element construction and button feedback |
| `Audio` | Interface sound requests handled by the client |
| `BattlePass` | Hub hover/scroll input components |

`SeasonsHubScreen` and its partials own the hub; `Models/Hub*` contain its presentation data. `Profiles/SeasonBanner` owns the menu widget. Client adapters supply EFT integration and media lifecycle. Shared contracts remain separate from UI models.

Keep each type in its own named file, and keep all `SeasonalScreen` partial files together. Native character creation, model previews, asset loading, and game sound playback remain in `Client`; the UI requests those services through callbacks and `ICreationIdentity`.

## Unity previews

Run `python tools/sync_ui_preview.py` after editing UI sources. It searches subfolders, excludes `bin` and `obj`, and converts file-scoped namespaces for Unity 2022's C# 9 compiler. Generated files retain their existing flat filenames and `.meta` GUIDs in the companion CJ-SDK project. Filenames must be unique across UI source folders.

Attachable components, their sound enum and the shared `UiElements` helper compile under `PreviewRuntime`; other sources compile under `Editor/Generated`. When adding a runtime component, update `RUNTIME_SOURCES` and keep its dependencies in that assembly. The sync script preserves metadata GUIDs when moving generated sources between these directories. Generated sources are not bundle dependencies.

Update the client and the companion SDK's editor preview/check imports when moving types. Build the solution, run the UI assembly compatibility checks described in [CONTRIBUTING](../CONTRIBUTING.md), and use **SDK / Seasonal Perks / Render UI previews** for Unity interaction checks. These namespace changes preserve UI behavior and layouts, but consumers must rebuild against the new CLR type names.

## Battle Pass transactions

`SeasonsHubScreen.Transactions` presents claim/shortage, exchange and result dialogs through `HubAction` callbacks. `HubRequirement` carries structured eligibility without EFT types. Client adapters save pending operation IDs, flush native inventory operations, reconcile responses and reload the profile. Copy the reviewed `tools/unity/SeasonalHubPreview.cs` into the SDK editor folder after syncing UI sources to exercise the transaction fixtures.
