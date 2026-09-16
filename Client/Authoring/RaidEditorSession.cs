using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Seasons;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring;

internal sealed class RaidEditorSession
{
    private sealed class Recovery
    {
        public string DraftId = "";
        public long Revision;
        public SeasonDefinition? Baseline,
            Definition;
    }

    internal string ClientId { get; } = Guid.NewGuid().ToString("N");
    internal string RaidId { get; } = Guid.NewGuid().ToString("N");
    internal string Location { get; }
    internal List<string> NativeZoneIds = new(),
        Scenes = new();
    internal string DraftId = "",
        Grant = "",
        Status = "Waiting for the web editor to connect a draft";
    internal long Revision;
    internal long ContentVersion;
    internal SeasonDefinition? Baseline,
        Definition;
    internal AuthoringResponse? Conflict;
    internal List<CaptureTask> Tasks = new();
    internal bool Busy,
        Hold,
        Previewing,
        Retired,
        Contacted;
    internal event Action? Changed;
    private AuthoringRequest? _pending;
    private readonly CancellationTokenSource _socketLifetime = new();
    private readonly Stack<SeasonDefinition> _undo = new(),
        _redo = new();
    private readonly string _recoveryRoot = Path.Combine(BepInEx.Paths.ConfigPath, "WTT-Campaigns", "raid-authoring");

    // Read by the frame loop. Compare only when committed content or its baseline
    // changes; serializing the entire campaign here allocates two JSON trees per frame.
    // Drag previews are held until Edit commits them or cancellation restores them.
    internal bool Dirty { get; private set; }

    private void RefreshDirty() =>
        Dirty =
            Definition != null
            && !JToken.DeepEquals(JObject.FromObject(Definition), Baseline == null ? null : JObject.FromObject(Baseline));

    internal RaidEditorSession(string location) => Location = location;

    internal static T Copy<T>(T value)
    {
        return SeasonCompiler.Copy(value);
    }

    private string RecoveryPath
    {
        get { return Path.Combine(_recoveryRoot, DraftId + ".json"); }
    }

    internal void Edit(Action<SeasonDefinition> action)
    {
        if (Definition == null || Conflict != null || Retired || Previewing)
        {
            return;
        }

        var before = Copy(Definition);
        try
        {
            action(Definition);
        }
        catch
        {
            Definition = before;
            throw;
        }
        if (Definition.MapLayouts.Count > 0)
            Definition.FormatVersion = Math.Max(
                Definition.FormatVersion,
                WTT.Campaigns.Shared.Spatial.MapLayoutRules.Format(Definition.MapLayouts)
            );
        if (Definition.Zones.Count > 0 || Definition.Captures.Count > 0)
        {
            Definition.FormatVersion = Math.Max(Definition.FormatVersion, 2);
        }

        if (JToken.DeepEquals(JObject.FromObject(before), JObject.FromObject(Definition)))
        {
            return;
        }

        ContentVersion++;
        _undo.Push(before);
        _redo.Clear();
        RefreshDirty();
        Persist();
        Changed?.Invoke();
    }

    internal void Undo(bool redo)
    {
        if (Definition == null || Conflict != null || Previewing)
        {
            return;
        }

        var from = redo ? _redo : _undo;
        var to = redo ? _undo : _redo;
        if (from.Count == 0)
        {
            return;
        }

        to.Push(Copy(Definition));
        Definition = from.Pop();
        ContentVersion++;
        RefreshDirty();
        Persist();
        Changed?.Invoke();
    }

    internal void Persist()
    {
        if (Definition == null || DraftId.Length == 0)
        {
            return;
        }

        Directory.CreateDirectory(_recoveryRoot);
        var bytes = JsonConvert.SerializeObject(
            new Recovery
            {
                DraftId = DraftId,
                Revision = Revision,
                Baseline = Baseline,
                Definition = Definition,
            },
            Formatting.Indented
        );
        File.WriteAllText(RecoveryPath + ".tmp", bytes);
        if (File.Exists(RecoveryPath))
        {
            File.Replace(RecoveryPath + ".tmp", RecoveryPath, null);
        }
        else
        {
            File.Move(RecoveryPath + ".tmp", RecoveryPath);
        }
    }

    internal AuthoringRequest Request()
    {
        return new()
        {
            Version = EditorMode.Ready ? 6 : 1,
            EditorSessionId = EditorMode.SessionId,
            ClientId = ClientId,
            RaidId = RaidId,
            Location = Location,
            NativeZoneIds = NativeZoneIds,
            Scenes = Scenes,
            Enabled = !Retired,
            Grant = Grant,
            DraftId = DraftId,
            Revision = Revision,
            SupportsZoneLayouts = true,
            OperationId = Guid.NewGuid().ToString("N"),
        };
    }

    private async Task<AuthoringResponse> Send(string route, AuthoringRequest request)
    {
        var response = await AuthoringSocket.Shared.Send(route, request, _socketLifetime.Token);
        if (response.Version != 1)
        {
            throw new InvalidOperationException("Update both authoring components together.");
        }

        if (response.Error != null)
        {
            throw new InvalidOperationException(response.Error);
        }

        return response;
    }

    internal async Task Tick()
    {
        if (Busy || Retired)
        {
            return;
        }

        Busy = true;
        Contacted = false;
        var previousContent = ContentVersion;
        var previousStatus = Status;
        var previousConflict = Conflict;
        var previousTasks = Tasks;
        try
        {
            var response = await Send("poll", Request());
            if (Retired)
            {
                return;
            }

            Contacted = true;
            if (response.Grant.Length == 0)
            {
                Persist();
                Grant = "";
                DraftId = "";
                if (Definition != null || Baseline != null)
                    ContentVersion++;
                Definition = Baseline = null;
                Dirty = false;
                _pending = null;
                Status = "Enable and connect this raid in the web campaign editor";
                return;
            }
            if (response.Grant != Grant || response.DraftId != DraftId)
            {
                Persist();
                _pending = null;
                _undo.Clear();
                _redo.Clear();
                Conflict = null;
                Grant = response.Grant;
                DraftId = response.DraftId;
                Baseline = Copy(response.Definition!);
                Definition = Copy(Baseline);
                RefreshDirty();
                ContentVersion++;
                Revision = response.Revision;
                if (File.Exists(RecoveryPath))
                {
                    var recovery = JsonConvert.DeserializeObject<Recovery>(File.ReadAllText(RecoveryPath));
                    if (recovery?.Baseline != null && recovery.Definition != null && recovery.DraftId == DraftId)
                    {
                        Baseline = recovery.Baseline;
                        Definition = recovery.Definition;
                        MergeRemote(response);
                    }
                }
            }
            else if (response.Definition != null && !Hold && _pending == null)
            {
                MergeRemote(response);
            }

            Tasks = response.Tasks;
            if (!Hold && Conflict == null && (Dirty || _pending != null))
            {
                _pending ??= Request();
                _pending.Definition ??= Copy(Definition!);
                var sent = _pending;
                response = await Send("submit", sent);
                if (Retired)
                {
                    return;
                }

                _pending = null;
                AcceptSubmit(response, sent.Definition!);
            }
            Status =
                Conflict != null ? "Resolve conflicting edits"
                : Dirty ? "Unsynchronized draft edits"
                : "Draft synchronized · revision " + Revision;
        }
        catch (Exception e)
        {
            if (Retired)
            {
                return;
            }
            if (e is InvalidOperationException)
            {
                _pending = null;
            }
            Status = "Connection needs attention: " + e.Message;
            Plugin.LogInfo(Status);
        }
        finally
        {
            Busy = false;
            if (
                !Retired
                && (
                    ContentVersion != previousContent
                    || Status != previousStatus
                    || Conflict != previousConflict
                    || !SameTasks(previousTasks, Tasks)
                )
            )
            {
                try
                {
                    Changed?.Invoke();
                }
                catch (Exception e)
                {
                    Plugin.Error(e);
                }
            }
        }
    }

    private static bool SameTasks(List<CaptureTask> before, List<CaptureTask> after)
    {
        if (before.Count != after.Count)
            return false;
        for (var i = 0; i < before.Count; i++)
        {
            var a = before[i];
            var b = after[i];
            if (
                a.Id != b.Id
                || a.Tool != b.Tool
                || a.TargetKind != b.TargetKind
                || a.TargetId != b.TargetId
                || a.RecordId != b.RecordId
                || a.Status != b.Status
            )
                return false;
        }
        return true;
    }

    private void AcceptSubmit(AuthoringResponse response, SeasonDefinition sent)
    {
        var changes = new List<DraftConflict>();
        var remote = response.Candidate ?? response.Definition!;
        var working = DraftMerge
            .Merge(JObject.FromObject(sent), JObject.FromObject(Definition!), JObject.FromObject(remote), changes)!
            .ToObject<SeasonDefinition>()!;
        if (response.Conflicts.Count == 0 && changes.Count > 0)
        {
            response.Conflicts = changes;
            response.Candidate = working;
            response.RemoteCandidate = DraftMerge
                .Merge(JObject.FromObject(sent), JObject.FromObject(remote), JObject.FromObject(Definition!), new())!
                .ToObject<SeasonDefinition>();
        }
        if (!JToken.DeepEquals(JObject.FromObject(Definition!), JObject.FromObject(working)))
            ContentVersion++;
        RebaseHistory(_undo, sent, remote);
        RebaseHistory(_redo, sent, remote);
        Baseline = Copy(response.Definition!);
        Revision = response.Revision;
        Tasks = response.Tasks;
        if (response.Conflicts.Count > 0)
        {
            Conflict = response;
            Definition = working;
        }
        else
        {
            Definition = working;
            Conflict = null;
        }
        RefreshDirty();
        Persist();
    }

    private static void RebaseHistory(Stack<SeasonDefinition> history, SeasonDefinition sent, SeasonDefinition remote)
    {
        if (JToken.DeepEquals(JObject.FromObject(sent), JObject.FromObject(remote)))
        {
            return;
        }

        var entries = history.AsValueEnumerable().Reverse().ToArray();
        history.Clear();
        foreach (var entry in entries)
        {
            var conflicts = new List<DraftConflict>();
            var merged = DraftMerge
                .Merge(JObject.FromObject(sent), JObject.FromObject(entry), JObject.FromObject(remote), conflicts)!
                .ToObject<SeasonDefinition>()!;
            if (conflicts.Count == 0)
            {
                history.Push(merged);
            }
        }
    }

    private void MergeRemote(AuthoringResponse response)
    {
        if (response.Definition == null || Definition == null || Baseline == null)
        {
            return;
        }

        var conflicts = new List<DraftConflict>();
        var b = JObject.FromObject(Baseline);
        var l = JObject.FromObject(Definition);
        var r = JObject.FromObject(response.Definition);
        var merged = DraftMerge.Merge(b, l, r, conflicts)!.ToObject<SeasonDefinition>()!;
        if (!JToken.DeepEquals(l, JObject.FromObject(merged)))
            ContentVersion++;
        if (conflicts.Count > 0)
        {
            Conflict = new()
            {
                Conflicts = conflicts,
                Definition = Copy(response.Definition),
                Candidate = merged,
                RemoteCandidate = DraftMerge.Merge(b, r, l, new())!.ToObject<SeasonDefinition>(),
                Revision = response.Revision,
            };
        }
        if (!JToken.DeepEquals(b, r))
        {
            _undo.Clear();
            _redo.Clear();
        }
        Baseline = Copy(response.Definition);
        Definition = merged;
        Revision = response.Revision;
        RefreshDirty();
        Persist();
    }

    internal void Resolve(bool local)
    {
        if (Conflict == null)
        {
            return;
        }

        Definition = Copy((local ? Conflict.Candidate : Conflict.RemoteCandidate)!);
        ContentVersion++;
        Baseline = Copy(Conflict.Definition!);
        Revision = Conflict.Revision;
        Conflict = null;
        _pending = null;
        RefreshDirty();
        Persist();
        Changed?.Invoke();
    }

    internal void TaskStatus(CaptureTask task, string status, string result = "")
    {
        if (Busy || _pending != null || Conflict != null)
        {
            return;
        }

        _pending = Request();
        _pending.TaskId = task.Id;
        _pending.TaskStatus = status;
        _pending.ResultId = result;
        if (Definition != null)
        {
            _pending.Definition = Copy(Definition);
        }
    }

    internal async Task Retire()
    {
        Persist();
        Retired = true;
        try
        {
            await Send("poll", Request());
        }
        catch (Exception e)
        {
            Plugin.LogInfo("Authoring disconnect: " + e.Message);
        }
        finally
        {
            _socketLifetime.Cancel();
        }
    }
}
