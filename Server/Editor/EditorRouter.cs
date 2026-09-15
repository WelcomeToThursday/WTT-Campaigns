using Newtonsoft.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;
using WTT.Campaigns.Server.Profiles;
using WTT.Campaigns.Shared.Authoring;

namespace WTT.Campaigns.Server.Editor;

public sealed class EditorRouteRequest : EditorSessionRequest, SPTarkov.Server.Core.Models.Utils.IRequestData { }

[Injectable]
public sealed class EditorRouter(JsonUtil json, EditorSessions editor, SeasonService seasons) : StaticRouter(json, Routes(editor, seasons))
{
    private static List<RouteAction> Routes(EditorSessions editor, SeasonService seasons) =>
        new[] { "begin", "status", "select", "map", "unload", "end" }
            .Select(operation =>
                (RouteAction)
                    new RouteAction<EditorRouteRequest>(
                        "/wtt-campaigns/editor/" + operation,
                        async (_, request, id, _, _) =>
                        {
                            try
                            {
                                if (request.Version != 2)
                                    throw new InvalidOperationException("Update both editor components together.");
                                var owner = EditorSessions.Find(id.ToString())?.Owner ?? seasons.ResolveRoot(id.ToString());
                                using var lease = seasons.Enter(owner);
                                var response = operation switch
                                {
                                    "begin" => await editor.Begin(owner),
                                    "select" => editor.Select(owner, request),
                                    "map" => editor.Map(owner, request),
                                    "unload" => editor.Unload(owner, request),
                                    "end" => editor.End(owner, request),
                                    _ => editor.Status(owner, request),
                                };
                                return JsonConvert.SerializeObject(response);
                            }
                            catch (Exception e)
                            {
                                return JsonConvert.SerializeObject(new EditorSessionResponse { Error = e.Message });
                            }
                        }
                    )
            )
            .ToList();
}
