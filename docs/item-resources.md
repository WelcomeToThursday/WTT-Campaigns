# Item-resource perks — build 0.1.18

Well That Hurt! and Diet are selectable. The captured catalogue is unchanged.

- **Well That Hurt!** uses the captured 1.25 multiplier for Grizzly, AFAK, Salewa, IFAK, Car and AI-2 medical kits. Both HP healing and injury-treatment costs consume additional resource. Other medical items are excluded.
- **Diet** uses the captured 0.5 multiplier for descendants of the provisions template, including food and drinks. Resource use changes while the existing healing, energy/hydration and skill calculations remain in their original units.

Raid hooks change resource subtraction, affordable HP and treatment affordability inside `ActiveHealthController.MedEffect.RegularUpdate` and `Residue`. Native timing, interruption, refresh, network synchronization, final flooring and disposal remain in place. The local seasonal PMC's health-controller identity gates these changes. The selected catalogue's medical items are all HP kits; the separate native charge-item decrement is not modified.

Stash use changes `OfflineHealthController.MedEffect.Started`, gated by the active seasonal profile's skill-manager identity. Requests retain SPT's original unscaled units; matching server handlers scale only the inventory cost. This protocol requires the matching client and server build. Normal profiles follow the original SPT handlers. Medical requests cannot heal more whole HP than their resource affords, and unaffordable injuries remain active.

## Native evidence and rounding

The local live module has SHA-256 `94ae9b20597624e9ee737ab2c64159dd3ef0680c71d6f106b02fe9ca3e362c8a`. Reports are under `Research/native`.

- `PerkManager.GetItemResourceUsageMultiplier`, RVA `0x8AF910`: multiply matching effects, with include rules ORed and exclusions taking precedence.
- `PerkManager.MatchesAnyRule`, RVA `0x14AF200`: ParentId uses `ItemTemplate.IsChildOf`, so the full ancestor chain is checked.
- `PerkRuntimeUtility.PositiveMultiplier`, RVA `0x17BDCE0`: nonpositive/NaN multipliers become one.
- Active `MedEffect.RegularUpdate`, RVA `0x1993DC0`: multiply resource consumption and divide affordable HP by the multiplier.
- Active `MedEffect.Residue`, RVA `0x264CDC0`: scale injury cost and its affordability check; preserve final resource flooring.
- Offline `MedEffect.Started`, RVA `0x32C54E0`: scale consumption; round food resource costs to whole units.

Stash food uses round-to-even. With Diet, three requested resource units cost two, while one requested unit costs zero. This includes one-unit foods, which can therefore remain after stash use. This native rounding edge case is deliberately preserved. Raid use retains its separate final flooring behavior; stash and raid remainders can differ.

SPT's stash healing rounds restored HP upward. This backport floors the resource-limited HP allowance before that rounding, avoiding an overdraft at low resource. Less than 1.25 remaining medical resource cannot fund a whole stash HP with Well That Hurt!; fractional leftovers can remain. This is a compatibility choice, not a claim of exact native parity at that boundary. Stash food retains SPT's existing energy/hydration calculation, including its differences from raid calculations.

## Validation

- 96 shared/assembly assertions pass, including every captured medical include rule, provision ancestry, unlisted items, removal and the 23-entry support count.
- The actual client transpiler executes against all three installed game methods and validates each expected patch site. This is a read-only instruction transformation test, not execution of EFT gameplay.
- 33 real isolated-server checks cover selection, HP versus cost, affordable/unaﬀordable treatment, partial consumption, missing resource state, rounding, single-resource food, exhaustion, normal-profile isolation and perk removal.
- Two restart checks confirm the resource fields and removed items survive SPT's normal game-logout save and server restart. Abrupt termination before a save is outside this test.

Run `tools/test_integration.py` on the isolated server to create a fresh synthetic account pair. Stop it, run `tools/test_item_resources.py prepare`, restart it and run `verify`. Stop/restart once more and run `restart`. Fixture preparation checks both the isolated-server directory and synthetic usernames. Use the project Python environment under `.tools/Scripts`.

`tools/package.ps1` builds Release and runs both the contract checks and the resource-hook transformations. No live installation or real player profiles are changed by packaging or the isolated tests.

An in-game test is still required: compare normal/seasonal medical HP and treatment use, partially consume provisions, interrupt each use, check item disappearance and UI refresh, then extract, reconnect and compare resources. Test stash fractional leftovers and the documented one-unit-food rounding separately.
