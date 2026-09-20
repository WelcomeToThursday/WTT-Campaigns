namespace WTT.Campaigns.Client.Authoring.Navigation;

internal readonly struct NavigationBuildStamp : IEquatable<NavigationBuildStamp>
{
    internal readonly string Session,
        Layout;
    internal readonly long Content,
        Geometry;

    internal NavigationBuildStamp(string session, string layout, long content, long geometry)
    {
        Session = session;
        Layout = layout;
        Content = content;
        Geometry = geometry;
    }

    public bool Equals(NavigationBuildStamp other) =>
        Session == other.Session && Layout == other.Layout && Content == other.Content && Geometry == other.Geometry;

    public override bool Equals(object? obj) => obj is NavigationBuildStamp other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Session, Layout, Content, Geometry);

    internal string ChangesSince(NavigationBuildStamp previous)
    {
        var changes = new List<string>();
        if (Session != previous.Session)
            changes.Add("editor session changed");
        if (Layout != previous.Layout)
            changes.Add("selected layout changed");
        if (Content != previous.Content)
            changes.Add($"layout content revision {previous.Content} → {Content}");
        if (Geometry != previous.Geometry)
            changes.Add($"scene geometry revision {previous.Geometry} → {Geometry}");
        return string.Join("; ", changes);
    }
}
