using UnityEngine;

namespace WTT.Campaigns.UI.Media;

public sealed class StoryRoomRenderState : MonoBehaviour
{
    public StoryEnvironmentState? Environment;
    private StoryEnvironmentState? _previous;
    private static readonly string[] AmbientNames = { "_SHAr", "_SHAg", "_SHAb", "_SHBr", "_SHBg", "_SHBb", "_SHC", "_EFT_Ambient" };
    private readonly Vector4[] _ambient = new Vector4[8];

    private void OnPreCull() => BeginRender();

    public void BeginRender()
    {
        if (Environment == null || _previous != null)
            return;
        _previous = StoryEnvironmentState.Capture();
        for (var i = 0; i < AmbientNames.Length; i++)
            _ambient[i] = Shader.GetGlobalVector(AmbientNames[i]);
        Environment.Apply();
        var sh = RenderSettings.ambientProbe;
        var top = Vector4.zero;
        for (var c = 0; c < 3; c++)
        {
            Shader.SetGlobalVector(AmbientNames[c], new Vector4(sh[c, 3], sh[c, 1], sh[c, 2], sh[c, 0] - sh[c, 6]));
            Shader.SetGlobalVector(AmbientNames[c + 3], new Vector4(sh[c, 4], sh[c, 5], sh[c, 6] * 3, sh[c, 7]));
            var color = sh[c, 1] + sh[c, 0] - sh[c, 6] - sh[c, 8];
            top[c] = QualitySettings.activeColorSpace == ColorSpace.Gamma ? color : Mathf.LinearToGammaSpace(color);
        }
        Shader.SetGlobalVector("_SHC", new Vector4(sh[0, 8], sh[1, 8], sh[2, 8], 1));
        Shader.SetGlobalVector("_EFT_Ambient", top);
    }

    private void OnPostRender() => Restore();

    private void OnDisable() => Restore();

    public void Restore()
    {
        if (_previous == null)
            return;
        _previous.Apply();
        for (var i = 0; i < AmbientNames.Length; i++)
            Shader.SetGlobalVector(AmbientNames[i], _ambient[i]);
        _previous = null;
    }
}
