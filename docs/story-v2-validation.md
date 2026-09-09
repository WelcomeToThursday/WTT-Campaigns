# Story client support validation — 0.5.2

Current workflow: [build, validate and always install](build-deployment.md). Never stop or start servers or clients. Any isolated-server results below are historical; those fixtures are retired.

Validated on September 9, 2026 against the local SPT 4.1.x installation and its dumped game assembly. Story requests/responses use protocol 2; saves and creator packs remain format 1.

| Check | Result |
| --- | --- |
| Solution and Release package builds | Passed with no warnings or errors |
| Contracts, story engine, authoring and installed assembly assertions | 4,918 passed |
| Client/native UI compatibility | 52 passed |
| Native resource, bush and experience patch compatibility | All seven patch targets passed |
| Isolated story routes | 49 passed |
| Isolated raid persistence and reconciliation | 37 passed |
| Protocol-2 regression routes | 76 passed |
| Committed protocol-1 receipt recovery after restart | Four passed; no repeated inventory, rewards or presentation |
| Unity dialogue UI | Passed at 1920×1080, 2560×1440 and 1902×992, including Continue and closing text |
| Unity custom room contract | Valid inactive room accepted; active root, missing camera and duplicate cameras rejected |

The protocol checks exercise automatic and multiple staged handovers, cancellation by abandoning preparation, invalid selections, inventory changes independent of story revision, revision changes, deterministic preparation retries, commit retries, arbitrary completion flags, objectives without notes, scene rejection, carried-item pickup/drop projection, changing objective completion, repeated/stale/wrong-character/wrong-raid observations, invalid targets and bounds, all four ordinary-event media references, cinematic interruption/skip follow-ups and survival-dependent progression. Unit checks also cover first visits, trader switching, optional room serialization and duplicate trader assignments.

Unity checks execute the actual UI source in the matching SDK. Historical HTTP checks used synthetic accounts on the now-retired isolated server. They do not substitute for native game interactions.

## Remaining game acceptance

- Visit and switch traders in the game; advance automatic text, acknowledge closing text, and cancel native handover windows. Confirm multiple handovers and normal inventory updates.
- Exercise actual occupied triggers, interaction/shoot callbacks, collectible scene lookup, pickup/drop and native objective updates during a raid. Verify reply and journal refresh.
- Play real Image, Audio, Video and Timeline assets; test completion, skip, death and raid transition cleanup, follow-up dialogue and chained media. Verify that ordinary-event media does not complete cinematic bindings.
- Load an authored assigned custom room, verify precedence over its built-in room, compatible native animation keys, audio isolation and repeated cleanup.

The game was closed during validation and installation. These interactions require a new game launch; no live game acceptance is claimed.

## Installation

Run `dotnet msbuild build.proj` to build, validate and install matching components with backups and SHA-256 verification. Configuration, creator content and profiles are preserved. Never stop or start servers or clients. If a required file is locked, report the blocked installation and let the user close the application. Installed assemblies take effect after the user manually restarts the affected application. See [build and deployment](build-deployment.md).

Paid dialogue services, compound-item handovers, automatic world placement and automatic loot spawning remain outside this update.
