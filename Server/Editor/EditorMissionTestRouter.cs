using Newtonsoft.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;
using WTT.Campaigns.Shared.Authoring;

namespace WTT.Campaigns.Server.Editor;

[Injectable]
public sealed class EditorMissionTestRouter(JsonUtil json, EditorMissionTestService tests)
    : StaticRouter(
        json,
        [
            new RouteAction<EditorTestMissionRouteRequest>(
                EditorTestRoutes.Mission,
                (_, request, profile, _, _) =>
                {
                    try
                    {
                        return new ValueTask<string>(JsonConvert.SerializeObject(tests.Handle(profile.ToString(), request)));
                    }
                    catch (Exception error)
                    {
                        return new ValueTask<string>(JsonConvert.SerializeObject(new EditorTestMissionResponse { Error = error.Message }));
                    }
                }
            ),
        ]
    );

public sealed class EditorTestMissionRouteRequest : EditorTestMissionRequest, SPTarkov.Server.Core.Models.Utils.IRequestData { }
