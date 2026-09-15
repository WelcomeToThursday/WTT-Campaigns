using System.Globalization;
using UnityEngine;
using WTT.Campaigns.Client.Authoring.Views;

namespace WTT.Campaigns.Client.Authoring;

public sealed partial class RaidEditor
{
    private float CameraSpeed =>
        float.IsNaN(_cameraSpeed.Value) || float.IsInfinity(_cameraSpeed.Value) ? 6f : Mathf.Clamp(_cameraSpeed.Value, .25f, 96f);

    private void BindCameraControls(RaidEditorView view)
    {
        void Set(float value)
        {
            _cameraSpeed.Value = Mathf.Clamp(value, .25f, 96f);
            view.Get<EditorInput>("CameraSpeed").SetTextWithoutNotify(CameraSpeed.ToString("0.##", CultureInfo.InvariantCulture));
            Refresh(false);
        }
        view.Button("CameraSlower", () => Set(CameraSpeed / 2));
        view.Button("CameraFaster", () => Set(CameraSpeed * 2));
        view.Input(
            "CameraSpeed",
            value =>
            {
                if (
                    float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed)
                    && !float.IsNaN(speed)
                    && !float.IsInfinity(speed)
                    && speed >= .25f
                    && speed <= 96f
                )
                    Set(speed);
                else
                {
                    _notice = "Camera speed must be 0.25–96 metres per second.";
                    view.Get<EditorInput>("CameraSpeed").SetTextWithoutNotify(CameraSpeed.ToString("0.##", CultureInfo.InvariantCulture));
                    Refresh(false);
                }
            }
        );
    }
}
