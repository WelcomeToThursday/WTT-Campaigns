# Built-in KORD campaign copies

New copies use the 12 released KORD BREACH quests with new campaign-owned identities. The original capture is unchanged. Unrelated EFT 1.0 story prerequisites are excluded; Prapor loyalty level 2 remains the entry requirement.

The copy requires Black Division (`com.blackdiv.tacticaltoaster`) and WTT-ContentBackport (`com.wtt.contentbackport`). It retains their enemies, equipment, clothing, heads, and voices.

- Recover the military case at Shoreline's hydroelectric station with a Leatherman multitool. Prapor forwards the package to the BTR driver because the installed client does not provide the 1.0 BTR task hand-in.
- Plant cameras with the native placement interaction. During Reverse Gear, recover them at their planting locations with the multitool.
- Black Division operatives carry the required code, document briefcase, SSDs, and laptop while the corresponding quest is active. The inventory placement respects the bot's available storage.
- Break the Chain uses shootable radio targets at the captured Customs and Woods locations. Shots from the local player update the campaign objective.
- Accepting Digital Puzzle unlocks the captured level-1 Intelligence Center recipe. It takes 15 minutes and consumes the laptop and original supporting materials.
- Sheep in Wolf's Clothing unlocks its campaign rifle offer at Skier loyalty level 3, using the backport's cash price.

Historical Perspectives is unreleased, so its reward stays disabled. Nine absent hideout/menu cosmetics are retained as disabled editable cards. Page requirements account for those cards, allowing the remaining rewards to be reached. The clothing, dogtags, heads, voices, and gear supplied by the backport remain available.

Existing drafts keep their edits; create a fresh copy to receive this template. Authored trader offers retain the normal client item-preview requirement before publication.

## Verification

`tools/build_kord_copy.py --check` checks the packaged template against its compiler and captured geometry. `tools/export_kord_geometry.py` records source hashes and reconstructs the visit, planting, and radio positions from the local EFT 1.0 assets without launching a game.

Shared checks exercise duplication, dependency milestones, item-tree rewards, remapped loot/craft/offer references, objective requirements, and native quest-stage parsing. The file-only `Tests.Web --kord-content <SPT directory> <built server output directory>` check validates a fresh copy against the installed database and backport definitions. It does not launch a server or read profiles.

Live acceptance still requires the user to restart the server and game, make a fresh copy, validate it, verify the rifle preview, and play through the chain. Confirm camera placement/recovery, Black Division kills and loot, radio hits, and the recipe in actual raids and the hideout.
