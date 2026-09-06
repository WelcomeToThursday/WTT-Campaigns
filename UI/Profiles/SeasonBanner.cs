using System;
using SeasonalPerks.UI.Controls;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SeasonalPerks.UI.Profiles;

public sealed class SeasonBanner : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    private Image? _hover;
    private Image? _left;
    private Image? _bottom;
    private Image? _sweep;
    private Text? _caption;
    private bool _over;
    private float _time;
    public Action? Clicked;
    public Action<bool>? HoverChanged;

    public void Initialize(Font font, Func<string, Sprite?> artwork, Action<string, RawImage, bool, Image?>? video)
    {
        var rect = (RectTransform)transform;
        UiElements.Fill(rect, Color.clear, true);
        var mask = UiElements.Rect("BannerArtwork", rect, 440, 112);
        mask.gameObject.AddComponent<RectMask2D>();
        Image Layer(string name, string asset, float width, float height, float x = 0)
        {
            var image = UiElements.Fill(UiElements.Rect(name, mask, width, height, x), Color.white);
            image.sprite = artwork(asset);
            image.type = Image.Type.Sliced;
            return image;
        }
        Layer("Background", "sharedassets44-931", 440, 112);
        _hover = Layer("HoverBackground", "sharedassets44-942", 440, 112);
        _left = Layer("LeftGlow", "sharedassets44-830", 323, 112, -58);
        _bottom = Layer("BottomGlow", "sharedassets44-644", 440, 112);
        _sweep = Layer("Sweep", "sharedassets44-935", 400, 112, -250);
        Layer("Pattern", "sharedassets44-948", 256, 67, 92);
        var logo = Layer("Logo", "sharedassets44-643", 360, 120);
        logo.type = Image.Type.Simple;
        logo.preserveAspect = true;
        if (video != null)
        {
            var raw = UiElements.Rect("AnimatedLogo", mask, 360, 120).gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            raw.color = Color.clear;
            video("Season_1_logo_video_1380x460.webm", raw, true, logo);
        }
        _caption = new UiElements(font).Label(mask, "HoverCaption", "BATTLE PASS  ›", 15, 390, 22, 0, -42);
        _caption.alignment = TextAnchor.MiddleRight;
        Apply(0);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_over)
        {
            return;
        }

        _over = true;
        _time = 0;
        HoverChanged?.Invoke(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!_over)
        {
            return;
        }

        _over = false;
        HoverChanged?.Invoke(false);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            Clicked?.Invoke();
        }
    }

    private void Update()
    {
        _time += Time.unscaledDeltaTime;
        Apply(Time.unscaledDeltaTime);
    }

    private void Apply(float delta)
    {
        void Alpha(Graphic? image, float target, float duration)
        {
            if (!image)
            {
                return;
            }

            var c = image!.color;
            c.a = delta == 0 ? target : Mathf.MoveTowards(c.a, target, delta / duration);
            image.color = c;
        }
        Alpha(_hover, _over ? 1 : 0, .3f);
        Alpha(_left, _over ? .5f : .33f, .4f);
        Alpha(_bottom, _over ? .36f : .22f, .6f);
        Alpha(_caption, _over ? 1 : 0, .25f);
        if (_sweep)
        {
            var phase = _time % 2.3f;
            var moving = phase < .5f;
            var x = moving ? Mathf.Lerp(-250, 250, phase / .5f) : -250;
            _sweep!.rectTransform.anchoredPosition = new Vector2(x, 0);
            var color = _sweep.color;
            color.a = _over ? (moving ? Mathf.Lerp(.8f, .05f, phase / .5f) : Mathf.Lerp(.05f, .8f, (phase - .5f) / 1.8f)) : 0;
            _sweep.color = color;
        }
    }

    private void OnDisable()
    {
        _over = false;
        _time = 0;
        Apply(0);
        HoverChanged?.Invoke(false);
    }
}
