# UI update 0.1.24

## Multiple seasonal characters

The current selector uses a wrapping horizontal card carousel, a season picker, and separate delete/wipe confirmations. Wipes preserve earned achievements and return to faction, appearance and modifier creation. See [character selection, persistence and verification](characters.md); this supersedes the historical two-card layout described below.

## Battle Pass and seasonal hub

The Seasonal character's main menu now exposes the KORD BREACH banner and the recorded Battle Pass, Seasonal Rewards and About the Season views. The full captured reward catalogue is browsable, with neutral progress and unavailable transaction actions. See [reference, asset workflow and validation](battle-pass-ui.md). Reward progression, exchanges, purchases and automatic resets remain unimplemented.

## Save modifiers confirmation

NEXT on Personal modifiers opens the live Save modifiers window before submitting creation. The 8:08-8:09 reference was rechecked frame by frame: the prior 0.1.16 interpretation missed this brief step. The recovered 1000x546 window presents the captured warning, positives first and negatives second with counts, compact icon/name/description rows, a scrollable list, and ACCEPT / CANCEL. Its border artwork is bundled; perk icons remain server-served.

CANCEL or Escape returns to the unchanged draft. ACCEPT locks navigation and submits once, then shows the native bottom-right loader while the existing local adapter creates, switches to, and reloads the seasonal profile. Error recovery retains cosmetics and the draft, or reconciles a completed creation before retrying loading.

Unity checks cover review-before-submission, group order/counts, scrolling, empty selection, cancellation, sounds, duplicate input suppression and retry. Preview renders cover 1080p, 1440p and 1902x992. In-game loading and visual verification remain necessary.

## Modifier card hover and artwork

Common and Personal modifier cards no longer open hover tooltips. Pointer entry shows the recovered neutral hover layer; exit restores the idle or selected tint. Selection while hovered preserves the highlight, and busy states, dialogs and disabled objects clear it. Common rules remain read-only.

Cards now bind the already bundled modifier-gradient, modifier-tint, modifier-grid and modifier-shadow sprites instead of replacing the background with a flat fill. The grid tiles at its original size, idle tint fades over the left part of a personal card, and selected tint covers the row. Colors and layer order come from level47-82. No new icon bundling or server image routes are introduced.

Compared against the supplied YouTube reference at 1:40 (Common) and 3:29 (Personal, neutral hover over Broken Secure Container). Automated Unity checks cover tooltip absence, artwork bindings, hover/selection/exit and busy state; preview images include idle and hover states at all three resolutions.

## Button feedback

Custom navigation, profile selection, character creation and dialog buttons now share EFT ButtonOver/ButtonClick feedback. Disabled buttons stay silent, pointer hover fires once per entry, and keyboard back uses MenuEscape. Native cloned controls retain their own feedback components.

Perk cards play the original live toggle-on/off clips only after accepted selection changes. RESET uses the original reset clip. Live UISoundsWrapper sound types 61/62/63 reference resources.assets AudioClips 4354/4004/3941; recovery verifies these bindings and records hashes. All five recovered UI sounds are bundled and played through GUISounds, respecting the interface mixer. Only perk icons come from the server.

Unity checks exercise single hover/click feedback, disabled buttons, perk on/off/reset, rejected selections, modal and busy suppression, and keyboard back. Audible playback and volume still require an in-game check.

## Live profile-card content

The two underlying Normal and Seasonal profiles now use PvE Zone / PvE Season captions for SPT and captured description text, including the seasonal reset and Arena wording reserved for future implementation. These text changes do not enable wipes or Arena synchronization.

The expanded seasonal card removes the custom SEASON MODIFIERS heading and VIEW GLOBAL RULES / EDIT PERSONAL PERKS buttons. It retains the KORD BREACH banner, six uniformly tinted common modifier labels, the recovered separator and the live SEASON STATS section: Battle Pass rewards, Story Chapters, K/D and Survivals. Statistics have presentation bindings but remain blank until their backing systems supply values. Existing editing remains reachable from the main seasonal interface.

The description brightens on hover and the information block moves by the captured 455 units. The modifier list and statistics move with it and are clipped to the card while entering, matching live's reveal instead of drawing below the card. Original divider and stat icon sprites (sharedassets47.assets objects 53, 74, 75, 64 and 37) are bundled; perk icons still come from the local server. Their assignments were recovered from the serialized CharacterSelectionSeasonPanel stat-icon dictionary. Captured English stat labels are retained in the localization fixture.

Unity checks cover original labels, statistics rows, blank unimplemented values and removal of custom shortcuts at 1080p, 1440p and the wider window size. The earlier live hover recording is no longer present at its supplied path, so this revision uses the recovered live hierarchy, metadata and captured localization. Final in-game visual comparison remains pending.

## Original profile hover sounds

Profile cards now play their distinct live hover clips. The live regular card ButtonFeedback selects EUISoundType 70 (SeasonProfileChooseButtonHover), while the seasonal card selects 82 (SeasonChooseModeButtonHover). The live UISoundsWrapper maps these to resources.assets AudioClips 4016 and 4241. Neither enum entry exists in SPT 0.16.9, so the decoded clips are bundled locally and played through GUISounds.PlaySound, retaining interface mixer settings.

`tools/recover_profile_audio.py` verifies the live wrapper bindings and records source/object/export hashes under CJ-SDK Audio/provenance.json. Feedback fires once per pointer entry, resets on exit/disable, and is suppressed while selection is busy. UI checks exercise both clip choices, duplicate entry suppression, reentry and busy state. Perk icons remain the only artwork requested from the server.

## Live Skills modifier display

The embedded Skills view now follows the supplied 14.00.30 recording: a native MODIFIERS tab with its recovered normal/selected icons, one scrollbar, and Common / Positive / Negative sections. Each section uses two 780 x 136 cards per row, 8-unit gutters, 16-unit header spacing and 32-unit section gaps. Names include signed costs. The original grid, gradient, tint and shadow sprites are bundled; perk icons remain requested individually from the local server. Long descriptions grow the row rather than clipping.

The view is transparent over EFT's background and retains the native experience bar and character navigation. Search, checkboxes, budgets and edit buttons are removed from this display. Editing remains available from character selection. Only enabled common rules and selected personal perks appear, preserving catalogue order. Normal and Scav profiles display an empty state. See skills-reference.md for source and validation details.

## Profile selection sound

The Normal and Seasonal SELECT/CREATE buttons play EFT's native ButtonClick interface sound once through shared button feedback, before switching or opening creation. Standard clicks use the game's existing audio; seasonal-specific clips are bundled as described above.

## First-open Skills crash

The PERKS tab now initializes after native SkillsAndMasteringScreen.Show. EFT creates its tab group in Awake, and Show activates the screen before using that group; the previous prefix could read a null group on the first opening. The mod validates the group before cloning anything, registers the tab only after its content is ready, and removes incomplete tabs on failure so a later opening can retry. Initialization errors are logged without interrupting the normal Skills screen.

Packaging checks the native activation order and verifies the compiled client uses a guarded postfix. These checks cover the reported initialization-order regression; first opening, repeated opening and both characters still need an in-game retest.

## Character switching follow-up

Pressing SELECT for a different character immediately displays a copy of EFT's native full-screen PvE loading screen, including its animated logo. The overlay renders before pending operations are flushed and stays above the profile cards until the requested character is loaded and verified. The backend's own loading-screen transitions cannot hide this overlay early. Completion or failure removes the overlay; a failed switch leaves the cards and error message available for retry. Selecting the already-loaded character still closes selection immediately. Client compilation and native loading-screen bindings are checked; in-game timing and visual verification remain pending.

The reported seasonal-to-normal failure accepted the server switch but did not finish loading the requested client profile. The client now flushes pending operations once before the mutation, checks the flush result, and uses EFT's native forced RecreateBackend operation. The previous custom sequence redundantly flushed after the server changed modes and awaited connection shutdown where native EFT deliberately does not. The pending target identity is applied by the existing SPT ModulePatch at CreateBackend, after the old profile's unload phase. Completion requires the actual loaded PMC ID to match the requested profile.

Selecting an already-selected server mode no longer skips reconnect when the client still holds the other character. Five regression assertions cover matching, mismatched and missing loaded identities. Nine isolated-server round trips verify both directions from normal/seasonal caller identities and unchanged PMC/Scav inventory, quests, skills, traders and appearance. The client build and 21 native compatibility checks pass. These checks do not exercise the in-game websocket, scene unloading or menu recreation; the reconnect fix still needs an in-game retest. Progress logs identify queue flush, reconnect, backend creation and verified completion without recording profile IDs.

## Recording follow-up

The supplied 06.05 recording of 0.1.5 shows faction selection, a blank appearance step, common/personal modifiers, confirmation and the newly created character. The accompanying client log records three undisposed PlayerBody errors when leaving identity creation. In 0.1.6 the cloned appearance canvas runs its native OnEnable initialization before binding, its visibility and input state are explicitly restored after transition, and Next is gated on loaded portraits/voice options, a visible canvas and a ready character preview. Native PlayerProfilePreview.Close now runs before destruction to release each model. A diagnostic records appearance visibility and option/model readiness without profile identifiers or nickname contents.

This patch builds against the SPT client; resolving the recorded blank appearance step still requires an in-game retest. The remaining visual differences, including the older native faction presentation, checkbox styling, tooltip placement and final confirmation, are not claimed resolved by this patch.

## Seasonal creation from the video reference

The empty Seasonal card now opens **faction → appearance/nickname → common modifiers → personal modifiers → confirmation**. The first two steps reuse cloned SPT native views with default preview profiles, head portraits and voice selection. They have no registered screen controller and never call EFT's normal create-profile operation. The final confirmation sends only the selected faction, nickname, head, voice and perks to the mod's local seasonal creation route. Server validation rejects missing-template, wrong-type, non-default and wrong-faction appearance IDs before creating a linked profile. Older requests without appearance IDs retain the previous defaults.

The modifier steps follow [Halfman's creation footage](https://www.youtube.com/watch?v=pmjyu5Dvo4g&t=48s): full-screen background and green top glow, six common cards in two columns, season logo, negative/positive lists with signed costs beside names, centered points, Next/Back and Reset. Their catalogue order and descriptions come from captured data. Unsupported and disabled rules remain explicitly marked. Back preserves appearance and perk drafts; nothing is created until the confirmation is accepted. Existing-character editing retains its editor view. The Skills tab uses the read-only modifier display described above.

This is the first creation-flow implementation. The native identity adapter compiles against the installed SPT assembly, but its model cameras, transitions, voice audio and lifecycle need an in-game check. SDK previews exercise the modifier screens, with a stub at the native identity boundary. They do not validate native identity rendering. The final confirmation still uses the backport's existing dialog; exact live confirmation parity is pending. See [reference observations](creation-reference.md).

The selector now opens at the first main-menu initialization, before menu input is available. It uses two cards, **Normal** and **Seasonal**, as requested. Their 390x800 proportions, spacing, Bender font, original mode badges, card backgrounds, faction emblems, season banner and empty-character artwork follow the supplied live screenshot. The seasonal card moves its information upwards and reveals the season modifiers on hover, following the supplied recording.

Existing characters use SPT's `PlayerModelView`, equipment descriptor and `CameraImage` to render each profile's own equipment. The camera and lighting rig comes from live level47; preview lights are isolated from the other card and game cameras. Closing or rebuilding a page cancels model loading and releases the render texture and model. New characters use live's original empty-slot artwork.

Selecting the already active profile closes the selector. Selecting the other profile uses the existing switch operation. Startup has no Back button; Escape returns to the profile cards while startup selection is pending. Subsequent visits from **CHARACTERS** or **F8** have a Back action. The native **PERKS** tab beside Skills and Mastery remains available.

## Recording fixes in 0.1.3

The selection background fills the viewport independently of the fixed card layout. The original `TopGlowPvPSeason` image from level45/sharedassets45 is rendered across the top using additive blending and the live 0.6 alpha. Seasonal idle/hover effects now use the six original frame sprites with restored atlas padding, a one-second hold and two-second InOutSine crossfade. Their source settings are in the recovered level47 hierarchy. These glow images are bundled with the rest of the UI artwork as of 0.1.4.

The information background preserves its native 27/27/27/75 sprite borders and extends to the card footer while sliding, eliminating the floating rectangular cutoff. The footer gradient now sits behind the season banner. The disabled native full-screen black overlay remains disabled.

## Assets and server data

The bundle contains all 26 recovered UI artwork sprites, 11 recovered layout prefabs, one camera/light prefab, the recovered Bender font, and an additive UI material/shader. UI sprites load synchronously from the bundle with their original borders and alpha. Only the 39 perk icons are requested by perk ID from the local server; they are excluded from the bundle. Assets and provenance remain under CJ-SDK's `Assets/Mods/SeasonalPerks.Assets`.

The snapshot adds an appearance-only descriptor for each character: nickname, level, faction, customization and the equipped item tree. It excludes stash roots, quests and other progression data. This is read-only UI data; no gameplay effects or selection rules were added in this update.

## Install

Run `tools/package_ui.ps1`, close the game and installed SPT server, then run `tools/install_ui.ps1`. The installer verifies package hashes, backs up existing mod files and installs both client and server support. Restart the server before launching the game. Existing server configuration and user profiles are not included in the package or overwritten.

Both parts must be updated: 0.1.4 removes the decorative-art server routes and loads those sprites from the bundle. The installer moves the obsolete server selection-artwork folder into its backup. The installer refuses to overwrite a running server's locked assembly.

## Validation and limits

- Client and server Release builds pass without warnings.

- 20 read-only assembly checks cover native menu/tab/input and model-rendering contracts.

- 49 Unity interaction checks pass at each of 1920x1080, 2560x1440 and 1902x992, including creation ordering, Back/draft persistence, budgeting, unsupported selections and reset/confirmation cancellation, plus existing selector/editor checks.

- 14 isolated-server creation checks cover explicit BEAR/USEC heads and voices, invalid/faction-mismatched IDs, duplicate creation and unchanged normal-character data.

- 42 read-only checks against an isolated SPT server validate appearance contracts, equipped item trees, all 39 perk-icon bytes and an unchanged synthetic normal-profile file.

- SDK previews use Gamma, matching both supplied game builds. The SDK's original Linear setting is restored afterwards; its project-settings file hash is unchanged.

`Research/UI/characters-*.png` and `characters-hover-*.png` are layout renders. Their character images are editor-only stand-ins using the recovered empty-slot artwork. They do not demonstrate runtime equipment models. In-game model pose, lighting, framing and repeated opening/closing still need testing. Exact animated visual parity is not yet established.

Intentional SPT differences: two cards instead of three; Normal/Seasonal labels; no online-PvP or Arena claims, countdown, automatic resets or online battle-pass statistics. Local Battle Pass progression is covered in [Battle Pass gameplay](battle-pass-gameplay.md). Personal-perk editing remains available through the seasonal details. Live's specialized character hover animation is not backported; SPT's existing menu-character animation is used.

Reproduce artwork with `tools/recover_selection_art.py`; build in Unity with **SDK / Seasonal Perks / Build recovered UI**. Run `tools/sync_ui_preview.py` then **Render UI previews** for 27 Gamma layout renders and interactions. `tools/test_selection_ui.py` uses only the isolated runtime and its existing synthetic account; `tools/test_creation_ui.py` creates fresh synthetic accounts there. Raw packet captures and user account data are not packaged.
