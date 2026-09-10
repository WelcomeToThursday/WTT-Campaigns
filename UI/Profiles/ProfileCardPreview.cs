using System;
using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.UI.Profiles;

public sealed class ProfileCardPreview : MonoBehaviour
{
    public Action<RawImage>? Load;
    public Transform? Host;
    private GameObject? _model;

    public void SetVisible(bool visible)
    {
        if (!visible)
        {
            Release();
            return;
        }
        if (_model || Load == null || !Host)
        {
            return;
        }

        var rect = UiElements.Rect("VisibleModel", Host!, 386, 740);
        _model = rect.gameObject;
        var image = _model.AddComponent<RawImage>();
        image.color = Color.clear;
        image.raycastTarget = false;
        Load(image);
    }

    private void Release()
    {
        if (_model)
        {
            _model!.SetActive(false);
            UiElements.Destroy(_model);
        }
        _model = null;
    }

    private void OnDisable()
    {
        Release();
    }
}
