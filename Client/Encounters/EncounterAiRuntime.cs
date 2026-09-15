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
    string Describe(BotOwner bot);
    void Reset();
}
