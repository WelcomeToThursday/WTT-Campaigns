using WTT.Campaigns.Shared.Native;

namespace WTT.Campaigns.Client.Authoring;

internal static class ItemStackPosition
{
    internal static int Require(NativeItemLocation? location, bool ammoBox)
    {
        // SPT AddCartridgesToAmmoBox omits location for the bottom stack.
        if (location == null && ammoBox)
            return 0;
        if (location?.Slot is >= 0)
            return location.Slot.Value;
        throw new InvalidOperationException("Ammunition has no valid stack position.");
    }
}
