using WTT.Campaigns.Shared.Authoring;

namespace WTT.Campaigns.Server.Web.Authoring;

public sealed class RaidEditorContext
{
    public bool Connected { get; set; }
    public Func<CaptureTask, Task> Request { get; set; } = _ => Task.CompletedTask;
}
