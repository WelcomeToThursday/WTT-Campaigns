# Shared project organization

`WTT-Seasonal.Shared` contains the contracts and game-independent logic used by the client and server. Namespaces match folders beneath `Shared`, for example `SeasonalPerks.Shared.Effects.Items`.

| Folder / namespace suffix | Responsibility |
| --- | --- |
| `Contracts` | API requests and responses: `Mutation`, `Snapshot`, and `CharacterSummary` |
| `Hub` | Conserved document identities, rolling pickup allowance, raid and transaction receipts |
| `Configuration` | Server-configurable selection rules |
| `Perks` | Catalogue entries and selection validation / point balance |
| `Profiles` | Saved perk state, rolled effect parameters, and loaded-character identity checks |
| `Effects` | Effect definitions, item filters, supported-effect checks, and runtime aggregation |
| `Effects.Consumables` | Consumable buffs, allergy selection, and once-per-use receipts |
| `Effects.Items` | Key usage and secure-container restrictions |
| `Effects.Movement` | Bush slowdown calculations |
| `Effects.Skills` | Experience scaling |
| `Effects.Trading` | Trader price and payment rounding |
| `Serialization` | JSON extension-data preservation and legacy parameter conversion |

Keep each type in a file named after it. The generic and non-generic `Snapshot` types share `Contracts/Snapshot.cs` because they form one response contract. Add game-independent calculations to the relevant effect area; keep Unity, EFT, SPT hooks, and native appearance adapters in the client or server project. Import the specific namespaces a file uses.

Namespace changes preserve JSON property names, defaults, extension data, and saved-state schema versions. They do change CLR type names: consumers of the former flat namespace must update their imports and rebuild. Package the rebuilt client, server, and shared assemblies together.

Build the solution and run the existing contract/serialization and assembly compatibility checks documented in [CONTRIBUTING](../CONTRIBUTING.md) after moving shared types.
