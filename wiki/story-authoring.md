# Story authoring

Story is an optional extension to the existing campaign definition. Use [the format-1 example overlay](examples/story-introduction.json) as a small working example. It adds a chapter, a journal note and a Prapor conversation; it contains no live campaign content or inventory rewards. All eight visit rooms are available to campaign characters, including campaigns without authored conversations.

## Edit and rehearse

The Campaign Creator story editor supports chapters, dialogues, entries, raid bindings and media. Use its reference pickers and validation before publication. Rehearsal includes the candidate trader, exact Scene, raid facts, and explicit cinematic complete/skip/interrupt controls. Rehearsal simulates progression; it cannot validate Unity bundle contents or native inventory windows.

Text-only automatic NPC lines use Continue between lines, and closing text stays until acknowledged. A final open NPC line shows the reply choices. Continue does not rerun story actions. Multiple automatic lobby handovers collect native item selections before any part of the transaction commits; cancelling a selection abandons the entire operation.

## Advanced: compose a pack from JSON

1. In the Campaign Creator, create or duplicate an unused campaign, configure its normal rules and publish/export a base ZIP. Gameplay changes to a used campaign require a new campaign identity.
2. Edit an overlay JSON with `Story` and, when needed, additional `Quests`, `Locales` and `Dependencies`. Native quest definitions remain in top-level `Quests`; `Story.Quests` supplies chapter membership. Added native quest IDs must not collide with base quests. Overlay Story replaces the base Story extension, so carry forward existing memberships when editing a story pack.
3. From the project root, compose and validate it:

```powershell
dotnet run --project Tests -c Release -- --story-pack base-season.zip wiki/examples/story-introduction.json new-story-season.zip
```

This optional command requires a source checkout and .NET SDK 10. It validates the composed pack without changing installed profiles or overwriting an existing output ZIP. If validation fails, the printed recovery workspace preserves your draft. For browser-only authoring, use the Creator's story editor and export the published pack directly.

4. Import the resulting ZIP through the Creator, validate installed dependencies and publish. Restart the server to load the published pack, then create or select a character for that campaign. There is no global activation step in the Creator. Preserve the character's existing pack alongside profile backups.
5. Send the Creator ZIP and any separately built client media together. Creator ZIPs contain definitions/PNG artwork only; they do not carry Unity bundles or executable code.

## Identities and state

Every owned chapter, note, link, variable, dialogue, line, action, entry and binding needs a unique lowercase 24-character hexadecimal ID. Native item/trader IDs are external references. Existing quests used as prerequisites need `quest:<id>` dependencies; story mutations can target only native quest definitions owned by that story.

Variables declare `Scope` as `Profile`, `Session` or `Dialogue`, plus `InitialValue`. Scope on SetVariable must match. `Profile` means this character in this season. Session values reset on a new client session; dialogue values belong to a conversation. A dialogue declares `MainVariable`, optional named `StartPoints` and conditional `Lines`.

A line has `Side` (`Npc` or `Player`), `Text`, `Trigger`, `Actions` and optional `Playback`, `Confirmation` and `Random`. Exactly one eligible automatic NPC line may run at a time. Change a phase variable so an NPC line becomes ineligible after execution. Ambiguous NPC branches and automatic loops roll back the operation. Random gates use one draw per named variable/group; all members of a group must use the same maximum.

Text falls back to the definition's English fields. Locale keys are `<chapter> name`, `<note> text`, `<line> text` and `<line> confirmation`. Chapter `Image`/`Icon` reference season-owned artwork registered through the existing Creator image pipeline. Note links support Item, Offer (with TraderId) and Craft targets. A chapter can include main and optional quests, visibility conditions, automatic start/completion and status-triggered notes.

Use the Creator's reference pickers and Validate action to check supported fields and relationships before publishing. Review the [compatibility limits](story-system.md#supported-contracts-and-explicit-limits) before authoring compound-item quests or paid services.

## Media and animation

See [custom story media bundles](story-media-bundles.md) for the complete Unity build, custom-trader Visit, installation, checksum, distribution and troubleshooting workflow.

Place reviewed bundles under `BepInEx/plugins/WTT-Campaigns/StoryMedia/`. Use a unique path such as `packs/<season-id>/visit-media.bundle` for each authored media set. Register each asset in `Story.Media`:

```json
{
  "Id": "770000000000000000000020",
  "Kind": "Cinematic",
  "Bundle": "examples/story-test.bundle",
  "Asset": "assets/story-test.prefab",
  "Sha256": "REPLACE_WITH_THE_64_CHARACTER_BUNDLE_SHA256"
}
```

Kinds are Image, Audio, Video, Cinematic and TraderScene. Assign TraderId on a TraderScene reference to add Visit support to a custom trader or override a built-in trader's room. Only one assigned room per trader is allowed in a season. Its prefab root must be inactive and contain exactly one camera named StoryCamera. Dialogue native animation/lip-sync cues also require a compatible SequenceReader with the authored keys. Rooms reuse camera isolation, UI audio routing and cleanup. An unassigned legacy TraderScene stays in the pack with an editor warning and is not selected for a visit. Hashes are checked before load, and conflicting hashes for a shared path are rejected. Media paths cannot escape StoryMedia. Bundles are locally installed trusted assets; the runtime does not download or execute arbitrary code from a Creator ZIP. Rebuilding a bundle requires updating its hash and publishing a compatible pack revision/new season.

`Playback.Image`, `Music` and `Sound` reference registered media IDs. Music loops for the line, Sound plays once, and timed subtitles display their localized Key between Start and End. Audio uses the UI mixer. Native animation/secondary/lip-sync sequences use exact dictionary keys from the corresponding room; each has Start, End, Speed and Volume. Do not reuse numeric live enum ordinals or assume all traders share keys. Peacekeeper has body gestures but no recovered lip-sync dictionary.

A cinematic prefab must have an **inactive root**, a single named `StoryCamera`, a finite PlayableDirector with Play On Awake disabled, and all explicit bindings inside its dependency closure. Use a reviewed camera layer/mask and no gameplay registration scripts. A Video resource references a VideoClip.

Creator ZIP files do not include media bundles. Distribute the matching media separately and give players its installation path. Text-only stories do not require custom bundles.

## Entry scenes

Entry Scene is the exact Unity scene name, not an object path or location ID. Empty allows any scene. Lobby visits use the native trader screen scene; raid entries use the bound object's scene. For object bindings, Scene must match the prefix before `:/` in ObjectPath; publication validates the relationship. Collectible scene context comes from the generated loot object. Rehearsal must use the same scene name to exercise a restricted entry. CurrentTrader always uses the entry's trader, including the first visit and trader switching.

## Raid bindings

`RaidBindings` declare Location, Kind, Condition, Once, PersistOnDeath and optional Actions/EntryPointId/MediaId. Kinds are Trigger, Interact, Shoot, Collectible and Cinematic. Match an existing target using the exact `scene:/Root/Child` path from the beta scene. Bind trigger callbacks to the actual trigger collider object, Interact to a raycastable object, and Shoot to an object with a BallisticCollider. Missing targets are logged rather than guessed by name.

Collectibles use ItemId (template) and the server's generated loot instance IDs. Place loot with the existing campaign placement/loot-editor tools; a binding does not spawn it. Early pickups are buffered until story state loads. Trigger/radio/notebook/intercom content is authored separately; no live-world placement is imported.

Ordinary Trigger, Interact, Shoot and Collectible bindings can reference Image, Audio, Video or Cinematic media in MediaId. Accepted events present that media immediately, then their conversation: images wait for Continue, audio plays once with Skip, and Video/Timeline use playback controls. This presentation never sends cinematic-binding completion messages.

`PersistOnDeath: true` commits eligible actions immediately. Otherwise the server defers them until a surviving raid result. Cinematics send begin followed by complete, skip or interrupt; skip completes the binding and interruption leaves it unfinished. Play through your published events in the game to confirm that targets, conditions and media behave as intended.

---

[Documentation home](Home.md) · [Guide navigation](_Sidebar.md)
