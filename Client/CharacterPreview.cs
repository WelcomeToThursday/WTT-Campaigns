using EFT;
using EFT.UI;
using SeasonalPerks.UI.Controls;
using UnityEngine;
using UnityEngine.UI;

namespace SeasonalPerks.Client;

public sealed class CharacterPreview : MonoBehaviour
{
    private GameObject? _rig;
    private PlayerModelView? _view;
    private CameraImage? _image;
    private bool _closed;
    private static int _next;
    private Camera? _camera;
    private Light[] _lights = Array.Empty<Light>();

    internal async void Show(CharacterVisual visual, GameObject cameraPrefab, Font font)
    {
        try
        {
            _rig = new GameObject("SeasonalCharacterPreviewRig");
            _rig.transform.position = new Vector3(5000 + (++_next % 100) * 100, 0, 5000);
            _view = _rig.AddComponent<PlayerModelView>();
            var label = new UiElements(font).Label(
                transform,
                "LoadingModel",
                "LOADING CHARACTER...",
                16,
                350,
                40,
                0,
                0
            );
            _view._progressSpinner = label.gameObject.AddComponent<ProgressSpinner>();
            var cameras = Instantiate(cameraPrefab, _rig.transform, false);
            var camera = cameras.GetComponentInChildren<Camera>(true);
            _camera = camera;
            camera.cullingMask = 1 << LayersMaskController.WeaponPreview;
            _lights = cameras.GetComponentsInChildren<Light>();
            foreach (var light in _lights)
            {
                light.cullingMask = camera.cullingMask;
                light.enabled = false;
            }
            Camera.onPreCull += BeforeCamera;
            _image = gameObject.AddComponent<CameraImage>();
            _image.InitCamera(camera);
            GetComponent<RawImage>().color = Color.white;
            // Use EFT's own equipment descriptor and converters, without creating a game session.
            await _view.Show(
                new PlayerVisualRepresentation(visual.CreateDescriptor()),
                position: Vector3.zero,
                animateWeapon: true
            );
            if (_closed)
            {
                return;
            }

            if (_view.ModelPlayerPoser && _view.ModelPlayerPoser.BottomShadow)
            {
                _view.ModelPlayerPoser.BottomShadow.SetActive(false);
            }
        }
        catch (Exception exception)
        {
            if (_closed)
            {
                return;
            }

            Plugin.Error(exception);
            var text = GetComponentInChildren<Text>(true);
            if (text)
            {
                text.gameObject.SetActive(true);
                text.text = "CHARACTER PREVIEW UNAVAILABLE";
            }
        }
    }

    private void OnDisable() => Release();

    private void BeforeCamera(Camera camera)
    {
        // Directional preview lights must not illuminate the other profile or the game scene.
        foreach (var light in _lights)
        {
            if (light)
            {
                light.enabled = camera == _camera;
            }
        }
    }

    private void OnDestroy() => Release();

    private void Release()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        Camera.onPreCull -= BeforeCamera;
        if (_image)
        {
            _image!.InitCamera(null);
        }

        if (_view)
        {
            _view!.Close();
        }

        if (_rig)
        {
            Destroy(_rig);
        }
    }
}
