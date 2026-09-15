namespace WTT.Campaigns.Client.Encounters;

internal static class EncounterMovementPolicy
{
    internal static bool ReturnToAnchor(bool returning, float squaredDistance) => squaredDistance > (returning ? 1.5f * 1.5f : 3f * 3f);

    internal static float Speed(bool run) => run ? 1f : .35f;

    internal static bool KeepPath(float secondsWithoutProgress) => secondsWithoutProgress < 3f;
}
