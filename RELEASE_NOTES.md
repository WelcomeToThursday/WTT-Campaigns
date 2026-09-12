# WTT-Campaigns 0.6.0 — Initial Beta

WTT-Campaigns 0.6.0 is the mod's **first public beta release**. This release introduces campaign character progression and the tools to create and share custom campaigns for SPT.

## Hotfixes

- Fix raid setup crashes caused by missing bot-role difficulty settings.
- Clear the affected character's pending raid state when raid loading fails or is cancelled, allowing another attempt.

## What is included

- Multiple independent campaign characters, with a character selector, switching, and confirmed delete or achievement-preserving wipe actions.
- A perk selection system with point budgets, conflicts and shared campaign rules. The bundled catalogue includes 33 supported entries covering character skills, survival, equipment use, trading and hideout behavior.
- A campaign hub with local Battle Pass rewards, raid document collection, document exchanges and saved claim progress.
- Story journals, dialogue, trader visits and support for authored raid objectives and events. Story content is supplied by campaign authors; a complete live campaign is not bundled.
- A browser-based Campaign Creator for starting loadouts, perks, quests, rewards, crates, artwork and stories, with validation, previews, tutorials and campaign pack import/export.
- Trader loyalty and task progression changes for regular and campaign characters, including grouped task lists, updated reputation requirements and no trader spending gates.

## Before playing

This beta targets **SPT 4.1.3 / EFT 0.16.9.40743**. Install **UnityToolkit 2.0.2 or later** with its prepatcher and **WTT-ContentBackport 2.0.1 or later** with its dependencies. These dependencies are separate downloads.

Extract the package's `BepInEx` and `SPT_Runtime` folders into your SPT installation while the game and server are closed. Install the complete matching package. Back up profiles before trying the beta, and preserve existing configuration, campaign profiles and the server mod's `creator` folder when updating.

Open **CHARACTERS** or press **F8** outside a raid to create a campaign character. For installation details, first steps and the Campaign Creator workflow, see the [README](README.md).

## Open the Campaign Creator

With the SPT server running, use the **WTT-Campaigns creator link in the SPT launcher** or open [https://127.0.0.1:6969/wtt-campaigns/creator](https://127.0.0.1:6969/wtt-campaigns/creator) in your browser for a default local server. If you changed the server address or port, use the address from your launcher followed by `/wtt-campaigns/creator`. Sign in with an administrator account if prompted.

Choose **Create blank campaign**, duplicate an existing campaign, or import a campaign ZIP to get started. The creator includes **Help and tutorials** for authoring, validation and publishing.

## Beta scope

This beta supports local campaigns and custom content. It does not include every live EFT feature; compatibility with other mods can vary.

Street Tax, Kappa Protocol, Lucky, Unlucky, Armor Shortage and Black Division remain unavailable. Rewards with missing content dependencies stay locked. Online purchases, leaderboards and automatic campaign wipes are not included.

Trader progression changes also affect regular characters. Existing reputation and task progress are retained, but loyalty can decrease when the new requirements are applied.

## Feedback

Reports about character creation and switching, saving after raids, perk behavior, reward claims, authored campaigns and UI issues are especially useful. Include your mod/SPT versions, other installed mods, reproduction steps, expected and actual behavior, and relevant log excerpts with personal identifiers removed.

## Known Issues

- Fika is not supported.
- The built-in campaign does not include Kord Breach quests.
- Some artwork and presentation are placeholders.

See [compatibility](docs/compatibility.md) for feature limits and [the documentation index](docs/README.md) for help.
