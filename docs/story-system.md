# Story system backport — 0.5.1 implementation candidate

## Story task layout correction

The journal presentation now uses the recovered live `MainQuestPanel` artwork and its chapter-icon/objective templates, rather than generic `UiElements` panel/button colors. `tools/recover_story_journal.py` records 37 presentation sprites in `UI/Resources/Story/journal-provenance.json`. The view follows the 122-pixel rail, 100-pixel chapter entries, 150-pixel title/status region, 32-pixel title, 20-pixel regular objective text, 16-pixel italic notes, 18-pixel group captions, compact expand icons, and native checkbox/status/unread artwork. Scrollbars use the native gray palette and hide when content fits. The title's recovered soft alpha mask is sampled onto a UI mesh, avoiding a dependency on live-only soft-mask shaders. Related links retain text fallbacks when no native item icon is supplied.

The style previews use synthetic chapter data and a cropped room render solely to exercise chapter artwork. They are not installed as season content. Expanded-history/objective controls, completed chapter styling, and read/scroll behavior are checked alongside the layout regression cases. These previews establish the recovered panel styling; they do not establish pixel-identical full-screen live parity.

The beta `TasksPart` uses a vertical layout with forced expansion disabled. Its native `TasksPanel` supplies flexible width and height through a `LayoutElement`. Copying only the RectTransform into the journal lost those inputs, collapsed the journal to zero size, and drew its fallback text outside the screen. `StoryTaskLayout.CreatePanel` now preserves the layout inputs and sibling position, centers the coordinate system, and clips journal content to the task area. The journal waits for a real layout size instead of drawing a fabricated minimum-sized panel.

Story, Side, and Operational now use fresh instances of the native animated task-toggle prefab in their own exclusive toggle group, preserving the native artwork, font, hover, and selection behavior. The original task/item controls are restored on cleanup. Quest inventory and Notes retain their native layout and behavior.

`CampaignsStoryUiPreview.Render` now tests the native parent-layout constraints rather than a standalone fixed-size journal. It checks empty-state containment, switching to the native task panel and back, resizing, visible read markers, and scroll restoration at 1080p, 1440p, and 1902×992. The installed-game tab animations and interaction still require a smoke test. Seasons without authored chapters continue to show an empty journal; this presentation repair does not add story content.

## Trader visit presentation repair

### Animation and environment follow-up

The room root now stays at its authored origin. A 120-frame comparison of each trader's idle animation found that the old `(20000, 20000, 20000)` placement introduced up to 4.7 mm of vertex error and 44–361 times the frame acceleration noise in the sampled facial meshes. The runtime and preview share `StoryRoomCamera.InstantiateRoom`, preventing the preview from silently using different coordinates.

The visit camera explicitly uses deferred shading, HDR and its own depth/depth-normal buffers rather than inheriting the lobby's rendering path. `StoryRoomIsolation` excludes the room from other cameras and lights and temporarily disables external reflection probes; all changes are restored on exit. Room renderers use recovered ambient lighting instead of sampling unrelated lobby LightProbeGroups, which are not included in the prefab bundles.

The original import also omitted `RenderSettings.m_AmbientProbe`. `tools/recover_story_ambient.py` extracts all 27 coefficients for the seven recovered rooms into a small embedded resource, with source hashes. The authored Peacekeeper room retains its flat ambient. `StoryRoomRenderState` applies each room's environment and EFT spherical-harmonic shader globals only during that camera's render and restores the prior values afterward.

`CampaignsStoryMotionChecks.Run` compares 120 identical local poses at both positions for all eight rooms, renders both camera paths, checks lobby camera/light/probe isolation and restoration, and checks ambient state restoration after rendering. Batch previews explicitly run the same lifecycle methods as the client; ordinary MonoBehaviour callbacks are not assumed to run in an editor outside Play mode. This fixes a weakness in the earlier static previews. Installed-game playback and exact live postprocessing parity remain separate checks.

The loader now activates the recovered camera's object and ancestors before enabling the room. Some debug cameras (including Prapor's) were saved inactive, so setting only `Camera.enabled` produced an empty render texture. The recovered shaders also output alpha as rendering data: a Prapor RGBA readback was 99.5% fully transparent. Room rendering now uses an RGB target (R11G11B10 float, RGB565 fallback) so uGUI receives opaque pixels. The camera setup retains authored room visibility, isolates the room on its rendering layer, and disables baked occlusion because the prefab has no scene occlusion data. An opaque backdrop covers the store while a visit loads; in-raid conversations explicitly hide both room layers. Cleanup tolerates Unity destroying audio objects first, and closing a visit invalidates pending presentation work.

Visit follows the locally recovered live `DialogueStartButton` in `level48-49.json`: centered in the trader header, 32 pixels high, 16-pixel type, and the original dialogue button/icon sprites. The dialogue panel follows the 800-pixel live conversation width, compact reply rows, borders and separators; empty dialogue no longer reserves a blank message area. Source artwork and checksums are recorded in `UI/Resources/Story/provenance.json` and embedded in the UI assembly.

`CampaignsStoryPreview.Render` checks all eight room cameras using the runtime preparation code at the runtime location. `CampaignsVisitPreview.Render` validates navigation, busy/skip states, confirmation, history, long-text scrolling and screen bounds at 1080p, 1440p and 1902×992. These are Unity SDK checks; the installed game still needs an interactive smoke test. Install this presentation repair with `tools/install_story_ui.ps1` after building and validating the client; it backs up and replaces only the client/UI assemblies and verifies the installed shared contract is unchanged. That older presentation-only procedure does not apply to 0.5.2: install matching client, UI, shared and server assemblies together with the server stopped.

The optional `SeasonDefinition.Story` extension adds a Seasonal character journal, server-authoritative dialogue and quest progression, eight trader visit rooms, and authored raid interactions. It does not import the live campaign. Existing seasons without Story retain their previous gameplay identity and content. Seasonal characters can open an empty Story journal and visit supported traders even without authored chapters. Installation does not activate an example season or alter an existing character's season.


## Protocol 2 client completion

All automatic text-only NPC lines remain visible until Continue is pressed when another line follows or the conversation closes. The last open line exposes reply choices. Continue advances only presentation; the server has already committed the ordered action chain. Closing a visit, character changes, death and raid transitions cancel pending presentation. One dispatcher orders dialogue, ordinary event media and cinematic completion/skip follow-ups. Interrupted cinematic bindings remain unfinished; ordinary event media never reports binding completion.

Entry availability evaluates CurrentTrader against the trader being visited. Entry Scene is an exact Unity scene name, with empty meaning unrestricted; lobby requests use the native trader screen's scene and raid requests use the bound object's scene. The server enforces restrictions before entry actions. Rehearsal exposes the same context. Story-owned CompleteItem targets project to native variables even without collectible bindings, and replacing the client projection clears obsolete owned keys. Visible objectives can be marked read without any note association.

Raid observations are ephemeral protocol data: character/raid identity, monotonic sequence, carried inventory, known native condition counters/completions, level, skills and free special slots. They replace carried-item eligibility rather than reading the saved stash. Native item JSON travels inside an explicit item envelope to preserve resource and found-in-raid fields across both serializers. Validation rejects wrong characters/raids, stale sequences, unknown targets and invalid numeric bounds. Observations are never saved as inventory or rewards. Open story views refresh on changed facts, and occupied triggers reevaluate as conditions become eligible. Native raid-end reconciliation still owns survival and inventory persistence.

Lobby start/select first calls `/wtt-campaigns/story/prepare` with Operation, operation ID, revision and Scene. The complete automatic chain runs in a staged native transaction. A pending Handover result contains ActionId, QuestId, ConditionId, ConditionJson, Current and authoritative candidate item IDs. The client opens the native handover window and returns Selections keyed by action ID. Repeated preparation retains random draws and earlier selections. A ready result has no pending Handover; committing uses the same PreparationId, operation ID and selections. The server checks profile/inventory/revision again. Cancellation or an invalid selection commits nothing, including earlier acceptance or rewards. Preparation expires after five minutes or a server restart; handovers remain unavailable in raids.

Already committed protocol-1 operations remain replayable using their original request fingerprints and native revisions. Uncommitted protocol-1 operations are rejected for refresh/retry. Story save format and format-1 packs are unchanged; an unset optional TraderId is omitted from media serialization so existing gameplay identities remain stable.

Protocol and Unity checks are recorded in `Research/story-v2-checks.json`, `Research/story-upgrade-checks.json` and `Research/story-v2-unity.log`. Installed-game acceptance still needs real trader switching, native handover cancellation/inventory changes, occupied triggers, pickup/drop, video/Timeline skip and death, plus custom-room audio and cleanup. These tests cannot establish in-game interaction without launching a raid.

## Architecture and conventions

- `Shared/Story`: named protocol enums, authoring contracts, validation, condition evaluation, dialogue transitions, and durable progress. Story saves and authoring remain format 1; story requests and responses use protocol 2, as does the existing season protocol.
- `Server/Story`: routes, profile checkpoints, native quest adapters, and raid reconciliation. Progress lives in the selected Seasonal PMC through `StoryStore`, with separate season/character identities.
- `Client/Story`: native task/trader hooks, journal host, visit playback, media loading, and raid event bindings. Patches follow the existing ModulePatch registration; server patches use the existing AbstractPatch conventions.
- `UI/Screens` and `UI/Media`: reusable presentation and room lighting. Source uses file-scoped namespaces and one new top-level type per file. Unity preview copies convert namespaces to C# 9 block syntax.

The native quest controller owns acceptance, item consumption, completion and rewards. A story operation clones the profile, captures native output and notifications, reconciles automatic transitions, then commits through the existing profile transaction service. A failed native operation leaves story and inventory unchanged. Notifications are released after the durable commit.

Requests carry character, season, operation ID, expected revision and conversation identity. Replayed operations return current story state with the original native-output revision; the client does not reapply an older reward update. Session/dialogue variables do not leak into profile variables. Raid-earned native XP, skills, standing and quest transitions are reconciled after the native raid report so a stale client snapshot cannot erase a committed reward.

## Implemented behavior

| Area | Behavior |
| --- | --- |
| Journal | Story / Side / Operational task categories, chapter navigation, active/completed chapters, latest note/history, required/optional objectives, counters, failure/completion marks, visible-row unread tracking and preserved scroll position. Native quest-item grids remain owned by the Tasks screen. |
| Dialogue | Conditional NPC/player lines, named start points, profile/session/dialogue variables, bounded automatic transitions, random groups, embedded dialogues, confirmations, history and read markers. |
| Native integration | Accept, hand over ordinary items/currency/quest items, finish and reward owned story quests; auto-start/auto-complete; native trade/tasks/services navigation; item inspection, offer and craft navigation. External quest dependencies are read-only. |
| Playback | Native SequenceReader animation/secondary animation/lip-sync keys, authored timed subtitles, image/audio media, skip and cleanup. Authored audio uses the existing UI audio mixer. |
| Raid | Exact-object trigger, Interact, local-player shooting, generated-loot collectible provenance and cinematic bindings. Interact uses the configured native action and an owned prompt. Radio/notebook/intercom entry kinds use the same dialogue authority. |
| Cinematics | Authored VideoClip or inactive prefab with a finite PlayableDirector and StoryCamera. Completion, skip, interruption, death and profile/raid changes clean up playback. Skipping a registered cinematic completes its binding; interruption does not. A dialogue-launched cinematic closes the visit first. |
| Persistence | Per-character and per-season checkpoints, duplicate-event handling, failed-action rollback, native reward reconciliation, and optional survival-dependent raid actions. |
| Authoring | Existing Creator pack import/export retains Story. The composition tool validates and exports overlays using the production repository. Synthetic JSON and cinematic examples are supplied. The Creator story editor includes reference pickers, field help, structural validation and rehearsal with trader, scene and raid facts. |

`PlayerReward` finishes the selected owned quest through native rewards; it is not an arbitrary grant API. `CompleteItem` records a story completion flag; it does not create or consume inventory. `SelectSubService` opens native Services; it does not select or purchase a particular paid service.

## Trader rooms and provenance

| Trader | Source |
| --- | --- |
| Prapor, Therapist | Recovered room and native actor; authored visit camera retained. |
| Fence, Jaeger, Mechanic, Ragman, Skier | Recovered room and actor; camera reconstructed at the unique authored visit-camera anchor. |
| Peacekeeper | User-requested custom logistics office. Beta Glukhar head and aviator glasses on an adapted Prapor body/rig, recolored clothing, and native office/supply props. This is an approximation, not a recovered Peacekeeper actor. Body animation and author-supplied audio are available; recovered Peacekeeper voice recordings and facial blend shapes are absent. |

The donor scenes are `Vendors_*` from the local live build, levels 638–645. The recovered Peacekeeper source itself contains placeholder dictionaries and no visit camera and is not shipped. Fence's 26 generic audio-only recordings are adapted to beta uLipSync BakedData. Other rooms retain recovered generic greetings/farewells/trade recordings. Campaign dialogue and quest text are excluded by the importer whitelist.

The donor's Unity version is 2022.3.43f2; the SDK editor is 2022.3.43f1. Target game references are SPT 4.1.3 / EFT 0.16.9.40743. AssetRipper export alone is insufficient: material dictionary serialization and shader PPtr types require correction, and Unity's editor strips several recovered compiled shaders. Finalization restores original binary shader data, remaps native MonoScript identities, rejects unreviewed scripts/dependencies, and records hashes. Do not distribute an intermediate SDK bundle.

The eight local room bundles total roughly 4 GB. They are ignored build inputs, not committed source. Native game assets retain their owners' rights. The local manifests record hashes and object/script counts. Room rendering has been checked in the SDK; exact live lighting/postprocessing and native beta animation playback are separate acceptance checks.

## Supported contracts and explicit limits

Story conditions include logical groups, variables, quest/condition status, level, trader loyalty/reputation, item counts/handover availability, special-slot availability, new quests, current trader, collectible/trigger flags, location, skills and hideout levels. Numeric comparisons are named; unknown conditions fail validation. Native counter subconditions such as Kills or ExitStatus must remain inside CounterCreator.

`PurchaseService` and `ServiceAvailable` are rejected at publication because this beta adapter does not implement live paid dialogue services. Use the native Services screen. WeaponAssembly is unsupported. Dialogue handover currently excludes items with children; assembled weapon/armor and plate-specific handovers require an additional native eligibility adapter. Normal native task screens retain their existing behavior.

The server knows generated collectible instance IDs, quest/inventory state and active raid identity. It cannot independently prove a client's world-space position or shot; exact-object reports retain the normal local SPT client trust boundary. Survival-dependent actions are applied after a surviving raid result; immediate actions can persist through death. No live maps, story placements, transitions or endings are created.

The journal supports season-owned PNG chapter art. The recovered 241-sprite story inventory is an SDK research input, not automatically assigned to authored chapters. Related-item links currently use text buttons. Story quests are removed from the character Side list; the native trader task list can still show those quests.

## Validation and remaining acceptance

Local evidence is under ignored `Research/Story`, `Research/story-route-checks.json` and `Research/story-raid-checks.json`:

- Full solution builds with zero warnings/errors; 2,486 contract/installed-assembly assertions and 47 UI compatibility checks pass. All 334 source/config files pass CSharpier.
- 49 isolated story route checks: native acceptance/handover/rewards, rollback, retries, read markers, old receipt handling, Normal/Seasonal isolation and content-free journal/visit availability without profile or season changes.
- 37 isolated raid checks: collectible provenance, duplicate events, survival/death behavior, cinematic protocol and stale native reward merging.
- Journal previews at 1920×1080, 2560×1440 and 1902×992: five visibility/scroll checks at each resolution.
- All eight room previews render without unsupported shaders. A five-second synthetic Timeline bundle is built and remapped to native Timeline script identities. SDK evaluation verifies its inactive root, bound two-meter animation, finite duration, render and unload.
- The authoring composition tool exports and re-imports a synthetic pack through the production checksum/gameplay-identity checks.

**Still required before calling this full live parity:** installed-beta visit playback/lip-sync and entrance checks for every room; native Tasks/inventory layout and input restoration; offer/craft navigation; actual in-raid interact/trigger/shoot/collectible callbacks; real Video/Timeline completion/skip/death cleanup; repeated visit memory/audio checks. The SDK previews and HTTP tests do not establish those results. The new observation regression tests cover pickup/drop and counter reset semantics; actual native game callbacks remain a manual acceptance check. Compound-item handover remains unsupported. No automated native game UI control was available in this session.

## Build, package and install

Run `dotnet msbuild build.proj` to build, validate and install matching components with backups and SHA-256 verification. Configuration, creator content and profiles are preserved. Never stop or start servers or clients. If a required file is locked, report the blocked installation and let the user close the application. Installed assemblies take effect after the user manually restarts the affected application. See [build and deployment](build-deployment.md).

The Story tab and supported trader VISIT buttons appear on the loaded Seasonal character. A season without Story returns an empty, transient definition; visiting and reading this empty system do not add content or change the season. Normal characters keep the original interface. Keep synthetic testing packs separate from production season selection.

## Existing research reused

The project task folders were inventoried, including Backport trader task unlocks, Plan season configurator tool, Plan BattlePass UI integration, Investigate seasonal perk backport, Improve project namespaces, Replace generic JSON types, the profile/tutorial UI tasks, and the perk implementation tasks. Their durable code and guides supply the native progression hooks, Creator repository, character transactions, Unity preview pattern and syntax conventions used here. The Map Loot Editor task and Assembly-CSharp documentation task were also checked for placement and native API context. Historical deployment actions in those tasks do not change the current instruction to leave running applications alone.

## 0.5.1 visibility correction

0.5.0 loaded correctly but gated the entire interface on Snapshot.HasStory. Installed Season One had no Story extension, hiding both the journal and VISIT. Availability now depends on the active Seasonal character and loaded native profile. HasStory continues to describe authored content. The server supplies an empty definition when content is absent, and the client skips quest reconciliation for that empty journal. No activation, migration or sample content is required.
