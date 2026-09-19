using EFT;
using WTT.Campaigns.Shared.Spatial;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("WTT-Campaigns.AI")]

namespace WTT.Campaigns.Client.Encounters;

// Keep this boundary free of optional-mod types: EFT enumerates loaded assembly types at startup.
internal interface IEncounterAiRuntime
{
    string Status { get; }
    void Tick();
    void Add(BotOwner bot, string squad, string route, SpatialCapture spawn);
    void Remove(BotOwner bot);
    string Describe(BotOwner bot);
    void Reset();
    Dictionary<string, PatrolCheckpoint> CapturePatrols();
    void RestorePatrols(Dictionary<string, PatrolCheckpoint> patrols);
}
