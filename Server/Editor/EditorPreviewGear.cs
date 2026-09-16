using Newtonsoft.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;
using WTT.Campaigns.Server.Profiles;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Native;
using WTT.Campaigns.Shared.Serialization;

namespace WTT.Campaigns.Server.Editor;

public sealed class EditorPreviewGearRouteRequest : EditorPreviewGearRequest, SPTarkov.Server.Core.Models.Utils.IRequestData { }

[Injectable]
public sealed class EditorPreviewGearRouter(JsonUtil json, SaveServer saves, SeasonService seasons)
    : StaticRouter(
        json,
        [
            new RouteAction<EditorPreviewGearRouteRequest>(
                "/wtt-campaigns/editor/preview-gear",
                (_, request, profile, _, _) =>
                {
                    try
                    {
                        if (request.Version != 2)
                            throw new InvalidOperationException("Update both editor components together.");
                        var transportIdentity = profile.ToString();
                        var session = EditorSessionRegistry
                            .Resolve(transportIdentity, request.SessionId, DateTimeOffset.UtcNow)
                            .RequireMap("preparing a playtest");
                        using var lease = seasons.Enter(session.Owner);
                        var current = EditorSessionRegistry.Resolve(transportIdentity, request.SessionId, DateTimeOffset.UtcNow);
                        if (!ReferenceEquals(current.Session, session))
                            throw new InvalidOperationException("Editor map session ended. Return to editor home and reconnect.");
                        session = current.RequireMap("preparing a playtest");
                        var now = DateTimeOffset.UtcNow;
                        session.Contact = now;
                        if (request.UseProfileKit)
                        {
                            var source = saves.GetProfile(new MongoId(session.ReturnProfile)).CharacterData!.PmcData!.Inventory!;
                            var items = JsonConvert.DeserializeObject<List<NativeItem>>(json.Serialize(source.Items)!)!;
                            var equipment = source.Equipment.ToString();
                            var bindings = JsonConvert.DeserializeObject<Dictionary<string, string>>(json.Serialize(source.FastPanel)!);
                            return new ValueTask<string>(
                                JsonConvert.SerializeObject(EditorPreviewGearCopy.Copy(items, equipment, bindings))
                            );
                        }
                        return new ValueTask<string>(JsonConvert.SerializeObject(EditorPreviewGearCopy.Placeholder(session)));
                    }
                    catch (Exception error)
                    {
                        return new ValueTask<string>(JsonConvert.SerializeObject(new EditorPreviewGearResponse { Error = error.Message }));
                    }
                }
            ),
        ]
    );
