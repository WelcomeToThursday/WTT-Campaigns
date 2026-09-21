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
        set
        {
            var display = value ? DisplayStyle.Flex : DisplayStyle.None;
            if (Element.style.display.keyword != StyleKeyword.Null && Element.style.display.value == display)
                return;
            Element.style.display = display;
            EditorActionGrid.Refresh(Element.parent);
        }
    }
    internal bool interactable
    {
        get => Element.enabledSelf;
        set
        {
            Element.SetEnabled(value);
            if (Element.parent?.ClassListContains("editor-range") == true)
                Element.parent.Q<Slider>()?.SetEnabled(value);
        }
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
    internal string Help = "";
    internal readonly UnityEvent onClick = new();
    internal string Identity = "";
    private string? _pressedIdentity;
    internal bool Pressed;

    internal EditorButton(VisualElement button)
        : base(button)
    {
        if (button is Toggle toggle)
            toggle.RegisterValueChangedCallback(_ => onClick.Invoke());
        else
            ((Button)button).clicked += () => onClick.Invoke();
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
        get => Element is Toggle toggle ? toggle.label : ((Button)Element).text;
        set
        {
            if (text != value)
            {
                if (Element is Toggle toggle)
                    toggle.label = value;
                else
                    ((Button)Element).text = value;
                EditorActionGrid.Refresh(Element.parent);
            }
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
    private readonly Slider? _range;
    internal EditorNumericDrag? NumericDrag;
    private bool _suppress;
    private readonly EditorEditState _edit = new();
    private Label? _error;
    internal bool Invalid => _edit.Invalid;
    internal string ValidationMessage => _edit.Error;
    internal readonly UnityEvent<string> onEndEdit = new(),
        onValueChanged = new();

    internal EditorInput(TextField field, bool immediate = false)
        : base(field)
    {
        field.isDelayed = !immediate;
        _range = field.parent?.ClassListContains("editor-range") == true ? field.parent.Q<Slider>() : null;
        if (_range != null)
        {
            _range.RegisterValueChangedCallback(evt =>
                field.value = evt.newValue.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
            );
            _range.tooltip = "Adjust preview percentage. Type a value for precision.";
        }
        field.RegisterValueChangedCallback(evt =>
        {
            if (_suppress)
                return;
            if (!_edit.Accept(field.name, evt.newValue))
            {
                ShowValidation();
                return;
            }
            ShowValidation();
            SyncRange(evt.newValue);
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
            return focus != null
                && (focus == Element || Element.Contains(focus) || _range != null && (focus == _range || _range.Contains(focus)));
        }
    }

    internal void SetTextWithoutNotify(string text)
    {
        _edit.Reset(text);
        ShowValidation();
        ((TextField)Element).SetValueWithoutNotify(text);
        SyncRange(text);
    }

    private void SyncRange(string text)
    {
        if (
            _range != null
            && float.TryParse(
                text,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var value
            )
            && float.IsFinite(value)
        )
            _range.SetValueWithoutNotify(Math.Clamp(value, 0, 100));
    }

    internal void CancelEdit()
    {
        NumericDrag?.Cancel();
        _suppress = true;
        ((TextField)Element).SetValueWithoutNotify(_edit.Committed);
        _edit.Reset(_edit.Committed);
        SyncRange(_edit.Committed);
        ShowValidation();
        if (isFocused)
            (Element.panel?.focusController.focusedElement as VisualElement)?.Blur();
        _suppress = false;
    }

    private void ShowValidation()
    {
        Element.EnableInClassList("editor-invalid", Invalid);
        // The toolbar's scroll viewport clips children below the speed input.
        // Its inline message is anchored beside that input by the owning view.
        if (Element.name == "CameraSpeed")
            return;
        if (Invalid && _error == null)
        {
            _error = EditorToolkitDocument.CloneTemplate<Label>("FieldMessage");
            Element.Add(_error);
        }
        if (_error != null)
        {
            _error.text = _edit.Error;
            _error.style.display = Invalid ? DisplayStyle.Flex : DisplayStyle.None;
        }
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
    private Label? _valueLabel;

    internal void AddFieldLabel(string caption, bool compact = false)
    {
        var button = (Button)Element;
        var content = EditorToolkitDocument.CloneTemplate<VisualElement>("ChoiceField");
        _valueLabel = content.Q<Label>("Value");
        _valueLabel.text = button.text;
        content.Q<Label>("Caption").text = caption;
        button.text = "";
        button.AddToClassList("editor-choice-field");
        button.EnableInClassList("editor-choice-compact", compact);
        while (content.childCount > 0)
            button.Add(content[0]);
        EditorActionGrid.Refresh(button.parent);
    }

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

    internal void RefreshShownValue()
    {
        var text = value >= 0 && value < options.Count ? options[value].text : "Select…";
        if (_valueLabel != null)
            _valueLabel.text = text;
        else
            ((Button)Element).text = text;
    }
}
