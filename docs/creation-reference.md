# Seasonal character creation reference

Source: [Halfman — Starting With Just a KNIFE on KORD BREACH, Episode 1](https://www.youtube.com/watch?v=pmjyu5Dvo4g&t=48s), supplied by the user. Inspected visually in browser playback on 2026-09-05. No YouTube video or imagery is downloaded or packaged as an asset.

| Position | Observed screen | Implementation direction |
| --- | --- | --- |
| 1:05 | Choose your faction: BEAR left, USEC right, full character previews and native buttons | Reuse SPT SideSelectionState with independent default preview profiles |
| 1:15 | Choose appearance: three-column head portraits, close-up character preview, voice dropdown, nickname/count, Next/Back | Reuse SPT HeadSelectionState; submit head/voice through seasonal route |
| 1:35–2:05 | Common modifiers: explanation, large season logo, two columns of three gray cards, Next/Back | Rebuilt full-screen layout with captured order and locally served icons |
| 3:25 | Personal modifiers: negative left, positive right, points centered, signed values beside titles, tinted rows, checkboxes, Reset and Next/Back | Rebuilt full-screen lists using existing selection validation |
| 8:08-8:09 | NEXT opens Save modifiers: warning, positive/negative groups with counts, compact rows, scrollbar, ACCEPT and CANCEL | Restore the recovered confirmation before any creation request |
| After ACCEPT | Navigation dims, bottom-right loader appears, then profile loading | Submit once, switch/reload the seasonal character and close the selector |

Common order observed and matched in captures: No Insurance, Handyman, Seasoned PMCs in the left column; Armor Shortage, Black Division, No FIR for Hideout in the right. Video descriptions are reference copy, not a replacement for captured behavior parameters. Unsupported rules remain disabled and identified in the UI.

Rechecked at 1:40 and 3:29 for update 0.1.15. There are no floating card tooltips. The Common cards have a fine grid over a gray gradient; Personal cards have a fading red/green idle tint, with a neutral gray hover state visible on Broken Secure Container at 3:29. The recovered level47-82 hierarchy supplies the original gradient, tint, tiled grid, shadow and hover colors used by the update. Footage was inspected in browser playback without downloading or packaging YouTube content.

The user identified the missed intermediate frame after 0.1.16. Frame stepping at 8:08 and inspection of the requested 8:09 sequence confirms Save modifiers is part of initial creation. Update 0.1.17 restores it using level49-2761 and level49-794 geometry and captured confirmation localization. The old assertion that this footage bypasses confirmation was incorrect.

Remaining visual work: verify native SPT identity rendering in game against the footage, including faction zoom/selection, head close-up, scrolling portrait grid, voice preview, nickname validation and repeated Back/re-entry. SPT-native art/controls may differ from the newer live build and need further asset recovery after this runtime pass. No full visual-parity claim is made.

UI artwork continues to load from the bundle or existing native EFT assets. Only perk icons use mod-server image requests. No live-backend destinations or normal-profile creation calls are introduced.
