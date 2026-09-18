using Newtonsoft.Json;
using SPT.Common.Http;
using WTT.Campaigns.Shared.Authoring;

namespace WTT.Campaigns.Client.Authoring;

/// <summary>Transport for a draft-backed editor mission rehearsal.</summary>
internal static class EditorMissionTestClient
{
    internal static Task<EditorTestMissionResponse> PrepareCheckpointsAsync(string draftId, string layoutId) =>
        PostAsync(
            new()
            {
                SessionId = EditorMode.SessionId,
                DraftId = draftId,
                LayoutId = layoutId,
                Action = EditorTestActions.PrepareCheckpoints,
                UseEncounters = true,
            },
            CancellationToken.None
        );

    internal static Task<EditorTestMissionResponse> ObserveAsync(
        EditorTestMissionResponse run,
        List<WTT.Campaigns.Shared.Missions.MissionSignal> signals,
        List<WTT.Campaigns.Shared.Missions.MissionActor> actors,
        string operationId,
        CancellationToken token
    ) =>
        PostAsync(
            new EditorTestMissionRequest
            {
                SessionId = run.SessionId,
                DraftId = run.DraftId,
                LayoutId = run.LayoutId,
                MissionId = run.MissionId,
                RunId = run.RunId,
                Action = EditorTestActions.Progress,
                Kind = "Observations",
                OperationId = operationId,
                Signals = signals,
                Actors = actors,
                AttemptGeneration = run.Run?.AttemptGeneration ?? 1,
            },
            token
        );

    internal static async Task<EditorTestMissionResponse> PrepareAsync(
        string draftId,
        string layoutId,
        string missionId = "",
        bool useEncounters = true,
        CancellationToken cancellationToken = default
    )
    {
        return await PostAsync(
            new EditorTestMissionRequest
            {
                SessionId = EditorMode.SessionId,
                DraftId = draftId,
                LayoutId = layoutId,
                MissionId = missionId,
                Action = EditorTestActions.Prepare,
                UseEncounters = useEncounters,
            },
            cancellationToken
        );
    }

    internal static async Task<EditorTestMissionResponse> ProgressAsync(
        EditorTestMissionResponse run,
        string checkpointId,
        string kind,
        CancellationToken cancellationToken = default
    )
    {
        if (run == null || string.IsNullOrWhiteSpace(run.RunId))
            throw new InvalidOperationException("The editor mission test run is unavailable.");
        return await PostAsync(
            new EditorTestMissionRequest
            {
                SessionId = run.SessionId,
                DraftId = run.DraftId,
                LayoutId = run.LayoutId,
                MissionId = run.MissionId,
                RunId = run.RunId,
                CheckpointId = checkpointId,
                AttemptGeneration = run.Run?.AttemptGeneration ?? 1,
                Kind = kind,
                OperationId = OperationId(run.RunId + ":" + (run.Run?.AttemptGeneration ?? 1), kind, checkpointId),
                Action = EditorTestActions.Progress,
            },
            cancellationToken
        );
    }

    internal static async Task<EditorTestMissionResponse> EndAsync(
        EditorTestMissionResponse run,
        CancellationToken cancellationToken = default
    )
    {
        if (run == null || string.IsNullOrWhiteSpace(run.RunId))
            throw new InvalidOperationException("The editor mission test run is unavailable.");
        return await PostAsync(
            new EditorTestMissionRequest
            {
                SessionId = run.SessionId,
                DraftId = run.DraftId,
                LayoutId = run.LayoutId,
                MissionId = run.MissionId,
                RunId = run.RunId,
                Action = EditorTestActions.End,
                AttemptGeneration = run.Run?.AttemptGeneration ?? 1,
            },
            cancellationToken
        );
    }

    internal static Task<EditorTestMissionResponse> ResetAsync(EditorTestMissionResponse run, CancellationToken cancellationToken = default)
    {
        if (run == null || string.IsNullOrWhiteSpace(run.RunId))
            throw new InvalidOperationException("The editor mission test run is unavailable.");
        return PostAsync(
            new EditorTestMissionRequest
            {
                SessionId = run.SessionId,
                DraftId = run.DraftId,
                LayoutId = run.LayoutId,
                MissionId = run.MissionId,
                RunId = run.RunId,
                Action = EditorTestActions.Reset,
            },
            cancellationToken
        );
    }

    private static async Task<EditorTestMissionResponse> PostAsync(EditorTestMissionRequest request, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await SendAsync(request, cancellationToken);
            }
            catch (Exception error)
                when (request.Action == EditorTestActions.Progress
                    && attempt < 4
                    && error is not InvalidOperationException
                    && error is not OperationCanceledException
                )
            {
                await Cysharp.Threading.Tasks.UniTask.Delay(
                    250 * (attempt + 1),
                    delayType: Cysharp.Threading.Tasks.DelayType.Realtime,
                    cancellationToken: cancellationToken
                );
            }
        }
    }

    private static async Task<EditorTestMissionResponse> SendAsync(EditorTestMissionRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        request.Version = 2;
        if (string.IsNullOrWhiteSpace(request.SessionId))
            throw new InvalidOperationException("The editor session is unavailable.");
        var traceTransition =
            request.Action != EditorTestActions.Progress || !request.Kind.Equals("Observations", StringComparison.OrdinalIgnoreCase);
        if (traceTransition)
            Plugin.LogInfo($"Mission test request: {request.Action}/{request.Kind}, attempt={request.AttemptGeneration}");
        var response = JsonConvert.DeserializeObject<EditorTestMissionResponse>(
            await RequestHandler.PostJsonAsync(EditorTestRoutes.Mission, JsonConvert.SerializeObject(request))
        );
        if (traceTransition)
            Plugin.LogInfo(
                $"Mission test response: {request.Action}/{request.Kind}, requested attempt={request.AttemptGeneration}, "
                    + $"status={response?.Run?.Status}, attempt={response?.Run?.AttemptGeneration}, restoring={response?.Run?.Restoring}, "
                    + $"committed={response?.Committed}, replayed={response?.Replayed}, checkpoints={response?.Run?.NextCheckpointIndex}, "
                    + $"exit={response?.Run?.ExitReached}, defeated={response?.Run?.PlayerDefeated}, error={response?.Error}"
            );
        cancellationToken.ThrowIfCancellationRequested();
        if (response == null)
            throw new InvalidDataException("The editor mission test returned an empty response.");
        if (response.Version != 2)
            throw new InvalidDataException("The editor mission test returned an unsupported response version.");
        if (!string.IsNullOrWhiteSpace(response.Error))
            throw new InvalidOperationException(response.Error);
        if (response.SessionId.Length > 0 && !string.Equals(response.SessionId, request.SessionId, StringComparison.Ordinal))
            throw new InvalidDataException("The editor mission test belongs to another session.");
        return response;
    }

    private static string OperationId(string runId, string kind, string id) => runId + ":" + kind.Trim().ToLowerInvariant() + ":" + id;
}
