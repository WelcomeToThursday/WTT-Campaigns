using System.Runtime.CompilerServices;
using EFT;
using EFT.HealthSystem;
using SeasonalPerks.Shared.Effects.Consumables;

namespace SeasonalPerks.Client.Patches.Health;

internal static class ConsumableHealthEffects
{
    private sealed class ActiveEffects
    {
        internal readonly Dictionary<string, ActiveHealthController.HealthBoost> Rates = new();
    }

    internal sealed class RegenerationRate(string kind, float rate)
    {
        internal readonly string Kind = kind;
        internal float Rate = rate;
        internal float Health
        {
            get { return Kind == "healthRegeneration" ? Rate : 0; }
        }

        internal float Energy
        {
            get { return Kind == "energyRecovery" ? Rate : 0; }
        }

        internal float Hydration
        {
            get { return Kind == "hydrationRecovery" ? Rate : 0; }
        }
    }

    private static readonly ConditionalWeakTable<ActiveHealthController, ActiveEffects> Controllers = new();
    internal static readonly ConditionalWeakTable<ActiveHealthController.HealthBoost, RegenerationRate> Regeneration = new();

    internal static bool IsLocalRaid(ActiveHealthController health)
    {
        return Plugin.InRaid
            && Plugin.SeasonalPlayer
            && health.IsAlive
            && Plugin.Player!.Profile.Info.Side != EPlayerSide.Savage
            && ReferenceEquals(health, Plugin.Player.ActiveHealthController);
    }

    internal static void Apply(ActiveHealthController health, ConsumableEffect effect, string templateId)
    {
        if (!IsLocalRaid(health))
        {
            return;
        }

        switch (effect.Kind)
        {
            case "pain":
                Refresh<ActiveHealthController.Pain>(health, effect.Duration, 1);
                return;
            case "tremor":
                Refresh<ActiveHealthController.Tremor>(health, effect.Duration);
                return;
            case "tunnelVision":
                Refresh<ActiveHealthController.TunnelVision>(health, effect.Duration, 1);
                return;
        }
        if (effect.Kind == "onPainkillers")
        {
            var existing = health.FindExistingEffect<ActiveHealthController.PainKiller>();
            if (existing != null)
            {
                existing.AddWorkTime(effect.Duration, true);
            }
            else
            {
                health.AddEffect<ActiveHealthController.PainKiller>(
                    EBodyPart.Common,
                    0,
                    effect.Duration,
                    0,
                    null,
                    e => e.StoreValues(templateId, effect.Duration)
                );
            }

            return;
        }

        var state = Controllers.GetValue(health, _ => new ActiveEffects());
        state.Rates.TryGetValue(effect.Kind, out var regeneration);
        if (regeneration != null && regeneration.State <= EEffectState.Started)
        {
            var rates = Regeneration.GetValue(regeneration, _ => new(effect.Kind, effect.Rate));
            rates.Rate = effect.Rate;
            regeneration.AddWorkTime(effect.Duration, true);
            regeneration.SetHealthRatesPerSecond(rates.Health, rates.Energy, rates.Hydration, 0);
            return;
        }

        // Use a separate, native-serializable carrier. Do not merge with a boss/mod
        // HealthBoost. Tag before NextState, which immediately invokes Started.
        regeneration = new ActiveHealthController.HealthBoost();
        regeneration.Init(health, EBodyPart.Common, 0, effect.Duration, 0, 0, health.UpdateTime);
        Regeneration.Add(regeneration, new(effect.Kind, effect.Rate));
        state.Rates[effect.Kind] = regeneration;
        regeneration.NextState();
    }

    private static void Refresh<T>(ActiveHealthController health, float duration, float? strength = null)
        where T : ActiveHealthController.Effect, new()
    {
        var existing = health.FindExistingEffect<T>();
        if (existing != null)
        {
            existing.AddWorkTime(duration, true);
        }
        else
        {
            health.AddEffect<T>(EBodyPart.Common, 0, duration, 0, strength);
        }
    }

    internal static void Tick(ActiveHealthController.HealthBoost effect, RegenerationRate rates, float deltaTime)
    {
        if (!IsLocalRaid(effect.HealthController))
        {
            effect.ForceRemove();
            return;
        }
        if (!ConsumableEffects.PositiveFinite(deltaTime))
        {
            return;
        }

        var controller = effect.HealthController;
        if (rates.Energy != 0)
        {
            controller.ChangeEnergy(rates.Energy * deltaTime);
        }

        if (rates.Hydration != 0)
        {
            controller.ChangeHydration(rates.Hydration * deltaTime);
        }

        var rate = rates.Health;
        if (rate == 0)
        {
            return;
        }

        var parts = HealthHelper.RealBodyParts;
        var start = UnityEngine.Random.Range(0, parts.Count);
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[(start + i) % parts.Count];
            var health = effect.HealthController;
            if (health.IsBodyPartDestroyed(part) || (rate > 0 && health.GetBodyPartHealth(part).AtMaximum))
            {
                continue;
            }
            // Live applies one total tick to the first eligible part in a randomly
            // rotated body-part list, with native clamping and no spillover.
            if (!health.GetBodyPartHealth(part).AtMinimum || rate > 0)
            {
                health.ChangeHealth(part, rate * deltaTime, DamageHelper.Existence);
            }

            if (rate < 0 && health.GetBodyPartHealth(part).AtMinimum)
            {
                health.DestroyBodyPart(part, EDamageType.Existence);
            }

            break;
        }
    }
}
