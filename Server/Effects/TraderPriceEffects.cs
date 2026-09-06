using SeasonalPerks.Shared.Effects;
using SeasonalPerks.Shared.Effects.Trading;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Ragfair;
using SPTarkov.Server.Core.Utils.Cloners;

namespace SeasonalPerks.Server.Effects;

[Injectable(InjectionType.Singleton)]
public sealed class TraderPriceEffects(ICloner cloner)
{
    internal sealed class SearchScope(RuntimeEffects effects)
    {
        internal RuntimeEffects Effects { get; } = effects;
        internal Dictionary<RagfairOffer, RagfairOffer> Offers { get; } = new(ReferenceEqualityComparer.Instance);
    }

    internal static readonly AsyncLocal<SearchScope?> Search = new();

    internal static bool Visible(RagfairOffer offer) => Search.Value?.Effects.Has("flea_market_npc_only") != true || offer.IsTraderOffer();

    internal TraderAssort Assort(TraderAssort original, decimal multiplier)
    {
        if (multiplier == 1m)
            return original;
        var result = cloner.Clone(original) ?? throw new InvalidOperationException("Could not copy trader assortment.");
        foreach (var requirement in result.BarterScheme.Values.SelectMany(s => s).SelectMany(s => s))
            if (requirement.Count is double count)
                requirement.Count = TraderPricing.Scale(count, multiplier);
        return result;
    }

    internal RagfairOffer Offer(RagfairOffer original)
    {
        var scope = Search.Value;
        if (scope == null || !original.IsTraderOffer())
            return original;
        if (scope.Offers.TryGetValue(original, out var cached))
            return cached;
        var multiplier = scope.Effects.TraderMultiplier(original.User.Id.ToString(), "buy");
        if (multiplier == 1m)
            return original;
        var result = cloner.Clone(original) ?? throw new InvalidOperationException("Could not copy flea offer.");
        foreach (var requirement in result.Requirements ?? [])
            if (requirement.Count is double count)
                requirement.Count = TraderPricing.Scale(count, multiplier);
        if (result.RequirementsCost is double cost)
            result.RequirementsCost = TraderPricing.Scale(cost, multiplier);
        if (result.SummaryCost is double summary)
            result.SummaryCost = TraderPricing.Scale(summary, multiplier);
        scope.Offers[original] = result;
        scope.Offers[result] = result;
        return result;
    }
}
