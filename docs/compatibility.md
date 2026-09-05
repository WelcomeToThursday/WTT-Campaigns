# Compatibility and remaining gates

This build is **not full parity**. Compilation, assembly inspection and backend tests do not prove a working game session. No claim of verified in-game switching, raid completion or native visual parity is made.

## Implemented catalogue entries

No Insurance, Handyman, Hemophilia, Osteoporosis, Incompetent, Polydipsia, Chronic Fatigue Syndrome, Dr. Jekyll, Marathon Runner, The Tarkov Shooter, Thrombophilia, Hypodipsia, Polyphagia, Sturdy Bones, Prodigy, Exhaustion, Safecracker, Youth, Hercules, Sprinter, Average, Well That Hurt! and Diet.

These cover skill presets/gain/caps, metabolism, body-part stamina capacity/restoration/consumption, injury probabilities, fall damage, sprint speed, mechanical-key consumption, filtered consumable resources, server crafting time and insurance rejection. Client hooks are scoped to the active seasonal PMC. AI, Scav and normal PMC effects are excluded. See [item-resource behavior and validation](item-resources.md).

## Deliberately unavailable

Selection rejects any entry with an unimplemented effect family:

- Seasoned PMCs: all PMC XP sources still need integration.
- No FiR for Hideout: server requirements and client requirement views still need integration.
- Personality Vacuum and Third Leg: trader pricing and payment validation remain unfinished.
- Allergic, Juice Time and Sailor's Nostalgia: native random-selection semantics were investigated, but persistent target generation and timed consumable effects remain unfinished.
- No Flea Market: NPC-only offer filtering, purchase validation and listing restrictions remain unfinished.
- Bushborne: bush interaction hooks remain unfinished.
- Broken Secure Container: preserve the live allow-list; apply it to available SPT items without importing the 45 missing live templates. Runtime container restrictions remain unfinished.
- Street Tax and Kappa Protocol: reward contents and initial delivery timing are unverified. No mail or scheduled grant is sent.
- Lucky and Unlucky: actual behavior is unverified.
- Armor Shortage and Black Division: empty effect arrays are not treated as no-ops. Supporting world/bot/asset analysis remains open.

## Evidence and discrepancies

The capture importer preserves complete effect objects, including `appliedRandomEffectCount`, and only extracts the required locale/profile-state fields. Catalogue/localization sources and icons have SHA-256 provenance. Two catalogue responses are identical; 6 common and 33 personal entries contain reciprocal exclusions. All 39 local images are valid 272x272 PNGs.

Polydipsia uses the captured **1.2** hydration multiplier despite its 15% description. Handyman grants Crafting 51 once and multiplies SPT's already skill-adjusted craft time by **0.87**, retaining SPT's five-second minimum. Its client craft-duration preview still needs comparison with the server result.

Live `ActiveHealthController.Wound.DefaultWorkTime` at RVA `0x40B20A0` returns positive infinity when PersistentFreshWounds is active. Dr. Jekyll uses the same duration for the seasonal PMC during a raid; normal bleed build-up remains intact. Raid-end validation remains open.

Live `PerkRuntimeUtility.ConvertDurabilityMultiplierToUsageChance` at RVA `0x4019390` converts values <=0 to 1, values >1 to their reciprocal, and other values to `1 - value`. Safecracker 0.25 therefore consumes on 75% of successful mechanical-key uses. The client patch replaces only the validated unlock increment, preserving SPT's last-use disposal logic. Shared boundary tests and actual IL site checks pass; empirical in-game probability testing remains open.

Live `ActiveHealthController.ApplyPerkItemUseEffect` at RVA `0x2BDF630` samples enabled sub-effects without replacement using **RandomSlotCount**. For Allergic that is three sub-effects. `appliedRandomEffectCount` is absent from this build's metadata contract and is not assumed to control native execution. Rate-effect duration, partial-use/interruption and persistent target behavior need further verification before enabling allergy entries.

Native reports record the GameAssembly hash. The inspection tool uses PE function boundaries and stops at padding to avoid attributing following code to a method. Recovered UI provenance records source levels 47â€“50 and object IDs.

## Validation status

- Release/Debug compilation: zero errors and warnings.
- Shared contracts plus actual client assembly compatibility: 96 assertions passed; all three resource transpilers also pass against the installed game methods.
- Item resources: 33 isolated server checks and two resource-persistence checks after a normal logout/save and restart passed. Actual in-game animations, interruptions, UI refresh and raid-end persistence remain unverified.
- Real isolated SPT server: 66 checks passed, including all icons, profile creation, skill presets, budget/conflict handling, edits, unsupported entry rejection and repeated switching.
- Restart recovery: 10 checks passed, including selected mode, revision, raid lock, child-to-root resolution, grant receipts and unchanged normal PMC/Scav.
- Recovered UI bundle: 11 SPT-compatible layouts, one camera/light prefab and the Bender font. All 26 decorative UI sprites and their material/shader are bundled. The 39 perk icons are excluded and served locally.
- UI 0.1.4: 20 native client binding checks, 36 Unity interaction checks at each of 1080p, 1440p and 1902x992, and 42 read-only UI server checks. Gamma previews use the client view source; equipment-model images in editor previews are stand-ins.

The restart comparison allows only SPT's verified hideout housekeeping: `sptUpdateLastRunTimestamp` advancing and the level-zero, empty generator changing from active to inactive. `HideoutHelper.UpdatePlayerHideout` and `UpdateFuel` perform those updates independently of the mod. Every other normal PMC/Scav difference fails the test. Repeated longer-running restart/raid tests are still required.

## In-game completion gate

Use a disposable SPT test account with both staged components. Verify menu -> create -> selection -> confirmed save -> seasonal switch -> skill/metabolism/key effect -> normal switch. Then test raid entry/end, reconnects, pending saves, cancelled operations and switching with hideout/UI state loaded. Compare complete normal and seasonal profile contents around these actions.

UI 0.1.4 opens a two-card profile selector at startup, using original selection artwork and Bender typography. The seasonal card expands on hover. Each existing profile supplies an appearance-only equipment descriptor to SPT's native character renderer, using recovered camera/light settings. Live's specialized hover animation remains unported; runtime framing, lighting, model lifecycle and exact visual parity still need in-game validation. The native PERKS tab and personal/global editor remain available. See [UI details](ui.md).

The package contains all icons and has no runtime CDN requests. Runtime offline behavior still needs testing with external networking unavailable. Battle pass, leaderboards, seasonal rewards and automatic wipes remain outside scope.
