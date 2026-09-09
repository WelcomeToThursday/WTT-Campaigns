# Bushborne and No FiR for Hideout — build 0.1.19

Current workflow: [build, validate and always install](build-deployment.md). Never stop or start servers or clients. Any isolated-server results below are historical; those fixtures are retired.

These two entries bring the implemented catalogue to 25 of 39. Both require the updated client and server. Automated checks pass; actual movement, audio and hideout interaction still need an in-game session.

## Bushborne

The captured perk spends five points and supplies `multiplicatorPrimary = 0.25` for slowdown and `multiplicatorSecondary = 0.25` for noise. Native `BushInteractionMultipliersPerkEffect.TryApply` at RVA `0x395CEB0` maps these to runtime stats 14 and 13 respectively. The checked GameAssembly SHA-256 is `94ae9b20597624e9ee737ab2c64159dd3ef0680c71d6f106b02fe9ca3e362c8a`.

Native `MovementContext.RefreshObstacleRestrictions` at RVA `0x6C4C10` scales the lost speed: `1 - (1 - originalLimit) * multiplier`. SPT's 0.2 obstacle limit therefore becomes 0.8 while the seasonal PMC occupies a tree trigger. The patch preserves obstacle restrictions on sprinting, jumping and prone movement, and leaves other speed-limit causes alone. A swamp without a tree trigger retains its original slowdown.

SPT lacks the newer game's bush-zone counter. The backport tracks tree/player collider pairs independently of audio-source allocation or camera distance. Duplicate entries and overlapping bushes remain distinct, exits refresh the native restrictions, and disabled/destroyed triggers are removed on the next movement tick. The weak-keyed tracker expires with the movement context.

The sound hooks scale only this PMC's tree-interaction range and randomized playback volume by 0.25. Entry, stay and playback use the same range factor. They leave the shared sound bank, volume history, normal characters, bots and foliage visibility calculations intact. Native `TreeInteractive.GetEffectiveRolloff` at RVA `0x3A5CB20` multiplies the sound-bank range by SoundRadius and BushNoiseMultiplier; playback at RVA `0x3DCE090` also incorporates that range ratio into volume. The backport applies the additional perk factor to SPT's existing sound calculations.

## No FiR for Hideout

The captured common rule is `hideout_fir` with `mode = not_require`. The client overrides `EFT.Hideout.ItemRequirement.IsSpawnedInSession` only after the seasonal session's actual loaded profile matches the selected identity. This getter supplies both item suitability and the requirement panel, so the displayed requirement agrees with available materials. Template, locked-item, functional-weapon and encoded-item checks remain native. Actual item FiR flags are never changed.

SPT 4.1.3's native `HideoutController.StartUpgrade` already accepts owned non-FiR materials. It removes submitted items and starts construction without a FiR check; no additional server bypass or global database edit is required. This existing server behavior also applies to direct requests from normal accounts; normal client requirement checks remain unchanged by this mod.

Fresh configurations enable supported common rules automatically. For an existing configuration, add `69ce5eb3e4b79de94a0d78c8` to `EnabledCommonIds`, let the user manually restart the server, and save the seasonal perk selection again. Removing a common rule likewise takes effect on the next successful selection save. Existing configuration choices are preserved on upgrade.

## Validation

- 117 shared/assembly assertions cover support count, captured multipliers, asymmetric primary/secondary fields, removal, session identity, trigger arguments and the common FiR getter used by suitability and display.
- All three bush sound transpilers execute against the actual installed game instructions, preserve branch labels and reject a missing range site. All three existing item-resource transpilers still pass.
- The existing 66 isolated server integration checks pass.
- Ten new isolated server checks cover perk selection, non-FiR materials consumed by a real water-collector upgrade, construction timing, unchanged normal PMC/Scav inventories, unchanged remaining FiR flags and an unchanged shared hideout database.
- Three checks after logout/save and server restart confirm consumed materials, construction state and both saved perk selections persist.

The server fixture described by earlier validation is retired from the workflow. Use the offline checks and mandatory installation in [build and deployment](build-deployment.md); never stop or start any server or client.

For in-game validation, compare bush walking speed and sound with Bushborne selected and removed, enter overlapping bushes, exit in either order, and confirm a swamp outside a tree trigger stays unchanged. Switch to the normal PMC and repeat. With No FiR enabled, confirm a non-FiR hideout material counts in the requirement panel, complete an upgrade, and verify that normal-character, barter and quest FiR requirements remain unchanged.
