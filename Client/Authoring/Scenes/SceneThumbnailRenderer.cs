using UnityEngine;

namespace WTT.Campaigns.Client.Authoring.Scenes;

// Session-owned rendering resources. Results own only a small GPU color texture.
internal sealed class SceneThumbnailRenderer : IDisposable
{
    private readonly GameObject _rig;
    private readonly Camera _camera;
    private readonly RenderTexture _target;
    private readonly Shader _shader;
    private readonly List<Material> _materials = new();

    internal SceneThumbnailRenderer(Shader shader)
    {
        _shader = shader;
        _rig = new GameObject("CampaignEditor thumbnail camera");
        _camera = _rig.AddComponent<Camera>();
        _camera.enabled = false;
        _camera.cullingMask = 1 << 31;
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = new Color(.09f, .095f, .09f, 1);
        _camera.orthographic = true;
        _camera.nearClipPlane = .01f;
        _target = new RenderTexture(192, 192, 24, RenderTextureFormat.ARGB32);
        _target.Create();
        _camera.targetTexture = _target;
    }

    internal Texture Render(GameObject model)
    {
        var used = 0;
        var previous = RenderTexture.active;
        try
        {
            Bounds bounds;
            using (EditorDiagnostics.Measure(EditorDiagnostics.Area.ThumbnailPrepare))
            {
                model.SetActive(true);
                model.transform.position = new Vector3(0, -10000, 0);
                foreach (var t in model.GetComponentsInChildren<Transform>(true))
                    t.gameObject.layer = 31;
                if (!SceneBounds.TryGet(model.transform, out bounds))
                    throw new InvalidOperationException("Prop has no visible mesh.");
                foreach (var renderer in model.GetComponentsInChildren<Renderer>())
                {
                    var originals = renderer.sharedMaterials;
                    var preview = new Material[originals.Length];
                    for (var i = 0; i < originals.Length; i++)
                    {
                        if (used == _materials.Count)
                            _materials.Add(new Material(_shader));
                        var material = _materials[used++];
                        var original = originals[i];
                        var opacity =
                            original
                            && PreviewMaterialPolicy.UsesOpacity(
                                original.GetTag("RenderType", false, ""),
                                original.shader ? original.shader.name : "",
                                original.IsKeywordEnabled("_ALPHATEST_ON"),
                                original.IsKeywordEnabled("_ALPHABLEND_ON") || original.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON")
                            );
                        var hasTexture = original && original.HasProperty("_MainTex");
                        material.SetTexture("_MainTex", hasTexture ? original.GetTexture("_MainTex") : null);
                        material.SetTextureScale("_MainTex", hasTexture ? original.GetTextureScale("_MainTex") : Vector2.one);
                        material.SetTextureOffset("_MainTex", hasTexture ? original.GetTextureOffset("_MainTex") : Vector2.zero);
                        material.SetColor("_Color", original && original.HasProperty("_Color") ? original.GetColor("_Color") : Color.white);
                        material.SetFloat("_UseOpacity", opacity ? 1 : 0);
                        preview[i] = material;
                    }
                    renderer.sharedMaterials = preview;
                }
                _camera.orthographicSize = Mathf.Max(.1f, bounds.extents.magnitude * 1.1f);
                _camera.farClipPlane = Mathf.Max(20, bounds.size.magnitude * 6);
                _camera.transform.position = bounds.center + new Vector3(1, .7f, -1).normalized * Mathf.Max(2, bounds.size.magnitude * 2);
                _camera.transform.LookAt(bounds.center);
            }
            using (EditorDiagnostics.Measure(EditorDiagnostics.Area.ThumbnailRender))
            {
                _camera.Render();
                return Snapshot(_target, 192, 192, Vector2.one, Vector2.zero);
            }
        }
        finally
        {
            RenderTexture.active = previous;
            model.SetActive(false);
            // Pooled materials must not keep bundle textures alive after a lease is released.
            foreach (var material in _materials)
                material.SetTexture("_MainTex", null);
            while (_materials.Count > 256)
            {
                UnityEngine.Object.Destroy(_materials[_materials.Count - 1]);
                _materials.RemoveAt(_materials.Count - 1);
            }
        }
    }

    internal static Texture Snapshot(Sprite sprite)
    {
        var rect = sprite.textureRect;
        var source = sprite.texture;
        var scale = Mathf.Min(1, 192f / Mathf.Max(rect.width, rect.height));
        return Snapshot(
            source,
            Mathf.Max(1, Mathf.RoundToInt(rect.width * scale)),
            Mathf.Max(1, Mathf.RoundToInt(rect.height * scale)),
            new Vector2(rect.width / source.width, rect.height / source.height),
            new Vector2(rect.x / source.width, rect.y / source.height)
        );
    }

    private static Texture Snapshot(Texture source, int width, int height, Vector2 scale, Vector2 offset)
    {
        var previous = RenderTexture.active;
        var result = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        try
        {
            result.Create();
            Graphics.Blit(source, result, scale, offset);
            return result;
        }
        catch
        {
            Release(result);
            throw;
        }
        finally
        {
            RenderTexture.active = previous;
        }
    }

    internal static void Release(Texture texture)
    {
        if (!texture)
            return;
        if (texture is RenderTexture target)
            target.Release();
        UnityEngine.Object.Destroy(texture);
    }

    public void Dispose()
    {
        _camera.targetTexture = null;
        Release(_target);
        UnityEngine.Object.Destroy(_rig);
        foreach (var material in _materials)
            UnityEngine.Object.Destroy(material);
        _materials.Clear();
    }
}
