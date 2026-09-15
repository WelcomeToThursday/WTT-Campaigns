using System.Threading;

namespace WTT.Campaigns.Server.Missions;

/// <summary>
/// Carries the server-issued mission launch marker through SPT's asynchronous
/// HTTP dispatcher. The marker is scoped to one native start request and is
/// restored when that dispatcher completes, so an ordinary request can never
/// inherit a previous mission context.
/// </summary>
public static class MissionLaunchContext
{
    public const string HeaderName = "X-WTT-Mission-Run";

    private static readonly AsyncLocal<Scope?> Slot = new();

    public static Scope? Current => Slot.Value;

    internal static Scope Push(string sessionId, bool headerPresent, string runId)
    {
        var scope = new Scope(sessionId, headerPresent, runId, Slot.Value);
        Slot.Value = scope;
        return scope;
    }

    public sealed class Scope
    {
        private readonly Scope? _previous;

        internal Scope(string sessionId, bool headerPresent, string runId, Scope? previous)
        {
            SessionId = sessionId;
            HeaderPresent = headerPresent;
            RunId = runId;
            _previous = previous;
        }

        public string SessionId { get; }
        public bool HeaderPresent { get; }
        public string RunId { get; }

        internal void Restore()
        {
            Slot.Value = _previous;
        }
    }
}
