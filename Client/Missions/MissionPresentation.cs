using WTT.Campaigns.Shared.Missions;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Missions;

/// <summary>Presentation derived only from acknowledged mission snapshots.</summary>
internal sealed class MissionPresentation
{
    internal sealed class Row
    {
        internal string Label = "",
            Text = "",
            Progress = "",
            Status = "";
    }

    internal readonly struct Notice
    {
        internal readonly string Title,
            Description,
            Status,
            Icon;

        internal Notice(string title, string description, string status, string icon) =>
            (Title, Description, Status, Icon) = (title, description, status, icon);
    }

    private readonly Dictionary<string, string> _objectives = new();
    private int _checkpoint;

    internal void Reset(MissionRun run)
    {
        _objectives.Clear();
        foreach (var pair in run.Logic.Objectives)
            _objectives[pair.Key] = pair.Value.Status;
        _checkpoint = run.NextCheckpointIndex;
    }

    internal IReadOnlyList<Notice> Accept(MissionDefinition mission, MapLayout layout, MissionRun run)
    {
        var notices = new List<Notice>();
        for (var i = _checkpoint; i < Math.Min(run.NextCheckpointIndex, layout.Checkpoints.Count); i++)
            notices.Add(
                new Notice(
                    layout.Checkpoints[i].Name,
                    "Checkpoint reached",
                    "Complete",
                    MissionNotificationIcons.Resolve(mission.CheckpointNotificationIcon, MissionNotificationIcons.Checkpoint)
                )
            );
        _checkpoint = run.NextCheckpointIndex;
        foreach (var objective in mission.Objectives)
        {
            var status = run.Logic.Objectives.TryGetValue(objective.Id, out var progress) ? progress.Status : "Pending";
            var previous = _objectives.TryGetValue(objective.Id, out var seen) ? seen : "Pending";
            _objectives[objective.Id] = status;
            if (status == previous)
                continue;
            var icon = MissionNotificationIcons.Resolve(
                objective.NotificationIcon,
                MissionNotificationIcons.Resolve(mission.NotificationIcon)
            );
            switch (status)
            {
                case "Active":
                    notices.Add(new Notice(objective.Name, "Objective started", "Started", icon));
                    break;
                case "Completed":
                    notices.Add(new Notice(objective.Name, "Objective completed", "Complete", icon));
                    break;
                case "Failed":
                    notices.Add(new Notice(objective.Name, "Objective failed", "Failed", icon));
                    break;
            }
        }
        return notices;
    }

    internal static List<Row> Rows(MissionDefinition mission, MapLayout layout, MissionRun run)
    {
        var rows = new List<Row>();
        for (var i = 0; i < mission.Objectives.Count; i++)
        {
            var objective = mission.Objectives[i];
            if (!run.Logic.Objectives.TryGetValue(objective.Id, out var progress) || progress.Status is not ("Active" or "Failed"))
                continue;
            rows.Add(
                new Row
                {
                    Label = "OBJ" + (i + 1).ToString("00"),
                    Text = objective.Name + (objective.Required ? "" : " (optional)"),
                    Status = progress.Status,
                    Progress =
                        progress.Status == "Failed" ? "Failed"
                        : objective.Type == MissionObjective.Defend ? $"{progress.Seconds:0}/{objective.Seconds:0}s"
                        : objective.Type is MissionObjective.Eliminate or MissionObjective.Target
                            ? $"{progress.Count}/{MissionLogic.Expected(layout, objective)}"
                        : "Active",
                }
            );
        }
        var next = Math.Max(0, run.NextCheckpointIndex);
        var checkpoint = next < layout.Checkpoints.Count ? layout.Checkpoints[next] : null;
        var canAdvance = MissionLogic.CanAdvance(mission, run.Logic, checkpoint?.Id ?? "", out _);
        rows.Add(
            new Row
            {
                Label = checkpoint == null ? "EXIT" : "CP" + (next + 1).ToString("00"),
                Text = checkpoint?.Name ?? layout.Exit?.Name ?? "Mission exit",
                Progress =
                    run.ExitReached ? "Reached"
                    : canAdvance ? "Reach"
                    : "Locked",
                Status =
                    run.ExitReached ? "Complete"
                    : canAdvance ? "Active"
                    : "Pending",
            }
        );
        return rows;
    }
}
