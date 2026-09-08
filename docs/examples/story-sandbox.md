# Story Sandbox

A separate playable season containing **Field dressing**, a short Prapor delivery. Existing characters keep their original season and progression.

After installing the pack and restarting the SPT server and game:

1. Create a new Seasonal character in **Story Sandbox**. Either faction works. The optional Enduring perk costs its one starting point.
2. Visit Prapor and choose **I'll make the delivery.**
3. Open Character → Tasks → Story to inspect the chapter, objective, and notes.
4. Return to Prapor, choose **Hand over one aseptic bandage.**, select a bandage in the native item selection window, and confirm. Two are supplied in the starter stash; no raid is required.
5. Collect the reward in the conversation. Completion grants 250 XP and a base payment of 5,000 roubles through Messenger. Native character reward bonuses can increase the payment.

You can leave and return before or after handing over the bandage. Completion cannot repeat the reward. Switching back to your original character preserves its inventory and progression.

## Rebuilding the content

Run the Tests authoring command with `--test-story-season`, the installed SeasonalPerks server mod directory, and a new output directory. The output includes a validated ZIP, a published pack with checksums, and `test-story.json` containing the generated identifiers. The helper refuses to overwrite an existing output directory.

`tools/test_playable_story.py` exercises this content on the dedicated `Testing/StoryQuestServerV2` runtime on port 6991. It creates synthetic profiles only and checks the native quest flow, journal notes, one-item consumption, reward persistence, retries, and normal-character isolation.
