using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace WTT.Campaigns.Client.Authoring.Views;

// Preview text while captured, then use the normal edit callback once on release.
// This preserves validation and makes one drag a single undoable edit.
internal sealed class EditorNumericDrag
{
    private readonly TextField _field;
    private readonly double _step,
        _min,
        _max;
    private readonly bool _integer;
    private int _pointer = -1;
    private float _lastX,
        _startX;
    private double _value;
    private string _original = "";
    private bool _moved;
    internal bool Active => _pointer >= 0;

    internal static EditorNumericDrag? Attach(TextField field, string id)
    {
        var (step, min, max, integer) = id switch
        {
            "CameraSpeed" => (.1, .25, 96d, false),
            "ContainerChance" => (1d, 0d, 100d, true),
            "ContainerQuantity" => (1d, 1d, 10000d, true),
            "AiRosterCount" => (1d, 1d, 256d, true),
            "AiWaveDelaySeconds" or "AiWaypointWaitSeconds" => (.1, 0d, 3600d, false),
            "Radius" => (.1, .001, (double)float.MaxValue, false),
            "WeatherClouds" or "WeatherRain" or "WeatherFog" or "WeatherWind" or "WeatherThunder" => (1d, 0d, 100d, false),
            _ => (0d, 0d, 0d, false),
        };
        foreach (var prefix in new[] { "Position", "Rotation", "Size", "MapPosition", "MapRotation", "MapSize" })
            if (id.Length == prefix.Length + 1 && id.StartsWith(prefix, StringComparison.Ordinal) && "XYZ".IndexOf(id[id.Length - 1]) >= 0)
            {
                step = prefix.Contains("Rotation") ? 1 : .1;
                min = prefix.Contains("Size") ? .001 : -float.MaxValue;
                max = float.MaxValue;
            }
        return step == 0 ? null : new EditorNumericDrag(field, step, min, max, integer);
    }

    private EditorNumericDrag(TextField field, double step, double min, double max, bool integer)
    {
        _field = field;
        _step = step;
        _min = min;
        _max = max;
        _integer = integer;
        field.tooltip = "Drag label or Alt-drag value · Shift: finer · Ctrl: faster · Escape: cancel";
        field.RegisterCallback<PointerDownEvent>(Down, TrickleDown.TrickleDown);
        field.RegisterCallback<PointerMoveEvent>(Move, TrickleDown.TrickleDown);
        field.RegisterCallback<PointerUpEvent>(Up, TrickleDown.TrickleDown);
        field.RegisterCallback<PointerCaptureOutEvent>(_ => Cancel());
        field.RegisterCallback<DetachFromPanelEvent>(_ => Cancel());
        field.RegisterCallback<KeyDownEvent>(
            evt =>
            {
                if (Active && evt.keyCode == KeyCode.Escape)
                {
                    Cancel();
                    evt.StopPropagation();
                    evt.PreventDefault();
                }
            },
            TrickleDown.TrickleDown
        );
    }

    private void Down(PointerDownEvent evt)
    {
        var target = evt.target as VisualElement;
        var onLabel = target == _field.labelElement || target != null && _field.labelElement.Contains(target);
        if (Active || evt.button != 0 || !onLabel && !evt.altKey || !_field.enabledInHierarchy || _field.isReadOnly)
            return;
        (_field.panel?.focusController.focusedElement as VisualElement)?.Blur();
        if (
            !double.TryParse(_field.value, NumberStyles.Float, CultureInfo.InvariantCulture, out _value)
            || double.IsNaN(_value)
            || double.IsInfinity(_value)
        )
            return;
        _original = _field.value;
        _lastX = _startX = evt.position.x;
        _moved = false;
        _pointer = evt.pointerId;
        _field.CapturePointer(_pointer);
        evt.StopPropagation();
        evt.PreventDefault();
    }

    private void Move(PointerMoveEvent evt)
    {
        if (!Active || evt.pointerId != _pointer)
            return;
        if (!_field.enabledInHierarchy || _field.isReadOnly)
        {
            Cancel();
            return;
        }
        if (!_moved && Mathf.Abs(evt.position.x - _startX) < 3)
            return;
        _moved = true;
        var sensitivity =
            evt.shiftKey ? .1
            : evt.ctrlKey ? 10
            : 1;
        _value = Math.Max(_min, Math.Min(_max, _value + (evt.position.x - _lastX) * _step * sensitivity));
        _lastX = evt.position.x;
        _field.SetValueWithoutNotify(
            (_integer ? Math.Round(_value) : _value).ToString(_integer ? "0" : "0.###", CultureInfo.InvariantCulture)
        );
        evt.StopPropagation();
        evt.PreventDefault();
    }

    private void Up(PointerUpEvent evt)
    {
        if (!Active || evt.pointerId != _pointer || evt.button != 0)
            return;
        var result = _field.value;
        var changed = _moved && result != _original && _field.enabledInHierarchy && !_field.isReadOnly;
        Cancel();
        if (changed)
            _field.value = result;
        evt.StopPropagation();
        evt.PreventDefault();
    }

    internal bool Cancel()
    {
        if (!Active)
            return false;
        var pointer = _pointer;
        _pointer = -1;
        _field.SetValueWithoutNotify(_original);
        if (_field.HasPointerCapture(pointer))
            _field.ReleasePointer(pointer);
        return true;
    }
}
