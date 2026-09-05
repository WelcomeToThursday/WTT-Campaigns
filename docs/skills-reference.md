# Skills modifier reference

Reference: local recording `Escape From Tarkov 2026.09.05 - 14.00.30.44.mp4` (6.70 seconds, 2560 x 1440). The beginning and end show the top of the list; the middle shows scrolling through positive and negative modifiers. Extracted reference frames remain under `Research/live-skills-reference` and are not shipped.

The live screen retains the native experience panel, Skills / Mastering / Modifiers tabs, character navigation and environment. Its modifier content uses one scrolling viewport. Common rules precede selected positive modifiers, followed by selected negative modifiers. Each group fills two columns in catalogue order, with the final odd card on the left. There is no search box, selection checkbox, budget or edit action in this view.

## Recovered sources

- `level44`, object 5113: SeasonalPerksList, containers and headings. Grid cells 780 x 136, grid gaps 8 x 8, group header gaps 16, section gaps 32.
- `level44`, object 6378: native Modifiers tab; normal and selected icon references.
- `sharedassets44.assets`, objects 1471 and 3075: common and personal display card templates, with 136-unit icons, 18-unit names and 16-unit descriptions.
- `sharedassets44.assets`, sprites 630 / 676 / 908 / 749: card gradient, personal tint, tiled grid and shadow. Sprites 511 / 492: normal and selected tab icons.

Recovery scripts preserve source hashes, object IDs and exported image hashes in CJ-SDK. The UI bundle contains these decorative assets, not the 39 server-served perk icons. The SDK builder reconstructs uGUI components without live MonoScripts.

## Validation

Unity previews exercise a native-sized content region at 1920 x 1080, 2560 x 1440 and 1902 x 992. Checks cover one shared scroll view, read-only controls, section ordering, active filtering, two-card rows, native card widths, text clipping, recovered grid sprites, bottom scrolling, empty personal groups, and normal/Scav empty states. The full personal catalogue is also laid out to check long descriptions and unavailable notices. Existing creation and editor checks still run.

The preview does not run EFT's native screen or render its environment. Header alignment, tab selection and overall integration still require an in-game retest. Disabled or unsupported common rules are not presented as active solely to match the reference's six cards; actual state determines displayed entries. This update adds no effect implementations.
