# Juice Time and Sailor's Nostalgia

Current workflow: [build, validate and always install](build-deployment.md). Never stop or start servers or clients. Any isolated-server results below are historical; those fixtures are retired.

Build 0.1.22 enables two more personal perks, bringing the implemented catalogue to 31/39. Each costs the captured two points and can be selected in the existing editor.

## Captured behavior

Both perks use the `allergy` effect family, but have four explicit item templates, four target slots and only one enabled sub-effect:

| Perk | Targets | Effect |
| --- | --- | --- |
| Juice Time (`69c406731d8aec4a2b0551bd`) | Apple, Grand, Vita and Russian Army pineapple juice: `57513f07245977207e26a311`, `57513f9324597720a7128161`, `57513fcc24597720a31c09a6`, `544fb62a4bdc2dfb738b4568` | Painkiller, 60 seconds |
| Sailor's Nostalgia (`69c40ae21d8aec4a2b0551c2`) | Humpback salmon, herring, pacific saury and sprats: `57347d5f245977448b40fa81`, `57347d9c245977448b40fa85`, `5673de654bdc2d180f8b456d`, `5bc9c29cd4351e003562b8a3` | Health regeneration, 2 HP/second for 30 seconds |

All eight templates exist in this SPT installation. `ConsumableEffects.Describe` admits only this deterministic subset: explicit template filters, all targets selected, one supported positive-duration sub-effect. It does not enable the entire `allergy` family. Build 0.1.23 implements Allergic separately; see [random targets and symptoms](allergy-container.md).

Selections save the four targets per perk under `SeasonalPerkEffectParameters.allergy[perkId].targetItems`, using the captured schema. Saving or reselecting does not reroll targets. Removing a perk removes only its parameter entry; unrelated effect parameters remain intact. Target parameters share the existing atomic selection save and rollback.

## Client integration

`ConsumableUsePatch` observes `ActiveHealthController.MedEffect.RegularUpdate` before and after native resource consumption. The first positive food/drink resource decrease triggers the matching perk once for that use operation. An animation that has not consumed anything grants nothing. An interrupted operation cannot newly trigger the perk; interruption after a consuming tick does not revoke an already granted buff. Later ticks of the same use cannot extend the buff. A new partial use of the same bottle can refresh it. Existing resource arithmetic, including Diet, is unchanged.

Both effects require the active seasonal PMC's own health controller, an actual raid and a living non-Scav player. AI, normal PMC, Scav, hideout and stash use do not gain these raid buffs. Native stash food/resource handling is preserved.

Juice Time adds or refreshes a native `PainKiller` effect on `EBodyPart.Common` with no delay or residue. Its work timer resets to 60 seconds rather than accumulating extra time. Ordinary medicine effects on other body parts retain their behavior.

Sailor's Nostalgia uses a separately created, native `HealthBoost` instance as a compatible timer/network carrier. Weak references identify only seasonal carriers; ordinary HealthBoost effects are unchanged. The tagged instance reports a total 2 HP/second health rate and replaces the built-in per-limb healing loop with the captured distribution: begin at a random body-part offset, skip destroyed/full parts, heal the first eligible part and stop. Native `ChangeHealth` clamps at that part's maximum; unused healing does not spill into another limb. This is a total rate, not 2 HP/second on every body part.

Reusing canned food refreshes the existing seasonal timer to 30 seconds without creating another regeneration effect. Native effect updates clamp the final tick to the remaining duration and clear rates when the timer ends. The carrier is not `IRestorable`, so it cannot be serialized as an unrelated persistent HealthBoost. Actual healed HP is part of the normal raid health result. Temporary effects are not carried into a new health controller.

## Native evidence

Evidence comes from the locally supplied live GameAssembly with SHA-256 `94ae9b20597624e9ee737ab2c64159dd3ef0680c71d6f106b02fe9ca3e362c8a` and the installed SPT client assembly:

- Live `MedEffect.RegularUpdate`, RVA `0x1993DC0`, records whether resources were consumed and calls item-use effects once, unless interrupted/already applied.
- Live `ApplyPerkItemUseEffect`, RVA `0x2BDF630`, takes all enabled sub-effects when the slot count is at least their count. These two perks each have only one. The extra captured `appliedRandomEffectCount` field is preserved without overriding this native behavior.
- `ApplyPerkPainkiller`, RVA `0x2D51CA0`, uses Common; `AddOrRefreshPerkEffect`, generic RVA `0x1C178C0`, resets the existing work timer.
- `ApplyDistributedHealthRate`, RVA `0x15FDA20`, chooses a random body-part starting index, skips destroyed/full parts and exits after healing an eligible part. Its continuation blocks extend beyond the first Windows unwind range; inspecting only that first range truncates the method.
- The installed `Effect.ManualUpdate` clamps delta time before `RegularUpdate`. `HealthHelper.EffectTypeCode` accepts the native HealthBoost carrier, and `ActiveHealthController.Store` persists only `IRestorable` effects.

Native reports and disassembly remain under ignored `Research/native`; no game binaries are added to the package.

## Validation and limits

- Release/Debug builds pass with no warnings or errors.
- 214 shared/native-assembly assertions pass, including each captured target, duration/rate, unsupported shape rejection, stable parameter saves/removal, once-per-use receipts, partial use, interruption, Diet, native field/argument bindings and timer/store compatibility.
- 55 isolated-server checks pass: selection/budget handling, fixed targets, unsupported Allergic rejection, use of all eight consumables by normal and seasonal PMCs, unchanged native resource use, no instant stash healing, Diet coexistence, character switching and unchanged shared templates.
- Five restart checks pass for target parameters, selections, both inventories and the shared item database. The existing 66-check server integration suite also passes.

The server fixture described by earlier validation is retired from the workflow. Use the offline checks and mandatory installation in [build and deployment](build-deployment.md); never stop or start any server or client.

An in-game disposable-account session is still required to validate actual animations, pain suppression, timed HP totals, effect display and raid-end health persistence. These tests do not claim an executed raid or exact live UI parity. No new regeneration icon or native triggered-perk toast is backported in this build.
