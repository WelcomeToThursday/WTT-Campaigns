# Dialogue and interaction authoring

The Creator calls player-facing exchanges **Conversations**. In the campaign format, the same records are stored under `Story.Dialogs`. A conversation contains conditional NPC lines and player replies; an entry point decides where it can start, and an optional raid event connects it to something in the world.

Start with a **Simple conversation**, **Branching exchange** or **Quest-linked conversation** template in the Creator. The templates create a dialogue-scoped phase variable, a safe opening flow and a matching lobby entry. Edit that structure before building a conversation from individual records.

## How a conversation runs

The runtime follows this cycle:

1. An eligible entry point starts a new conversation for its trader.
2. The entry initializes the conversation's main phase variable.
3. Exactly one eligible NPC line runs automatically. Its actions execute in order.
4. Conditions are evaluated again. Further NPC lines continue automatically, one at a time.
5. When no NPC line is eligible, every eligible player line becomes a reply.
6. Choosing a reply executes its actions, then automatic NPC evaluation resumes.
7. A root `QuitAction`, navigation or the player leaving ends the conversation. A `QuitAction` inside an embedded dialogue returns to its caller instead.

Only one conversation is current for a campaign character. Starting an entry creates a fresh conversation and history. Dialogue-scoped values belong to that conversation; they do not carry into the next visit.

Each choice is an authoritative transaction. Story state and native quest changes are staged together and commit only if the entire choice succeeds. An invalid action, stale reply, ambiguous automatic branch or loop rejects the step without keeping its earlier changes. Presentation actions such as opening Tasks or playing a cinematic run after the transaction commits.

## Build the phase flow

A conversation's `MainVariable` is normally a variable with `Dialogue` scope. Treat it as a state-machine phase:

| Phase | Eligible line | Side | Actions |
| ---: | --- | --- | --- |
| 0 | Greeting | NPC | Set phase to 1 |
| 1 | Accept or decline | Player | Perform the choice, then set another phase or quit |
| 2 | Response | NPC | Set phase to 3 |
| 3 | Follow-up choices | Player | Continue, switch dialogue or quit |

For every automatic NPC line, make one of its actions change the conditions that made it eligible. Usually that means assigning the next phase. If the same NPC line remains eligible, or two NPC lines are eligible together, the runtime rejects the operation as an automatic loop or ambiguous branch.

Multiple player replies may be eligible at once; that is how normal branching works. Conditions are checked again when the player selects a reply, so a reply that became stale cannot run twice.

`SetVariable` assigns an integer; it does not increment the old value. Its `Scope` must match the variable declaration.

## Main variables and start points

An entry point may leave `StartPoint` blank or name one of the dialogue's `StartPoints`:

- With a blank start point, a `Dialogue` main variable starts at its declared `InitialValue`.
- With a blank start point, a `Profile` or `Session` main variable keeps its existing value, falling back to `InitialValue` when it has never been set.
- A named start point assigns its configured integer whenever the entry is opened, regardless of variable scope.

Use named start points when several interactions should enter the same conversation at different phases. Use a profile-scoped main variable only when reopening the conversation should resume durable character progress; use session scope for progress that resets after reconnecting.

## Lines, replies and playback

Every line has a `Side`, `Text`, `Trigger`, ordered `Actions`, optional `Confirmation`, optional `Random` gate and optional `Playback`:

- `Npc` lines run automatically. The runtime requires one or zero eligible NPC lines at each step.
- `Player` lines appear as selectable replies. Any number may be eligible.
- `Confirmation` is shown before a player reply is submitted. Cancelling it makes no story change.
- Text-only automatic lines use **Continue** between lines, and closing text remains until acknowledged. Continue only advances presentation; it does not run the line's actions again.
- Image, sound, music, subtitles, animation and lip-sync cues belong to the line's `Playback`. See [custom story media](story-media-bundles.md#register-other-bundled-media).

The conversation history records lines after they execute. English `Text` and `Confirmation` are fallbacks. Localization keys are `<dialogue-id> name` for the topic shown in a trader visit, `<line-id> text`, and `<line-id> confirmation`. Give each lobby dialogue a translated name when a trader can offer several topics; otherwise its topic falls back to **Talk**.

### Random branches

A random gate accepts an inclusive `Start`–`End` range from zero up to `Maximum - 1`. Related gates share one draw when both their `VariableId` and `Group` match, and every member of that group must use the same `Maximum`.

`Random.VariableId` is a draw name, not a declared story variable. A new draw is prepared when a dialogue is entered, switched to, embedded or resumed after an embedded dialogue exits.

## Entry points

An entry point connects a context to one dialogue. Its trader must match the dialogue's trader.

| Kind | How it starts |
| --- | --- |
| `InLobby` | Appears as a topic during that trader's **Visit**. If it is the only eligible topic, it opens immediately. |
| `InRaid` | Starts only through a linked raid event. |
| `ViaRadio` | Raid-context label for a radio interaction; connect the actual object through a raid event. |
| `ViaNotebook` | Raid-context label for a notebook interaction; connect the actual object through a raid event. |
| `ViaIntercom` | Raid-context label for an intercom interaction; connect the actual object through a raid event. |

The non-lobby kinds classify the intended entry; they do not discover or place a world object. A raid event is the concrete trigger.

`Condition` controls whether the entry is available. `CurrentTrader` evaluates against the entry's trader, including before a conversation has started. `Scene` is an optional exact Unity scene name, not a location ID or object path. Leave it blank unless the entry must be restricted to one scene. For a bound raid entry, it must match the scene prefix of the captured object path or the selected authored zone.

## Action reference

Actions on a line run from top to bottom. Put state and quest mutations before a terminal `QuitAction` or presentation/navigation action so the authored intent remains easy to inspect.

| Action | Effect and important fields |
| --- | --- |
| `SetVariable` | Assigns `Value` to variable `Target`; `Scope` must match the declaration. |
| `DiaryNote` | Publishes the journal note in `Target` if it is not already available. |
| `CompleteItem` | Records `Target` as a durable story completion flag. It does not create, consume or hand over an inventory item. |
| `SelectQuest` | Stores `QuestId` as this conversation's quest context. A later quest action with blank `QuestId` uses that selection. |
| `AcceptQuest` | Accepts the owned story quest in `QuestId`, or the selected quest when blank. Native start requirements must pass. |
| `HandoverItem` | Hands eligible inventory items to the owned active quest and `ConditionId`. It is lobby-only and uses native item selection when a choice is required. |
| `FinishQuest` / `PlayerReward` | Completes the owned quest through the native quest system and applies its native completion rewards. Required finish objectives must pass. |
| `SwitchDialog` | Replaces the current dialogue with `Target` for the same trader. It does not create a return point. |
| `EmbedQuestDialog` | Pushes the current dialogue, then enters `Target` for the same trader. A later `QuitAction` returns to the pushed dialogue. |
| `QuitAction` | Returns from an embedded dialogue; if none is stacked, closes the conversation. |
| `TradingScreenAction` | Ends the visit presentation and opens the trader's Buy/Sell screen. |
| `QuestsScreenAction` | Ends the visit presentation and opens the trader's Tasks screen. |
| `SelectSubService` | Opens the trader's native Services screen. It does not select or purchase a paid service. |
| `StartCinematic` | Closes the conversation presentation and plays the registered Video or Cinematic in `Target` after the story choice commits. |
| `UnlockMission` | Unlocks the campaign mission link in `Target` for this character. It does not start a raid or accept/complete a quest; see [mission links](missions.md#link-a-mission-to-a-campaign). |
| `UnlockMission` | Unlocks the campaign mission link in `Target` for this character. It does not start a raid or accept/complete a quest; see [mission links](missions.md#link-a-mission-to-a-campaign). |
| `PurchaseService` | Unsupported. Use the trader's native Services screen. |

Only quests owned by this story can be changed. External quests declared through `quest:<id>` dependencies may be used in conditions, but dialogue actions cannot accept, hand over, finish or reward them.

### Item handovers

`HandoverItem` requires an active owned quest and one of its `HandoverItem` completion objectives. The normal item filters still apply, including template/category, found-in-raid, dogtag, encoding and durability requirements. Items with children are excluded, so assembled weapons, armor and other compound items cannot be handed over through a conversation.

Currency and native quest items can be selected automatically. Other eligible items open the game's handover window. When one reply contains several handovers, all required selections are collected before the transaction commits. Cancelling any selection cancels the whole reply, including actions that appeared earlier in its list. If the character, inventory or story changes while choosing, reopen the conversation.

## Link conversations to raid interactions

A raid event may run its own actions, present media and start an entry point. Choose the event kind that matches the real interaction:

| Event kind | Player interaction |
| --- | --- |
| `Trigger` | Activates when the campaign PMC enters the bound trigger or authored zone. |
| `Interact` | Shows **Interact** while the player looks at the eligible raycastable object within 2.5 metres and uses the normal interaction control. |
| `Shoot` | Activates when the campaign PMC hits the exact object through its ballistic collider. |
| `Collectible` | Activates when the player picks up an existing loot instance with the configured item template. The binding does not spawn it. |
| `Cinematic` | Begins a bound Video or Cinematic on trigger entry and records complete, skip or interrupt separately. |

For `Trigger`, `Interact`, `Shoot` and `Cinematic`, bind an authored zone or an exact `scene:/Root/Child` path. The path must resolve to exactly one object. Use the connected [raid authoring](raid-authoring.md) tools to capture targets instead of guessing paths.

For an ordinary event, `MediaId` is presented before its linked conversation. Image waits for Continue, audio offers Skip, and video/cinematic media uses its playback controls. A `Cinematic` event uses its media as the event itself rather than as a pre-conversation presentation.

`PersistOnDeath` controls the raid event's own actions and completion mark:

- Enabled: the event is completed and its actions commit immediately.
- Disabled: completion and the event's actions remain pending and commit only on survival, run-through or transit.

This setting does not defer choices the player makes later inside a linked conversation; those are separate authoritative conversation transactions. Avoid inventory handovers and trader-screen navigation in raid conversations. Death, character changes and raid transitions close their presentation.

## Safe patterns and common failures

| Symptom | Check |
| --- | --- |
| No topic appears during Visit | The entry must be `InLobby`, use the same trader as the dialogue, pass its condition and match the current scene when `Scene` is set. |
| An NPC line repeats or the step rolls back | Make the line change its phase or another trigger fact so it becomes ineligible. |
| The runtime reports an ambiguous automatic reply | Make NPC triggers mutually exclusive. Multiple player replies are allowed; multiple eligible NPC lines are not. |
| A reply remains available after it was chosen | Advance a variable or change another fact in that reply's actions. Stale re-selection is rejected, but the flow should make the transition visible. |
| A named start point fails validation | Add the exact name to the dialogue's `StartPoints`; values are integers. |
| A raid interaction does nothing | Confirm the location, exact scene object/zone, collider type, event condition and linked non-lobby entry. Bindings do not create world objects. |
| A handover cannot start | The quest must be owned and active, the objective must be a matching handover objective, the player must be outside a raid, and eligible leaf items must exist. |
| A nested conversation closes everything | Use `EmbedQuestDialog` when `QuitAction` should return. `SwitchDialog` deliberately has no return stack. |
| A cinematic or room cue fails | Verify the separately installed bundle, hash, asset name, camera contract and trader-specific animation keys. |

Use **Connections** to inspect inferred phase transitions, **Preview** to review text, and **Story rehearsal** to exercise entry conditions, choices and rollback without touching a player profile. Rehearsal records native/media intent but cannot validate Unity assets, native item windows or scene colliders. Save, validate and restart rehearsal after changing the draft.

For a complete small JSON example, see the [format-1 story overlay](examples/story-introduction.json). For the broader pack format and publishing workflow, continue with [story authoring](story-authoring.md).

---

[Documentation home](Home.md) · [Guide navigation](_Sidebar.md)
