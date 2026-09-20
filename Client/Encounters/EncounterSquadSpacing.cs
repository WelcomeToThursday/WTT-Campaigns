using System.Numerics;

namespace WTT.Campaigns.Client.Encounters;

internal static class EncounterSquadSpacing
{
    internal static float Pace(
        Vector3 position,
        Vector3 heading,
        Vector3 otherPosition,
        Vector3 otherHeading,
        bool yieldPriority,
        bool alreadyYielding
    )
    {
        var delta = otherPosition - position;
        if (MathF.Abs(delta.Y) > 1f)
            return 1;
        delta.Y = heading.Y = otherHeading.Y = 0;
        var distance = delta.Length();
        if (distance >= 3.5f || heading.LengthSquared() < .001f)
            return 1;
        heading = Vector3.Normalize(heading);
        var ahead = Vector3.Dot(delta, heading);
        var lateral = MathF.Abs(delta.X * heading.Z - delta.Z * heading.X);
        if (lateral > 1.1f || ahead < -.2f)
            return 1;
        var following = otherHeading.LengthSquared() > .001f && Vector3.Dot(heading, Vector3.Normalize(otherHeading)) > .25f;
        // Following traffic yields to whoever is physically ahead. Crossing,
        // opposing and overlapping traffic uses stable roster priority so both
        // bots cannot decide to wait for each other.
        if (following && ahead > .1f || yieldPriority && (!following || distance < .3f))
        {
            if (distance < (alreadyYielding ? 2.75f : 2f))
                return 0;
            return Math.Clamp(.65f + .35f * (distance - 2.75f) / .75f, .45f, 1f);
        }
        return 1;
    }
}
