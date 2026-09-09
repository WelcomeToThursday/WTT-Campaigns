# Authoring a story-system pack

Story is an optional extension to the existing season definition. Use [the format-1 synthetic overlay](examples/story-introduction.json) as a small working example. It adds a chapter, a journal note and a Prapor conversation; it contains no live campaign content or inventory rewards. All eight visit rooms are available on the loaded Seasonal character, including seasons without authored conversations.

## Edit and rehearse

The Season Creator story editor supports chapters, dialogues, entries, raid bindings and media. Use its reference pickers and validation before publication. Rehearsal includes the candidate trader, exact Scene, raid facts, and explicit cinematic complete/skip/interrupt controls. Rehearsal simulates progression; it cannot validate Unity bundle contents or native inventory windows.

Text-only automatic NPC lines use Continue between lines, and closing text stays until acknowledged. A final open NPC line shows the reply choices. Continue does not rerun story actions. Multiple automatic lobby handovers collect native item selections before any part of the transaction commits; cancelling a selection abandons the entire operation.

## Create and exchange a pack

1. In the Season Creator, create or duplicate an unused season, configure its normal rules and publish/export a base ZIP. Gameplay changes to a used season require a new season identity.
2. Edit an overlay JSON with `Story` and, when needed, additional `Quests`, `Locales` and `Dependencies`. Native quest definitions remain in top-level `Quests`; `Story.Quests` supplies chapter membership. Added native quest IDs must not collide with base quests. Overlay Story replaces the base Story extension, so carry forward existing memberships when editing a story pack.
3. From the project root, compose and validate it:

```powershell
dotnet run --project Tests -c Release -- --story-pack base-season.zip docs/examples/story-introduction.json new-story-season.zip
```

The command uses the production repository's import, validation, publication and export logic in a separate temporary authoring directory. It re-imports the result to verify checksums and gameplay identity. It never opens installed profiles, queues a season or overwrites an existing output ZIP. The printed recovery workspace preserves the draft if validation fails. The host is the existing Tests console, consistent with the project's fixture tools; it is not a game runtime dependency.

4. Import the resulting ZIP through the Creator, validate installed dependencies and publish. Queue activation only when ready; after the user manually restarts the server, create/select a character for that season. Preserve the character's existing pack alongside profile backups.
5. Send the Creator ZIP and any separately built client media together. Creator ZIPs contain definitions/PNG artwork only; they do not carry Unity bundles or executable code.

## Identities and state

Every owned chapter, note, link, variable, dialogue, line, action, entry and binding needs a unique lowercase 24-character hexadecimal ID. Native item/trader IDs are external references. Existing quests used as prerequisites need `quest:<id>` dependencies; story mutations can target only native quest definitions owned by that story.

Variables declare `Scope` as `Profile`, `Session` or `Dialogue`, plus `InitialValue`. Scope on SetVariable must match. `Profile` means this character in this season. Session values reset on a new client session; dialogue values belong to a conversation. A dialogue declares `MainVariable`, optional named `StartPoints` and conditional `Lines`.

A line has `Side` (`Npc` or `Player`), `Text`, `Trigger`, `Actions` and optional `Playback`, `Confirmation` and `Random`. Exactly one eligible automatic NPC line may run at a time. Change a phase variable so an NPC line becomes ineligible after execution. Ambiguous NPC branches and automatic loops roll back the operation. Random gates use one draw per named variable/group; all members of a group must use the same maximum.

Text falls back to the definition's English fields. Locale keys are `<chapter> name`, `<note> text`, `<line> text` and `<line> confirmation`. Chapter `Image`/`Icon` reference season-owned artwork registered through the existing Creator image pipeline. Note links support Item, Offer (with TraderId) and Craft targets. A chapter can include main and optional quests, visibility conditions, automatic start/completion and status-triggered notes.

See `Shared/Story/StoryDefinition.cs` and its adjacent typed contracts for the complete serialized shape; `StoryValidator` is the authoritative structural check. Unsupported native behavior is an error, not a fallback to success. Review the [compatibility limits](story-system.md#supported-contracts-and-explicit-limits) before authoring compound-item quests or paid services.

## Media and animation

Place reviewed bundles under `BepInEx/plugins/SeasonalPerks/StoryMedia/`. Use a unique path such as `packs/<season-id>/visit-media.bundle` for each authored media set. Register each asset in `Story.Media`:

```json
{
  "Id": "770000000000000000000020",
  "Kind": "Cinematic",
  "Bundle": "examples/story-test.bundle",
  "Asset": "assets/story-test.prefab",
  "Sha256": "REPLACE_WITH_THE_64_CHARACTER_BUNDLE_SHA256"
}
```

Kinds are Image, Audio, Video, Cinematic and TraderScene. Assign optional TraderId on a TraderScene reference to override that trader's built-in room. Only one assigned room per trader is allowed in a season. Its prefab root must be inactive and contain exactly one camera named StoryCamera. Dialogue native animation/lip-sync cues also require a compatible SequenceReader with the authored keys. Rooms reuse camera isolation, UI audio routing and cleanup. An unassigned legacy TraderScene stays in the pack with an editor warning and is not selected for a visit. Hashes are checked before load, and conflicting hashes for a shared path are rejected. Media paths cannot escape StoryMedia. Bundles are locally installed trusted assets; the runtime does not download or execute arbitrary code from a Creator ZIP. Rebuilding a bundle requires updating its hash and publishing a compatible pack revision/new season.

`Playback.Image`, `Music` and `Sound` reference registered media IDs. Music loops for the line, Sound plays once, and timed subtitles display their localized Key between Start and End. Audio uses the UI mixer. Native animation/secondary/lip-sync sequences use exact dictionary keys from the corresponding room; each has Start, End, Speed and Volume. Do not reuse numeric live enum ordinals or assume all traders share keys. Peacekeeper has body gestures but no recovered lip-sync dictionary.

A cinematic prefab must have an **inactive root**, a single named `StoryCamera`, a finite PlayableDirector with Play On Awake disabled, and all explicit bindings inside its dependency closure. Use a reviewed camera layer/mask and no gameplay registration scripts. A Video resource references a VideoClip. The supplied synthetic Timeline moves a marker for five seconds and contains only native Timeline scripts.

Build the example with `SeasonalPerks.Tools.SeasonalStoryExampleBuilder.Build` in the matching SDK, then run:

```powershell
.\.tools\Scripts\python.exe tools/finalize_story_example.py
```

The finalized hash and native script audit are written beside `examples/story-test.bundle` in `story-test.json`. Use that hash in authoring. This example is a playback fixture, not a raid placement.

## Entry scenes

Entry Scene is the exact Unity scene name, not an object path or location ID. Empty allows any scene. Lobby visits use the native trader screen scene; raid entries use the bound object's scene. For object bindings, Scene must match the prefix before `:/` in ObjectPath; publication validates the relationship. Collectible scene context comes from the generated loot object. Rehearsal must use the same scene name to exercise a restricted entry. CurrentTrader always uses the entry's trader, including the first visit and trader switching.

## Raid bindings

`RaidBindings` declare Location, Kind, Condition, Once, PersistOnDeath and optional Actions/EntryPointId/MediaId. Kinds are Trigger, Interact, Shoot, Collectible and Cinematic. Match an existing target using the exact `scene:/Root/Child` path from the beta scene. Bind trigger callbacks to the actual trigger collider object, Interact to a raycastable object, and Shoot to an object with a BallisticCollider. Missing targets are logged rather than guessed by name.

Collectibles use ItemId (template) and the server's generated loot instance IDs. Place loot with the existing season placement/loot-editor tools; a binding does not spawn it. Early pickups are buffered until story state loads. Trigger/radio/notebook/intercom content is authored separately; no live-world placement is imported.

Ordinary Trigger, Interact, Shoot and Collectible bindings can reference Image, Audio, Video or Cinematic media in MediaId. Accepted events present that media immediately, then their conversation: images wait for Continue, audio plays once with Skip, and Video/Timeline use playback controls. This presentation never sends cinematic-binding completion messages.

`PersistOnDeath: true` commits eligible actions immediately. Otherwise the server defers them until a surviving raid result. Cinematics send begin followed by complete, skip or interrupt; skip completes the binding and interruption leaves it unfinished. Scene callbacks and camera playback still require actual beta raid acceptance testing.

## Recovering the built-in rooms

These steps need the locally supplied donor, beta game, AssetRipper, Python dependencies and companion CJ-SDK. They are not a clean-checkout asset download process.

1. `recover_story.py` inventories UI hierarchies/sprites and the eight scenes.
2. `prepare_story_scene.py` and `prepare_story_dependencies.py` repair donor type trees and generic dictionaries for export. Export the prepared dependency set with AssetRipper into `Research/Story/CompleteTradersExport`.
3. `recover_story_shaders.py` supplies SDK material/shader inputs. `import_story_traders.py` imports only the reviewed closure and strips unsupported gameplay scripts; `recover_story_fence.py` adapts the whitelisted Fence voice clips. `import_story_peacekeeper.py` imports the bounded beta actor/accessory assets needed for the custom room.
4. Sync UI preview sources with `tools/sync_ui_preview.py`. Copy `tools/unity/SeasonalStoryBuilder.cs`, `SeasonalPeacekeeperBuilder.cs`, `SeasonalStoryPreview.cs` and related builders into the SDK Editor assembly, converting file-scoped namespaces for Unity C# 9.
5. Execute `SeasonalPerks.Tools.SeasonalStoryBuilder.Build`, then `tools/finalize_story_traders.py`. Selective rebuild/finalization is supported for Peacekeeper and Fence. Finalization restores compiled shaders and remaps scripts to beta assemblies; skipping it produces unusable intermediate bundles.
6. Execute `SeasonalPerks.Tools.SeasonalStoryPreview.Render` for all room previews. `SeasonalStoryUiPreview.Render` checks journal visibility/scroll at three sizes and builds the synthetic cinematic; finalize that example afterward.
7. Run `tools/package.ps1`. Keep all game assets and generated bundles out of Git.

The local prepared/export directories are large. Preserve the manifests and exact donor version when rebuilding. A newer donor needs a fresh dependency/script/shader review.

## Historical acceptance fixture

The server fixture described by earlier validation is retired from the workflow. Use the offline checks and mandatory installation in [build and deployment](build-deployment.md); never stop or start any server or client. The old fixture used synthetic accounts and deliberate fake trigger/cinematic object paths. It is not a live-game acceptance check.
