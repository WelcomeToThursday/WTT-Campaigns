using WTT.Campaigns.Shared.Seasons;

namespace WTT.Campaigns.Shared.Missions;

/// <summary>Stable server-authored icon choices shared by Creator and the notification renderer.</summary>
public static class MissionNotificationIcons
{
    public const string Default = "quest:Completion",
        Checkpoint = "quest:Exploration";
    public static readonly IReadOnlyDictionary<string, string> Choices = new Dictionary<string, string>
    {
        ["quest:PickUp"] = "Pick up",
        ["quest:Elimination"] = "Elimination",
        ["quest:Discover"] = "Discover",
        [Default] = "Completion",
        [Checkpoint] = "Exploration",
        ["quest:Levelling"] = "Levelling",
        ["quest:Experience"] = "Experience",
        ["quest:Standing"] = "Standing",
        ["quest:Loyalty"] = "Loyalty",
        ["quest:Merchant"] = "Merchant",
        ["quest:Skill"] = "Skill",
        ["quest:Multi"] = "Multiple tasks",
        ["quest:WeaponAssembly"] = "Weapon assembly",
        ["quest:Daily"] = "Daily",
    };

    public static bool IsValid(string? icon) =>
        icon != null && (icon.Length == 0 || Choices.ContainsKey(icon) || SeasonValidator.IsId(icon));

    public static string Resolve(string? icon, string fallback = Default) =>
        !string.IsNullOrEmpty(icon) && IsValid(icon) ? icon! : fallback;
}
