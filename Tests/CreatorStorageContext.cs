namespace WTT.Campaigns.Server;

// Repository tests supply their own temporary mod directory. This metadata shim
// keeps the filesystem service testable without booting or referencing the SPT host.
internal static class Metadata
{
    public static string DirectoryPath
    {
        get { throw new InvalidOperationException("Tests must supply an isolated mod directory."); }
    }
}
