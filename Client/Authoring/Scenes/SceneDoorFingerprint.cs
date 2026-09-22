using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace WTT.Campaigns.Client.Authoring.Scenes;

internal static class SceneDoorFingerprint
{
    internal const string DiagnosticType = "DebugPlus.Utils.OverlayProvider";

    internal static string Legacy(string components, string position, string rotation, string scale, string meshes) =>
        Hash(components + "|" + position + "|" + rotation + "|" + scale + "|" + meshes);

    internal static string Stable(string components, string position, string closedRotation, string scale, string meshes, string hinge) =>
        Hash("door-v2|" + components + "|" + position + "|" + closedRotation + "|" + scale + "|" + meshes + "|" + hinge);

    internal static IEnumerable<string> LegacyComponents(string actual, string clean, int rootComponents, string name)
    {
        yield return actual;
        if (actual != clean)
            yield return clean;
        // Older captures included DebugPlus's inspection-only root overlay.
        // Reconstruct that exact old hash, never accept an ID-only fallback.
        var parts = new List<string>(clean.Split('|'));
        parts.Insert(rootComponents, DiagnosticType + ":" + name);
        yield return string.Join("|", parts);
    }

    private static string Hash(string evidence)
    {
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(evidence))).Replace("-", "");
    }
}
