using Newtonsoft.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Bot;
using SPTarkov.Server.Core.Utils;
using WTT.Campaigns.Server.Profiles;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Shared.Authoring;

namespace WTT.Campaigns.Server.Editor;

public sealed class EditorEncounterProfilesRouteRequest : EditorEncounterProfilesRequest, SPTarkov.Server.Core.Models.Utils.IRequestData { }

[Injectable]
public sealed class EditorEncounterProfilesRouter(JsonUtil json, BotController bots, SeasonRepository repository, SeasonService seasons)
    : StaticRouter(
        json,
        [
            new RouteAction<EditorEncounterProfilesRouteRequest>(
                "/wtt-campaigns/editor/encounter-profiles",
                async (_, request, profile, _, _) =>
                {
                    try
                    {
                        if (request.Version != 1)
                            throw new InvalidOperationException("Update both editor components together.");
                        var transportIdentity = profile.ToString();
                        var session = EditorSessionRegistry
                            .Resolve(transportIdentity, request.SessionId, DateTimeOffset.UtcNow)
                            .RequireMap("generating encounter bots");
                        string location;
                        string draft;
                        if (
                            request.Count is < 1 or > 16
                            || request.Role is not ("assault" or "pmcUSEC" or "pmcBEAR")
                            || request.Difficulty is not ("easy" or "normal" or "hard" or "impossible")
                        )
                            throw new InvalidOperationException("Unsupported encounter roster request.");
                        using (seasons.Enter(session.Owner))
                        {
                            var current = EditorSessionRegistry.Resolve(transportIdentity, request.SessionId, DateTimeOffset.UtcNow);
                            if (!ReferenceEquals(current.Session, session))
                                throw new InvalidOperationException("Editor map session ended. Return to editor home and reconnect.");
                            session = current.RequireMap("generating encounter bots");
                            var now = DateTimeOffset.UtcNow;
                            session.Contact = now;
                            location = session.Location;
                            draft = session.Draft;
                            if (!repository.Load(draft).Definition.MapLayouts.Any(l => l.Id == request.LayoutId && l.Location == location))
                                throw new InvalidOperationException("Select an authored layout on this map before previewing encounters.");
                        }
                        var generated = (
                            await bots.Generate(
                                new MongoId(session.Profile),
                                new GenerateBotsRequestData
                                {
                                    Conditions =
                                    [
                                        new GenerateCondition
                                        {
                                            Role = request.Role,
                                            Difficulty = request.Difficulty,
                                            Limit = request.Count,
                                        },
                                    ],
                                }
                            )
                        ).Where(p => p != null).Take(request.Count).ToArray();
                        // Reject results completing after a session/map was retired.
                        using (seasons.Enter(session.Owner))
                        {
                            var current = EditorSessionRegistry.Resolve(transportIdentity, request.SessionId, DateTimeOffset.UtcNow);
                            if (!ReferenceEquals(current.Session, session))
                                throw new InvalidOperationException("The encounter preview session ended during bot generation.");
                            var active = current.RequireMap("generating encounter bots");
                            if (active.Location != location || active.Draft != draft)
                                throw new InvalidOperationException("The encounter preview map changed during bot generation.");
                            active.Contact = DateTimeOffset.UtcNow;
                        }
                        if (generated.Length != request.Count)
                            throw new InvalidOperationException("The installed bot generator returned an incomplete roster.");
                        return JsonConvert.SerializeObject(
                            new EditorEncounterProfilesResponse { ProfilesJson = json.Serialize(generated)! }
                        );
                    }
                    catch (Exception error)
                    {
                        return JsonConvert.SerializeObject(new EditorEncounterProfilesResponse { Error = error.Message });
                    }
                }
            ),
        ]
    );
