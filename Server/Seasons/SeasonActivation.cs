using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;

namespace SeasonalPerks.Server.Seasons;

[Injectable(InjectionType.Singleton, OnLoadOrder.PostLoad + 500)]
public sealed class SeasonActivation(SeasonContentService content) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        content.CompleteActivation();
        return Task.CompletedTask;
    }
}
