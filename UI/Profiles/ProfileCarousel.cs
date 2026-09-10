using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WTT.Campaigns.UI.Profiles;

public sealed class ProfileCarousel : MonoBehaviour, IScrollHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private readonly List<RectTransform> _cards = new List<RectTransform>();
    private readonly List<CanvasGroup> _groups = new List<CanvasGroup>();
    private float _position;
    private float _target;
    private bool _dragging;
    public Func<bool>? CanNavigate;
    public Text? Counter;

    public void Add(RectTransform card)
    {
        _cards.Add(card);
        _groups.Add(card.gameObject.AddComponent<CanvasGroup>());
    }

    public void Focus(int index)
    {
        _position = _target = Mathf.Max(0, index);
        Layout();
    }

    public void Move(int direction)
    {
        if (CanNavigate?.Invoke() == false || _cards.Count < 2)
        {
            return;
        }

        _target = Mathf.Round(_target) + direction;
    }

    public void OnScroll(PointerEventData data)
    {
        var delta = Mathf.Abs(data.scrollDelta.x) > Mathf.Abs(data.scrollDelta.y) ? -data.scrollDelta.x : data.scrollDelta.y;
        if (Mathf.Abs(delta) > .01f)
        {
            Move(delta > 0 ? -1 : 1);
        }
    }

    public void OnBeginDrag(PointerEventData data)
    {
        _dragging = data.button == PointerEventData.InputButton.Left && CanNavigate?.Invoke() != false;
        if (_dragging)
        {
            _target = _position;
        }
    }

    public void OnDrag(PointerEventData data)
    {
        if (!_dragging || CanNavigate?.Invoke() == false)
        {
            return;
        }

        var scale = Mathf.Max(.1f, transform.lossyScale.x);
        _target -= data.delta.x / (430 * scale);
        _position = _target;
        Layout();
    }

    public void OnEndDrag(PointerEventData data)
    {
        _dragging = false;
        _target = Mathf.Round(_target);
    }

    private void Update()
    {
        if (CanNavigate?.Invoke() == false)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.LeftArrow))
        {
            Move(-1);
        }

        if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            Move(1);
        }

        if (!_dragging)
        {
            _position = Mathf.Lerp(_position, _target, 1 - Mathf.Exp(-12 * Time.unscaledDeltaTime));
        }

        if (Mathf.Abs(_position - _target) < .001f)
        {
            _position = _target;
        }
        // Keep long-running navigation precise after many revolutions.
        if (_cards.Count > 0 && Mathf.Abs(_target) > _cards.Count * 4)
        {
            var turns = Mathf.Floor(_target / _cards.Count) * _cards.Count;
            _target -= turns;
            _position -= turns;
        }
        Layout();
    }

    private void Layout()
    {
        if (_cards.Count == 0)
        {
            return;
        }

        var depths = new float[_cards.Count];
        var edge = Mathf.Min(_cards.Count * .5f, 2.5f);
        for (var i = 0; i < _cards.Count; i++)
        {
            var offset = Mathf.Repeat(i - _position + _cards.Count * .5f, _cards.Count) - _cards.Count * .5f;
            var distance = Mathf.Abs(offset);
            var angle = offset * .49f;
            var scale = Mathf.Lerp(.70f, 1, Mathf.Clamp01(1 - distance / 3));
            _cards[i].anchoredPosition = new Vector2(Mathf.Sin(angle) * 940, -distance * 12);
            _cards[i].localScale = Vector3.one * scale;
            _groups[i].alpha = Mathf.Clamp01((edge - distance) / .40f) * Mathf.Lerp(1, .65f, distance / edge);
            _groups[i].blocksRaycasts = _groups[i].interactable =
                !_dragging && Mathf.Abs(_position - _target) < .03f && _groups[i].alpha > .45f;
            _cards[i].GetComponent<ProfileCardPreview>()?.SetVisible(_groups[i].alpha > .04f);
            depths[i] = distance;
        }
        foreach (var i in Enumerable.Range(0, _cards.Count).OrderByDescending(i => depths[i]))
        {
            _cards[i].SetAsLastSibling();
        }

        if (Counter)
        {
            Counter!.text = (Mathf.RoundToInt(Mathf.Repeat(_target, _cards.Count)) % _cards.Count + 1) + " / " + _cards.Count;
        }
    }
}
