namespace WTT.Campaigns.Client.Authoring.Scenes;

internal static class PreviewMaterialPolicy
{
    internal static bool UsesOpacity(string renderType, string shader, bool alphaTest, bool alphaBlend) =>
        alphaTest
        || alphaBlend
        || renderType.IndexOf("Transparent", StringComparison.OrdinalIgnoreCase) >= 0
        || shader.IndexOf("Cutout", StringComparison.OrdinalIgnoreCase) >= 0
        || shader.IndexOf("Transparent", StringComparison.OrdinalIgnoreCase) >= 0
        || shader.IndexOf("Foliage", StringComparison.OrdinalIgnoreCase) >= 0
        || shader.IndexOf("Leaves", StringComparison.OrdinalIgnoreCase) >= 0;
}
