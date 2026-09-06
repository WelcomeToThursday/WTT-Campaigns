# Compatibility and remaining gates

This build is **not full parity**. Compilation, assembly inspection and backend tests do not prove a working game session. No claim of verified in-game switching, raid completion or native visual parity is made.

## Implemented catalogue entries

No Insurance, Handyman, Hemophilia, Osteoporosis, Incompetent, Polydipsia, Chronic Fatigue Syndrome, Dr. Jekyll, Marathon Runner, The Tarkov Shooter, Thrombophilia, Hypodipsia, Polyphagia, Sturdy Bones, Prodigy, Exhaustion, Safecracker, Youth, Hercules, Sprinter, Average, Well That Hurt!, Diet, Bushborne, No FiR for Hideout, Personality Vacuum, Third Leg, Seasoned PMCs, No Flea Market, Juice Time, Sailor's Nostalgia, Allergic and Broken Secure Container.

These cover skill presets/gain/caps, metabolism, body-part stamina capacity/restoration/consumption, injury probabilities, fall damage, sprint speed, mechanical-key consumption, filtered consumable resources, bush slowdown/noise, hideout FiR requirements, server crafting time, trader purchase prices and insurance rejection. Client hooks are scoped to the active seasonal PMC. AI, Scav and normal PMC effects are excluded. See [item-resource behavior and validation](item-resources.md).

## Deliberately unavailable

Selection rejects any entry with an unimplemented effect family:

- Street Tax and Kappa Protocol: reward contents and initial delivery timing are unverified. No mail or scheduled grant is sent.
- Lucky and Unlucky: actual behavior is unverified.
- Armor Shortage and Black Division: empty effect arrays are not treated as no-ops. Supporting world/bot/asset analysis remains open.

## Evidence and discrepancies

The capture importer preserves complete effect objects, including `appliedRandomEffectCount`, and only extracts the required locale/profile-state fields. Catalogue/localization sources and icons have SHA-256 provenance. Two catalogue responses are identical; 6 common and 33 personal entries contain reciprocal exclusions. All 39 local images are valid 272x272 PNGs.

Polydipsia uses the captured **1.2** hydration multiplier despite its 15% description. Handyman grants Crafting 51 once and multiplies SPT's already skill-adjusted craft time by **0.87**, retaining SPT's five-second minimum. Its client craft-duration preview still needs comparison with the server result.

Live `ActiveHealthController.Wound.DefaultWorkTime` at RVA `0x40B20A0` returns positive infinity when PersistentFreshWounds is active. Dr. Jekyll uses the same duration for the seasonal PMC during a raid; normal bleed build-up remains intact. Raid-end validation remains open.

Live `PerkRuntimeUtility.ConvertDurabilityMultiplierToUsageChance` at RVA `0x4019390` converts values <=0 to 1, values >1 to their reciprocal, and other values to `1 - value`. Safecracker 0.25 therefore consumes on 75% of successful mechanical-key uses. The client patch replaces only the validated unlock increment, preserving SPT's last-use disposal logic. Shared boundary tests and actual IL site checks pass; empirical in-game probability testing remains open.

Live `ActiveHealthController.ApplyPerkItemUseEffect` at RVA `0x2BDF630` samples enabled sub-effects without replacement using **RandomSlotCount**. For Allergic that is three sub-effects. `appliedRandomEffectCount` is absent from this build's metadata contract and is not assumed to control native execution. Juice Time and Sailor's Nostalgia each have one enabled sub-effect and four fixed targets, so the selection is deterministic; see [consumable evidence and validation](consumables.md).

Native reports record the GameAssembly hash. The inspection tool uses PE function boundaries and stops at padding to avoid attributing following code to a method. Recovered UI provenance records source levels 47Ã¢â‚¬â€œ50 and object IDs.

## Validation status

- Allergic / Broken Secure Container: 55 isolated server checks and five restart checks pass. Actual raid symptoms and inventory UI feedback remain unverified. See [details](allergy-container.md).

- Juice Time / Sailor's Nostalgia: 55 isolated server checks and five restart checks pass. Actual in-game timing, animations and HP persistence remain unverified. See [details](consumables.md).

- Seasoned PMCs / No Flea Market: 45 isolated server checks and six restart checks pass. The actual raid XP transpiler passes against the installed assembly; gameplay and client UI checks still need an in-game session. See [behavior and limits](experience-flea.md).

- Release/Debug compilation: zero errors and warnings.
- Shared contracts plus actual client assembly compatibility: 253 assertions passed; all three resource, three bush sound and the raid XP transpilers also pass against the installed game methods.
- Trader prices: 98 isolated server checks and four restart checks pass, including all eight captured traders, three currencies, barters, rejection without inventory/stock changes, flea filtering/lookups/build requests and concurrent normal/seasonal searches. See [details](trader-prices.md).
- Bushborne / No FiR: 10 isolated upgrade/selection checks and three restart checks pass; gameplay and movement/audio checks still need an in-game session. See [details](bush-hideout.md).
- Item resources: 33 isolated server checks and two resource-persistence checks after a normal logout/save and restart passed. Actual in-game animations, interruptions, UI refresh and raid-end persistence remain unverified.
- Real isolated SPT server: 66 checks passed, including all icons, profile creation, skill presets, budget/conflict handling, edits, unsupported entry rejection and repeated switching.
- Restart recovery: 10 checks passed, including selected mode, revision, raid lock, child-to-root resolution, grant receipts and unchanged normal PMC/Scav.
- Recovered UI bundle: 11 SPT-compatible layouts, one camera/light prefab and the Bender font. All 26 decorative UI sprites and their material/shader are bundled. The 39 perk icons are excluded and served locally.
- UI 0.1.4: 20 native client binding checks, 36 Unity interaction checks at each of 1080p, 1440p and 1902x992, and 42 read-only UI server checks. Gamma previews use the client view source; equipment-model images in editor previews are stand-ins.

The restart comparison allows only SPT's verified hideout housekeeping: `sptUpdateLastRunTimestamp` advancing and the level-zero, empty generator changing from active to inactive. `HideoutHelper.UpdatePlayerHideout` and `UpdateFuel` perform those updates independently of the mod. Every other normal PMC/Scav difference fails the test. Repeated longer-running restart/raid tests are still required.

## In-game completion gate

Use a disposable SPT test account with both staged components. Verify menu -> create -> selection -> confirmed save -> seasonal switch -> skill/metabolism/key effect -> normal switch. Then test raid entry/end, reconnects, pending saves, cancelled operations and switching with hideout/UI state loaded. Compare complete normal and seasonal profile contents around these actions.

UI 0.1.4 opens a two-card profile selector at startup, using original selection artwork and Bender typography. The seasonal card expands on hover. Each existing profile supplies an appearance-only equipment descriptor to SPT's native character renderer, using recovered camera/light settings. Live's specialized hover animation remains unported; runtime framing, lighting, model lifecycle and exact visual parity still need in-game validation. The native PERKS tab and personal/global editor remain available. See [UI details](ui.md).

The package contains all icons and has no runtime CDN requests. Runtime offline behavior still needs testing with external networking unavailable. Battle Pass and Seasonal Rewards browsing are included in UI 0.1.24, alongside About the Season. Reward delivery, document transactions, leaderboards and automatic wipes remain outside scope. See [hub evidence and validation](battle-pass-ui.md).

## Battle Pass gameplay 0.2.0

See [Battle Pass gameplay](battle-pass-gameplay.md) for capture changes, dependency gates, item recovery and tests. Documents/crates register during Preload, before the SPT database-integrity checkpoint. Nine bundles use unique `wtt-seasonal/` keys and SDK-generated PreviewPivot references mapped to the installed native type.

Native document hooks bind successful `ItemController.RaiseAddEvent` and `MergeResult`, `TransferResult`, and `SplitResult.RaiseEvents`. The profile commit adapter verifies the SPT 4.1 `SaveServer.profiles` concurrent dictionary because `GetProfiles()` returns a copy. Raid-end deduplication and native inventory operations share the account lock. These bindings must be rechecked when SPT changes. No CommonLib quest import dependency is added.

Automated server and SDK results are separate from installed-game acceptance. Item rendering, native event timing, animation, audio and profile-reload behavior still need an in-game pass; visual parity is not declared.
