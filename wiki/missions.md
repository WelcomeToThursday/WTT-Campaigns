# Missions

Missions turn a campaign map layout into a playable raid. Each mission links a briefing, an authored layout, and a campaign quest. Routes and encounters remain editable in the Campaign Editor.

## Play a mission

1. Accept its linked quest from the trader. This unlocks the mission for the current campaign character.
2. Open **Missions**, select the mission, and read the briefing. Equip your character before deploying.
3. Follow the checkpoints in their displayed order. Fighting is optional in this first version.
4. After the final checkpoint, reach the authored mission exit. Ordinary map extraction points are unavailable during mission runs.
5. Return to the trader to turn in the quest. The mission remains in the list for replay.

Mission runs use your campaign character's equipment and normal raid rules. Death, timeout, or quitting ends the attempt without mission completion. Successful extraction retains normal loot and experience. Quest rewards are granted through normal quest turn-in once; replaying does not grant the quest reward again.

Only authored enemies spawn during a mission. Map scenery, doors, barriers, placed loot, and layout zones belong to that mission run. Ordinary raids continue using their normal map and spawn rules.

## Author a mission

Create a layout with a valid player start, at least one checkpoint, and an exit. Place its encounters and scene changes in the Campaign Editor. In Creator, link the layout to a mission and its quest, then enter the mission name and briefing.

The quest must belong to the story system and have a completion condition backed by a profile story variable. Mission completion sets that variable; the normal quest system handles completion availability and turn-in. The quest should not auto-complete if you want the player to return to the trader.

Mission content uses campaign format 7. Existing older campaigns remain supported. Save and publish the campaign to make it available for normal mission play. A running mission uses the content revision selected when it was prepared; changing the draft does not change a run already in progress.

Fix validation errors before deployment. Missing scene targets require rebinding; a failed layout or enemy setup must not silently become an easier mission.

## Test without changing your character

**Test mission** runs the saved draft on disposable editor state with copied equipment. Use it to check route traversal, scene changes, and combat, then retry or return to editing. Test loot, damage, ammunition use, and results do not transfer to your real character.

From editor home, select **New campaign** and its **Test** layout, then choose **Test mission**. The map loads and the rehearsal starts with the editor character's placeholder starting kit. Complete both checkpoints and enter the authored exit; press **R** to retry after completion or **Esc** to return to editing. Unload the map to return to editor home.

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
