using Newtonsoft.Json.Linq;
using SPTarkov.DI.Annotations;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Server.Web.Authoring;

[Injectable(InjectionType.Singleton)]
public sealed class RaidAuthoringService(SeasonRepository repository)
{
    private readonly object _gate = new();

    private sealed class Connection
    {
        public AuthoringClient Client = new();
        public string AutoDraft = "";
        public string Owner = "",
            Grant = "";
        public HashSet<string> NativeZoneIds = new(),
            Scenes = new();
        public Dictionary<long, SeasonDefinition> Versions = new();
        public Dictionary<string, (string Hash, AuthoringResponse Response)> Receipts = new();
    }

    private readonly Dictionary<string, Connection> _clients = new();

    private static T Copy<T>(T value)
    {
        return SeasonCompiler.Copy(value);
    }

    internal Func<DateTimeOffset> UtcNow = () => DateTimeOffset.UtcNow;

    private void Expire()
    {
        foreach (var id in _clients.Where(p => UtcNow() - p.Value.Client.LastSeen > TimeSpan.FromSeconds(20)).Select(p => p.Key).ToArray())
        {
            _clients.Remove(id);
        }
    }

    public List<AuthoringClient> Clients()
    {
        lock (_gate)
        {
            Expire();
            return _clients.Values.Select(c => Copy(c.Client)).ToList();
        }
    }

    public bool Connected(string draft)
    {
        return Clients().Any(c => c.DraftId == draft);
    }

    public void Connect(string client, string draft)
    {
        lock (_gate)
        {
            Expire();
            if (!_clients.TryGetValue(client, out var c))
            {
                throw new InvalidOperationException("The raid is no longer connected.");
            }

            if (repository.Load(draft).Status != DraftStatus.Active)
            {
                throw new InvalidOperationException("Restore this draft before connecting.");
            }

            c.Client.DraftId = draft;
            c.Grant = Guid.NewGuid().ToString("N");
            c.Versions.Clear();
            c.Receipts.Clear();
            c.Client.Tasks.Clear();
        }
    }

    public void Disconnect(string client)
    {
        lock (_gate)
        {
            if (_clients.TryGetValue(client, out var c))
            {
                c.Grant = "";
                c.Client.DraftId = "";
                c.Client.Tasks.Clear();
                c.Versions.Clear();
            }
        }
    }

    public void Request(string draft, CaptureTask task)
    {
        lock (_gate)
        {
            Expire();
            var matches = _clients.Values.Where(c => c.Client.DraftId == draft).ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidOperationException("Connect exactly one raid to this draft before requesting a capture.");
            }

            var c = matches[0];
            if (task.TargetKind is not ("" or "Condition" or "Binding" or "MapLayout"))
            {
                throw new InvalidOperationException("Unsupported capture destination.");
            }

            if (task.Tool is not ("Zone" or "Object" or "Transform" or "MapLayout"))
            {
                throw new InvalidOperationException("Unsupported capture tool.");
            }

            if (c.Client.Tasks.Count(t => t.Status is "Pending" or "Opened") >= 16)
            {
                throw new InvalidOperationException("Finish or cancel pending captures first.");
            }

            if (task.Tool == "MapLayout" && Editor.EditorSessionRegistry.Find(c.Client.CharacterId)?.Ready != true)
                throw new InvalidOperationException("Open this map in Campaign Editor first.");
            c.Client.Tasks.RemoveAll(t => t.Status is "Completed" or "Cancelled");
            task.Id = Guid.NewGuid().ToString("N");
            task.Status = "Pending";
            c.Client.Tasks.Add(Copy(task));
        }
    }

    public AuthoringResponse Poll(string owner, string character, AuthoringRequest r)
    {
        lock (_gate)
        {
            Expire();
            if (
                r.Version is not (1 or 2 or 3 or 4)
                || !Guid.TryParseExact(r.ClientId, "N", out _)
                || !Guid.TryParseExact(r.RaidId, "N", out _)
                || r.Location.Length is 0 or > 120
            )
            {
                throw new InvalidOperationException("Invalid authoring presence or protocol.");
            }

            if (_clients.TryGetValue(r.ClientId, out var prior) && prior.Owner != owner)
            {
                throw new InvalidOperationException("This authoring client belongs to another account.");
            }

            if (!r.Enabled)
            {
                _clients.Remove(r.ClientId);
                return new();
            }
            if (
                prior == null
                || prior.Client.RaidId != r.RaidId
                || prior.Client.CharacterId != character
                || prior.Client.Location != r.Location
            )
            {
                if (_clients.Count >= 32)
                {
                    throw new InvalidOperationException("Too many connected authoring clients.");
                }

                prior = new Connection
                {
                    Owner = owner,
                    Client = new()
                    {
                        Id = r.ClientId,
                        CharacterId = character,
                        RaidId = r.RaidId,
                        Location = r.Location,
                    },
                };
                _clients[r.ClientId] = prior;
            }
            if (r.NativeZoneIds.Count > 10000 || r.Scenes.Count > 500)
                throw new InvalidOperationException("Scene catalog exceeds authoring limits.");
            prior.NativeZoneIds = r.NativeZoneIds.ToHashSet();
            prior.Scenes = r.Scenes.ToHashSet();
            prior.Client.LastSeen = UtcNow();
            var editor = Editor.EditorSessionRegistry.Find(character);
            if (editor != null && editor.Id == r.EditorSessionId && editor.Draft.Length > 0 && prior.AutoDraft != editor.Draft)
            {
                Connect(r.ClientId, editor.Draft);
                prior.AutoDraft = editor.Draft;
            }
            if (prior.Grant.Length == 0)
            {
                return new();
            }

            var draft = repository.Load(prior.Client.DraftId);
            if (draft.Status != DraftStatus.Active)
            {
                Disconnect(r.ClientId);
                return new();
            }
            var response = new AuthoringResponse
            {
                Grant = prior.Grant,
                DraftId = draft.Id,
                Revision = draft.Revision,
                Tasks = Copy(prior.Client.Tasks),
            };
            if (r.Grant != prior.Grant || r.Revision != draft.Revision)
            {
                response.Definition = draft.Definition;
                prior.Versions[draft.Revision] = Copy(draft.Definition);
                while (prior.Versions.Count > 32)
                {
                    prior.Versions.Remove(prior.Versions.Keys.Min());
                }
            }
            return response;
        }
    }

    public AuthoringResponse Submit(string owner, string character, AuthoringRequest r)
    {
        lock (_gate)
        {
            Expire();
            if (
                r.Version is not (1 or 2 or 3 or 4)
                || !_clients.TryGetValue(r.ClientId, out var c)
                || c.Owner != owner
                || c.Client.CharacterId != character
                || c.Client.RaidId != r.RaidId
                || c.Client.DraftId != r.DraftId
                || c.Grant.Length == 0
                || c.Grant != r.Grant
            )
            {
                throw new InvalidOperationException("Reconnect this raid and draft before submitting.");
            }

            if (!Guid.TryParseExact(r.OperationId, "N", out _))
            {
                throw new InvalidOperationException("An operation identity is required.");
            }

            var hash = JObject.FromObject(r).ToString();
            if (c.Receipts.TryGetValue(r.OperationId, out var receipt))
            {
                if (receipt.Hash != hash)
                {
                    throw new InvalidOperationException("An operation identity cannot be reused for different edits.");
                }

                return Copy(receipt.Response);
            }
            CaptureTask? task = null;
            if (r.TaskId.Length > 0)
            {
                task =
                    c.Client.Tasks.SingleOrDefault(t => t.Id == r.TaskId)
                    ?? throw new InvalidOperationException("This capture has expired.");
                if (task.Status is "Completed" or "Cancelled")
                {
                    throw new InvalidOperationException("This capture is already finished.");
                }

                if (r.TaskStatus == "Completed" && r.Definition == null)
                {
                    throw new InvalidOperationException("Completing a capture requires its result.");
                }

                if (r.TaskStatus is not ("Opened" or "Completed" or "Cancelled"))
                {
                    throw new InvalidOperationException("Invalid capture status.");
                }
            }
            var draft = repository.Load(r.DraftId);
            AuthoringResponse result;
            if (r.Definition != null)
            {
                if (!c.Versions.TryGetValue(r.Revision, out var baseline))
                {
                    throw new InvalidOperationException("This draft baseline expired. Refresh and reconcile your recovered edits.");
                }
                // Client writes only spatial authoring records and bindings. Other season settings stay on the server.
                var proposed = Copy(baseline);
                proposed.Zones = Copy(r.Definition.Zones);
                if (!r.SupportsZoneLayouts)
                {
                    // Older raid authoring clients do not know LayoutId. Keep the server's
                    // existing ownership when they round-trip a draft so a normal raid cannot
                    // silently turn layout zones into Shared zones.
                    foreach (var zone in proposed.Zones)
                    {
                        var original = baseline.Zones.FirstOrDefault(z => z.Id == zone.Id);
                        if (!string.IsNullOrEmpty(original?.LayoutId) && string.IsNullOrEmpty(zone.LayoutId))
                        {
                            zone.LayoutId = original.LayoutId;
                        }
                    }
                }
                proposed.Captures = Copy(r.Definition.Captures);
                if (
                    r.Version >= 2
                    && Editor.EditorSessionRegistry.Find(character) is { Ready: true } editor
                    && editor.Id == r.EditorSessionId
                )
                {
                    if (
                        r.Version < 4
                        && (
                            baseline.MapLayouts.Any(MapLayoutRules.NeedsFormat6)
                            || draft.Definition.MapLayouts.Any(MapLayoutRules.NeedsFormat6)
                            || r.Definition.MapLayouts.Any(MapLayoutRules.NeedsFormat6)
                        )
                    )
                        throw new InvalidOperationException("Update the client before editing layouts with AI encounters or patrols.");
                    if (
                        r.Version < 3
                        && (
                            baseline.MapLayouts.Any(MapLayoutRules.NeedsFormat5)
                            || draft.Definition.MapLayouts.Any(MapLayoutRules.NeedsFormat5)
                        )
                    )
                        throw new InvalidOperationException("Update the client before editing layouts with scene catalog records.");
                    proposed.MapLayouts = Copy(r.Definition.MapLayouts);
                    foreach (
                        var layout in proposed.MapLayouts.Where(l =>
                            !baseline.MapLayouts.Any(b => b.Id == l.Id && JToken.DeepEquals(JObject.FromObject(b), JObject.FromObject(l)))
                        )
                    )
                    {
                        if (layout.Location != c.Client.Location)
                            throw new InvalidOperationException("Edit layouts on the connected map.");
                        if (
                            c.Scenes.Count > 0
                            && (
                                MapLayoutRules.Points(layout).Any(p => !c.Scenes.Contains(p.Scene))
                                || layout.Doors.Any(d => !c.Scenes.Contains(d.Target.Scene))
                            )
                        )
                            throw new InvalidOperationException("Layout targets must belong to loaded scenes.");
                        var errors = MapLayoutRules.Errors(layout);
                        if (errors.Count > 0)
                            throw new InvalidOperationException(string.Join("\n", errors));
                    }
                    if (
                        proposed.MapLayouts.Count > 128
                        || proposed.MapLayouts.SelectMany(MapLayoutRules.OwnedIds).GroupBy(id => id).Any(g => g.Count() > 1)
                    )
                        throw new InvalidOperationException("Map layouts require unique record identities (at most 128 layouts).");
                    if (proposed.MapLayouts.Count > 0)
                        proposed.FormatVersion = Math.Max(proposed.FormatVersion, MapLayoutRules.Format(proposed.MapLayouts));
                }
                if (r.Definition.Story != null)
                {
                    proposed.Story ??= new();
                    proposed.Story.RaidBindings = Copy(r.Definition.Story.RaidBindings);
                }
                if (proposed.Zones.Count > 0 || proposed.Captures.Count > 0)
                {
                    proposed.FormatVersion = Math.Max(proposed.FormatVersion, 2);
                }

                foreach (
                    var zone in proposed.Zones.Where(z =>
                        !baseline.Zones.Any(b => b.Id == z.Id && JToken.DeepEquals(JObject.FromObject(b), JObject.FromObject(z)))
                    )
                )
                {
                    if (zone.Location != c.Client.Location)
                    {
                        throw new InvalidOperationException("Capture zones on the connected raid's map.");
                    }
                }

                if (proposed.Zones.Any(z => z.Location == c.Client.Location && c.NativeZoneIds.Contains(z.Id)))
                    throw new InvalidOperationException("An authored zone ID collides with a native scene zone.");
                if (c.Scenes.Count > 0 && proposed.Zones.Any(z => z.Location == c.Client.Location && !c.Scenes.Contains(z.Scene)))
                    throw new InvalidOperationException("The zone scene is not loaded in this raid.");
                if (task != null && r.TaskStatus == "Completed")
                {
                    Assign(proposed, task, r.ResultId, c.Client.Location);
                }

                result = Save(draft.Id, baseline, proposed);
            }
            else
            {
                result = new()
                {
                    DraftId = draft.Id,
                    Revision = draft.Revision,
                    Definition = draft.Definition,
                };
            }

            result.Grant = c.Grant;
            if (result.Conflicts.Count == 0)
            {
                if (task != null)
                {
                    task.Status = r.TaskStatus;
                }

                c.Receipts[r.OperationId] = (hash, Copy(result));
                while (c.Receipts.Count > 128)
                {
                    c.Receipts.Remove(c.Receipts.Keys.First());
                }
            }
            c.Versions[result.Revision] = Copy(result.Definition!);
            result.Tasks = Copy(c.Client.Tasks);
            return result;
        }
    }

    private static void Assign(SeasonDefinition s, CaptureTask task, string id, string map)
    {
        if (task.Tool == "MapLayout")
        {
            if (task.TargetKind != "MapLayout" || task.RecordId != id || !s.MapLayouts.Any(l => l.Id == id && l.Location == map))
                throw new InvalidOperationException("Open the layout on its map.");
            return;
        }
        if (task.TargetKind.Length == 0)
        {
            return;
        }

        if (task.TargetKind == "Condition")
        {
            var condition =
                SpatialRules.Conditions(s).SingleOrDefault(c => c.Id == task.TargetId)
                ?? throw new InvalidOperationException("The destination objective was deleted.");
            var zone =
                s.Zones.SingleOrDefault(z => z.Id == id && z.Location == map)
                ?? throw new InvalidOperationException("Select a zone on this map.");
            if (!zone.Uses.Contains(condition.ConditionType))
            {
                zone.Uses.Add(condition.ConditionType);
            }

            SpatialRules.Assign(condition, id);
        }
        else if (task.TargetKind == "Binding")
        {
            var binding =
                s.Story?.RaidBindings.SingleOrDefault(b => b.Id == task.TargetId)
                ?? throw new InvalidOperationException("The destination raid event was deleted.");
            binding.Location = map;
            if (task.Tool == "Zone")
            {
                binding.ZoneId = id;
                binding.ObjectPath = "";
            }
            else
            {
                binding.ObjectPath = s.Captures.Single(c => c.Id == id).ObjectPath;
                binding.ZoneId = "";
            }
        }
    }

    public AuthoringResponse Save(string id, SeasonDefinition baseline, SeasonDefinition local)
    {
        lock (_gate)
        {
            for (var retry = 0; retry < 3; retry++)
            {
                var draft = repository.Load(id);
                var conflicts = new List<DraftConflict>();
                var b = JObject.FromObject(baseline);
                var l = JObject.FromObject(local);
                var r = JObject.FromObject(draft.Definition);
                var candidate = DraftMerge.Merge(b, l, r, conflicts)!.ToObject<SeasonDefinition>()!;
                var result = new AuthoringResponse
                {
                    DraftId = id,
                    Revision = draft.Revision,
                    Definition = draft.Definition,
                    Conflicts = conflicts,
                };
                if (conflicts.Count > 0)
                {
                    result.Candidate = candidate;
                    result.RemoteCandidate = DraftMerge.Merge(b, r, l, new())!.ToObject<SeasonDefinition>();
                    return result;
                }
                foreach (var removed in draft.Definition.Zones.Where(z => candidate.Zones.All(n => n.Id != z.Id)))
                {
                    if (SpatialRules.Uses(candidate, removed.Id).Any())
                    {
                        throw new InvalidOperationException("Reassign references before deleting zone " + removed.Name);
                    }
                }

                // Captures may be saved before their salvage items are configured in the browser.
                // Publishing still requires the complete salvage configuration.
                var errors = SpatialRules.Errors(candidate, requireCompleteSalvage: false);
                errors.AddRange(candidate.MapLayouts.SelectMany(l => MapLayoutRules.Errors(l)));
                if (errors.Count > 0)
                {
                    throw new InvalidOperationException(string.Join("\n", errors.Take(10)));
                }

                if (JToken.DeepEquals(JObject.FromObject(candidate), r))
                {
                    return result;
                }

                draft.Definition = candidate;
                try
                {
                    draft = repository.Save(draft);
                }
                catch (InvalidOperationException) when (retry < 2 && repository.Load(id).Revision != draft.Revision)
                {
                    continue;
                }
                return new()
                {
                    DraftId = id,
                    Revision = draft.Revision,
                    Definition = draft.Definition,
                };
            }
            throw new InvalidOperationException("The draft is changing rapidly. Retry synchronization.");
        }
    }
}
