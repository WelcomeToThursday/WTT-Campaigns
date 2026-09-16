using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
using Newtonsoft.Json;
using WTT.Campaigns.Server.Profiles;

namespace WTT.Campaigns.Web.Tests;

internal static class CharacterRecoveryChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var page = typeof(WTT.Campaigns.Server.Web.Pages.CharacterRecovery);
        check(page.GetCustomAttributes<AuthorizeAttribute>().Any(a => a.Policy == "Administrator"), "Character recovery requires Administrator authorization");
        check(!page.GetCustomAttributes<AllowAnonymousAttribute>().Any(), "Character recovery cannot bypass authorization");
        var home = typeof(WTT.Campaigns.Server.Web.Pages.Home);
        check(home.GetCustomAttributes<RouteAttribute>().Any(a => a.Template == new WTT.Campaigns.Server.Metadata().HomePage), "Registered launcher link opens the mod home hub");
        var character = new SeasonCharacterLink { ProfileId = "character", SeasonId = "campaign", Created = true, Name = "Survivor", CreationOperationId = "receipt" };
        var sibling = new SeasonCharacterLink { ProfileId = "sibling", SeasonId = "other", Created = true };
        var source = new AccountLink
        {
            Characters = [character, sibling], Seasons = new() { ["campaign"] = character, ["other"] = sibling },
            SeasonalId = character.ProfileId, CurrentSeasonId = character.SeasonId, Created = true, Mode = "seasonal",
            ActiveRaidProfiles = [character.ProfileId], ActiveRaidIds = new() { [character.ProfileId] = "stale-raid" },
        };
        var target = new AccountLink { Characters = [sibling], SeasonalId = sibling.ProfileId, CurrentSeasonId = sibling.SeasonId, Created = true, Mode = "seasonal" };
        var sourceBefore = JsonConvert.SerializeObject(source);
        var targetBefore = JsonConvert.SerializeObject(target);
        var attached = CharacterRecovery.Attach(target, character);
        var detached = CharacterRecovery.Detach(source, character.ProfileId);
        check(JsonConvert.SerializeObject(source) == sourceBefore && JsonConvert.SerializeObject(target) == targetBefore, "Preparing recovery does not mutate cached links before persistence");
        check(attached.Characters.Count == 2 && attached.SeasonalId == sibling.ProfileId && attached.Mode == "seasonal", "Recovery preserves destination characters and current selection");
        check(attached.Characters.Single(c => c.ProfileId == character.ProfileId).CreationOperationId == "receipt", "Recovery preserves character creation receipts");
        check(detached.Characters.Single().ProfileId == sibling.ProfileId && detached.Seasons.Single().Value.ProfileId == sibling.ProfileId, "Recovery removes legacy ownership without removing siblings");
        check(detached.SeasonalId == null && detached.CurrentSeasonId == null && !detached.Created && detached.Mode == "normal", "Legacy selected character cannot resurrect transferred ownership");
        check(!detached.ActiveRaidProfiles.Contains(character.ProfileId) && !detached.ActiveRaidIds.ContainsKey(character.ProfileId), "Deleted account stale raid references are removed");
        check(CharacterRecovery.Attach(attached, character).Characters.Count == 2, "Interrupted attachment retries do not duplicate characters");
        check(JsonConvert.SerializeObject(CharacterRecovery.Detach(detached, character.ProfileId)) == JsonConvert.SerializeObject(detached), "Interrupted detachment retries are idempotent");
        void Reject(Action action, string message)
        {
            try { action(); }
            catch (InvalidOperationException) { check(true, message); return; }
            check(false, message);
        }
        Reject(() => CharacterRecovery.Attach(new() { RetiredCharacters = new() { ["character"] = "campaign" } }, character), "Retired destination ownership is rejected");
        Reject(() => CharacterRecovery.Attach(new() { Characters = [new() { ProfileId = "character", SeasonId = "different", Created = true }] }, character), "Conflicting campaign ownership is rejected");
        Reject(() => CharacterRecovery.Attach(target, new() { ProfileId = "wiped", Wiped = true }), "Wiped characters cannot be revived");
        var operation = new CharacterRecoveryOperation { CharacterId = "character", PreviousAccount = "old", TargetAccount = "new", Character = character };
        CharacterRecovery.ValidateOwnership("character", "old", "new", "old", 1, false, null);
        // Replay every persisted boundary: before links, after destination, after source,
        // and after character ownership was saved but journal removal failed.
        foreach (var owner in new[] { "old", "old", "old", "new" })
            CharacterRecovery.ValidateOwnership("character", "old", "new", owner, 1, false, operation);
        check(true, "Recovery permits replay before and after its final ownership commit");
        Reject(() => CharacterRecovery.ValidateOwnership("character", "old", "new", "old", 1, true, null), "Existing source account blocks transfer");
        Reject(() => CharacterRecovery.ValidateOwnership("character", "old", "other", "old", 1, false, operation), "Interrupted recovery cannot be redirected to another account");
        Reject(() => CharacterRecovery.ValidateOwnership("character", "old", "new", "new", 1, false, null), "A stale page cannot transfer an already recovered character");
        Reject(() => CharacterRecovery.ValidateOwnership("character", "old", "new", "old", 0, false, null), "Non-campaign profiles cannot be recovered");
    }

    internal static async Task CheckDeniedAction(Action<bool, string> check)
    {
        var page = new WTT.Campaigns.Server.Web.Pages.CharacterRecovery();
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = page.GetType();
        type.GetProperty("Authorization", flags)!.SetValue(page, new DeniedAuthorization());
        type.GetProperty("Authentication", flags)!.SetValue(page, new ViewerAuthentication());
        type.GetField("_confirmed", flags)!.SetValue(page, true);
        type.GetField("_target", flags)!.SetValue(page, "destination");
        type.GetField("_selected", flags)!.SetValue(page, new RecoverableCharacter("character", "Survivor", "campaign", "old", ""));
        await (Task)type.GetMethod("Recover", flags)!.Invoke(page, null)!;
        var error = (string)type.GetField("_error", flags)!.GetValue(page)!;
        check(error.StartsWith("Administrator access is required", StringComparison.Ordinal), "A non-administrator action is denied before any recovery service access");
    }

    private sealed class ViewerAuthentication : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    private sealed class DeniedAuthorization : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements) => Task.FromResult(AuthorizationResult.Failed());
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName) => Task.FromResult(AuthorizationResult.Failed());
    }
}
