# Quest backports and reputation changes

The captured PvE and seasonal quest lists contain **293 distinct quest IDs absent from the SPT base database**. The compatibility audit accepts **Demonstration Model** and skips **292** quests. No seasonal-only quest passed this audit.

## Added quest

**Demonstration Model** is a Peacekeeper Loyalty Level III task: make 15 headshot kills on Reserve using an allowed marksman rifle. Its captured requirements, rewards and translations are retained. The installed quest icon is packaged locally. Unavailable alternative weapon IDs remain in the original filter; verified obtainable rifles provide a completion route.

## Why quests were skipped

A quest needs a complete playable definition, supported conditions and rewards, obtainable required items, an available encounter, and verified client zones or quest-item placements. A matching condition name or an installed item template alone is insufficient. Quests depending on a skipped quest are also excluded; prerequisites are never removed to unlock them.

The Huntsman Path – Control and Setting Priorities require trader unlocks that cannot be faithfully bound to their installed offers. The Huntsman Path – Administrator requires an unverified Lighthouse zone. Story state, Arena conditions and unavailable seasonal encounters exclude many other candidates.

The machine-readable audit is installed as `SPT_Runtime/user/mods/WTT-Campaigns/data/quest-backports.json`. It records each candidate, source hashes, exact blockers and dependency paths. Existing native quests are preserved.

Empty capture-only sections such as `AutoStart` are omitted because the installed client cannot read those quest stages, even when they contain no conditions. Unsupported stages containing conditions or rewards exclude the quest. Offline validation checks the packaged and server-serialized stages against the installed client’s quest status enum.

## Completion reputation changes

These are the final changes from the previous captured reputation overlay. XP, money, items, negative rewards and other cross-trader rewards remain unchanged. Supplier’s new Ragman reward is the deliberate exception needed to unlock his native chain.

| Quest | Trader receiving reputation | Previous | Updated |
| --- | --- | ---: | ---: |
| Gunsmith - Part 1 | Mechanic | 0.10 | 0.15 |
| Gunsmith - Part 2 | Mechanic | 0.10 | 0.15 |
| Gunsmith - Part 3 | Mechanic | 0.10 | 0.15 |
| Saving the Mole | Mechanic | 0.10 | 0.15 |
| Lend-Lease - Part 1 | Peacekeeper | 0.10 | 0.50 |
| Background Check | Prapor | 0.10 | 0.12 |
| Debut | Prapor | 0.10 | 0.12 |
| Luxurious Life | Prapor | 0.10 | 0.12 |
| Search Mission | Prapor | 0.10 | 0.12 |
| Shooting Cans | Prapor | 0.10 | 0.12 |
| Shootout Picnic | Prapor | 0.10 | 0.12 |
| Supplier | Ragman | 0.00 | 0.50 |
| First in Line | Therapist | 0.10 | 0.27 |
| Operation Aquarius - Part 1 | Therapist | 0.10 | 0.27 |
| Operation Aquarius - Part 2 | Therapist | 0.10 | 0.27 |
| Sanitary Standards - Part 1 | Therapist | 0.25 | 0.48 |
| Shortage | Therapist | 0.10 | 0.27 |
| The Tarkov Butcher | Therapist | 0.25 | 0.48 |

## Progression evidence

Separate offline Standard-edition USEC and BEAR routes reach LL4 for all seven regular traders. The routes evaluate start and finish loyalty gates, native unlock delays, trader unlocks, faction and edition restrictions, and real quest success/failure states. They exclude event quests, seasonal-only imports and Arena, retain failure penalties, and never count both rewards from mutually exclusive outcomes.

The simulation assumes the player earns the required levels through normal play and successfully completes native raid/item objectives. It proves a quest-state and reputation route, not a live playthrough or every possible branch choice. Ref and Fence are not rebalanced. Full routes are installed in `data/trader-progression-reachability.json`.

## Existing characters

Older characters receive only the positive difference for completed affected quests, once. Normal and campaign characters have independent receipts. The server verifies a profile backup before adding reputation. Quests completed after migration earn their full new reward normally. See [older-character migration](trader-progression.md#updating-older-characters).

[Documentation home](Home.md) · [Trader progression](trader-progression.md)
