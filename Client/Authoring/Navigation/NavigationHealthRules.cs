namespace WTT.Campaigns.Client.Authoring.Navigation;

[Flags]
internal enum NavigationHealthIssue
{
    None = 0,
    Support = 1,
    Clearance = 2,
    Disconnected = 4,
    Slope = 8,
    Narrow = 16,
    Height = 32,
    Unchecked = 64,
}

internal static class NavigationHealthRules
{
    // These are diagnostic observations, never instructions to change navigation.
    internal static NavigationHealthIssue Classify(
        bool supported,
        bool clearance,
        bool connected,
        float slope,
        float allowedSlope,
        bool narrow,
        float heightDifference,
        float allowedStep
    )
    {
        var result = NavigationHealthIssue.None;
        if (!supported)
            result |= NavigationHealthIssue.Support;
        if (!clearance)
            result |= NavigationHealthIssue.Clearance;
        if (!connected)
            result |= NavigationHealthIssue.Disconnected;
        if (slope > allowedSlope)
            result |= NavigationHealthIssue.Slope;
        if (narrow)
            result |= NavigationHealthIssue.Narrow;
        if (supported && heightDifference > Math.Max(.15f, allowedStep))
            result |= NavigationHealthIssue.Height;
        return result;
    }
}
