# Trader-price perks — build 0.1.20

Personality Vacuum and Third Leg bring the implemented catalogue to 27 of 39. Install the updated client and server together. These personal perks need no configuration change; select and save one in the perk editor.

## Effects

| Perk | Captured behavior |
| --- | --- |
| Personality Vacuum | Purchases from the eight captured traders cost 20% more; Charisma cannot gain experience. Grants two points. |
| Third Leg | Therapist purchases cost 5% less; the captured **sprint-speed** multiplier is 0.99. Grants one point. |

The captured catalogue makes these perks mutually exclusive. Third Leg also retains its captured conflict with Sprinter. Custom trader IDs are excluded from Personality Vacuum's allow-list. The existing client skill-growth and sprint-speed hooks provide the secondary effects.

The pricing backport scales purchase requirement counts in SPT's trader assortments, including currencies and barter items. Requirement templates, dogtag levels/sides, functional-item flags, loyalty locks, quest locks and purchase limits are preserved. Counts remain fractional until the client prepares payment: `EFT.Trading.Requisite.RequiredItemsCount` uses `Ceiling(count * quantity)`. For example, a one-item barter scaled to 1.2 requires two items for one purchase, or six for five purchases. A 101-rouble price discounted to 95.95 requires 96 roubles for one purchase.

The multiplier is read as a decimal, avoiding a float 1.2 becoming slightly larger than 1.2 and causing an extra currency unit at otherwise integral prices. Purchase totals follow the game's double-precision quantity calculation. This documents the SPT implementation; live-server barter rounding has not been independently compared.

These purchase modifiers do not alter selling proceeds, repair prices, insurance, clothing-service fees or non-trader flea offers. Third Leg uses the captured sprint-speed effect family despite the locale's broader “movement speed” wording.

## Shop and flea behavior

The server returns a separate assortment copy for the affected character, including Fence assortments. Base trader stock and shared barter schemes are not rewritten. Repeated requests always start from the original price, so refreshes cannot compound a modifier.

Flea search, direct offer lookup, required-item search and build search read adjusted copies of affected trader offers within the current request. Price filtering and sorting see the adjusted costs before pagination. Non-trader offers keep their original requirements. The request scope is restored by a finalizer, including when an exception occurs; concurrent normal and seasonal requests cannot share a price scope. No client price patch is needed because the normal shop and flea views read these server requirements.

SPT's native payment handler trusts the client-supplied amount and grants the item before payment. A new check verifies the selected assortment scheme, quantity, supplied requirement totals, owned stack amounts and available currency before allowing that sequence. Missing payment, stale higher/lower totals and insufficient funds return a client-visible error without changing inventory or stock. A stale price requires refreshing the offer; the server does not silently choose a different charge.

The validation holds SPT's existing reentrant buy lock through the native purchase. The trader-from-flea caller is checked too, because it otherwise reduces flea stock after a rejected inner purchase. The checks preserve SPT's existing handling of other purchase restrictions; they are not a replacement for the entire trading subsystem.

Removing a perk and saving reconnects the client and restores the original assortment and flea prices. Profile changes use the existing independent seasonal-profile mechanism.

## Validation

- Release/Debug compilation: zero warnings or errors.
- 130 shared and client-assembly assertions cover captured price filters, secondary effects, mutual exclusion, decimal calculations and rounding, alongside existing contracts.
- All six existing resource/bush client transpiler checks still pass.
- The existing 66 isolated server checks pass.
- 98 trader integration checks pass: all eight affected traders, repeated requests without compounding, normal assortments, rouble/dollar/euro purchases, barters, Therapist discounts, invalid-payment rejection without inventory/stock changes, flea pricing/filtering/direct lookup/build requests, concurrent normal/seasonal searches and perk removal.
- Four restart checks verify both test inventories, saved selection and a stable seasonal price after logout/save and restart.

Reproduce with a fresh run of `tools/test_integration.py` on the isolated server. Stop it, run `tools/test_trader_prices.py prepare`, start it and run `verify`. Restart it and run `restart`. Preparation modifies only synthetic accounts from that fresh integration run; use a fresh pair when repeating the suite so native trader purchase limits are not already exhausted.

Actual in-game shop/flea display, purchase confirmation, Charisma gain blocking and Third Leg sprint speed still need validation. Use a disposable account, compare each perk separately with the normal PMC, and confirm that removing the perk refreshes both purchase screens.
