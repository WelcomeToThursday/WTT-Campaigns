using System.Runtime.CompilerServices;
using EFT;
using HarmonyLib;
using SPT.Custom.CustomAI;

namespace WTT.Campaigns.Client.Encounters;

/// <summary>Retains SPT's successful brain selection for checkpoint actors only.</summary>
internal sealed class EncounterBrainChoice : IDisposable
{
    private sealed class Choice
    {
        internal WildSpawnType Role;
    }

    private static readonly ConditionalWeakTable<BotOwner, Choice> Choices = new();
    private static readonly Dictionary<string, EncounterBrainChoice> Active = new();
    private static bool _installed;
    private readonly string _id;
    private readonly WildSpawnType? _restore;

    internal EncounterBrainChoice(string id, WildSpawnType? restore)
    {
        if (!_installed)
        {
            var harmony = new Harmony("com.wtt.campaigns.checkpoint-brains");
            foreach (var name in new[] { "GetRandomisedPlayerScavType", "GetAssaultScavWildSpawnType", "GetPmcWildSpawnType" })
                harmony.Patch(
                    AccessTools.Method(typeof(AIBrainSpawnWeightAdjustment), name)
                        ?? throw new MissingMethodException(typeof(AIBrainSpawnWeightAdjustment).FullName, name),
                    prefix: new HarmonyMethod(typeof(EncounterBrainChoice), nameof(Prefix)),
                    postfix: new HarmonyMethod(typeof(EncounterBrainChoice), nameof(Postfix))
                );
            _installed = true;
        }
        _id = id;
        _restore = restore;
        Active.Add(id, this);
    }

    private static bool Prefix(BotOwner __0, ref WildSpawnType __result)
    {
        if (!Active.TryGetValue(__0.ProfileId, out var scope) || !scope._restore.HasValue)
            return true;
        __result = scope._restore.Value;
        return false;
    }

    private static void Postfix(BotOwner __0, WildSpawnType __result)
    {
        if (!Active.ContainsKey(__0.ProfileId))
            return;
        Choices.GetValue(__0, _ => new Choice()).Role = __result;
    }

    internal static WildSpawnType Capture(BotOwner bot) =>
        Choices.TryGetValue(bot, out var choice) ? choice.Role : bot.Profile.Info.Settings.Role;

    public void Dispose()
    {
        if (Active.TryGetValue(_id, out var current) && ReferenceEquals(current, this))
            Active.Remove(_id);
    }
}
