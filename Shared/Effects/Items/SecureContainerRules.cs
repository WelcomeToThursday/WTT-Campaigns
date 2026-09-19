namespace WTT.Campaigns.Shared.Effects.Items;

public static class SecureContainerRules
{
    public const string EffectId = "pouch_item_filter_restrict";
    public const string Category = "5448bf274bdc2dfc2f8b456a";
    public const string Message = "Broken Secure Container prevents placing this item or its contents in the secure container.";

    public static bool Allows(RuntimeEffects effects, string templateId, IEnumerable<string> ancestors)
    {
        return effects.Matching(EffectId).AsValueEnumerable().All(e => RuntimeEffects.MatchesFilter(e.ItemFilter, templateId, ancestors));
    }
}
