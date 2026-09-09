using Microsoft.AspNetCore.Components;
using Newtonsoft.Json;
using SeasonalPerks.Server.Seasons;
using SeasonalPerks.Server.Web.Authoring;
using SeasonalPerks.Shared.Authoring;
using SeasonalPerks.Shared.Seasons;

namespace SeasonalPerks.Server.Web.Pages;

public partial class Creator
{
    [Inject]
    private RaidAuthoringService RaidAuthoring { get; set; } = null!;
    private AuthoringResponse? _raidConflict;
    private string _raidStatus = "";
    private bool _inputPending;
    private RaidEditorContext RaidContext
    {
        get { return new() { Connected = _draft != null && RaidAuthoring.Connected(_draft.Id), Request = RequestCapture }; }
    }

    private void InputPending()
    {
        _inputPending = true;
        Dirty();
    }

    private void InputCommitted()
    {
        _inputPending = false;
        Dirty();
    }

    private Task SyncRaid()
    {
        if (_draft != null && !_inputPending && _raidConflict == null && RaidAuthoring.Connected(_draft.Id))
        {
            try
            {
                SyncDraft();
            }
            catch (Exception e)
            {
                _raidStatus = e.Message;
            }
        }
        return Task.CompletedTask;
    }

    private Task FlushRaid()
    {
        SyncDraft();
        if (_raidConflict != null)
        {
            throw new InvalidOperationException("Resolve draft conflicts before requesting a capture.");
        }

        return Task.CompletedTask;
    }

    private void SyncDraft()
    {
        if (_draft == null)
        {
            return;
        }

        var result = RaidAuthoring.Save(_draft.Id, JsonConvert.DeserializeObject<SeasonDefinition>(_baseline)!, S);
        if (result.Conflicts.Count > 0)
        {
            _raidConflict = result;
            _raidStatus = "Choose which conflicting values to keep.";
            return;
        }
        if (result.Revision != _draft.Revision || DirtyState)
        {
            _draft = new DraftEnvelope
            {
                Id = result.DraftId,
                Revision = result.Revision,
                Definition = result.Definition!,
            };
            _baseline = JsonConvert.SerializeObject(S);
            _validationSnapshot = null;
        }
        _raidStatus = "Draft synchronized · revision " + result.Revision;
    }

    private Task RequestCapture(CaptureTask task)
    {
        try
        {
            SyncDraft();
            if (_raidConflict == null && _draft != null)
            {
                RaidAuthoring.Request(_draft.Id, task);
            }
        }
        catch (Exception e)
        {
            _raidStatus = e.Message;
        }
        return Task.CompletedTask;
    }

    private void ResolveRaidConflict(bool local)
    {
        if (_raidConflict == null || _draft == null)
        {
            return;
        }

        _draft.Definition = (local ? _raidConflict.Candidate : _raidConflict.RemoteCandidate)!;
        _draft.Revision = _raidConflict.Revision;
        _baseline = JsonConvert.SerializeObject(_raidConflict.Definition);
        _raidConflict = null;
        try
        {
            SyncDraft();
        }
        catch (Exception e)
        {
            _raidStatus = e.Message;
        }
    }
}
