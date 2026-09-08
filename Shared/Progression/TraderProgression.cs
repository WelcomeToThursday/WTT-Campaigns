using System;
using System.Collections.Generic;
using SeasonalPerks.Shared.Native;

namespace SeasonalPerks.Shared.Progression;

public sealed class TraderProgression
{
    public int Version { get; set; } = 1;
    public Dictionary<string, List<LoyaltyThreshold>> Traders { get; set; } = new();
    public Dictionary<string, TaskProgression> Quests { get; set; } = new();

    public static int Loyalty(IReadOnlyList<LoyaltyThreshold> thresholds, int level, double standing)
    {
        var result = 1;
        for (var i = 0; i < thresholds.Count; i++)
        {
            if (level >= thresholds[i].Level && standing >= thresholds[i].Standing)
            {
                result = i + 1;
            }
        }
        return result;
    }

    public static bool Compare(double current, double required, string? comparison)
    {
        return comparison switch
        {
            ">=" => current >= required,
            ">" => current > required,
            "<=" => current <= required,
            "<" => current < required,
            "=" or "==" => Math.Abs(current - required) < 0.00000001,
            "!=" => Math.Abs(current - required) >= 0.00000001,
            _ => false,
        };
    }
}

public sealed class LoyaltyThreshold
{
    public int Level { get; set; }
    public double Standing { get; set; }
}

public sealed class TaskProgression
{
    public string TraderId { get; set; } = "";
    public int Tier { get; set; }
    public bool UseBetaStart { get; set; }
    public List<NativeCondition> Start { get; set; } = new();
    public Dictionary<string, List<NativeReward>> Reputation { get; set; } = new();
}

public sealed class ProgressionMetadata
{
    public int Version { get; set; } = 1;
    public Dictionary<string, TaskTier> Quests { get; set; } = new();
    public List<string> Traders { get; set; } = new();
}

public sealed class TaskTier
{
    public string TraderId { get; set; } = "";
    public int Tier { get; set; }
}
