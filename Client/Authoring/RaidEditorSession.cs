using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SeasonalPerks.Shared.Authoring;
using SeasonalPerks.Shared.Seasons;
using SPT.Common.Http;

namespace SeasonalPerks.Client.Authoring;

internal sealed class RaidEditorSession
{
    private sealed class Recovery
    {
        public string DraftId = "";
        public long Revision;
        public SeasonDefinition? Baseline, Definition;
    }
    internal string ClientId { get; } = Guid.NewGuid().ToString("N");
    internal string RaidId { get; } = Guid.NewGuid().ToString("N");
    internal string Location { get; }
    internal List<string> NativeZoneIds = new(), Scenes = new();
    internal string DraftId = "", Grant = "", Status = "Waiting for the web editor to connect a draft";
    internal long Revision;
    internal SeasonDefinition? Baseline, Definition;
    internal AuthoringResponse? Conflict;
    internal List<CaptureTask> Tasks = new();
    internal bool Busy, Hold, Retired, Contacted;
    internal event Action? Changed;
    private AuthoringRequest? _pending;
    private readonly Stack<SeasonDefinition> _undo = new(), _redo = new();
    private readonly string _recoveryRoot = Path.Combine(BepInEx.Paths.ConfigPath, "SeasonalPerks", "raid-authoring");
    internal bool Dirty
    {
        get
        {
            return Definition != null && !JToken.DeepEquals(JObject.FromObject(Definition), Baseline == null ? null : JObject.FromObject(Baseline));
        }
    }

    internal RaidEditorSession(string location) => Location = location;
    internal static T Copy<T>(T value)
    {
        return SeasonCompiler.Copy(value);
    }

    private string RecoveryPath
    {
        get
        {
            return Path.Combine(_recoveryRoot, DraftId + ".json");
        }
    }

    internal void Edit(Action<SeasonDefinition> action)
    {
        if (Definition == null || Conflict != null || Retired)
        {
            return;
        }

        var before = Copy(Definition);
        action(Definition);
        if (Definition.Zones.Count > 0 || Definition.Captures.Count > 0)
        {
            Definition.FormatVersion = 2;
        }

        if (JToken.DeepEquals(JObject.FromObject(before), JObject.FromObject(Definition)))
        {
            return;
        }

        _undo.Push(before); _redo.Clear(); Persist(); Changed?.Invoke();
    }
    internal void Undo(bool redo)
    {
        if (Definition == null || Conflict != null)
        {
            return;
        }

        var from = redo ? _redo : _undo; var to = redo ? _undo : _redo;
        if (from.Count == 0)
        {
            return;
        }

        to.Push(Copy(Definition)); Definition = from.Pop(); Persist(); Changed?.Invoke();
    }
    internal void Persist()
    {
        if (Definition == null || DraftId.Length == 0)
        {
            return;
        }

        Directory.CreateDirectory(_recoveryRoot);
        var bytes = JsonConvert.SerializeObject(new Recovery { DraftId = DraftId, Revision = Revision, Baseline = Baseline, Definition = Definition }, Formatting.Indented);
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
        return new() { ClientId = ClientId, RaidId = RaidId, Location = Location, NativeZoneIds = NativeZoneIds, Scenes = Scenes, Enabled = !Retired, Grant = Grant, DraftId = DraftId, Revision = Revision, OperationId = Guid.NewGuid().ToString("N") };
    }

    private static async Task<AuthoringResponse> Send(string route, AuthoringRequest request)
    {
        var json = await RequestHandler.PostJsonAsync("/wtt-seasonal/authoring/" + route, JsonConvert.SerializeObject(request));
        var response = JsonConvert.DeserializeObject<AuthoringResponse>(json) ?? throw new InvalidOperationException("Empty authoring response.");
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

        Busy = true; Contacted = false;
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
                Persist(); Grant = ""; DraftId = ""; Definition = Baseline = null; _pending = null;
                Status = "Enable and connect this raid in the web season editor"; Changed?.Invoke(); return;
            }
            if (response.Grant != Grant || response.DraftId != DraftId)
            {
                Persist(); _pending = null; _undo.Clear(); _redo.Clear(); Conflict = null;
                Grant = response.Grant; DraftId = response.DraftId;
                Baseline = Copy(response.Definition!); Definition = Copy(Baseline); Revision = response.Revision;
                if (File.Exists(RecoveryPath))
                {
                    var recovery = JsonConvert.DeserializeObject<Recovery>(File.ReadAllText(RecoveryPath));
                    if (recovery?.Baseline != null && recovery.Definition != null && recovery.DraftId == DraftId)
                    { Baseline = recovery.Baseline; Definition = recovery.Definition; MergeRemote(response); }
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
            Status = Conflict != null ? "Resolve conflicting edits" : Dirty ? "Unsynchronized draft edits" : "Draft synchronized · revision " + Revision;
            Changed?.Invoke();
        }
        catch (Exception e) { if (e is InvalidOperationException) { _pending = null; } Status = "Connection needs attention: " + e.Message; Plugin.LogInfo(Status); }
        finally { Busy = false; }
    }
    private void AcceptSubmit(AuthoringResponse response, SeasonDefinition sent)
    {
        var changes = new List<DraftConflict>();
        var remote = response.Candidate ?? response.Definition!;
        var working = DraftMerge.Merge(JObject.FromObject(sent), JObject.FromObject(Definition!), JObject.FromObject(remote), changes)!.ToObject<SeasonDefinition>()!;
        if (response.Conflicts.Count == 0 && changes.Count > 0)
        {
            response.Conflicts = changes; response.Candidate = working;
            response.RemoteCandidate = DraftMerge.Merge(JObject.FromObject(sent), JObject.FromObject(remote), JObject.FromObject(Definition!), new())!.ToObject<SeasonDefinition>();
        }
        RebaseHistory(_undo, sent, remote); RebaseHistory(_redo, sent, remote);
        Baseline = Copy(response.Definition!); Revision = response.Revision; Tasks = response.Tasks;
        if (response.Conflicts.Count > 0) { Conflict = response; Definition = working; }
        else { Definition = working; Conflict = null; }
        Persist();
    }
    private static void RebaseHistory(Stack<SeasonDefinition> history, SeasonDefinition sent, SeasonDefinition remote)
    {
        if (JToken.DeepEquals(JObject.FromObject(sent), JObject.FromObject(remote)))
        {
            return;
        }

        var entries = history.Reverse().ToArray(); history.Clear();
        foreach (var entry in entries)
        {
            var conflicts = new List<DraftConflict>();
            var merged = DraftMerge.Merge(JObject.FromObject(sent), JObject.FromObject(entry), JObject.FromObject(remote), conflicts)!.ToObject<SeasonDefinition>()!;
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
        var b = JObject.FromObject(Baseline); var l = JObject.FromObject(Definition); var r = JObject.FromObject(response.Definition);
        var merged = DraftMerge.Merge(b, l, r, conflicts)!.ToObject<SeasonDefinition>()!;
        if (conflicts.Count > 0)
        {
            Conflict = new() { Conflicts = conflicts, Definition = Copy(response.Definition), Candidate = merged, RemoteCandidate = DraftMerge.Merge(b, r, l, new())!.ToObject<SeasonDefinition>(), Revision = response.Revision };
        }
        if (!JToken.DeepEquals(b, r)) { _undo.Clear(); _redo.Clear(); }
        Baseline = Copy(response.Definition); Definition = merged; Revision = response.Revision; Persist();
    }
    internal void Resolve(bool local)
    {
        if (Conflict == null)
        {
            return;
        }

        Definition = Copy((local ? Conflict.Candidate : Conflict.RemoteCandidate)!);
        Baseline = Copy(Conflict.Definition!); Revision = Conflict.Revision; Conflict = null; _pending = null;
        Persist(); Changed?.Invoke();
    }
    internal void TaskStatus(CaptureTask task, string status, string result = "")
    {
        if (Busy || _pending != null || Conflict != null)
        {
            return;
        }

        _pending = Request(); _pending.TaskId = task.Id; _pending.TaskStatus = status; _pending.ResultId = result;
        if (Definition != null)
        {
            _pending.Definition = Copy(Definition);
        }
    }
    internal async Task Retire()
    {
        Persist(); Retired = true;
        try { await Send("poll", Request()); } catch (Exception e) { Plugin.LogInfo("Authoring disconnect: " + e.Message); }
    }
}
