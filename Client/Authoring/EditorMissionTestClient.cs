using Newtonsoft.Json;
using SPT.Common.Http;
using WTT.Campaigns.Shared.Authoring;

namespace WTT.Campaigns.Client.Authoring;

/// <summary>Transport for a draft-backed editor mission rehearsal.</summary>
internal static class EditorMissionTestClient
{
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
                Kind = kind,
                OperationId = OperationId(run.RunId, kind, checkpointId),
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
            },
            cancellationToken
        );
    }

    internal static Task<EditorTestMissionResponse> ResetAsync(
        EditorTestMissionResponse run,
        CancellationToken cancellationToken = default
    )
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

    private static async Task<EditorTestMissionResponse> PostAsync(
        EditorTestMissionRequest request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        request.Version = 1;
        if (string.IsNullOrWhiteSpace(request.SessionId))
            throw new InvalidOperationException("The editor session is unavailable.");
        var response = JsonConvert.DeserializeObject<EditorTestMissionResponse>(
            await RequestHandler.PostJsonAsync(EditorTestRoutes.Mission, JsonConvert.SerializeObject(request))
        );
        cancellationToken.ThrowIfCancellationRequested();
        if (response == null)
            throw new InvalidDataException("The editor mission test returned an empty response.");
        if (response.Version != 1)
            throw new InvalidDataException("The editor mission test returned an unsupported response version.");
        if (!string.IsNullOrWhiteSpace(response.Error))
            throw new InvalidOperationException(response.Error);
        if (response.SessionId.Length > 0 && !string.Equals(response.SessionId, request.SessionId, StringComparison.Ordinal))
            throw new InvalidDataException("The editor mission test belongs to another session.");
        return response;
    }

    private static string OperationId(string runId, string kind, string id) =>
        runId + ":" + kind.Trim().ToLowerInvariant() + ":" + id;
}
