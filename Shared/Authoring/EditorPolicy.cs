namespace WTT.Campaigns.Shared.Authoring;

public static class EditorPolicy
{
    // Native read/setup endpoints remain available to the disposable identity.
    public static bool Blocks(string path) =>
        path.StartsWith("/wtt-campaigns/", StringComparison.Ordinal) && !path.StartsWith("/wtt-campaigns/editor/", StringComparison.Ordinal)
        || path == "/client/game/profile/items/moving"
        || path is "/client/quest/accept" or "/client/quest/complete" or "/client/quest/handover"
        || path is "/client/trading/api/buy" or "/client/trading/api/sell"
        || path is "/client/ragfair/offer/add" or "/client/ragfair/offer/remove"
        || path is "/client/hideout/production/start" or "/client/hideout/upgrade/start";
}
