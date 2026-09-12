# Campaign Creator instructions and tutorials

Open **Help and tutorials** at the top of the Campaign Creator. It is available in the library and while editing a draft. Search for a section or a term such as **handover**, **variables** or **publishing**. Every editor section also has a collapsible **How to use this section** guide.

Two walkthroughs run alongside the editor:

- **Campaign basics:** create a practice draft, configure starting items and documents, add a battle-pass reward, validate and export.
- **Your first story quest:** create a chapter, a supply quest, a completion note and a trader conversation, then rehearse the complete loop.

Use **Open [section]** to navigate, **Back** and **Next step** to move through the directions, or **Jump to step** to revisit a topic. **Pause tutorial** hides it; **Resume tutorial** restores the current step during the same editor session. Reloading the page resets the tutorial position. Steps are self-paced, not automatic checks of your draft.

The tutorial never creates content or saves or publishes a pack on your behalf. Save your edits with **Save changes**. Use a practice draft to keep experiments separate from the campaign you play.

## Your first story quest

1. **Overview:** create a blank practice draft called `Tutorial — supply run`.
2. **Chapters and quests:** add `First contact`. In Chapter settings, leave Visibility as empty **All**; artwork is optional.
3. Open the chapter's **Quests** tab and **Create quest in this chapter**. Name it `A small favor`, choose Prapor, and keep the Level start requirement at 1. Set the HandoverItem objective to one ordinary inventory item, with found-in-raid off and durability 0–100. The quest is already assigned to First contact. In its Story tab, keep Main enabled, enable Auto complete, and leave Auto start off.
4. Stay in the quest's **Story** tab. Under Journal notes for this quest, choose **Success → Create and link note**. Expand the note and write `Prapor received the supplies. We have made our first contact.`
5. Under **Quest conversations**, keep Prapor selected and **Create quest conversation**. Expand it to edit the dialogue while preserving its triggers and phase-changing actions. Under Lines, rename the Goodbye player line `Here are the supplies.` Add a **Handover item** action targeting this quest and its handover objective; move it before **Quit action**.
6. Expand **Conversation availability and phase** to check the generated entry uses InLobby, Prapor and your dialogue. Leave Start point blank and its condition as empty All. The separate Conversations section also provides Connections and Preview views.
7. **Story rehearsal:** keep level 1 and In raid off. Under simulated **Items**, set the chosen item template's count to 1. Under **Handover items**, set the handover objective's eligible count to 1. Start rehearsal, open the entry point, accept the quest and choose the handover reply. The quest should reach Success, the chapter should complete and the note should appear.
8. **Preview and publish:** save and validate. Fix issues, then publish/export if desired. Restart SPT to load the practice pack, then choose it when creating a campaign character. For a real campaign, add quest-status conditions so completed jobs do not keep offering the same handover, and verify the behavior in-game.

## Useful distinctions

- **Native quest vs. story membership:** the native quest owns objectives and rewards. Story membership adds its chapter, visibility, automatic progression and journal notes.
- **Condition vs. action:** conditions decide whether something can run; actions make changes after it runs. Actions execute in their listed order.
- **Variable scopes:** Profile persists for a character, Session resets on reconnect, and Dialogue belongs to a conversation. Set variable assigns an integer rather than incrementing it.
- **Always available:** an empty All condition passes. Any needs at least one passing child; Not reverses exactly one child.
- **Published vs. playable:** a published revision can be downloaded. Restart SPT to load new packs with their required dependencies, then choose a campaign when creating a campaign character.

## Troubleshooting

| Symptom | What to check |
| --- | --- |
| No conversation is available | An entry point must reference the dialogue and the same trader; InLobby requires non-raid context; its conditions must pass. |
| Automatic dialogue rolls back | Each NPC line must advance its phase or become ineligible. Two eligible NPC lines are ambiguous. Preserve the template's Set variable actions. |
| A handover is blocked | The quest must be active, the player must be outside a raid, and the action must target a handover objective in that owned quest. Rehearsal needs both inventory and eligible-handover counts. |
| A journal note is missing | Creating it does not reveal it. Link it to a quest status or Diary note action, or associate completed objective IDs. |
| A chapter does not complete | It needs at least one required quest, and all required quests must succeed. Optional quests do not block completion. |
| Rehearsal says the draft changed | Restart rehearsal to copy the latest definition. Simulated state never changes a player profile. |
| A raid event does nothing in-game | Verify the exact scene target and location. Bindings do not spawn objects or loot. Deferred actions commit only after survival. |
| Publication or play is blocked | Follow the validation issues. Missing dependencies can allow export but still prevent play; structural errors must be fixed. |

Rehearsal records native rewards and media requests without granting or playing them. Filtered item eligibility, world detection and Unity playback still require in-game testing. See [story authoring](https://github.com/WelcomeToThursday/WTT-Campaigns/wiki/story-authoring) for media and raid details.


You can also complete the note and dialogue steps inside **Chapters and quests → your chapter → Quests → your quest → Story**. Use **Create and link note** with the Success status, then expand the new note to write its text. Under **Quest conversations**, use **Create quest conversation** and expand it to edit the lines. **Conversation availability and phase** contains the generated entry point and variable. These controls edit the same records as the standalone sections and use the normal Save changes action.
