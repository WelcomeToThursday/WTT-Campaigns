using System.Globalization;
using BepInEx.Configuration;
using Newtonsoft.Json;
using UnityEngine;
using WTT.Campaigns.Client.Authoring.Views;

namespace WTT.Campaigns.Client.Authoring;

public sealed partial class RaidEditor
{
    private ConfigEntry<string>? _cameraBookmarks;
    private string _cameraBookmarkDraft = "",
        _cameraBookmarkMap = "";

    private EditorCameraBookmarks ReadCameraBookmarks()
    {
        _cameraBookmarks ??= Plugin.Instance.Config.Bind(
            "Campaign editor",
            "Camera bookmarks",
            "",
            "Last editor camera position and viewing angle for each draft and map. Saved when leaving the editor."
        );
        try
        {
            return JsonConvert.DeserializeObject<EditorCameraBookmarks>(_cameraBookmarks.Value) ?? new();
        }
        catch (JsonException)
        {
            Plugin.LogInfo("Saved editor camera positions were invalid; using the player camera until a new position is saved.");
            return new();
        }
    }

    private void RestoreCameraBookmark()
    {
        _cameraBookmarkDraft = _session?.DraftId ?? "";
        _cameraBookmarkMap = _session?.Location ?? "";
        try
        {
            if (!ReadCameraBookmarks().TryGet(_cameraBookmarkDraft, _cameraBookmarkMap, out var pose))
                return;
            _flyPosition = new Vector3(pose.Position[0], pose.Position[1], pose.Position[2]);
            _flyRotation = new Quaternion(pose.Rotation[0], pose.Rotation[1], pose.Rotation[2], pose.Rotation[3]).normalized;
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            Plugin.Error(error);
        }
    }

    private void SaveCameraBookmark()
    {
        try
        {
            var bookmarks = ReadCameraBookmarks();
            if (
                !bookmarks.Save(
                    _cameraBookmarkDraft,
                    _cameraBookmarkMap,
                    new EditorCameraBookmarks.Pose
                    {
                        Position = new[] { _flyPosition.x, _flyPosition.y, _flyPosition.z },
                        Rotation = new[] { _flyRotation.x, _flyRotation.y, _flyRotation.z, _flyRotation.w },
                    }
                )
            )
                return;
            var json = JsonConvert.SerializeObject(bookmarks);
            if (_cameraBookmarks!.Value == json)
                return;
            _cameraBookmarks.Value = json;
            if (!Plugin.Instance.Config.SaveOnConfigSet)
                Plugin.Instance.Config.Save();
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            // Read-only preferences must not interrupt editor/raid teardown.
            Plugin.Error(error);
        }
    }

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
        view.Button("ViewportSlower", () => Set(CameraSpeed / 2));
        view.Button("ViewportFaster", () => Set(CameraSpeed * 2));
        view.Button(
            "ViewportSnap",
            () =>
            {
                _snap = !_snap;
                Refresh();
            }
        );
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
