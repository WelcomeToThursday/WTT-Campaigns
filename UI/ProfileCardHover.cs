using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SeasonalPerks.UI;

public sealed class ProfileCardHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public CanvasGroup? Glow;
    public CanvasGroup? Idle;
    public Image[] IdleFrames = new Image[0];
    public Image[] HoverFrames = new Image[0];
    public CanvasGroup? Details;
    public CanvasGroup? Model;
    public RectTransform? Info;
    public RectTransform? InfoBackground;
    public Text? Description;
    public bool Seasonal;
    public Action? Entered;
    private bool _hovered;
    private float _elapsed;

    public void OnPointerEnter(PointerEventData data)
    {
        if (_hovered)
        {
            return;
        }
        _hovered = true;
        Entered?.Invoke();
    }

    public void OnPointerExit(PointerEventData data) => _hovered = false;

    public void Apply(float blend)
    {
        if (Idle)
        {
            Idle!.alpha = 1 - blend;
        }
        if (Glow)
        {
            Glow!.alpha = blend;
        }
        if (Details)
        {
            Details!.alpha = blend;
            Details.blocksRaycasts = blend > .95f;
            Details.interactable = blend > .95f;
        }
        if (Model)
        {
            Model!.alpha = Seasonal ? 1 - blend * .78f : 1;
        }
        if (Info && Seasonal)
        {
            Info!.anchoredPosition = new Vector2(0, Mathf.Lerp(-150, 305, blend));
        }
        if (Description)
        {
            Description!.color = new Color(.584f, .620f, .639f, Mathf.Lerp(.6f, 1, blend));
        }
        if (InfoBackground && Info)
        {
            // Keep the gradient connected to the card footer throughout the slide.
            InfoBackground!.sizeDelta = new Vector2(390, Info!.anchoredPosition.y + 424);
        }
    }

    public void AnimateGlow(float elapsed)
    {
        // Recovered FrameSequenceAnimator: one second hold, two second InOutSine fade.
        var frame = Mathf.FloorToInt(elapsed / 3) % 3;
        var fade = Mathf.Clamp01((elapsed % 3 - 1) / 2);
        fade = .5f - Mathf.Cos(fade * Mathf.PI) * .5f;
        for (var i = 0; i < IdleFrames.Length; i++)
        {
            var alpha =
                i == frame ? 1 - fade
                : i == (frame + 1) % 3 ? fade
                : 0;
            IdleFrames[i].color = new Color(1, 1, 1, alpha * .8f);
            HoverFrames[i].color = new Color(1, 1, 1, alpha);
        }
    }

    private void Update()
    {
        var blend = Glow ? Glow!.alpha : 0;
        Apply(Mathf.MoveTowards(blend, _hovered ? 1 : 0, Time.unscaledDeltaTime / .3f));
        _elapsed += Time.unscaledDeltaTime;
        AnimateGlow(_elapsed);
    }

    private void OnDisable()
    {
        _hovered = false;
        Apply(0);
    }
}
