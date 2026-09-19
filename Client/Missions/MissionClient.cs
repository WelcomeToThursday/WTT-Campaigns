using Cysharp.Threading.Tasks;
using Diz.Jobs;
using Newtonsoft.Json;
using SPT.Common.Http;
using WTT.Campaigns.Shared.Missions;
using WTT.Campaigns.Shared.Profiles;

namespace WTT.Campaigns.Client.Missions;

/// <summary>Authenticated client transport for the mission list and one active run.</summary>
internal static class MissionClient
{
    internal static string CharacterId => Plugin.Current?.EffectiveProfileId ?? Plugin.App?.Session?.Profile?.Id ?? "";
    internal static string SeasonId => Plugin.Current?.ActiveMode == "seasonal" ? Plugin.Current.SeasonId : MissionLibrary.StandaloneScope;

    internal static Task<MissionResponse> ListAsync(CancellationToken cancellationToken = default) =>
        PostAsync("/wtt-campaigns/missions/list", new MissionRequest(), cancellationToken);

    internal static Task<MissionResponse> PrepareAsync(
        string missionId,
        long expectedRevision,
        CancellationToken cancellationToken = default
    ) => PrepareAsync(missionId, expectedRevision, NewOperationId(), cancellationToken);

    internal static Task<MissionResponse> PrepareAsync(
        string missionId,
        long expectedRevision,
        string operationId,
        CancellationToken cancellationToken = default
    ) =>
        PostMutationAsync(
            "/wtt-campaigns/missions/prepare",
            new MissionRequest
            {
                MissionId = missionId,
                ExpectedRevision = expectedRevision,
                OperationId = operationId,
            },
            cancellationToken
        );

    internal static Task<MissionResponse> DescriptorAsync(
        string missionId,
        string runId,
        string raidId,
        CancellationToken cancellationToken = default
    ) =>
        PostAsync(
            "/wtt-campaigns/missions/descriptor",
            new MissionRequest
            {
                MissionId = missionId,
                RunId = runId,
                RaidId = raidId,
            },
            cancellationToken
        );

    internal static Task<MissionResponse> ProgressAsync(
        string missionId,
        string runId,
        string raidId,
        string checkpointId,
        string kind,
        long expectedRevision,
        CancellationToken cancellationToken = default
    ) => ProgressAsync(missionId, runId, raidId, checkpointId, kind, expectedRevision, NewOperationId(), cancellationToken);

    internal static Task<MissionResponse> ProgressAsync(
        string missionId,
        string runId,
        string raidId,
        string checkpointId,
        string kind,
        long expectedRevision,
        string operationId,
        CancellationToken cancellationToken = default,
        long attemptGeneration = 1
    ) =>
        PostMutationAsync(
            "/wtt-campaigns/missions/progress",
            new MissionRequest
            {
                AttemptGeneration = attemptGeneration,
                MissionId = missionId,
                RunId = runId,
                RaidId = raidId,
                CheckpointId = checkpointId,
                Kind = kind,
                ExpectedRevision = expectedRevision,
                OperationId = operationId,
            },
            cancellationToken
        );

    internal static Task<MissionResponse> CancelAsync(
        string missionId,
        string runId,
        string raidId,
        long expectedRevision,
        CancellationToken cancellationToken = default,
        long attemptGeneration = 1
    ) => CancelAsync(missionId, runId, raidId, expectedRevision, NewOperationId(), cancellationToken, attemptGeneration);

    internal static Task<MissionResponse> CancelAsync(
        string missionId,
        string runId,
        string raidId,
        long expectedRevision,
        string operationId,
        CancellationToken cancellationToken = default,
        long attemptGeneration = 1
    ) =>
        PostMutationAsync(
            "/wtt-campaigns/missions/cancel",
            new MissionRequest
            {
                AttemptGeneration = attemptGeneration,
                MissionId = missionId,
                RunId = runId,
                RaidId = raidId,
                ExpectedRevision = expectedRevision,
                OperationId = operationId,
            },
            cancellationToken
        );

    internal static string NewOperationId() => Guid.NewGuid().ToString("N");

    internal static Task<MissionResponse> TransitionAsync(
        MissionRun run,
        string kind,
        long revision,
        string operationId,
        CancellationToken token
    ) => ProgressAsync(run.MissionId, run.RunId, run.RaidId, run.CheckpointId, kind, revision, operationId, token, run.AttemptGeneration);

    internal static Task<MissionResponse> ObserveAsync(
        MissionRun run,
        List<MissionSignal> signals,
        long revision,
        string operationId,
        CancellationToken token
    ) =>
        PostMutationAsync(
            "/wtt-campaigns/missions/progress",
            new MissionRequest
            {
                MissionId = run.MissionId,
                RunId = run.RunId,
                RaidId = run.RaidId,
                AttemptGeneration = run.AttemptGeneration,
                Kind = "Observations",
                Signals = signals,
                ExpectedRevision = revision,
                OperationId = operationId,
            },
            token
        );

    private static async Task<MissionResponse> PostMutationAsync(string route, MissionRequest request, CancellationToken cancellationToken)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                // Reuse the same operation identifier after a lost response. The
                // server receipt makes a committed mutation replayable.
                return await PostAsync(route, request, cancellationToken);
            }
            catch (Exception exception)
                when (attempt < 4 && exception is not InvalidOperationException && exception is not OperationCanceledException)
            {
                last = exception;
                await UniTask.Delay(250 * (attempt + 1), delayType: DelayType.Realtime, cancellationToken: cancellationToken);
            }
        }

        throw last ?? new InvalidOperationException("The mission operation was not acknowledged.");
    }

    private static async Task<MissionResponse> PostAsync(string route, MissionRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        request.Version = 3;
        request.SeasonId = SeasonId;
        request.CharacterId = CharacterId;
        if (request.SeasonId.Length == 0 || request.CharacterId.Length == 0)
            throw new InvalidOperationException("A loaded character is required for missions.");

        var response = JsonConvert.DeserializeObject<MissionResponse>(
            await RequestHandler.PostJsonAsync(route, JsonConvert.SerializeObject(request))
        );
        cancellationToken.ThrowIfCancellationRequested();
        if (response == null)
            throw new InvalidDataException("The mission server returned an empty response.");
        if (!string.IsNullOrWhiteSpace(response.Error))
            throw new InvalidOperationException(response.Error);
        if (response.Version != 3)
            throw new InvalidDataException("The mission server returned an unsupported response version.");
        if (response.SeasonId.Length > 0 && !string.Equals(response.SeasonId, request.SeasonId, StringComparison.Ordinal))
            throw new InvalidDataException("The mission response belongs to another campaign season.");
        if (response.CharacterId.Length > 0 && !string.Equals(response.CharacterId, request.CharacterId, StringComparison.Ordinal))
            throw new InvalidDataException("The mission response belongs to another campaign character.");
        return response;
    }
}
