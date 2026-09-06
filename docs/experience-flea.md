# Seasoned PMCs and No Flea Market

Build 0.1.21 enables two more captured entries, bringing support to 29 of 39.

## Seasoned PMCs

The common perk `69c41adf883efd5e3b09ccae` supplies the captured 1.25 PMC XP multiplier. Existing configurations must add this ID to `EnabledCommonIds`, then save the seasonal selection. Fresh configurations include supported common perks automatically. Existing XP is never retroactively multiplied.

The installed EFT client has separate award paths:

- `BaseStatisticsManager.EndStatisticsSession(ExitStatus, float)` combines raid counters, the exit multiplier and the profile experience bonus. The patch multiplies `ExperienceBonusMult` before native integer conversion. Kills, loot, exploration, raid healing/food/drink and exit rewards therefore receive one bonus together. Native transit deferral and death/run-through multipliers remain in place.
- `Profile.Experience` wraps incremental examination and `ItemManipulator.FinishConditional` quest awards. The prefix scales only positive deltas, truncating fractional integer XP and saturating instead of overflowing. Profile deserialization and server reconciliation use `ProfileInfo.Experience` directly, so they do not apply the perk again.
- `OfflineStatisticManager.ExperienceGained` scales treatment awards before its fractional accumulator. This does not change SPT's existing menu treatment persistence/reconciliation behavior.

All three client paths require the loaded seasonal PMC identity and exclude Savage profiles. The backend scales `ProfileHelper.AddExperienceToPmc` for quest/achievement rewards and the PMC delta from `InventoryController.FlagItemsAsInspectedAndRewardXp` for examination. Scav examination XP stays at its native value. Shared item/quest templates are not changed. Administrative XP assignments remain exact.

The server accepts the client's already calculated raid XP without scaling it a second time. This build requires both client and server components. In-game raid completion, transit chains, Lightkeeper, treatment persistence and reward-preview text still require a disposable account session; assembly and server checks do not establish those outcomes.

## No Flea Market

Personal perk `69c3da8fc0e4deb02605f3c9` costs the captured 10 points. Trader offers remain available through the flea interface; SPT's generated PMC offers and player listings are excluded.

Offer filtering shares the existing request-local trader-price scope. `GetOffers`, `GetOffersOfType` and offer-ID reads exclude non-traders before search selection, build choice, category counts and pagination. Internal-ID lookups use the same scope. Filtering never removes offers from the shared service, and the scope restores in a finalizer.

`TradeController.ConfirmRagfairTrading` validates every requested offer before the native purchase loop. A cached non-trader offer, missing offer or mixed basket is rejected before money, items or stock change. Trader purchases retain native availability/loyalty checks and seasonal pricing validation.

`AddPlayerOffer` and `ExtendOffer` reject before listing fees or inventory changes. Existing listings can still expire, settle or be removed through SPT's normal lifecycle; selecting the perk does not confiscate outstanding offers or rewards. Removing the perk restores searches, purchases and new listings immediately. The native listing controls still display; attempted listing/extension receives a server error explaining the perk restriction.

## Validation

- 45 isolated-server checks cover examination/quest XP, Scav neutrality, reload neutrality, selection, ordinary/owner/build/linked/required searches, pagination/category counts, internal-ID lookups, concurrent normal/seasonal requests, cached/mixed purchase rejection, stock/payment preservation, trader purchases, listing/extension rejection and restoration after removal.
- Six restart checks cover both profiles' XP/inventories, both perk selections and trader-only searches.
- The actual raid XP transpiler is checked against the installed client method: branch labels are preserved and a missing bonus store is rejected. Additional assembly checks cover examination/quest award routes, treatment argument bindings and reconciliation bypass.

Use the existing isolated server fixture from `test_integration.py` and `test_restart.py`. With the test server stopped, run `test_experience_flea.py prepare`; start it and run `verify`; restart and run `restart`; stop it and run `restore-config`. Only synthetic accounts in `Testing/Server` are edited. The report is written under ignored `Research/`. Run the client check with `dotnet run --project Tests -c Release -- --experience-hooks <SPT> <client-dll>`; packaging includes it automatically.
