using EFT;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Encounters;

internal sealed class EncounterAiRuntime(MapLayout layout) : IEncounterAiRuntime
{
    private readonly EncounterPatrolRuntime _patrol = new(layout);
    private readonly EncounterHoldRuntime _holds = new();
    private readonly EncounterCoverRuntime _cover = new();

    public string Status => _patrol.Status;

    public void Tick() => _patrol.Tick();
    public Dictionary<string, PatrolCheckpoint> CapturePatrols() => _patrol.Capture();
    public void RestorePatrols(Dictionary<string, PatrolCheckpoint> patrols) => _patrol.Restore(patrols);

    public void Add(BotOwner bot, string squad, string route, SpatialCapture spawn)
    {
        _cover.Add(bot);
        if (route.Length > 0)
            _patrol.Add(bot, squad, route);
        else
            _holds.Add(bot, spawn);
    }

    public string Describe(BotOwner bot) => EncounterPatrolRuntime.Describe(bot) + "; " + EncounterCoverRuntime.Describe(bot);

    public void Reset()
    {
        try
        {
            _patrol.Reset();
        }
        finally
        {
            try
            {
                _holds.Reset();
            }
            finally
            {
                _cover.Reset();
            }
        }
    }
}
