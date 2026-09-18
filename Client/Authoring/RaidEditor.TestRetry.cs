using EFT;
using WTT.Campaigns.Client.Encounters;
using WTT.Campaigns.Client.Missions;
using WTT.Campaigns.Shared.Missions;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Authoring;

public sealed partial class RaidEditor
{
    private MissionRetryGuard? _testRetryGuard;
    private MissionRaidCheckpoint? _testCheckpoint;
    private MissionHud? _testHud;
    private EDamageType? _testDeath;
    private string _testFailure = "";
    private bool _testRestoreBroken, _testFailureShown;

    private async Task CaptureTestStart(CancellationToken token)
    {
        if (_editorMissionTest?.Descriptor?.Definition.CheckpointRetries != true) return;
        _testRetryGuard = new MissionRetryGuard(_player!, damage =>
        {
            _testDeath = damage; _testFailure = "Operator down"; _testFailureShown = false; _editorDirector?.Pause();
        });
        _testHud = new MissionHud(_editorMissionTest.Descriptor.Definition.Name);
        RefreshTestHud();
        _aiRuntime!.RetryGuard = _testRetryGuard;
        _testRetryGuard.Freeze(); _editorDirector!.Pause();
        await _aiRuntime.SettleAsync(token);
        await MissionInventorySnapshot.SettleHands(_player!, token);
        await MissionWorldSnapshot.SettleAsync(token);
        var response = await EditorMissionTestClient.ProgressAsync(_editorMissionTest, "", "start-checkpoint", token);
        MissionAcknowledgement.Require(_editorMissionTest.Run!, response.Run, response.Committed);
        _editorMissionTest = response;
        _testCheckpoint = new MissionRaidCheckpoint("", _player!, _aiRuntime);
        ReleaseTestHold();
    }

    private void ReleaseTestHold()
    {
        _testRetryGuard!.Release(); _editorDirector?.Resume(); _aiRuntime?.Resume();
    }

    private void CheckTestFailure()
    {
        var failure = _editorMissionTest?.Run?.Logic.Failure ?? "";
        if (failure.Length == 0) return;
        if (_testRetryGuard == null) { _aiPreviewStatus = failure; EndAiPreview(); return; }
        _testFailure = failure; _testFailureShown = false;
        _testRetryGuard.Freeze(); _editorDirector?.Pause();
    }

    private bool HoldTestFailure()
    {
        RefreshTestHud();
        if (_testRetryGuard?.Frozen != true) return false;
        _testRetryGuard.Hold();
        if (!_editorMissionProgressPending && !_editorMissionRetrying && !_testFailureShown && _testFailure.Length > 0)
        {
            _testFailureShown = true;
            var name = _testCheckpoint?.Id is not { Length: > 0 } ? "Mission start"
                : _editorMissionLayout!.Checkpoints.Find(c => c.Id == _testCheckpoint.Id)?.Name ?? "Checkpoint";
            _testHud?.ShowFailure(_testFailure, name, _testRestoreBroken || _testCheckpoint == null ? null
                : () => { _ = RestoreTestCheckpoint(); }, () => EndAiPreview());
        }
        return true;
    }

    private void RefreshTestHud()
    {
        if (_editorMissionTest?.Run == null || _editorMissionLayout == null) return;
        _testHud?.SetRoute(_editorMissionTest.Run.NextCheckpointIndex, _editorMissionLayout.Checkpoints.Count,
            _editorMissionCompleted, _testFailure.Length > 0 ? _testFailure : _aiPreviewStatus);
    }

    private async Task DrainTestObservations(CancellationToken token)
    {
        _editorDirector!.ObserveInteractions();
        while (_editorDirector!.HasPending || _editorSignals != null)
        {
            if (_editorSignals == null)
            {
                _editorSignals = _editorDirector.Take();
                _editorObservationActors = new(_editorDirector.Actors);
                _editorObservationOperation = Guid.NewGuid().ToString("N");
            }
            var response = await EditorMissionTestClient.ObserveAsync(_editorMissionTest!, _editorSignals,
                _editorObservationActors, _editorObservationOperation, token);
            MissionAcknowledgement.Require(_editorMissionTest!.Run!, response.Run, response.Committed);
            _editorMissionTest = response; _editorSignals = null;
            _editorDirector.Accept(response.Run!.Logic);
        }
    }

    private async Task RestoreTestCheckpoint()
    {
        if (_editorMissionRetrying || _editorMissionProgressPending || _testRestoreBroken || _testCheckpoint == null) return;
        _editorMissionRetrying = true; _editorMissionProgressPending = true;
        var token = _editorMissionLifetime!.Token;
        try
        {
            await DrainTestObservations(token);
            if (_testDeath.HasValue)
            {
                var defeated = await EditorMissionTestClient.ProgressAsync(_editorMissionTest!, "", "defeat", token);
                MissionAcknowledgement.Require(_editorMissionTest!.Run!, defeated.Run, defeated.Committed);
                _editorMissionTest = defeated;
            }
            var before = _editorMissionTest!;
            var prepared = await EditorMissionTestClient.ProgressAsync(before, "", "retry-prepare", token);
            MissionAcknowledgement.RequireRestore(before.Run!, prepared.Run, prepared.Committed, preparing: true);
            if (prepared.Run!.CheckpointId != _testCheckpoint.Id) throw new InvalidDataException("The test checkpoint identity changed.");
            _editorMissionTest = prepared;
            _editorDirector!.Dispose(); _editorDirector = null;
            _aiRuntime!.Reset(preserveWorld: true); _aiRuntime = null;
            await _testCheckpoint.ClearAsync(_player!, token);
            _aiRuntime = new EncounterPreviewRuntime { RetryGuard = _testRetryGuard };
            await _aiRuntime.BeginAsync(new EncounterRuntimeContext
            {
                SessionId = EditorMode.SessionId, RaidId = _session!.RaidId, LayoutId = _editorMissionLayout!.Id,
                LayoutRevision = _session.ContentVersion, Mode = EncounterRuntimeModes.Preview,
                PreviewGeneration = Guid.NewGuid().ToString("N"), AttemptGeneration = prepared.Run.AttemptGeneration,
            }, _editorMissionLayout, _player!, false, token);
            await _testCheckpoint.RestoreWorldAsync(_player!, _testRetryGuard!, token);
            await _aiRuntime.RestoreAsync(_testCheckpoint.Encounters, prepared.Run.RestoredActorIds, token);
            _testCheckpoint.RestoreAccounting(_player!);
            await _testCheckpoint.RestoreAudioAsync(token);
            _editorDirector = new MissionDirector(_editorMissionLayout, _player!, _aiRuntime);
            _editorDirector.BindInteractions(_mapScene!.MissionInteractions(_editorMissionLayout));
            _editorDirector.BindInteractions(_editorMissionLoot!.MissionInteractions(_editorMissionLayout));
            _editorDirector.Restore(prepared.Run.Logic);
            var committed = await EditorMissionTestClient.ProgressAsync(prepared, "", "retry-commit", token);
            MissionAcknowledgement.RequireRestore(prepared.Run, committed.Run, committed.Committed, preparing: false);
            _editorMissionTest = committed;
            _editorMissionCheckpoint = committed.Run!.NextCheckpointIndex;
            _editorDirector.Accept(committed.Run.Logic);
            if (_testCheckpoint.Id.Length == 0) _aiRuntime.MissionStart();
            _editorSignals = null; _testDeath = null; _testFailure = ""; _testFailureShown = false;
            _aiPreviewStatus = "Mission test · checkpoint restored";
            ReleaseTestHold();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { BreakTestRestore(error); }
        finally { _editorMissionRetrying = false; _editorMissionProgressPending = false; }
    }

    private void BreakTestRestore(Exception error)
    {
        _testRestoreBroken = true; _testFailureShown = false;
        _testFailure = "Checkpoint restoration stopped: " + error.Message;
        _testRetryGuard?.Freeze(); Plugin.Error(error);
    }

    private void EndTestRetry()
    {
        if (_aiRuntime != null) _aiRuntime.RetryGuard = null;
        _testRetryGuard?.Dispose(); _testRetryGuard = null;
        _testHud?.Dispose(); _testHud = null; _testCheckpoint = null;
        _testDeath = null; _testFailure = ""; _testRestoreBroken = false; _testFailureShown = false;
    }
}
