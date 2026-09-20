using System.Globalization;

namespace WTT.Campaigns.Client.Authoring.Views;

// Pure policies shared with offline checks; runtime controls use the same decisions.
internal static class EditorInteractionPolicy
{
    internal static (double Step, double Min, double Max, bool Integer) Numeric(string id)
    {
        foreach (var prefix in new[] { "Position", "Rotation", "Size", "MapPosition", "MapRotation", "MapSize", "Spline" })
            if (id.Length == prefix.Length + 1 && id.StartsWith(prefix, StringComparison.Ordinal) && "XYZ".IndexOf(id[id.Length - 1]) >= 0)
                return (prefix.Contains("Rotation") ? 1 : .1, prefix.Contains("Size") ? .001 : -float.MaxValue, float.MaxValue, false);
        return id switch
        {
            "CameraSpeed" => (.1, .25, 96, false),
            "SplineStrength" => (1, 0, 100, false),
            "ContainerChance" => (1, 0, 100, true),
            "ContainerQuantity" => (1, 1, 10000, true),
            "AiRosterCount" => (1, 1, 256, true),
            "AiWaveDelaySeconds" or "AiWaypointWaitSeconds" => (.1, 0, 3600, false),
            "Radius" => (.1, .001, float.MaxValue, false),
            "WeatherClouds" or "WeatherRain" or "WeatherFog" or "WeatherWind" or "WeatherThunder" => (1, 0, 100, false),
            _ => (0, 0, 0, false),
        };
    }

    internal static string ValidateNumber(string id, string text)
    {
        var rule = Numeric(id);
        if (rule.Step == 0)
            return "";
        var parsed = rule.Integer
            ? int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            : float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && float.IsFinite(number);
        if (!parsed)
            return rule.Integer ? "Enter a whole number." : "Enter a finite number using a decimal point.";
        var value = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        return value < rule.Min || value > rule.Max
            ? "Enter a value from "
                + rule.Min.ToString("G", CultureInfo.InvariantCulture)
                + " to "
                + rule.Max.ToString("G", CultureInfo.InvariantCulture)
                + "."
            : "";
    }

    internal static List<int> Matches(IReadOnlyList<string> labels, string query)
    {
        var result = new List<int>();
        for (var i = 0; i < labels.Count; i++)
            if (labels[i].IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                result.Add(i);
        return result;
    }

    internal static (float X, float Y, float Width, float Height) Popup(
        float left,
        float top,
        float bottom,
        float requestedWidth,
        float requestedHeight,
        float width,
        float height
    )
    {
        var w = Math.Min(Math.Max(250, requestedWidth), Math.Max(1, width - 16));
        var below = Math.Max(0, height - 8 - bottom);
        var above = Math.Max(0, top - 8);
        var up = requestedHeight > below && above > below;
        var h = Math.Max(1, Math.Min(requestedHeight, up ? above : below));
        return (Math.Clamp(left, 8, Math.Max(8, width - w - 8)), Math.Clamp(up ? top - h : bottom, 8, Math.Max(8, height - h - 8)), w, h);
    }

    internal static bool Expanded(IReadOnlyDictionary<string, bool> saved, string context, string section, bool defaultValue) =>
        saved.TryGetValue(context + "/" + section, out var value) ? value : defaultValue;

    internal static (string Before, string Local, string Remote, string After) Difference(string local, string remote)
    {
        var start = 0;
        while (start < local.Length && start < remote.Length && local[start] == remote[start])
            start++;
        var end = 0;
        while (
            end < local.Length - start && end < remote.Length - start && local[local.Length - end - 1] == remote[remote.Length - end - 1]
        )
            end++;
        return (
            local.Substring(0, start),
            local.Substring(start, local.Length - start - end),
            remote.Substring(start, remote.Length - start - end),
            local.Substring(local.Length - end)
        );
    }
}

internal sealed class EditorEditState
{
    internal string Committed { get; private set; } = "";
    internal string Error { get; private set; } = "";
    internal bool Invalid => Error.Length > 0;

    internal bool Accept(string id, string text)
    {
        Error = EditorInteractionPolicy.ValidateNumber(id, text);
        if (Invalid)
            return false;
        Committed = text;
        return true;
    }

    internal void Reset(string text)
    {
        Committed = text;
        Error = "";
    }
}
