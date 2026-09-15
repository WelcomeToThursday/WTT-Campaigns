using System.Globalization;

namespace WTT.Campaigns.Client.Authoring.Rendering;

internal static class EnvironmentValues
{
    internal static bool TryHour(string text, out float hour)
    {
        hour = 0;
        if (
            !TimeSpan.TryParseExact(text.Trim(), new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out var time)
            || time.TotalHours < 0
            || time.TotalHours >= 24
        )
            return false;
        hour = (float)time.TotalHours;
        return true;
    }

    internal static bool TryPercent(string text, out float value) =>
        float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && !float.IsNaN(value)
        && value >= 0
        && value <= 100;
}
