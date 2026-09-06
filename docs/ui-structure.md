# UI project organization

`SeasonalPerks.UI` contains Unity presentation code without EFT or SPT dependencies. Namespaces match folders beneath `UI`, for example `SeasonalPerks.UI.Screens`.

| Folder / namespace suffix | Responsibility |
| --- | --- |
| `Screens` | `SeasonalScreen`, its page/dialog/creation partial files, and `ScreenPage` navigation |
| `Models` | Presentation data: `ScreenState`, `CharacterEntry`, and `PerkEntry` |
| `Creation` | The character draft and `ICreationIdentity` adapter contract |
| `Profiles` | Profile selection layout and profile-card hover behavior |
| `Modifiers` | Modifier-card hover and pointer/keyboard focus components |
| `Controls` | Reusable UI element construction and button feedback |
| `Audio` | Interface sound requests handled by the client |

Keep each type in its own named file, and keep all `SeasonalScreen` partial files together. Native character creation, model previews, asset loading, and game sound playback remain in `Client`; the UI requests those services through callbacks and `ICreationIdentity`.

## Unity previews

Run `python tools/sync_ui_preview.py` after editing UI sources. It searches subfolders, excludes `bin` and `obj`, and converts file-scoped namespaces for Unity 2022's C# 9 compiler. Generated files retain their existing flat filenames and `.meta` GUIDs in the companion CJ-SDK project. Filenames must be unique across UI source folders.

Attachable components and their sound enum compile under `PreviewRuntime`; other sources compile under `Editor/Generated`. When adding a runtime component, update `RUNTIME_SOURCES` in the sync script and keep its dependencies available to that runtime assembly. Generated preview sources are not dependencies of the asset bundle.

Update the client and the companion SDK's editor preview/check imports when moving types. Build the solution, run the UI assembly compatibility checks described in [CONTRIBUTING](../CONTRIBUTING.md), and use **SDK / Seasonal Perks / Render UI previews** for Unity interaction checks. These namespace changes preserve UI behavior and layouts, but consumers must rebuild against the new CLR type names.
