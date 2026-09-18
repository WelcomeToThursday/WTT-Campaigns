# Editor presentation

The Campaign Editor uses UI Toolkit. Shared presentation is authored in
`tools/unity/EditorToolkit` and built into `wtt_campaigns_editor_toolkit.bundle`.

- `Window.uxml` owns the shared title bar, close button and resize handle.
- `WorkspaceTitleBar.uxml`, `TransformToolbar.uxml`, `CaptureTask.uxml` and
  `StatusBar.uxml` own their complete control hierarchy and initial captions.
- `Library.uxml` owns browser filters, search, list/grid containers, paging and the
  scrollable creation/AI action areas. Each tool clones its own instance.
- `Inspector.uxml` owns the Properties hierarchy: scene previews, record transforms,
  zone/hazard settings, AI settings, map/door settings and selection/detail sections.
- `EnvironmentMenu.uxml` owns time-of-day and weather controls.
- `LootConfiguration.uxml` owns loot pools, fixed contents, spawn chances, locks and keys.
- `Controls.uxml` owns the scrollable editor help text.
- All five panel content templates are mounted into the shared `Window.uxml` shell without
  adding a wrapper that changes docking or scrolling.
- `Home.uxml`, `HomePicker.uxml` and `PickerRow.uxml` own the native editor home
  screen and its selection dialogs; home scaling and selection callbacks remain runtime behavior.
- `CategoryRail.uxml`, `WindowsMenu.uxml`, `ContextMenu.uxml` and
  `ConflictShield.uxml` own the remaining navigation and conflict controls.
- Shared dropdowns, browser/tree rows, dock tabs/dividers, tooltips, toolbar pieces,
  campaign-test controls and pooled route captions also have UXML templates.
  Scrollbar, tree, menu-focus and selection styling is in `Editor.uss`.
- `Action.uxml`, `Field.uxml` and `Row.uxml` are reusable templates for generated forms.
- `Editor.uss` owns shared field, action, window and toolbar styling, including
  compact filter/paging rules. Keep compact rules after generic control rules.

C# binds named controls, supplies data, manages visibility, handles input and
maintains docking/resizing geometry. `EditorLayoutSpec` remains the binding inventory for authored templates; its names
and control types are checked against UXML. All fixed editor sections now bind authored UXML; the generic code-built layout fallback has been removed. Browser row data, virtualized tree
factories and tool-specific action visibility remain in C#. `EditorControlLayout` and `EditorToolWindowStyle` assign
semantic/state classes instead of overriding the shared theme with inline styles.

Edit the UXML/USS in the companion CJ-SDK project's UI Builder after syncing
these source files into `Assets/Mods/WTT-Campaigns.Assets/EditorToolkit`.
Copy UI Builder edits back to this repository before building. The repository
files are authoritative; the builder copies them into the SDK on every rebuild.
Preserve the window binding names `TitleBar`, `Heading`, `Close` and `Resize`,
and authored control IDs/types from `EditorLayoutSpec`. Avoid extra wrapper elements
around window contents: docking and tool composition use direct children.

Rebuild assets and run offline validation through MSBuild:

```powershell
dotnet msbuild build.proj -t:Validate -p:RebuildEditorToolkit=true '-p:UnityEditorExe=F:\Unity\Editor\2022.3.43f1\Editor\Unity.exe'
dotnet msbuild build.proj -t:Install
```

The Unity step runs only the asset compiler. It imports and clones templates,
checks window binding slots, rebuilds and reloads the bundle, and records source
and bundle hashes. Validation rejects stale source/bundle combinations and checks
every fixed editor section and the home screen against their binding inventories.
Compiled template calls are checked for missing assets and wrong root types; the
Views layer is checked for code-built standard controls. It also checks
browser clone independence and keeps paging outside scrolling content. Client-only deployment is
supported with `-p:DeploymentScope=Client` when the installed server already has
the matching shared assembly. The client and editor bundle must ship together.

Offline checks do not establish live visual acceptance. After installation,
manually restart the game and check window dragging, resizing and docking;
close/reopen tool windows; toolbar actions and horizontal scrolling; field edits
and numeric dragging; search and paging at narrow widths; and editor UI scale.
Restart the server manually as well if the matching server components changed.

Runtime geometry remains in C#: viewport scaling, docking rectangles, popup placement,
virtualized row data, grid capacity and scene-overlay drawing. Those are behaviors,
not additional fixed screens waiting to be ported.

## Compact editing interactions

Numeric fields share validation and drag limits through `EditorInteractionPolicy`.
Invalid edits remain visible without reaching authoring callbacks or being replaced
by passive refresh. Enter or focus loss commits valid values; Escape restores the
last committed value. Weather channels also apply on commit.

Dropdowns virtualize their options, preserve original indices when filtered, and
show search above ten options. Arrow keys navigate, Enter chooses, Escape closes,
and closing restores focus. Their geometry is constrained to available viewport
space, including placement above bottom-edge controls.

The inspector pins identity and common actions outside the properties scroll.
Foldout state is saved by tool and selection type in a separate preference; existing
window layout and UI-size preferences retain their values. Previews and advanced
assignment/detail sections start collapsed. Scene secondary actions use an overflow
menu when the panel cannot fit their measured captions.

Synchronization and preview status have separate slots. Operation notices stay
available through the Notice button until dismissed or replaced. Conflict rows pair
local and remote values by field path and highlight their differing spans; both
resolution buttons still apply to the whole conflict set.

Offline interaction checks cover invalid values, cancellation, filtered identity,
large option lists, section defaults, conflict text preservation, and popup bounds
at 720p, 1080p, 1440p, and ultrawide resolutions at 60%, 85%, and 130% UI size.
The asset builder clones and reloads the authored templates and checks foldout,
pinned-header, and validation-message composition. These checks do not establish
live rendered readability, focus behavior, or docking acceptance in EFT; those
still require a user-controlled game session.
