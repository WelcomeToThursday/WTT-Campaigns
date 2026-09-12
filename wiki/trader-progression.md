# Trader and task progression

WTT-Campaigns changes trader loyalty and task progression for **both regular and campaign characters**.

## Loyalty

Trader spending requirements are removed. Loyalty still depends on player level and reputation; historical spending remains visible as information.

Existing task progress is preserved. Loyalty is recalculated against the current requirements and can decrease. Accepted tasks remain active after a loyalty loss, while unaccepted tasks can become locked. Older characters receive a one-time positive reputation adjustment for completed quests affected by this update; quest rewards are not replayed.

The seven regular traders have offline-verified routes to maximum loyalty without event quests, Arena, repeatables or edition bonuses. Normal level requirements and quest chains still apply. Ref and Fence retain their existing progression. Skier's **Supplier** now also awards **+0.50 Ragman reputation**, allowing characters to reach Ragman LL2 and finish **Only Business**.

## Tasks

Supported existing tasks use updated reputation rewards, penalties, loyalty requirements and trader assignments. Existing objectives, item rewards and XP rewards are preserved. The compatible backport **Demonstration Model** is added to Peacekeeper's Loyalty Level III tasks. See the [quest audit and reward changes](quest-backports.md). Custom tasks retain their own progression.

All supported tasks, including Essential Tasks, retain SPT's quest prerequisites, player-level requirements and unlock delays. Tasks in Loyalty Level I–IV also require their loyalty tier. Reaching a loyalty level does not unlock every task in that group at once. For example, The Punisher – Part 3 requires level 19 and completion of Part 2.

The task list groups tasks into collapsible **Loyalty Level I–IV**, **Essential Tasks** and repeatable-task sections. Empty sections are hidden, and collapse choices are saved per trader. The selected task shows its loyalty tier.

Use **Show completed** and **Show locked** to control which tasks appear. Locked previews still respect faction, edition, event, secret-task, trader and campaign restrictions. Previewing a task does not accept it or grant rewards.

Trader stock, prices, service fees and discovery rules keep their existing behavior unless affected by a selected [trader-price perk](trader-prices.md).

## Updating older characters

On the next server start, the mod checks normal and campaign characters separately. Completed affected quests receive only the positive difference between the old and new reputation rewards. Completing a quest after updating pays its new reward normally and does not also earn migration credit. A receipt stored in the character prevents duplicate credits after reconnects and restarts.

Before applying a credit, the server saves and verifies a full profile snapshot under `SPT_Runtime/user/seasonal/progression-backups`. The receipt and adjusted standing are saved together through SPT. Missing trader records leave their credit pending; a failed backup prevents the adjustment. Accepted quests, completion statuses, timers, inventories and existing penalties are retained.

This migration repairs the changed reputation rewards. It does not complete quests, restore deleted quest records, or make skipped quests playable. If restoring a backup manually, restore the complete profile with its matching migration receipt while the server is closed.

---

[Documentation home](Home.md) · [Guide navigation](_Sidebar.md)
