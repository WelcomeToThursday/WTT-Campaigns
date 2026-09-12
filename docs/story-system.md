# Story and quests

Campaigns can include chapters, journal notes, trader conversations and raid events. A complete live EFT campaign is not bundled; available story content depends on the campaign author.

## Read the journal

Open **Character → Tasks → Story** with a campaign character active. Select a chapter to see its status, quests, objectives and journal notes. Expand sections and scroll to read longer entries. Related links can open items, trader offers or crafts.

Story quests appear in the Story journal instead of the character's Side list. They may also appear in the trader's normal task list. A campaign without authored chapters shows an empty journal and still allows supported trader visits.

[Chapter notifications](story-notifications.md) announce newly available, completed or failed chapters. An availability notification does not automatically accept a quest unless the author enabled automatic acceptance.

## Conversations and handovers

Choose **Visit** at a supported trader. Available dialogue depends on your character's quests, inventory and campaign state.

Use **Continue** to advance automatic text, then choose from the available replies. Closing text remains until acknowledged. Conversations can accept quests, request item handovers, complete objectives and award quest rewards.

For a handover, choose eligible items in the normal item selection window and confirm. Cancelling a multi-step handover abandons the pending operation without committing its earlier steps. Handovers are available in the lobby, not during raids.

See [trader visits](trader-media-and-visit.md) for navigation.

## Raid events and cinematics

Authors can attach story events to zones, interactions, shooting targets and collectible pickups. Conditions determine when an event becomes available.

Some actions save immediately and can persist through death; others require survival. The campaign determines which rule applies.

Images wait for Continue; audio can offer Skip. Video and cinematic playback provide their own controls. Skipping a registered cinematic completes its binding, while interruption leaves it unfinished. Death, character changes and raid transitions close playback.

## Supported contracts and explicit limits

For campaign authors, supported conditions include variables, quest and objective status, player level, trader loyalty/reputation, item counts, handover availability, special-slot availability, current trader, collectibles, triggers, location, skills and hideout levels.

Paid dialogue services (`PurchaseService` and `ServiceAvailable`) and `WeaponAssembly` are unsupported. Dialogue handovers exclude items with children, including assembled weapons and armor. Ordinary native task screens keep their existing behavior.

`PlayerReward` awards an owned quest's native rewards. `CompleteItem` records a story flag without creating or consuming inventory. `SelectSubService` opens Services without selecting or buying a paid service.

Story events and cinematics require authored content and compatible installed media. No live map placements, transitions or endings are added automatically. See [story authoring](story-authoring.md) to create them.
