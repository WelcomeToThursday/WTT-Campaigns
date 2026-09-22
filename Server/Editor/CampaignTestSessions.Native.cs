using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Shared.Seasons;

namespace WTT.Campaigns.Server.Editor;

public sealed partial class CampaignTestSessions
{
    private async Task CreateProfile(string id, string owner, SeasonDefinition season)
    {
        var ownerProfile = saves.GetProfile(new MongoId(owner));
        var side = ownerProfile.CharacterData?.PmcData?.Info?.Side == "Bear" ? "Bear" : "Usec";
        var cosmetic = templates
            .Customization.Values.Where(v =>
                v.Parent is "5cc085e214c02e000c6bea67" or "5fc100cf95572123ae738483"
                && v.Properties.AvailableAsDefault
                && v.Properties.Side.Contains(side)
            )
            .GroupBy(v => v.Parent)
            .ToDictionary(g => g.Key, g => g.OrderBy(v => v.Id.ToString(), StringComparer.Ordinal).First().Id);
        if (
            !cosmetic.TryGetValue("5cc085e214c02e000c6bea67", out var head)
            || !cosmetic.TryGetValue("5fc100cf95572123ae738483", out var voice)
        )
            throw new InvalidOperationException("SPT has no default PMC appearance for a campaign test.");

        saves.CreateProfile(
            new SPTarkov.Server.Core.Models.Eft.Profile.Info
            {
                ProfileId = new MongoId(id),
                ScavengerId = new MongoId(SeasonRepository.NewId()),
                Aid = saves.GetProfiles().Values.Max(p => p.ProfileInfo?.Aid ?? 0) + 1,
                Username = "CampaignTest",
                Edition = ownerProfile.ProfileInfo!.Edition,
                IsWiped = false,
            }
        );
        await creator.CreateProfile(
            new MongoId(id),
            new ProfileCreateRequestData
            {
                Side = side,
                Nickname = "CampaignTest",
                HeadId = head,
                VoiceId = voice,
            }
        );
        await starting.Apply(id, side, season.Id);
    }

    private static bool EditorIdentityMatches(string sessionId, string identity)
    {
        var resolution = EditorSessionRegistry.Resolve(identity, sessionId, DateTimeOffset.UtcNow);
        return resolution.Session != null
            && resolution.Status is (EditorSessionRegistry.ResolutionStatus.Accepted or EditorSessionRegistry.ResolutionStatus.MissingMap);
    }

    private static EditorSessionRegistry.Session ResolveEditor(string identity, string sessionId, string draftId)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || !SeasonValidator.IsId(draftId))
            throw new InvalidOperationException("Choose an editor session and saved draft before starting a campaign test.");
        var resolution = EditorSessionRegistry.Resolve(identity, sessionId, DateTimeOffset.UtcNow);
        if (
            resolution.Session == null
            || resolution.Status
                is not (EditorSessionRegistry.ResolutionStatus.Accepted or EditorSessionRegistry.ResolutionStatus.MissingMap)
        )
            throw new InvalidOperationException(
                resolution.Status
                    is EditorSessionRegistry.ResolutionStatus.Expired
                        or EditorSessionRegistry.ResolutionStatus.NotReady
                        or EditorSessionRegistry.ResolutionStatus.RetiredScratch
                    ? "Editor session expired. Return to editor home and reconnect."
                    : "Editor session does not match."
            );
        var session = resolution.Session;
        session.Contact = DateTimeOffset.UtcNow;
        return session;
    }
}
