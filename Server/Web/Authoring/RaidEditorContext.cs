using SeasonalPerks.Shared.Authoring;

namespace SeasonalPerks.Server.Web.Authoring;

public sealed class RaidEditorContext
{
    public bool Connected { get; set; }
    public Func<CaptureTask, Task> Request { get; set; } = _ => Task.CompletedTask;
}
