using Newtonsoft.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;
using WTT.Campaigns.Server.Profiles;

namespace WTT.Campaigns.Server.Routing;

public record AppearanceRequest : IRequestData
{
    public string ProfileId { get; set; } = "";
    public string HeadId { get; set; } = "";
    public string VoiceId { get; set; } = "";
}

[Injectable]
public sealed class AppearanceRouter(JsonUtil json, SaveServer saves, TemplateTable templates, SeasonService seasons)
    : StaticRouter(
        json,
        [
            new RouteAction<AppearanceRequest>(
                "/wtt-campaigns/appearance",
                async (_, request, session, _, _) =>
                {
                    try
                    {
                        var root = seasons.ResolveRoot(session.ToString());
                        using var lease = seasons.Enter(root);
                        // Bind to the authenticated, active character; never accept a target account from the body.
                        if (request.ProfileId != session.ToString() || seasons.EffectiveId(root) != session.ToString())
                        {
                            throw new InvalidOperationException("The active character changed. Reopen Customization.");
                        }
                        seasons.EnsureNotInRaid(session.ToString());
                        if (saves.IsProfileInvalidOrUnloadable(session))
                        {
                            throw new InvalidOperationException("The character cannot be saved.");
                        }
                        var pmc =
                            saves.GetProfile(session).CharacterData?.PmcData
                            ?? throw new InvalidOperationException("The character is unavailable.");
                        var side = pmc.Info?.Side ?? throw new InvalidOperationException("The character's faction is unavailable.");
                        var customization =
                            pmc.Customization ?? throw new InvalidOperationException("The character's appearance is unavailable.");
                        await AppearanceSelection.Apply(
                            templates.Customization,
                            side,
                            customization,
                            request.HeadId,
                            request.VoiceId,
                            async () =>
                            {
                                await saves.SaveProfileAsync(session);
                            }
                        );
                        return JsonConvert.SerializeObject(
                            new
                            {
                                request.ProfileId,
                                request.HeadId,
                                request.VoiceId,
                            }
                        );
                    }
                    catch (Exception exception)
                    {
                        return JsonConvert.SerializeObject(new { Error = exception.Message });
                    }
                }
            ),
        ]
    );
