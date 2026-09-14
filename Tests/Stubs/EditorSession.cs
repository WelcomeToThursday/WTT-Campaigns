// Offline session tests use only an in-memory transport and a temporary recovery folder.
namespace BepInEx
{
    internal static class Paths
    {
        internal static string ConfigPath { get; set; } = "";
    }
}

namespace WTT.Campaigns.Client
{
    internal static class Plugin
    {
        internal static void LogInfo(string message) { }

        internal static void Error(Exception error) => throw new InvalidOperationException("Session callback failed", error);
    }
}

namespace WTT.Campaigns.Client.Authoring
{
    internal static class EditorMode
    {
        internal static bool Ready => false;
        internal static string SessionId => "";
    }
}
