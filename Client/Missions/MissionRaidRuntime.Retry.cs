using EFT;
using WTT.Campaigns.Client.Encounters;
using WTT.Campaigns.Shared.Missions;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Missions;

internal sealed partial class MissionRaidRuntime
{
    private MissionRetryGuard? _retryGuard;
    private MissionRaidCheckpoint? _checkpoint;
    private EDamageType? _retryDeath;
    private bool _retryBusy,
        _retryBroken,
        _retryShown;
    private string _retryFailure = "";
    private bool _technicalFailure,
        _technicalFailureAcknowledged;
    private string _technicalFailureOperation = "";
    private long _technicalFailureRevision;

    private void EncounterFailed(string reason)
    {
        if (_technicalFailure || _ending || _player == null)
            return;
        _technicalFailure = true;
        _retryShown = false;
        _retryFailure = "AI encounter interrupted: " + reason;
        _retryGuard ??= new MissionRetryGuard(_player, _ => { });
        _retryGuard.Freeze();
        _director?.Pause();
        if (_encounters != null)
        {
            _encounters.RetryGuard = _retryGuard;
            _encounters.StopWork();
        }
        _ = RecordEncounterFailure();
    }

    // Caller holds the same semaphore used by observations and checkpoint transitions.
    private async Task AcknowledgeEncounterFailure(CancellationToken token)
    {
        if (_technicalFailureAcknowledged)
            return;
        if (_technicalFailureOperation.Length == 0)
        {
            // An earlier observation/checkpoint may have committed despite losing its response.
            // Refresh only this exact attempt before choosing the interruption's expected revision.
            var current = await MissionClient.DescriptorAsync(_run!.MissionId, _run.RunId, _run.RaidId, token);
            MissionAcknowledgement.RequireCurrent(_run, current.Run);
            _run = current.Run!;
            _revision = current.Revision;
            _technicalFailureOperation = MissionClient.NewOperationId();
            _technicalFailureRevision = _revision;
        }
        var response = await MissionClient.TransitionAsync(
            _run!,
            "technical-failure",
            _technicalFailureRevision,
            _technicalFailureOperation,
            token
        );
        AcceptTransition(response);
        if (!_run!.TechnicalFailure)
            throw new InvalidDataException("The server did not acknowledge the technical encounter failure.");
        _technicalFailureAcknowledged = true;
    }

    private async Task RecordEncounterFailure()
    {
        _retryBusy = true;
        var token = _lifetime!.Token;
        var acquired = false;
        try
        {
            await _progressGate.WaitAsync(token);
            acquired = true;
            await AcknowledgeEncounterFailure(token);
            if (_encounters != null)
                await _encounters.WaitForWorkAsync();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Plugin.Error(exception);
            _retryFailure = "AI encounter interrupted. The server could not record the interruption: " + exception.Message;
        }
        finally
        {
            if (acquired)
                _progressGate.Release();
            _retryBusy = false;
        }
    }

    private async Task CaptureStartCheckpoint(CancellationToken token)
    {
        _retryGuard = new MissionRetryGuard(
            _player!,
            damage =>
            {
                _retryDeath = damage;
                _retryFailure = "Operator down";
                _retryShown = false;
                _director?.Pause();
            }
        );
        _encounters!.RetryGuard = _retryGuard;
        _retryBusy = true;
        _retryGuard.Freeze();
        _director!.Pause();
        await _encounters.SettleAsync(token);
        await MissionInventorySnapshot.SettleHands(_player!, token);
        await MissionWorldSnapshot.SettleAsync(token);
        var response = await MissionClient.TransitionAsync(_run!, "start-checkpoint", _revision, MissionClient.NewOperationId(), token);
        AcceptTransition(response);
        _checkpoint = new MissionRaidCheckpoint("", _player!, _encounters);
        _retryBusy = false;
        ReleaseRetryHold();
    }

    private void AcceptTransition(MissionResponse response)
    {
        MissionAcknowledgement.Require(_run!, response.Run, response.Committed);
        _run = response.Run!;
        _revision = response.Revision;
    }

    // Called only while holding the progress semaphore (or before publishing the active start).
    private async Task DrainObservations(CancellationToken token)
    {
        _director!.ObserveInteractions();
        while (_director!.HasPending || _unacknowledgedSignals != null)
        {
            if (_unacknowledgedSignals == null)
            {
                _unacknowledgedSignals = _director.Take();
                _observationOperation = MissionClient.NewOperationId();
                _observationRevision = _revision;
            }
            var response = await MissionClient.ObserveAsync(
                _run!,
                _unacknowledgedSignals,
                _observationRevision,
                _observationOperation,
                token
            );
            AcceptTransition(response);
            _unacknowledgedSignals = null;
            _director.Accept(_run!.Logic);
        }
    }

    private void CheckObjectiveFailure()
    {
        if (_technicalFailure || _run == null || _run.Logic.Failure.Length == 0)
            return;
        if (_retryGuard == null)
        {
            RequestNativeStartupFailure(_run.Logic.Failure);
            return;
        }
        _retryFailure = _run.Logic.Failure;
        _retryShown = false;
        _retryGuard.Freeze();
        _director?.Pause();
    }

    private void ShowRetryFailure()
    {
        if (_retryBusy || _retryShown || _retryFailure.Length == 0)
            return;
        _retryShown = true;
        var name = _checkpoint?.Id is not { Length: > 0 }
            ? "Mission start"
            : _descriptor!.Layout.Checkpoints.Find(c => c.Id == _checkpoint.Id)?.Name ?? "Checkpoint";
        _hud?.ShowFailure(
            _retryFailure,
            name,
            _retryBroken || _checkpoint == null
                ? null
                : () =>
                {
                    _ = RetryCheckpoint();
                },
            EndRetryAttempt
        );
    }

    private void ReleaseRetryHold()
    {
        _retryGuard!.Release();
        _director?.Resume();
        _encounters?.Resume();
    }

    private void BrokenRestore(Exception exception)
    {
        _retryBroken = true;
        _retryBusy = false;
        _retryShown = false;
        _retryFailure = "Checkpoint restoration stopped: " + exception.Message;
        _retryGuard?.Freeze();
        Plugin.Error(exception);
    }

    private async Task RetryCheckpoint()
    {
        if (_retryBusy || _retryBroken || _checkpoint == null || _lifetime == null)
            return;
        _retryBusy = true;
        var token = _lifetime.Token;
        var acquired = false;
        try
        {
            await _progressGate.WaitAsync(token);
            acquired = true;
            _hud?.SetStatus("Restoring checkpoint…");
            if (_technicalFailure)
            {
                await AcknowledgeEncounterFailure(token);
                if (_encounters != null)
                    await _encounters.WaitForWorkAsync();
            }
            else
                await DrainObservations(token);
            if (_retryDeath.HasValue)
                AcceptTransition(await MissionClient.TransitionAsync(_run!, "defeat", _revision, MissionClient.NewOperationId(), token));
            var previous = _run!;
            var prepared = await MissionClient.TransitionAsync(previous, "retry-prepare", _revision, MissionClient.NewOperationId(), token);
            MissionAcknowledgement.RequireRestore(previous, prepared.Run, prepared.Committed, preparing: true);
            if (prepared.Run!.CheckpointId != _checkpoint.Id)
                throw new InvalidDataException("The checkpoint identity changed.");
            _run = prepared.Run;
            _revision = prepared.Revision;
            _runtimeGeneration++;
            _director!.Dispose();
            _director = null;
            _encounters!.Reset(preserveWorld: true);
            _encounters = null;
            await _checkpoint.ClearAsync(_player!, token);
            var oldContext = _missionContext!;
            EncounterSpawnAdmissionGate.ClearMissionContext(oldContext);
            _missionContext = new EncounterRuntimeContext
            {
                SessionId = oldContext.SessionId,
                RaidId = oldContext.RaidId,
                LayoutId = oldContext.LayoutId,
                LayoutRevision = oldContext.LayoutRevision,
                Mode = oldContext.Mode,
                PreviewGeneration = oldContext.PreviewGeneration,
                AttemptGeneration = _run.AttemptGeneration,
                PublishedLayoutConfirmed = true,
            };
            EncounterSpawnAdmissionGate.SetMissionContext(_missionContext);
            _encounters = new EncounterPreviewRuntime { RetryGuard = _retryGuard };
            await _encounters.BeginAsync(_missionContext, _descriptor!.Layout, _player!, false, token, _run.EncounterToken);
            await _checkpoint.RestoreWorldAsync(_player!, _retryGuard!, token);
            await _encounters.RestoreAsync(_checkpoint.Encounters, _run.RestoredActorIds, token);
            _checkpoint.RestoreAccounting(_player!);
            await _checkpoint.RestoreAudioAsync(token);
            _director = new MissionDirector(_descriptor.Layout, _player!, _encounters);
            _director.BindInteractions(_scene!.MissionInteractions(_descriptor.Layout));
            _director.BindInteractions(_loot!.MissionInteractions(_descriptor.Layout));
            _director.Restore(_run.Logic);
            var committed = await MissionClient.TransitionAsync(_run, "retry-commit", _revision, MissionClient.NewOperationId(), token);
            MissionAcknowledgement.RequireRestore(_run, committed.Run, committed.Committed, preparing: false);
            _run = committed.Run;
            _revision = committed.Revision;
            _director.Accept(_run.Logic);
            if (_checkpoint.Id.Length == 0)
                _encounters.MissionStart();
            _progressOperations.Clear();
            _unacknowledgedSignals = null;
            _retryFailure = "";
            _retryDeath = null;
            _technicalFailure = _technicalFailureAcknowledged = false;
            _technicalFailureOperation = "";
            _retryShown = false;
            _retryBusy = false;
            _hud?.SetRoute(_run.NextCheckpointIndex, _descriptor.Layout.Checkpoints.Count, false, "Checkpoint restored");
            _checkpoint.RestoreTime();
            ReleaseRetryHold();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            BrokenRestore(exception);
        }
        finally
        {
            if (acquired)
                _progressGate.Release();
        }
    }

    private void EndRetryAttempt()
    {
        if (_retryBusy || _player == null)
            return;
        if (_technicalFailure)
        {
            _ = EndInterruptedAttempt();
            return;
        }
        _retryGuard?.Dispose();
        _retryGuard = null;
        if (_retryDeath.HasValue)
            _player.ActiveHealthController.Kill(_retryDeath.Value);
        else
            RequestNativeStartupFailure("Mission attempt ended");
    }

    private async Task EndInterruptedAttempt()
    {
        _retryBusy = true;
        var token = _lifetime!.Token;
        var acquired = false;
        try
        {
            await _progressGate.WaitAsync(token);
            acquired = true;
            await AcknowledgeEncounterFailure(token);
            if (_encounters != null)
                await _encounters.WaitForWorkAsync();
            _retryGuard?.Dispose();
            _retryGuard = null;
            RequestNativeStartupFailure("Mission interrupted by a technical AI failure");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            BrokenRestore(exception);
        }
        finally
        {
            if (acquired)
                _progressGate.Release();
            _retryBusy = false;
        }
    }
}
