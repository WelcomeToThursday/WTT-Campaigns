using System.Collections;
using EFT.Hideout;
using EFT.UI;
using SeasonalPerks.UI.Controls;
using UnityEngine;

namespace SeasonalPerks.Client.UI;

public sealed partial class SeasonUi
{
    private PveGameModeLoadingScreen? _switchLoader;
    private TaskCompletionSource<bool>? _switchLoadingFrame;

    private Task ShowSwitchLoader()
    {
        if (!_switchLoader)
        {
            // Keep the native screen above our cards and independent of the backend's
            // own show/fade calls, so it stays visible until the profile is verified.
            _switchLoader = Instantiate(PreloaderUI.Instance._pveLoadingScreen, _canvas!.transform, false);
            _switchLoader.name = "SeasonalSwitchLoadingScreen";
            UiElements.Stretch((RectTransform)_switchLoader.transform);
        }

        _switchLoader!.transform.SetAsLastSibling();
        _switchLoader.ShowGameObject(instant: true);
        _switchLoader.CanvasGroup.alpha = 1;
        _switchLoader.CanvasGroup.blocksRaycasts = true;
        _switchLoader._logoGroup.alpha = 1;
        _switchLoader._screenAnimator.Play("LoadingState", 0, 0);
        _switchLoader._screenAnimator.Update(0);

        var frame = new TaskCompletionSource<bool>();
        _switchLoadingFrame = frame;
        StartCoroutine(RenderSwitchLoader(frame));
        return frame.Task;
    }

    private static IEnumerator RenderSwitchLoader(TaskCompletionSource<bool> frame)
    {
        // Cross a full rendered frame even if the click arrived before coroutine
        // processing. Saving and reconnecting may do synchronous main-thread work.
        yield return null;
        yield return null;
        frame.TrySetResult(true);
    }

    private void HideSwitchLoader()
    {
        _switchLoadingFrame?.TrySetCanceled();
        _switchLoadingFrame = null;
        if (_switchLoader)
        {
            _switchLoader!.Close();
        }
    }
}
