using Newtonsoft.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Missions;

namespace WTT.Campaigns.Server.Missions;

[Injectable]
public sealed class MissionRouter(JsonUtil json, MissionService missions) : StaticRouter(json, Routes(missions))
{
    private static List<RouteAction> Routes(MissionService missions) =>
        [
            new RouteAction<MissionRouteRequest>(
                "/wtt-campaigns/missions/list",
                (_, request, id, _, _) => Respond(() => Task.FromResult(missions.Read(id.ToString(), request)))
            ),
            new RouteAction<MissionRouteRequest>(
                "/wtt-campaigns/missions/prepare",
                (_, request, id, _, _) => Respond(() => missions.Prepare(id.ToString(), request))
            ),
            new RouteAction<MissionRouteRequest>(
                "/wtt-campaigns/missions/descriptor",
                (_, request, id, _, _) => Respond(() => Task.FromResult(missions.Descriptor(id.ToString(), request)))
            ),
            new RouteAction<MissionRouteRequest>(
                "/wtt-campaigns/missions/progress",
                (_, request, id, _, _) => Respond(() => missions.Progress(id.ToString(), request))
            ),
            new RouteAction<MissionRouteRequest>(
                "/wtt-campaigns/missions/cancel",
                (_, request, id, _, _) => Respond(() => missions.Cancel(id.ToString(), request))
            ),
            new RouteAction<MissionEncounterProfilesRouteRequest>(
                "/wtt-campaigns/missions/encounter-profiles",
                (_, request, id, _, _) => RespondProfiles(() => missions.EncounterProfiles(id.ToString(), request))
            ),
        ];

    private static async ValueTask<string> Respond(Func<Task<MissionResponse>> action)
    {
        try
        {
            return JsonConvert.SerializeObject(await action());
        }
        catch (InvalidOperationException e)
        {
            return JsonConvert.SerializeObject(new MissionResponse { Error = e.Message });
        }
    }

    private static async ValueTask<string> RespondProfiles(Func<Task<EditorEncounterProfilesResponse>> action)
    {
        try
        {
            return JsonConvert.SerializeObject(await action());
        }
        catch (InvalidOperationException e)
        {
            return JsonConvert.SerializeObject(new EditorEncounterProfilesResponse { Error = e.Message });
        }
    }
}
