# Patch organization

Group patches by the game feature they modify. A patch can support several perks, so catalogue names belong in the data rather than in the folder hierarchy. Each patch class has its own file; reusable server effect calculations live in `Server/Effects`.

## Client

| Folder under `Client/Patches` | Responsibility |
| --- | --- |
| `Session` | Backend identity and HTTP request identity during character switching |
| `Health` | Energy/hydration drain, injury probabilities, damage context and persistent wounds |
| `Movement` | Sprint speed and stamina capacity, restoration and consumption |
| `Skills` | Skill progression and caps |
| `Items` | Consumable resources and key usage |
| `UI` | Menu entry, native perks tab and seasonal input handling |

Namespaces follow the folders, for example `SeasonalPerks.Client.Patches.Items`.

`PatchRegistration.EnableAll()` is the client entry point, called from `Plugin.Awake`. Its feature methods list every patch instance explicitly, including separate targets for parameterized patches. Session hooks register first, the damage-context hook precedes injury probability, and UI hooks register last. Add new client patches to the relevant method here.

## Server

| Folder under `Server/Patches` | Responsibility |
| --- | --- |
| `Session` | New-session recovery and raid start/end lifecycle |
| `Hideout` | Craft-time adjustments |
| `Trading` | Insurance and future trader transaction hooks |
| `Items` | Stash medical and food resource handlers |

Server namespaces follow the same convention. Keep `[Injectable]` on each `AbstractPatch` class. `ServerStartup` already discovers and enables this assembly's `IRuntimePatch` services; server patches need no second registration list.

`Server/Effects/ItemResourceEffects.cs` holds the resource-filter calculation shared by `MedicalResourcePatch` and `FoodResourcePatch`. It is a helper, not a discoverable patch.

## Extending or moving a patch

Keep the target method, Harmony attributes, scope checks and any explicit patch identifier intact during organizational changes. Preserve dependencies between hooks that share context. When enabling a new effect family, update shared effect support only after its required hooks are present.

The resource-hook and UI compatibility checks locate client patch types by full name. Update their lookups when changing namespaces. Build the solution and run those checks after a move; server startup checks confirm dependency-injection discovery.

This organization change preserves the existing perk implementations and version 0.1.18. The moved patch/helper method bodies were compared with the preceding build, and the client registration still contains the same patch targets and parameterized instances.
