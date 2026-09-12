# Custom story media bundles

Campaign authors can use Unity AssetBundles for custom trader rooms, dialogue images and audio, videos, and cinematics. Bundles are optional: text-only conversations, uploaded campaign artwork, journal content, quests, zones and ordinary raid bindings do not need them.

Creator ZIPs contain the campaign definition and uploaded PNG artwork, but they do not contain Unity bundles. Install and distribute story media separately from the campaign ZIP.

## Features that use bundles

| Media kind | Asset in the bundle | Where it is used |
| --- | --- | --- |
| `TraderScene` | Inactive `GameObject` prefab with one camera named `StoryCamera` | Adds Visit support to a custom trader, or replaces a built-in trader's visit room. |
| `Image` | `Texture` or `Texture2D` | A conversation line's Image field, or presentation before an ordinary raid event. |
| `Audio` | `AudioClip` | A conversation line's Music or Sound field, or presentation before an ordinary raid event. |
| `Video` | `VideoClip` | A Start Cinematic action, a Cinematic raid event, or presentation before an ordinary raid event. |
| `Cinematic` | Inactive `GameObject` prefab with a `PlayableDirector` and one `StoryCamera` | A Start Cinematic action, a Cinematic raid event, or presentation before an ordinary raid event. |

`TraderScene` is reserved for visits and cannot be selected as raid-event media. A Cinematic raid event requires `Video` or `Cinematic`; ordinary Trigger, Interact, Shoot and Collectible events can present `Image`, `Audio`, `Video` or `Cinematic` media before opening their optional conversation.

## Add Visit to a custom trader

### 1. Identify the trader

Install the custom trader normally and confirm that it appears in EFT's trader screen. Copy the trader's exact base ID from the trader mod. WTT-Campaigns expects a lowercase 24-character hexadecimal ID, for example `0123456789abcdef01234567`.

The same ID must be used by all three records:

- the custom trader;
- the `TraderScene` media reference;
- the conversation and `InLobby` entry point, when the room has authored dialogue.

Do not add the custom trader to WTT-Campaigns' `StoryMedia/traders.json`. That manifest belongs to the eight rooms shipped with WTT-Campaigns. An assigned campaign `TraderScene` enables Visit for any installed trader ID and also overrides a shipped room when assigned to a built-in trader.

### 2. Build the room prefab

Use the Unity Editor version that matches the target EFT client and build for `StandaloneWindows64`. The SPT 4.1.x client used by this project runs Unity `2022.3.43f1`; recheck the installed client's `UnityPlayer.dll` when targeting another EFT version.

The prefab contract is:

- the saved prefab root is inactive;
- exactly one child camera is named `StoryCamera`;
- the room, trader model, materials, textures, animation assets and other required objects are included in the same self-contained bundle;
- required component types already exist in EFT or an installed client plugin; an AssetBundle cannot install code;
- optional native animation, secondary-animation and lip-sync cues require a compatible EFT `SequenceReader` in the room and exact keys from that reader's dictionaries.

Other cameras may exist, but WTT-Campaigns disables them and uses `StoryCamera`. At runtime it moves every room object to an isolated layer, updates camera and light masks, disables light-probe sampling, routes child `AudioSource` components through the UI mixer, and unloads the room when the visit closes. Author the room so it remains correct under those changes.

The bundle should have no separately loaded bundle dependencies. A minimal Unity editor build uses an explicit addressable asset name so the value registered in the campaign is stable:

```csharp
var build = new AssetBundleBuild
{
    assetBundleName = "packs/example-trader/visit.bundle",
    assetNames = new[] { "Assets/Story/MyTraderRoom.prefab" },
    addressableNames = new[] { "assets/story/my-trader-room.prefab" },
};

BuildPipeline.BuildAssetBundles(
    outputDirectory,
    new[] { build },
    BuildAssetBundleOptions.ChunkBasedCompression,
    BuildTarget.StandaloneWindows64
);
```

In this example, register `assets/story/my-trader-room.prefab` as the media Asset. Rebuild after the root has been saved inactive, then inspect the finalized bundle rather than relying on the source prefab's current editor state.

### 3. Install and hash the finalized bundle

Choose a unique path owned by the campaign and copy the bundle beneath:

```text
F:\SPT 4.1.x\BepInEx\plugins\WTT-Campaigns\StoryMedia\
```

For the example above, the installed file is:

```text
StoryMedia\packs\example-trader\visit.bundle
```

Calculate the SHA-256 hash after the final build and copy:

```powershell
(Get-FileHash -LiteralPath 'F:\SPT 4.1.x\BepInEx\plugins\WTT-Campaigns\StoryMedia\packs\example-trader\visit.bundle' -Algorithm SHA256).Hash.ToLowerInvariant()
```

Every media reference that uses this bundle must contain that exact 64-character hash. The client refuses a missing file, a changed bundle or two references that give different hashes for the same path.

### 4. Register the room in the campaign

In the Campaign Creator, open **Story media**, add a media reference, and set:

- **Kind**: `TraderScene`
- **Bundle**: the relative path beneath `StoryMedia`, such as `packs/example-trader/visit.bundle`
- **Asset**: the exact addressable asset name in the bundle
- **Sha256**: the finalized bundle's hash
- **Trader**: the installed custom trader

The equivalent campaign JSON is:

```json
{
  "Id": "770000000000000000000020",
  "Kind": "TraderScene",
  "Bundle": "packs/example-trader/visit.bundle",
  "Asset": "assets/story/my-trader-room.prefab",
  "Sha256": "REPLACE_WITH_THE_64_CHARACTER_BUNDLE_SHA256",
  "TraderId": "0123456789abcdef01234567"
}
```

The media `Id` is a new campaign-owned lowercase 24-character hexadecimal ID. Assign only one `TraderScene` to a trader in a campaign. An unassigned `TraderScene` can be saved but produces a warning and is never used for a visit.

Once the campaign is published and loaded for a campaign character, the matching custom trader gains **Visit** beside Buy and Sell. A room does not require a conversation: without an eligible entry point, the player can still enter it and navigate to Trade, Tasks or available Services.

### 5. Add an optional conversation

In **Conversations**, create a dialogue assigned to the same trader. In **Entry points**, create an `InLobby` entry assigned to that trader and dialogue. Add conditions when the conversation should not always be available.

The minimum relationship looks like this. This example advances its dialogue phase before offering a reply, so the automatic NPC line cannot repeat:

```json
{
  "Dialogs": [
    {
      "Id": "770000000000000000000021",
      "TraderId": "0123456789abcdef01234567",
      "MainVariable": "770000000000000000000023",
      "Lines": [
        {
          "Id": "770000000000000000000024",
          "Side": "Npc",
          "Text": "Welcome.",
          "Trigger": {
            "Type": "VariableValue",
            "Target": "770000000000000000000023",
            "Operator": "==",
            "Value": 0
          },
          "Actions": [
            {
              "Id": "770000000000000000000025",
              "Type": "SetVariable",
              "Target": "770000000000000000000023",
              "Scope": "Dialogue",
              "Value": 1
            }
          ]
        },
        {
          "Id": "770000000000000000000026",
          "Side": "Player",
          "Text": "Goodbye.",
          "Trigger": {
            "Type": "VariableValue",
            "Target": "770000000000000000000023",
            "Operator": "==",
            "Value": 1
          },
          "Actions": [
            {
              "Id": "770000000000000000000027",
              "Type": "QuitAction"
            }
          ]
        }
      ]
    }
  ],
  "Variables": [
    {
      "Id": "770000000000000000000023",
      "Scope": "Dialogue",
      "InitialValue": 0
    }
  ],
  "EntryPoints": [
    {
      "Id": "770000000000000000000022",
      "DialogId": "770000000000000000000021",
      "TraderId": "0123456789abcdef01234567",
      "Kind": "InLobby"
    }
  ]
}
```

Use the Creator to generate IDs and author the conversation; the fragment focuses on the required trader relationship and a minimal safe dialogue phase. With one eligible lobby entry, the conversation starts when the room opens. With several eligible entries, the player chooses which topic to start. The `CurrentTrader` condition automatically evaluates to the trader being visited.

## Register other bundled media

Register every asset in **Story media** using the same Bundle, Asset and Sha256 fields. Several assets may share one bundle and therefore share its hash.

For a dialogue line:

- **Image** selects an `Image` media ID and displays it above the conversation;
- **Music** selects an `Audio` media ID and loops it for the line;
- **Sound** selects an `Audio` media ID and plays it once;
- subtitle entries use localization keys and timing only, so they do not need a separate media asset.

For full-screen playback:

- a **Start Cinematic** action targets a `Video` or `Cinematic` media ID;
- a **Cinematic** raid event selects `Video` or `Cinematic` in Media;
- other raid-event kinds may select `Image`, `Audio`, `Video` or `Cinematic` in Media.

An `Image` waits for Continue, `Audio` plays once and can be skipped, and `Video`/`Cinematic` use the full-screen playback controls. Starting a cinematic from a trader conversation closes the visit before playback.

### Cinematic prefab contract

A `Cinematic` asset must be an inactive prefab with:

- exactly one camera named `StoryCamera`;
- a `PlayableDirector` on the root or a child;
- Play On Awake disabled;
- a finite duration greater than zero;
- every explicit Timeline binding contained in the prefab's dependency closure.

Use a reviewed camera layer and culling mask, and do not depend on gameplay-registration scripts running when the prefab is instantiated. A `Video` media reference points directly to a `VideoClip`, not a prefab with a `VideoPlayer`.

## Validate and distribute

Before publishing:

1. In the Creator, run **Validate** and resolve errors. Rehearsal confirms story references and progression, but it records media rather than loading Unity assets.
2. Confirm the finalized bundle contains each registered addressable asset name and has no missing scripts or external bundle dependencies.
3. Recalculate SHA-256 after every rebuild and update all references to that bundle.
4. Install the bundle on the same client that will run the campaign, then test the published campaign in game. Do not treat Creator rehearsal as a Unity playback test.
5. Distribute the campaign ZIP and the matching `StoryMedia` files together, preserving their relative directories. Each player needs the custom trader mod and all required client component assemblies as well as the media bundle.

Changing a bundle without changing the published hash intentionally makes it fail to load. Publish a compatible campaign revision or a new campaign identity when gameplay changes require it, and keep old bundle paths available for characters that still use older campaign versions.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| The custom trader has no Visit tab | Use a campaign character; confirm the published story has one assigned `TraderScene` whose `TraderId` exactly matches the installed trader. |
| The visit reports missing media | Confirm Bundle is relative to `StoryMedia`, ends in `.bundle`, and the file exists at that exact path. |
| The checksum fails | Hash the installed file, update Sha256 after the last rebuild, and make sure every reference to that bundle uses the same hash. |
| The room asset is missing | Use the bundle's exact addressable asset name, including its path. |
| The room is rejected before display | Save the prefab root inactive and include exactly one camera named `StoryCamera`. |
| The room is black or incomplete | Check camera framing, included shaders/materials, missing scripts and external bundle dependencies. Remember that WTT-Campaigns replaces room layers and camera/light masks. |
| Dialogue opens but animation cues fail | Include a compatible `SequenceReader` and use its exact animation and lip-sync keys. Do not copy keys from another trader. |
| Creator rehearsal works but the game does not | Rehearsal does not inspect or play Unity bundles. Verify the installed bundle, asset type, asset name and client log. |

---

[Story authoring](story-authoring.md) · [Trader visits](trader-media-and-visit.md) · [Campaign Creator](season-creator.md) · [Guide navigation](_Sidebar.md)
