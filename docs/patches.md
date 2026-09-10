# Patch organization

Group patches by the game feature they modify. A patch can support several perks, so catalogue names belong in the data rather than in the folder hierarchy. Each patch class has its own file; reusable server effect calculations live in `Server/Effects`.

## Client

| Folder under `Client/Patches` | Responsibility |
| --- | --- |
| `Session` | Backend identity and HTTP request identity during character switching |
| `Health` | Energy/hydration drain, injury probabilities, damage context, persistent wounds, consumable buffs and allergy symptoms |
| `Movement` | Sprint speed, stamina and bush movement/sound |
| `Skills` | Skill progression/caps and PMC examination, treatment and raid XP |
| `Items` | Consumable resources, once-per-use perk triggers, key usage and secure-container restrictions |
| `Hideout` | Seasonal found-in-raid requirement checks and display |
| `UI` | Menu entry, native perks tab and seasonal input handling |

Namespaces follow the folders, for example `WTT.Campaigns.Client.Patches.Items`.

`PatchRegistration.EnableAll()` is the client entry point, called from `Plugin.Awake`. Its feature methods list every patch instance explicitly, including separate targets for parameterized patches. Session hooks register first, the damage-context hook precedes injury probability, and UI hooks register last. Add new client patches to the relevant method here.

## Server

| Folder under `Server/Patches` | Responsibility |
| --- | --- |
| `Session` | New-session recovery and raid start/end lifecycle |
| `Hideout` | Craft-time adjustments |
| `Trading` | Insurance, trader assortment prices, flea search pricing and purchase/listing restrictions |
| `Skills` | PMC quest/achievement and examination XP |
| `Items` | Stash medical/food resources and secure-container inventory-operation validation |

Server namespaces follow the same convention. Keep `[Injectable]` on each `AbstractPatch` class. `ServerStartup` already discovers and enables this assembly's `IRuntimePatch` services; server patches need no second registration list.

`Server/Effects/ItemResourceEffects.cs` holds the resource-filter calculation shared by `MedicalResourcePatch` and `FoodResourcePatch`. It is a helper, not a discoverable patch. `TraderPriceEffects` creates seasonal copies of trader offers within each flea search. `TraderPaymentValidation` checks the selected scheme and funds under the native buy lock before either purchase path can change inventory or stock.

## Extending or moving a patch

Keep the target method, Harmony attributes, scope checks and any explicit patch identifier intact during organizational changes. Preserve dependencies between hooks that share context. When enabling a new effect family, update shared effect support only after its required hooks are present.

The client-transpiler and UI compatibility checks locate client patch types by full name. Update their lookups when changing namespaces. Build the solution and run those checks after a move; server startup checks confirm dependency-injection discovery.

This organization change preserves the existing perk implementations and version 0.1.18. The moved patch/helper method bodies were compared with the preceding build, and the client registration still contains the same patch targets and parameterized instances.
