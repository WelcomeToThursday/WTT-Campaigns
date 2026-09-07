using UnityEngine;

namespace SeasonalPerks.Client.UI;

public sealed class LoadingTextAnimation : MonoBehaviour
{
    // Brightness sampled across the native pveLoadingAnimation sprite loop
    // (sharedassets49.assets, clip 98). Keep the text legible at its dimmest.
    private static readonly float[] Brightness =
    {
        .572f, .650f, .793f, .936f, .992f, .866f, .632f,
        .727f, .698f, .664f, .635f, .601f, .572f,
    };

    private Animator _logo = null!;
    private CanvasGroup _text = null!;

    internal void Initialize(Animator logo, CanvasGroup text)
    {
        _logo = logo;
        _text = text;
    }

    private void LateUpdate()
    {
        if (!_logo || !_text || !_logo.isActiveAndEnabled)
        {
            return;
        }

        // Follow the logo's actual playback phase, including pauses and restarts.
        var phase = Mathf.Repeat(_logo.GetCurrentAnimatorStateInfo(0).normalizedTime, 1);
        var position = phase * (Brightness.Length - 1);
        var index = Mathf.Min(Mathf.FloorToInt(position), Brightness.Length - 2);
        var brightness = Mathf.Lerp(Brightness[index], Brightness[index + 1], position - index);
        _text.alpha = Mathf.Lerp(.65f, 1, Mathf.InverseLerp(.572f, .992f, brightness));
    }
}
