using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;

namespace WTT.Campaigns.Server.Hub;

[Injectable(InjectionType.Singleton, OnLoadOrder.PostLoad + 1000)]
public sealed class HubStartup(HubQuestService quests, HubGameplay gameplay) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        quests.Initialize();
        gameplay.Initialize();
        return Task.CompletedTask;
    }
}
