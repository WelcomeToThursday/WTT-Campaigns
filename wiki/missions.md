# Missions

Missions are independent, published playable raids. A mission owns its briefing, layout, objectives, encounters and loot. Campaigns can reuse a specific published revision and choose their own story unlock and optional quest-completion binding.

## Play a mission

1. Use a normal character for missions published with **Allow standalone play**, or a campaign character for that campaign's linked missions. Locked entries explain which story milestone is required.
2. Open **Missions**, select the mission, and read the briefing. Equip your character before deploying.
3. Follow the checkpoints and complete any authored objectives, including combat objectives where required.
4. After the final checkpoint, reach the authored mission exit. Ordinary map extraction points are unavailable during mission runs.
5. If the campaign link has a quest objective, return to the trader for normal quest turn-in. Unlocked missions remain available for replay.

Mission runs use your active character's equipment and normal raid rules. Death, timeout, or quitting ends the attempt without mission completion. Successful extraction retains normal loot and experience. Quest rewards are granted through normal quest turn-in once; replaying does not grant the quest reward again.

Only authored enemies spawn during a mission. Map scenery, doors, barriers, placed loot, and layout zones belong to that mission run. Ordinary raids continue using their normal map and spawn rules.

## Interrupted encounters

If an authored encounter cannot recover from a technical failure, the mission pauses and explains the interruption separately from player defeat. **Retry checkpoint** restores the last saved checkpoint with a fresh attempt when retries are enabled. **End attempt** exits alive without awarding mission completion. If the server cannot acknowledge the interruption, the attempt remains paused until communication succeeds. See [Encounter failure recovery](ai-encounters.md#encounter-failure-recovery) for AI limits, bounded retries and cleanup.

## Author a mission

Choose **New Mission** on the in-game editor home to create and open an independent draft from its name and map. You can also open **Mission library** from Campaign Creator, or visit `/wtt-campaigns/creator/missions`, choose **Create mission**, select its map, and save. Open Campaign Editor in the game, select that mission draft and map, and author its start, checkpoints, exit, scene edits and encounters. The dedicated web editor provides objectives, events and container loot. No campaign or quest is required.

Each package contains one mission and one layout. **Allow standalone play** defaults off; enable it to offer the published mission to normal characters. Save, validate and publish, then manually restart SPT to load the new content. Publishing creates an immutable revision; editing a draft does not change published runs. Export the mission pack to share it, or import one as an editable draft.

## Mission time and weather

**Mission timer** offers **Map default**, **Timed** (1–1440 minutes), and **Infinite**. The server saves this setting with the mission and sends it to raids and mission playtests. Infinite shows **∞** beside **Complete the mission** and has no time-based expiry. Timed missions start counting down when mission setup is ready; expiry ends a deployed raid using the normal timeout result. In a playtest, expiry returns to editing instead of closing the editor raid.

The gold mission banner and objective rows remain visible during mission playtests and deployed missions. Checkpoint retries restore the time remaining at the saved checkpoint. The countdown pauses while saving/restoring checkpoints and while the retry menu is open. World time and event timers remain separate from this countdown.

In **Mission events and objectives**, choose a **Default objective icon** and **Checkpoint icon** for the left side of story notifications. Each objective also has a **Notification icon** override. The built-in choices use Tarkov's actual side-quest list sprites (including Elimination, Pick up, Exploration and Weapon assembly). Creator also supports custom PNGs through the artwork picker; these are included when exporting the mission. Unconfigured objectives use Completion, and checkpoints use Exploration.

In Mission Editor, **Playtest** starts the selected layout's mission, including its objectives, events, timer and checkpoint notifications. If several missions share that layout, choose the mission in **Mission events and objectives** first. Layouts without a mission still use the AI-only playtest; **Observe** remains an AI preview.

In the server web mission editor, open **Missions → Time of day and weather**. Enable **Set mission start time** and choose a clock time. Time advances at the native raid rate unless **Hold time fixed** is enabled. Enable **Set mission weather** to configure clouds, rain, fog, wind, thunder and wind direction. Authored weather remains fixed throughout the mission; disabled overrides use normal raid conditions. Maps without a sky or weather controller retain their native indoor environment.

These settings are saved with the mission and included in published revisions, exports and playtest snapshots. They are separate from the local in-game editor preview preferences. Restart a playtest after saving changes to use the updated settings.

When checkpoint retries are enabled, retrying after death or failure restores both the visible sky time and its native clock source to the saved checkpoint, including retries from mission start and across midnight. Advancing time resumes at its original rate; frozen time remains frozen. Leaving a playtest restores the underlying raid clock and weather.

### Link a mission to a campaign

In the campaign's **Missions** section, select a published mission revision and choose **Link mission**. Availability can be **Available from start**, **Quest accepted**, **Quest completed**, **Chapter reached**, or **Unlocked by story action**. Chapter gates use the chapter's existing visibility condition. Add an **Unlock mission** action to a conversation or raid event for an explicit story gate. Once earned, access stays unlocked.

An optional completion binding selects a story quest and a completion objective backed by a zero-initialized profile variable. An earlier completion by the same campaign character counts when that quest is accepted. This does not accept or hand in quests automatically. Completion by a normal character or another campaign character never grants story credit.

Links pin a revision until the author explicitly updates it. Campaign exports include those pinned packages and referenced artwork; installing a campaign does not add its missions to the normal character's standalone library. Existing restrictions on changing gameplay in used campaigns still apply.

### Existing embedded missions

Older campaigns continue to use their embedded missions and original quest-acceptance unlocks. In an editable campaign draft, **Extract to mission library** copies a mission and its required content to a new independent draft. Publish that draft, return to the campaign, select the extracted revision, then choose **Replace with selected published revision**. Replacement keeps the original mission progress identity and quest binding. Extraction does not remove the source; shared layouts and assets remain in the campaign.

Independent mission packages and campaign links use content format 11 and require matching updated client/server components. Old formats remain supported; no existing published packs or profiles are rewritten during installation.

Fix validation errors before deployment. Missing scene targets require rebinding; a failed layout or enemy setup must not silently become an easier mission.

## Test without changing your character

**Test mission** runs the saved draft on disposable editor state with copied equipment. Use it to check route traversal, scene changes, and combat, then retry or return to editing. Test loot, damage, ammunition use, and results do not transfer to your real character.

From editor home, select **New campaign** and its **Test** layout, then choose **Test mission**. The map loads and the rehearsal starts with the editor character's placeholder starting kit. Complete both checkpoints and enter the authored exit to finish the mission and return automatically to editing. Start another playtest from the editor to replay it, or press **Esc** during a test to return early. Unload the map to return to editor home.

**Test campaign flow** creates an isolated campaign snapshot and a disposable character. Use this path to accept the quest, select and complete the mission, turn in the quest, and replay. Resetting the test starts a fresh test character. The source draft and normal characters remain separate from the test snapshot.

To start the full flow, unload the editor map and select **Test campaign flow** on editor home. Use the trader's normal task screen to accept the quest, then choose **Missions** from the main menu. **Reset test** starts over; **Return to editor** disposes of the test and returns to the selected draft.

Save changes before testing. Test snapshots do not follow edits made after they were created; reset or start a fresh test to exercise new content.

## Local Test layout

The local **Test** layout on **Interchange** has two checkpoints and an authored exit. Its encounter spawns three hard Scavs at mission start. Kills are optional: reaching both checkpoints in order and then the exit completes the mission.

The accompanying Prapor field-exercise quest unlocks the mission on acceptance and has no bonus reward. You must accept it and turn it in manually to exercise the complete flow.

## Live acceptance checklist

After installing matching components, manually restart the server and client. No automated validation starts or stops either application.

- Test both the editor shortcut and the full disposable campaign flow.
- Confirm the three Scavs spawn, the second checkpoint cannot skip the first, and the exit is unavailable before both checkpoints.
- Complete and extract, turn in the quest, then replay without receiving a second quest reward.
- Try death, quitting, and retrying. Check that each retry resets route and encounter progress.
- Leave testing and check the original character's gear and progress. Load an ordinary raid and confirm mission scenery, objectives, and spawn restrictions are gone.

Offline contracts and native assembly checks validate integration assumptions. Successful build and file installation do not establish in-game behavior; the above checks require a live session controlled by the user.

On the main toolbar, choose **Placeholder kit** (default) or **Copy main-profile kit** before starting a playtest. Both use disposable item copies; the main profile is unchanged. The selection lasts for the current editor session. Reconnect the editor after installing this update to prepare its placeholder kit.

## Standalone mission editor and container loot

Open **Mission library** from Campaign Creator or visit `/wtt-campaigns/creator/missions`. Create or select an independent mission draft. Use the **Missions** and **Layouts** tabs to author mission logic and inspect its layout. Existing embedded missions remain editable through **Save and edit embedded missions** in their campaign.

Under a mission's **Container loot** section, select a container placed in its layout. Choose native random loot (with an optional pool), fixed contents, or empty contents. Configure container spawn chance and optionally select a key/keycard before enabling **Start locked**. Switching modes retains the fixed item list.

For fixed contents, search the paged item catalogue, add items, edit quantities, and remove entries. Select an **Image provider** to use the same client-rendered thumbnails as Assorts. The selected client must have authoring enabled and be at the main menu for new images. Cached thumbnails work without a connected client; some items may not have a supported preview. Images do not verify whether the whole loot list fits the container. Native stack limits and capacity are checked when preparing the mission.

Container settings belong to the layout, so every mission referencing it shares those changes. Mission-owned layouts are excluded from ordinary-raid layers. Containers authored on a separate ordinary level use that level's saved settings. Place containers with the in-game Scene editor first; this page edits their loot and access settings. Save before returning to Campaign Creator to edit the linked story quest.
