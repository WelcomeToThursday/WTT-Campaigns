# Documents and rewards

Campaign characters earn local Battle Pass rewards by collecting documents in raids. Each character has separate claims, document allowances and unlocks. New progress starts at zero; there is no automatic campaign expiry or wipe.

Custom campaigns can change document types, reward costs and collection settings. Always check the selected campaign's displayed requirements.

## Collect documents

In the bundled campaign, up to **eight ordinary documents** can appear in an eligible campaign PMC raid. Documents spawn as loose loot at existing eligible locations, rather than inside containers. Fewer can appear when the map has too few suitable loot points or your remaining allowance is lower.

Document types have map restrictions and per-type limits. Maps without eligible document types receive none. Regular PMC and Scav raids do not receive campaign document spawns.

The first pickup starts a **23-hour collection window** with a **30-document allowance**. Picking up the same document again, splitting or merging stacks, or bringing an existing document into a raid does not create another new pickup.

Survived and run-through extractions give each newly acquired extracted document a **5% chance** to award an additional Classified document by default. You keep the ordinary document. Failed raids do not award this bonus.

## Claim rewards

Select a reward in the [campaign hub](battle-pass-ui.md) to see its requirements. These can include faction, level, completed quests, prior-page claims and document costs.

Claims spend the required ordinary document types first. **Classified documents can cover a shortage one-for-one**, but only after confirmation.

Physical rewards go into your stash. Make room before claiming: there is no mail or sorting-table fallback. A shortage, missing dependency, full stash or outdated reward state rejects the whole transaction. Refresh the hub if its displayed state is outdated.

A tile can contain several rewards but counts as one claim. Trader unlocks still obey normal stock, loyalty, quest and purchase limits. Local Tarcoins do not enable online purchases.

## Exchanges

The bundled exchange offers:

| Give | Receive |
| --- | --- |
| Five mixed ordinary documents | One selected ordinary document |
| Ten ordinary documents | The configured gear crate, when an eligible contents pool is available |

Classified documents are excluded from exchange sources. Campaign authors can disable the crate exchange or supply their own eligible crate.

## Locked rewards

A listed reward is not necessarily available in your installation. Missing quests, customization, trader offers or crate contents keep the affected reward locked. Historical Perspectives and rewards depending on unavailable quest chains cannot be completed through this mod alone.

The bundled crate definitions do not include a contents pool. Their claims and exchange remain unavailable until the campaign supplies one. No substitute contents are awarded.

Install WTT-ContentBackport and its dependencies for document and crate models. Missing required models can prevent the server from loading the mod; check the startup error for the missing dependency.

## Server settings

For the bundled setup, an optional `hub-config.json` beside `WTT-Campaigns.Server.dll` accepts:

| Setting | Allowed values | Default |
| --- | --- | --- |
| `DocumentsPerRaid` | 0–8 | 8 |
| `MapCounts` | Map names mapped to caps of 0–8 | Uses the overall cap |
| `ClassifiedChancePercent` | 0–100 | 5 |

These caps do not bypass document map restrictions or per-type limits. Custom campaigns also have document settings in the [Campaign Creator](season-creator.md).

Back up existing configuration before editing and restart the server to load changes. Preserve character profiles and campaign packs during updates.
