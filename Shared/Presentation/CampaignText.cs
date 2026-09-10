using System.Text.RegularExpressions;

namespace WTT.Campaigns.Shared.Presentation;

/// <summary>Updates display copy without changing protocol keys or persisted campaign identities.</summary>
public static class CampaignText
{
    private static readonly Regex Terms = new Regex(
        @"\b(seasonal|seasons|season)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );

    public static string Display(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }
        return Terms.Replace(
            value,
            match =>
            {
                var replacement = match.Value.Equals("seasons", System.StringComparison.OrdinalIgnoreCase) ? "campaigns" : "campaign";
                return match.Value == match.Value.ToUpperInvariant() ? replacement.ToUpperInvariant()
                    : char.IsUpper(match.Value[0]) ? char.ToUpperInvariant(replacement[0]) + replacement.Substring(1)
                    : replacement;
            }
        );
    }
}
