# WTT-Campaigns

**0.6.0 — Initial beta release**

WTT-Campaigns brings campaign characters, configurable perks, local Battle Pass progression and campaign authoring to SPT. Create separate characters for different campaigns, build a playstyle around benefits and drawbacks, and use the Campaign Creator to make and share your own content.

## Features

- **Separate campaign characters.** Keep multiple characters across multiple campaigns alongside your regular PMC. Each campaign character has its own inventory, quests, traders, hideout, mail, insurance and Scav progression. Switch characters from the in-game selector.
- **Perks and common campaign rules.** Balance beneficial and detrimental modifiers within a campaign's point budget. Supported effects cover skills, stamina, metabolism, injuries, consumables, keys, trader prices, insurance, the flea market and hideout requirements. The bundled catalogue offers 33 supported perks and common rules; six entries are unavailable.
- **Local Battle Pass and rewards.** Collect campaign documents in raids, spend them on eligible rewards, and use document exchanges. Progress and claims are saved per character. Rewards depend on the campaign's rules and installed content.
- **Story tools and trader visits.** Visit trader rooms and play authored chapters, dialogue, objectives and raid events. The Story tab and trader visits are available even in campaigns without a story. A complete live campaign is not included.
- **Campaign Creator.** Build campaigns in SPT's administrator web interface: configure starting characters, perks, quests, documents, rewards, crates, artwork and story content. Validate, preview, publish, import and export campaign packs.
- **Trader and task progression.** Updated loyalty requirements, task reputation rewards and grouped task lists apply to both regular and campaign characters. Spending requirements are removed; loyalty still depends on level and reputation. Existing progress is retained, but recalculated loyalty can decrease.

## Requirements

- **SPT >=4.1.3 / EFT 0.16.9.40743** is the target build for this beta.
- **UnityToolkit 2.0.2 or later**, including its plugin libraries and prepatcher.
- **WTT-ContentBackport 2.0.1 or later** and its dependencies, which supply the document and crate models.

Install the dependencies separately. They are not bundled with WTT-Campaigns. Use matching client and server components from the same release.

## Installation

1. Close the game and SPT server before replacing mod files.
2. Back up your SPT profiles. If WTT-Campaigns is already installed, also back up its server mod folder and `SPT_Runtime/user/seasonal`.
3. Extract the release's `BepInEx` and `SPT_Runtime` folders into your SPT installation, merging them with the existing folders.
4. Check that these files exist:

   ```text
   <SPT>/BepInEx/plugins/WTT-Campaigns/WTT-Campaigns.Client.dll
   <SPT>/BepInEx/plugins/WTT-Campaigns/WTT-Campaigns.UI.dll
   <SPT>/SPT_Runtime/user/mods/WTT-Campaigns/WTT-Campaigns.Server.dll
   ```

5. Start SPT and the game normally.

When updating, preserve configuration files and the server mod's entire `creator` folder. Campaign profiles live in `SPT_Runtime/user/seasonal/profiles`; keep these together with your regular account profiles and do not remove linked profiles manually.

## Getting started

1. Open **CHARACTERS** from the menu, or press **F8** outside a raid.
2. Choose the creation card, select a campaign, and set up your character's faction and appearance.
3. Select perks within the point budget. Detrimental perks provide room for beneficial ones; incompatible choices cannot be combined. Confirm the selection to save.
4. Select the new character's card to switch to it. Creating a character leaves your regular PMC active until you switch, and does not clone existing progress.
5. Use the **MODIFIERS** tab beside Skills and Mastery to inspect saved modifiers. Open the editor from the character selector to change selections. Editing is subject to the campaign's rules and is unavailable during raids.

The campaign hub contains the campaign overview, Battle Pass, rewards and Story pages. Available rewards and story content depend on the selected campaign. Missing content dependencies leave the affected rewards locked.

Character cards also offer confirmed deletion and wiping. **Delete** removes that campaign character. **Wipe** resets its progression while preserving earned in-game achievements and lets you recreate it. Your regular account cannot be deleted through this screen.

## Create and share campaigns

With the SPT server running, open the **WTT-Campaigns creator link in the SPT launcher**, or open the [Campaign Creator on your local server](https://127.0.0.1:6969/wtt-campaigns/creator) in a browser.

The direct URL for a default local server is **https://127.0.0.1:6969/wtt-campaigns/creator**. If your server uses a different address or port, replace `127.0.0.1:6969` with the address configured in your launcher and keep `/wtt-campaigns/creator` at the end. Sign in with an administrator account if prompted.

Start with **Create blank campaign**, duplicate an existing campaign, or import a campaign ZIP.

Configure the content, save the draft, then validate and publish it. Published packs can be exported as ZIP files for sharing. Restart the server after publishing or importing a new pack so its content can load; players can then choose it during character creation. Several compatible campaigns can be played in the same server session.

Once a campaign has characters, gameplay changes require duplicating it into a new campaign. Text and artwork can still be revised. The creator includes **Help and tutorials**; see the [Campaign Creator guide](docs/season-creator.md) and [story authoring guide](docs/story-authoring.md) for details.

## Beta limitations and feedback

This beta supports local campaign play and custom campaign authoring. It does not reproduce every live EFT feature. See [compatibility and known limitations](docs/compatibility.md) before choosing a campaign or combining mods.

- Street Tax, Kappa Protocol, Lucky, Unlucky, Armor Shortage and Black Division are unavailable.
- A complete live story campaign, online purchases, leaderboards and automatic campaign wipes are not included.
- Rewards requiring unavailable quests, customization or crate contents remain locked.
- Fika is not supported.
- The bundled campaign does not include Kord Breach quests. Some artwork and presentation are placeholders.

When reporting a problem, include the mod and SPT versions, installed mods, the affected campaign and character type, steps to reproduce it, and relevant client/server log excerpts. Remove account identifiers and credentials before sharing logs.

## Guides

Browse the [documentation index](docs/README.md) for player guides, perk details, profile recovery and campaign authoring. Start with [characters](docs/characters.md), [documents and rewards](docs/battle-pass-gameplay.md), or [Creator tutorials](docs/creator-tutorials.md).

For changes in this release, see [release notes](RELEASE_NOTES.md). Original code is MIT-licensed; game assets and external dependencies retain their respective ownership as described in [third-party notices](THIRD_PARTY_NOTICES.md).
