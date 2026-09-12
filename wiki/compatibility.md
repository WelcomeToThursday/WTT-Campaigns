# Compatibility and known limitations

## Requirements

Use **SPT 4.1.3 / EFT 0.16.9.40743**, with:

- **UnityToolkit 2.0.2 or later**, including its plugin libraries and prepatcher.
- **WTT-ContentBackport 2.0.1 or later** and its dependencies.

Dependencies are separate downloads. Install matching WTT-Campaigns client, UI and server components from the same release. See [installation](../README.md#installation).

**Fika is not supported.** Compatibility with other mods can vary, especially when they also change profiles, perks, trader progression or the same menus.

## Available perks

The bundled catalogue has 39 entries; 33 are available:

No Insurance, Handyman, Hemophilia, Osteoporosis, Incompetent, Polydipsia, Chronic Fatigue Syndrome, Dr. Jekyll, Marathon Runner, The Tarkov Shooter, Thrombophilia, Hypodipsia, Polyphagia, Sturdy Bones, Prodigy, Exhaustion, Safecracker, Youth, Hercules, Sprinter, Average, Well That Hurt!, Diet, Bushborne, No FiR for Hideout, Personality Vacuum, Third Leg, Seasoned PMCs, No Flea Market, Juice Time, Sailor's Nostalgia, Allergic and Broken Secure Container.

Campaign authors choose which supported perks and common rules their campaign offers. Personal effects apply to the active campaign PMC. The separate [trader progression changes](trader-progression.md) also affect regular characters.

Street Tax, Kappa Protocol, Lucky, Unlucky, Armor Shortage and Black Division are unavailable and cannot be selected.

## Perk behavior notes

Some bundled descriptions do not precisely describe the numeric effect:

| Perk | Behavior to expect |
| --- | --- |
| Polydipsia | Hydration consumption uses a 1.2 multiplier, despite the description saying 15%. |
| Handyman | Grants Crafting level 51 once and multiplies skill-adjusted crafting time by 0.87, with SPT's five-second minimum. |
| Dr. Jekyll | Fresh wounds do not expire naturally during the campaign PMC's raid. |
| Safecracker | Successful mechanical-key uses consume a use with 75% probability. |
| Third Leg | The 1% speed reduction affects sprint speed. |
| Allergic | Each matching use applies three distinct symptoms. |

See the [perk guides](Home.md#perk-details) for resource rounding, prices, symptoms and other details.

## Campaign and reward limits

- A complete live EFT story campaign and the built-in Kord Breach quest campaign are not included.
- Online purchases, leaderboards, cross-mode synchronization and automatic campaign wipes are unavailable.
- Missing quests, customization definitions, models or crate contents can lock individual rewards. Check the reward's requirement message.
- Story dialogue does not support paid services or compound-item handovers such as assembled weapons and armor. Use ordinary trader/task screens where applicable.
- Some artwork and presentation are placeholders. This beta does not reproduce every live EFT feature or visual detail.

The [Battle Pass](battle-pass-gameplay.md) supports local claims, exchanges and saved character progress. A reward being locked does not mean all reward transactions are unavailable.

## Report a problem

Include the mod and SPT versions, other installed mods, affected campaign and character type, steps to reproduce, expected and actual behavior, and relevant client/server log excerpts. Remove account identifiers and credentials before sharing.

For missing characters or migration conflicts, see [profile recovery](typed-models-and-profile-storage.md). For content that will not publish or play, see [Creator troubleshooting](creator-tutorials.md#troubleshooting).

---

[Documentation home](Home.md) · [Guide navigation](_Sidebar.md)
