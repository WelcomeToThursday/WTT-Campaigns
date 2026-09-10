using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using WTT.Campaigns.Server.Patches.Session;

namespace WTT.Campaigns.Server.Profiles;

[Injectable(InjectionType.Singleton, OnLoadOrder.SaveCallbacks - 1)]
public sealed class SeasonProfileStorageStartup(
    SeasonProfileSavePathPatch save,
    SeasonProfileLoadPathPatch load,
    SeasonProfileRemovePathPatch remove,
    SeasonProfileBackupPathPatch backup,
    SeasonProfileRestorePathPatch restore,
    SeasonProfileLoadedPatch loaded
) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        save.Enable();
        load.Enable();
        remove.Enable();
        backup.Enable();
        restore.Enable();
        loaded.Enable();
        return Task.CompletedTask;
    }
}
