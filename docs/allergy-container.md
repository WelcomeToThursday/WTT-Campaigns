# Allergic and Broken Secure Container

Current workflow: [build, validate and always install](build-deployment.md). Never stop or start servers or clients. Any isolated-server results below are historical; those fixtures are retired.

Build 0.1.23 enables two more captured personal perks, bringing the implemented catalogue to 33/39.

## Allergic

`69c3d6a9af28f094100fe128` grants three points. The server samples three distinct installed item templates matching the captured Drugs, Stimulator and FoodDrink ancestry filters. It stores them in `SeasonalPerkEffectParameters.allergy[perkId].targetItems` in the same atomic profile save as the selection. The client receives those parameters in its snapshot and never generates its own targets.

Existing valid rolls survive saves, mode switches, restart and deselection/reselection. Keeping the inactive receipt across edits is a backport policy to prevent rerolling through the editable SPT perk editor; inactive perks never trigger. Missing or malformed rolls are generated when selecting the perk. If fewer than three candidates exist, selection fails before changing the profile. Missing templates from another installation are not silently substituted in an existing roll.

Each matching use samples three distinct enabled symptoms, following live `RandomSlotCount=3`. The additional captured `appliedRandomEffectCount=1` is retained in the catalogue but is absent from the live metadata contract and does not override that behavior.

| Symptom | Duration | Rate |
| --- | --- | --- |
| Pain | 30 seconds | Native pain effect |
| Tremor | 20 seconds | Native tremor effect |
| Tunnel vision | 20 seconds | Native tunnel vision effect |
| Health loss | 30 seconds | -3 HP/second total |
| Hydration loss | 30 seconds | -3/second |
| Energy loss | 30 seconds | -3/second |

Food/drink and medical resource decreases trigger once per use operation. Completed non-food medication/stimulant use also invokes the same receipt before native completion handling; interrupted completion cannot newly apply symptoms. This completion hook is the compatibility mapping for the older client's medicine/stimulant lifecycle and still needs an actual animation test. Native consumption, healing, stimulator effects, interruption and item disposal remain responsible for their original actions.

Only the active seasonal PMC's own living raid health controller receives symptoms. Normal PMC, Scav, AI and stash/hideout use remain unaffected. The existing tagged native HealthBoost timer now supports separate health, energy and hydration rate carriers. Refreshing a rate kind resets its timer instead of adding another instance. Health loss uses a random starting body part, skips destroyed parts, changes the first eligible part and destroys it if it reaches minimum health, with native Existence damage attribution. Positive healing still skips full parts. Neither rate spills into other parts. Native lifecycle clamping prevents an oversized last tick.

Juice Time and Sailor's Nostalgia remain compatible. Application follows catalogue order, including when multiple perks refresh the same native symptom/rate family. Native untagged HealthBoost behavior remains unchanged.

## Broken Secure Container

`69c3da13eaf97663fb0bb36d` grants six points. The complete captured `pouch_item_filter_restrict` allow-list remains unchanged. It admits matching money, keys, special equipment, maps and explicit dogtag/container templates. The 45 live templates absent from this installation are not imported or replaced.

Client grid compatibility and item move/add/stack-transfer checks reject a disallowed item whenever the destination has a secure-container ancestor owned by the active seasonal PMC. Compound items are checked recursively, including the container itself and its contents. Native filters still apply in addition to this allow-list. Restore/rollback calls that explicitly ignore restrictions retain their native bypass.

The server checks Move, Split, Merge, Transfer, Swap and ApplyInventoryChanges before changing inventory. It resolves actual source/destination ownership, including Scav and mail transfers, and only applies the restriction to the seasonal PMC destination. Both swap destinations and the proposed parent graph for sort operations are validated. Rejected operations return a warning and leave item counts, positions and contents intact.

Enabling the perk does not delete, relocate or confiscate existing items. Players can remove previously stored prohibited items. Removing the perk restores ordinary container handling. Shared item templates are never modified, so normal characters and bots retain their usual restrictions.

## Evidence and validation

Native evidence uses the supplied GameAssembly SHA-256 `94ae9b20597624e9ee737ab2c64159dd3ef0680c71d6f106b02fe9ca3e362c8a`:

- `PerkManager.CanStoreInSecureContainer`, RVA `0x12CB620`, rejects when any restriction filter fails.
- `PerkManager.MatchesItemRestriction`, RVA `0x3046A80`, enumerates compound contents and applies include/exclude rules to every item.
- `Grid.TryGetSecureContainerPerkRestriction`, RVA `0x40159D0`, checks ownership and secure-container ancestry.
- `ActiveHealthController.ApplyPerkItemUseEffect`, RVA `0x2BDF630`, samples enabled symptoms without replacement.
- `ApplyDistributedHealthRate`, RVA `0x15FDA20`, and `PerkTimedRatesEffect.RegularUpdate`, RVA `0x1F049A0`, define the rate distribution and Existence attribution. Continuation blocks were included in inspection.

Shared and game-assembly checks cover target stability, eligible pools, distinct symptom sampling, all six durations/rates, overlap order, unavailable shape rejection, allow-list behavior, once-per-use receipts and native hook bindings. The isolated server suite passes 55 checks plus five restart checks. The previous consumable suite also passes 55 checks plus five restart checks, and core integration passes 66 checks. The core comparison now uses the same narrowly verified hideout housekeeping exceptions as the existing restart suite; all other normal PMC/Scav changes fail.

Compilation and isolated-server tests do not prove actual raid behavior. In-game symptom timing, medication cancellation, visual effects, nested inventory UI feedback and raid-end health persistence still need validation with a disposable account. No live installed profile was used, and the historical package was staged without installation. Current updates must be installed through MSBuild without stopping or starting applications.
