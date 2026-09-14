namespace WTT.Campaigns.Client.Authoring;

internal static class EditorRenderSize
{
    internal static bool CanProcess(bool editor, int width, int height, int downsample = 4) =>
        !editor || (width >= System.Math.Max(4, downsample) && height >= System.Math.Max(4, downsample));
}
