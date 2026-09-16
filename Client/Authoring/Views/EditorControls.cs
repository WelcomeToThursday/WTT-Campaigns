using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

namespace WTT.Campaigns.Client.Authoring.Views;

// Typed accessors target Toolkit elements directly. No Component or GameObject proxies.
internal class EditorControl
{
    internal readonly VisualElement Element;

    internal EditorControl(VisualElement element) => Element = element;

    internal bool Visible
    {
        get => Element.style.display.value != DisplayStyle.None;
        set => Element.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
    }
    internal bool interactable
    {
        get => Element.enabledSelf;
        set => Element.SetEnabled(value);
    }
}

internal sealed class EditorLabel : EditorControl
{
    internal EditorLabel(Label label)
        : base(label)
    {
        label.enableRichText = false;
    }

    internal string text
    {
        get => ((Label)Element).text;
        set
        {
            if (text != value)
                ((Label)Element).text = value;
        }
    }
}

internal sealed class EditorButton : EditorControl
{
    internal readonly UnityEvent onClick = new();
    internal string Identity = "";
    private string? _pressedIdentity;
    internal bool Pressed;

    internal EditorButton(Button button)
        : base(button)
    {
        button.clicked += () => onClick.Invoke();
        button.RegisterCallback<PointerDownEvent>(
            evt =>
            {
                if (evt.button == 0)
                {
                    _pressedIdentity = Identity;
                    Pressed = true;
                }
            },
            TrickleDown.TrickleDown
        );
        button.RegisterCallback<PointerUpEvent>(_ => Pressed = false, TrickleDown.TrickleDown);
        button.RegisterCallback<PointerCaptureOutEvent>(_ => Pressed = false, TrickleDown.TrickleDown);
        button.RegisterCallback<DetachFromPanelEvent>(_ =>
        {
            Pressed = false;
            _pressedIdentity = null;
        });
    }

    internal string text
    {
        get => ((Button)Element).text;
        set
        {
            if (text != value)
                ((Button)Element).text = value;
        }
    }

    internal string Consume()
    {
        var id = _pressedIdentity ?? Identity;
        _pressedIdentity = null;
        return id;
    }
}

internal sealed class EditorInput : EditorControl
{
    internal EditorNumericDrag? NumericDrag;
    private bool _suppress;
    private string _committed = "";
    internal readonly UnityEvent<string> onEndEdit = new(),
        onValueChanged = new();

    internal EditorInput(TextField field, bool immediate = false)
        : base(field)
    {
        field.isDelayed = !immediate;
        field.RegisterValueChangedCallback(evt =>
        {
            if (_suppress)
                return;
            _committed = evt.newValue;
            if (immediate)
                onValueChanged.Invoke(evt.newValue);
            else
                onEndEdit.Invoke(evt.newValue);
        });
    }

    internal string text
    {
        get => ((TextField)Element).value;
        set => ((TextField)Element).value = value;
    }
    internal bool readOnly
    {
        get => ((TextField)Element).isReadOnly;
        set => ((TextField)Element).isReadOnly = value;
    }
    internal bool isFocused
    {
        get
        {
            if (NumericDrag?.Active == true)
                return true;
            var focus = Element.panel?.focusController.focusedElement as VisualElement;
            return focus != null && (focus == Element || Element.Contains(focus));
        }
    }

    internal void SetTextWithoutNotify(string text)
    {
        _committed = text;
        ((TextField)Element).SetValueWithoutNotify(text);
    }

    internal void CancelEdit()
    {
        NumericDrag?.Cancel();
        if (!isFocused)
            return;
        _suppress = true;
        ((TextField)Element).SetValueWithoutNotify(_committed);
        (Element.panel?.focusController.focusedElement as VisualElement)?.Blur();
        _suppress = false;
    }
}

internal sealed class EditorImage : EditorControl
{
    internal EditorImage(Image image)
        : base(image)
    {
        image.scaleMode = ScaleMode.ScaleToFit;
        image.pickingMode = PickingMode.Ignore;
    }

    internal string name => Element.name;
    internal Texture? texture
    {
        get => ((Image)Element).image;
        set
        {
            if (texture != value)
                ((Image)Element).image = value;
        }
    }
    internal Rect uvRect
    {
        get => ((Image)Element).uv;
        set
        {
            if (uvRect != value)
                ((Image)Element).uv = value;
        }
    }
    internal Color color
    {
        get => ((Image)Element).tintColor;
        set
        {
            if (color != value)
                ((Image)Element).tintColor = value;
        }
    }
}

internal sealed class EditorChoice : EditorControl
{
    internal sealed class OptionData
    {
        internal string text;

        internal OptionData(string text) => this.text = text;
    }

    internal readonly UnityEvent<int> onValueChanged = new();
    internal List<OptionData> options = new();
    internal int value;

    internal EditorChoice(Button button, Action<EditorChoice> open)
        : base(button) => button.clicked += () => open(this);

    internal void SetValueWithoutNotify(int index)
    {
        value = index;
        RefreshShownValue();
    }

    internal void RefreshShownValue() => ((Button)Element).text = value >= 0 && value < options.Count ? options[value].text : "";
}
