using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// Asset-only SDK build; never launches a game or server.
public static class CampaignsEditorToolkitBuilder
{
    private static readonly string[] Templates =
    {
        "Action",
        "BrowserRow",
        "CampaignTest",
        "CaptureTask",
        "CategoryRail",
        "ChoiceField",
        "ChoiceOption",
        "ChoicePopup",
        "FieldMessage",
        "InspectorSection",
        "InspectorHeader",
        "ConflictRow",
        "ConflictShield",
        "ContextMenu",
        "Console",
        "ConsoleRow",
        "Navigation",
        "Controls",
        "DockDivider",
        "DockTab",
        "DockTabs",
        "DropPreview",
        "EditorWalkStatus",
        "EnvironmentMenu",
        "Field",
        "Home",
        "HomePicker",
        "Inspector",
        "Library",
        "LootConfiguration",
        "MenuShield",
        "PickerRow",
        "RouteCaption",
        "RouteLegend",
        "Row",
        "SceneActionGroup",
        "ScopedActions",
        "StatusBar",
        "ToolbarIcon",
        "ToolbarScroll",
        "ToolbarSeparator",
        "Tooltip",
        "TransformToolbar",
        "TreeRow",
        "Window",
        "WindowsMenu",
        "Workspace",
        "WorkspaceTitleBar",
        "GameViewport",
        "ViewportToolbar",
    };

    [Serializable]
    private class SourceHash
    {
        public string file;
        public string sha256;
    }

    [Serializable]
    private class Validation
    {
        public int schema = 2;
        public string unity;
        public bool validated = true;
        public string sha256;
        public SourceHash[] sources;
    }

    private static string Hash(string file)
    {
        using (var sha = System.Security.Cryptography.SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file))).Replace("-", "");
    }

    private static void ValidateTemplates(Func<string, VisualTreeAsset> load)
    {
        foreach (var name in Templates)
        {
            var tree = load(name);
            if (!tree)
                throw new InvalidOperationException("Missing editor template: " + name);
            var first = tree.CloneTree();
            var second = tree.CloneTree();
            // Exercise imported and reloaded UXML factories, including controls
            // whose fields are bound dynamically rather than by EditorLayoutSpec.
            string[] slots =
                name == "ViewportToolbar"
                    ? new[]
                    {
                        "ViewportTitle:Label",
                        "ViewportTools:ScrollView",
                        "CameraSpeed:TextField",
                        "ViewportMaximize:Button",
                        "ViewportSnap:Button",
                        "ViewportOverlayMenu:VisualElement",
                        "OverlayZones:Toggle",
                        "OverlayRoutes:Toggle",
                        "OverlayAi:Toggle",
                        "OverlayBounds:Toggle",
                        "OverlayHandles:Toggle",
                    }
                : name == "Navigation"
                    ? new[]
                    {
                        "NavBuild:Button",
                        "NavCancel:Button",
                        "NavRestore:Button",
                        "NavState:Label",
                        "NavStatus:Label",
                        "NavFloorLock:Toggle",
                        "NavAdvanced:Toggle",
                        "NavBrushSize:TextField",
                        "NavPaintAdd:Button",
                        "NavPaintBlock:Button",
                        "NavPaintErase:Button",
                        "NavPaintConnect:Button",
                        "NavPaintPath:Button",
                        "NavigationScroll:ScrollView",
                        "NavIssueSelect:Button",
                    }
                : name == "Console"
                    ? new[]
                    {
                        "ConsoleMessages:ListView",
                        "ConsoleCommand:TextField",
                        "ConsoleSearch:TextField",
                        "ConsoleDetailsFold:Foldout",
                        "ConsoleDetails:TextField",
                        "ConsoleClear:Button",
                        "ConsoleCopy:Button",
                        "ConsoleAll:Toggle",
                        "ConsoleTextSize:Label",
                        "ConsoleTextSmaller:Button",
                        "ConsoleTextLarger:Button",
                        "ConsoleTextReset:Button",
                    }
                : name == "ConsoleRow" ? new[] { "ConsoleTime:Label", "ConsoleMessage:Label" }
                : name == "HomePicker" ? new[] { "Heading:Label", "Close:Button", "Choices:ListView", "Empty:Label" }
                : name == "PickerRow" ? new[] { "selected:Label", "name:Label" }
                : name == "ChoicePopup"
                    ? new[] { "ChoicePanel:VisualElement", "ChoiceSearch:TextField", "Choices:ListView", "ChoiceEmpty:Label" }
                : name == "ChoiceField" ? new[] { "Caption:Label", "Value:Label" }
                : name == "TreeRow" ? new[] { "Fold:Foldout", "tree-label:Label" }
                : name == "BrowserRow" ? new[] { "Icon:Image", "Status:Label" }
                : name == "CampaignTest" ? new[] { "Reset:Button", "Return:Button", "Status:Label" }
                : name == "CategoryRail" ? new[] { "RailScroll:ScrollView" }
                : name == "SceneActionGroup" ? new[] { "Caption:Label" }
                : new string[0];
            foreach (var slot in slots)
            {
                var parts = slot.Split(':');
                var element = first.Q(parts[0]);
                if (element == null || element.GetType().Name != parts[1] || ReferenceEquals(element, second.Q(parts[0])))
                    throw new InvalidOperationException("Invalid or shared template slot: " + name + "/" + slot);
            }
            if (
                name == "ConflictShield"
                && (!first.Q<TextField>("LocalConflict").multiline || !first.Q<TextField>("RemoteConflict").isReadOnly)
            )
                throw new InvalidOperationException("Conflict fields must remain multiline and read-only.");
            if (first.childCount != 1 || second.childCount != 1 || ReferenceEquals(first[0], second[0]))
                throw new InvalidOperationException("Editor templates must clone one independent root: " + name);
            if (
                name == "Window"
                && (
                    first.Q<Label>("Heading") == null
                    || first.Q<Button>("Close") == null
                    || first.Q("TitleBar") == null
                    || first.Q("Resize") == null
                )
            )
                throw new InvalidOperationException("Window template is missing runtime binding slots.");
            if (name == "Action" && !(first[0] is Button) || name == "Field" && !(first[0] is TextField))
                throw new InvalidOperationException("Wrong editor control template type: " + name);
            if (name == "GameViewport" && (!(first[0] is Image) || first[0].pickingMode != PickingMode.Ignore))
                throw new InvalidOperationException("Viewport must be an image that leaves input to the editor.");
            if (name == "RouteLegend" && (!(first[0] is ScrollView) || first.Q<Label>("RouteLegendText") == null))
                throw new InvalidOperationException("Route diagnostics require a scrollable legend with a text label.");
            if (name == "Library")
            {
                var search = first.Q<TextField>("Search");
                if (
                    search == null
                    || second.Q<TextField>("Search") == null
                    || first.Q<ScrollView>("BrowserPages") == null
                    || first.Q<ListView>("BrowserTree") == null
                    || first.Q<ScrollView>("ToolActionsScroll") == null
                    || first.Q<ScrollView>("AiToolsScroll") == null
                )
                    throw new InvalidOperationException("Browser template is missing typed runtime slots.");
                search.value = "independent tool search";
                if (second.Q<TextField>("Search").value == search.value)
                    throw new InvalidOperationException("Browser instances share search state.");
                if (ReferenceEquals(first.Q<ListView>("BrowserTree"), second.Q<ListView>("BrowserTree")))
                    throw new InvalidOperationException("Browser instances share their list view.");
            }
            if (name == "Console")
            {
                if (first.Q<ListView>("ConsoleMessages").virtualizationMethod != CollectionVirtualizationMethod.DynamicHeight)
                    throw new InvalidOperationException("Console output must allow wrapped, variable-height messages.");
                foreach (var toggle in first.Query<Toggle>(className: "editor-console-toggle").ToList())
                    if (!toggle.ClassListContains("editor-setting"))
                        throw new InvalidOperationException("Console filters must use the editor's setting controls.");
                if (
                    !first.Q<TextField>("ConsoleDetails").isReadOnly
                    || !first.Q<TextField>("ConsoleDetails").multiline
                    || first.Q<Foldout>("ConsoleDetailsFold").value
                    || first.Q<Toggle>("ConsoleAll").value
                    || !first.Q<Toggle>("ConsoleInfo").value
                    || !first.Q<Toggle>("ConsoleScroll").value
                )
                    throw new InvalidOperationException("Console defaults or message details are invalid.");
                if (first.Q<ListView>("ConsoleMessages").Contains(first.Q<TextField>("ConsoleCommand")))
                    throw new InvalidOperationException("Console command entry must remain outside the scrolling list.");
            }
            if (name == "Inspector")
            {
                foreach (var id in new[] { "AiWaypointInsert", "AiWaypointEarlier", "AiWaypointLater", "AiRouteReverse" })
                    if (first.Q<Button>(id) == null || second.Q<Button>(id) == null)
                        throw new InvalidOperationException("Missing patrol editing control: " + id);
                foreach (
                    var id in new[]
                    {
                        "SceneRepeat",
                        "MapWalkStart",
                        "MapNormalRaid",
                        "AiWaveWaitPrevious",
                        "SniperPlaySound",
                        "SniperSuppressed",
                    }
                )
                {
                    var setting = first.Q<Toggle>(id);
                    if (setting == null)
                        throw new InvalidOperationException("Missing checkbox setting: " + id);
                    var changes = 0;
                    setting.RegisterValueChangedCallback(_ => changes++);
                    setting.SetValueWithoutNotify(true);
                    if (!setting.value || changes != 0 || second.Q<Toggle>(id).value)
                        throw new InvalidOperationException("Refreshing a setting must be silent and isolated: " + id);
                }
                var section = (Foldout)load("InspectorSection").CloneTree()[0];
                section.RemoveFromHierarchy();
                var position = first.Q("PositionGroup");
                position.parent.Insert(position.parent.IndexOf(position), section);
                section.Add(position);
                section.SetValueWithoutNotify(false);
                if (section.value || !section.Contains(position))
                    throw new InvalidOperationException("Inspector foldouts must retain their bound fields when collapsed.");
                var field = first.Q<TextField>("PositionX");
                var error = load("FieldMessage").CloneTree()[0];
                error.RemoveFromHierarchy();
                field.Add(error);
                if (!field.Contains(error))
                    throw new InvalidOperationException("Numeric fields must support inline validation messages.");
                var header = load("InspectorHeader").CloneTree()[0];
                header.RemoveFromHierarchy();
                var scroll = first.Q<ScrollView>("PropertyScroll");
                scroll.parent.Insert(scroll.parent.IndexOf(scroll), header);
                header.Add(first.Q("NameGroup"));
                if (scroll.Contains(header) || header.parent != scroll.parent)
                    throw new InvalidOperationException("Inspector identity must remain outside scrolling properties.");
            }
            if (
                name == "Inspector"
                && (
                    first.Q<ScrollView>("PropertyScroll") == null
                    || first.Q<Image>("ScenePreview") == null
                    || first.Q<TextField>("PositionX") == null
                    || first.Q<TextField>("AiRosterCount") == null
                    || first.Q<Toggle>("MapNormalRaid") == null
                )
            )
                throw new InvalidOperationException("Properties template is missing typed runtime slots.");
            if (name == "EnvironmentMenu")
            {
                foreach (var channel in new[] { "Clouds", "Rain", "Fog", "Wind", "Thunder" })
                {
                    var field = first.Q<TextField>("Weather" + channel);
                    var slider = field?.parent.Q<Slider>();
                    if (slider == null || slider.lowValue != 0 || slider.highValue != 100)
                        throw new InvalidOperationException("Weather percentage needs a bounded slider: " + channel);
                    slider.value = -10;
                    if (slider.value != 0)
                        throw new InvalidOperationException("Weather slider failed its lower bound.");
                    slider.value = 110;
                    if (slider.value != 100)
                        throw new InvalidOperationException("Weather slider failed its upper bound.");
                }
                var scroll = first.Q<ScrollView>("EnvironmentScroll");
                if (
                    scroll == null
                    || scroll.Q<TextField>("EnvironmentHour") == null
                    || scroll.Q<TextField>("WeatherRain") == null
                    || scroll.Q<Button>("WeatherApply") == null
                    || scroll.Q<Button>("EnvironmentEarlier")?.text != "\u22121 hour"
                )
                    throw new InvalidOperationException("Environment template is missing controls or has damaged captions.");
            }
            if (name == "LootConfiguration")
            {
                var scroll = first.Q<ScrollView>("ContainerScroll");
                if (
                    scroll == null
                    || scroll.Q<Button>("ContainerMode") == null
                    || scroll.Q<TextField>("ContainerQuantity") == null
                    || scroll.Q<Button>("ContainerUseKey") == null
                    || scroll.Q("ContainerSettingsGroup") == null
                )
                    throw new InvalidOperationException("Loot template is missing typed runtime slots.");
            }
            if (name == "Controls")
            {
                var help = first.Q<ScrollView>("ControlsScroll")?.Q<Label>("Help");
                if (help == null || help.text.Split('\n').Length != 6)
                    throw new InvalidOperationException("Help template must preserve the six instruction lines in its scroll view.");
            }
        }
    }

    private static void ValidateNavigationShader(Shader shader)
    {
        if (!shader || ShaderUtil.ShaderHasError(shader) || !shader.isSupported)
            throw new InvalidOperationException("Navigation surface shader failed to compile or reload.");
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            Debug.Log(
                "Navigation shader compiled; GPU pixel checks skipped on the headless device. Use UnityGraphicsArguments=-force-d3d11 for pixel checks."
            );
            return;
        }
        var previous = RenderTexture.active;
        var host = new GameObject("Navigation shader validation camera");
        var surface = new GameObject("Navigation shader validation surface");
        var blocker = GameObject.CreatePrimitive(PrimitiveType.Quad);
        var target = new RenderTexture(32, 32, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var readback = new Texture2D(32, 32, TextureFormat.RGBA32, false, true);
        var material = new Material(shader);
        var opaque = new Material(Shader.Find("Unlit/Color")) { color = Color.black };
        var mesh = new Mesh();
        try
        {
            target.Create();
            var camera = host.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 1;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 10;
            camera.transform.position = new Vector3(0, 0, -3);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = 1 << 31;
            camera.targetTexture = target;
            camera.allowHDR = camera.allowMSAA = false;
            camera.renderingPath = RenderingPath.Forward;
            surface.layer = blocker.layer = 31;
            mesh.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(-1, 1, 0), new Vector3(1, 1, 0), new Vector3(1, -1, 0) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            surface.AddComponent<MeshFilter>().sharedMesh = mesh;
            surface.AddComponent<MeshRenderer>().sharedMaterial = material;
            blocker.GetComponent<MeshRenderer>().sharedMaterial = opaque;
            blocker.transform.localScale = Vector3.one * 2;
            blocker.transform.position = Vector3.forward;
            material.SetFloat("_Diagnostic", 1);
            Color Render(float issues)
            {
                mesh.uv = new[] { new Vector2(issues, 0), new Vector2(issues, 0), new Vector2(issues, 0), new Vector2(issues, 0) };
                camera.Render();
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, 32, 32), 0, 0);
                readback.Apply();
                return readback.GetPixel(16, 16);
            }
            var clear = Render(0);
            if (clear.b <= clear.r * 2 || clear.b < .02f)
                throw new InvalidOperationException("Navigation shader lost clear-sample tint.");
            var visible = Render(6);
            if (visible.r < .1f || visible.r <= visible.b * 2)
                throw new InvalidOperationException("Navigation shader lost overlapping issue severity.");
            material.SetFloat("_IssueFilter", 4);
            var purple = Render(6);
            if (purple.b < .1f || purple.r <= purple.g)
                throw new InvalidOperationException("Navigation shader cannot filter overlapping connectivity issues.");
            material.SetFloat("_IssueFilter", 8);
            var filtered = Render(6);
            if (filtered.maxColorComponent > .02f)
                throw new InvalidOperationException("Navigation shader does not hide nonmatching issues.");
            material.SetFloat("_IssueFilter", 0);
            blocker.transform.position = -Vector3.forward;
            if (Render(6).r > .02f)
                throw new InvalidOperationException("Raw navigation should retain normal world occlusion.");
            blocker.transform.position = Vector3.forward;
            material.SetFloat("_Stale", 1);
            var stale = Render(6);
            if (stale.b < .005f || stale.r > stale.b * 1.2f)
                throw new InvalidOperationException("Stale navigation diagnostics retain a false issue color.");
            material.SetFloat("_Stale", 0);
            material.SetFloat("_Diagnostic", 0);
            material.color = new Color(.2f, .65f, 1, .3f);
            foreach (var rendering in new[] { RenderingPath.Forward, RenderingPath.DeferredShading })
            {
                camera.renderingPath = rendering;
                blocker.transform.position = Vector3.forward;
                var nativeVisible = Render(0);
                blocker.transform.position = -Vector3.forward;
                var nativeBuried = Render(0);
                if (nativeVisible.b < .1f || nativeBuried.b > .02f)
                    throw new InvalidOperationException("Native navigation terrain occlusion regression in " + rendering);
            }
            Debug.Log("Navigation GPU regression: exposed/occluded surfaces, overlapping diagnostic filters and stale tint passed.");
        }
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(host);
            UnityEngine.Object.DestroyImmediate(surface);
            UnityEngine.Object.DestroyImmediate(blocker);
            UnityEngine.Object.DestroyImmediate(mesh);
            UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(opaque);
            UnityEngine.Object.DestroyImmediate(readback);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    private static void ValidateGroundProjection(Shader shader)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            return;
        var previous = RenderTexture.active;
        var host = new GameObject("Navigation projection validation camera");
        var ground = new GameObject("Navigation projection curved ground") { layer = 31 };
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var upper = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var pipe = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        var foliage = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var grass = GameObject.CreatePrimitive(PrimitiveType.Quad);
        var terrainData = new TerrainData { heightmapResolution = 33, size = new Vector3(8, 4, 8) };
        var hill = new Mesh();
        var footprint = new Mesh();
        var floorFootprint = new Mesh();
        var floorMaterial = new Material(shader) { color = new Color(.2f, .65f, 1, .3f) };
        var opaque = new Material(
            AssetDatabase.LoadAssetAtPath<Shader>("Assets/Mods/WTT-Campaigns.Assets/EditorToolkit/NavigationProjectionTest.shader")
        );
        var material = new Material(shader) { color = new Color(.2f, .65f, 1, .3f) };
        var commands = new UnityEngine.Rendering.CommandBuffer();
        var source = new RenderTexture(256, 192, 24);
        var copy = new Material(
            AssetDatabase.LoadAssetAtPath<Shader>("Assets/Mods/WTT-Campaigns.Assets/EditorToolkit/ViewportCopy.shader")
        );
        var processed = new RenderTexture(256, 192, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var layer = new RenderTexture(128, 96, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var readback = new Texture2D(128, 96, TextureFormat.RGBA32, false, true);
        try
        {
            material.SetFloat("_ProjectGround", 1);
            floorMaterial.SetFloat("_ProjectGround", 1);
            floorFootprint.vertices = new[]
            {
                new Vector3(1, 3.6f, 2.5f),
                new Vector3(1, 3.6f, 3.5f),
                new Vector3(2, 3.6f, 3.5f),
                new Vector3(2, 3.6f, 2.5f),
            };
            floorFootprint.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            floorFootprint.RecalculateBounds();
            var vertices = new List<Vector3>();
            var indices = new List<int>();
            for (var z = 0; z <= 32; z++)
            for (var x = 0; x <= 32; x++)
                vertices.Add(new Vector3(x * .25f, 1.2f * Mathf.Sin(x * Mathf.PI / 32) * Mathf.Sin(z * Mathf.PI / 32), z * .25f));
            for (var z = 0; z < 32; z++)
            for (var x = 0; x < 32; x++)
            {
                var a = z * 33 + x;
                indices.AddRange(new[] { a, a + 33, a + 34, a, a + 34, a + 1 });
            }
            var heights = new float[33, 33];
            for (var z = 0; z <= 32; z++)
            for (var x = 0; x <= 32; x++)
                heights[z, x] = vertices[z * 33 + x].y / 4;
            terrainData.SetHeights(0, 0, heights);
            material.SetFloat("_TerrainReceiver", 1);
            material.SetTexture("_TerrainHeightmap", terrainData.heightmapTexture);
            material.SetVector("_TerrainRegion", new Vector4(0, 0, 1f / 8, 1f / 8));
            material.SetVector("_TerrainHeight", new Vector4(0, 4 * (65535f / 32766f), 32f / 33, .5f / 33));
            hill.SetVertices(vertices);
            hill.SetTriangles(indices, 0);
            hill.RecalculateNormals();
            hill.RecalculateBounds();
            ground.AddComponent<MeshFilter>().sharedMesh = hill;
            ground.AddComponent<MeshRenderer>().sharedMaterial = opaque;
            var groundCollider = ground.AddComponent<MeshCollider>();
            groundCollider.sharedMesh = hill;
            wall.layer = upper.layer = pipe.layer = foliage.layer = grass.layer = 31;
            pipe.transform.position = new Vector3(4, 1.6f, 6);
            pipe.transform.rotation = Quaternion.Euler(0, 0, 90);
            pipe.transform.localScale = new Vector3(.4f, 2, .4f);
            foliage.transform.position = new Vector3(1.5f, 1.2f, 2);
            foliage.transform.localScale = new Vector3(1.2f, .8f, 1.2f);
            grass.transform.position = new Vector3(6.8f, .55f, 1);
            grass.transform.rotation = Quaternion.Euler(60, 0, 0);
            grass.transform.localScale = new Vector3(1, .7f, 1);
            pipe.GetComponent<MeshRenderer>().sharedMaterial = opaque;
            foliage.GetComponent<MeshRenderer>().sharedMaterial = opaque;
            grass.GetComponent<MeshRenderer>().sharedMaterial = opaque;
            // Primitive sphere/capsule colliders do not match nonuniformly scaled
            // rendered meshes. The pixel oracle must use the visible geometry.
            UnityEngine.Object.DestroyImmediate(foliage.GetComponent<Collider>());
            foliage.AddComponent<MeshCollider>().sharedMesh = foliage.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.DestroyImmediate(pipe.GetComponent<Collider>());
            pipe.AddComponent<MeshCollider>().sharedMesh = pipe.GetComponent<MeshFilter>().sharedMesh;
            wall.transform.position = new Vector3(6, 1.5f, 3);
            wall.transform.localScale = new Vector3(.3f, 3, 2);
            upper.transform.position = new Vector3(1.5f, 3.5f, 3);
            upper.transform.localScale = new Vector3(1, .2f, 1);
            wall.GetComponent<MeshRenderer>().sharedMaterial = opaque;
            upper.GetComponent<MeshRenderer>().sharedMaterial = opaque;
            vertices.Clear();
            indices.Clear();
            void Rect(float x0, float z0, float x1, float z1)
            {
                var a = vertices.Count;
                vertices.AddRange(new[] { new Vector3(x0, 0, z0), new Vector3(x0, 0, z1), new Vector3(x1, 0, z1), new Vector3(x1, 0, z0) });
                indices.AddRange(new[] { a, a + 1, a + 2, a, a + 2, a + 3 });
            }
            Rect(0, 0, 3, 8);
            Rect(5, 0, 8, 8);
            Rect(3, 0, 5, 3);
            Rect(3, 5, 5, 8);
            footprint.SetVertices(vertices);
            footprint.SetTriangles(indices, 0);
            footprint.RecalculateBounds();
            var camera = host.AddComponent<Camera>();
            camera.enabled = false;
            camera.cullingMask = 1 << 31;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 100;
            camera.allowHDR = camera.allowMSAA = false;
            camera.depthTextureMode = DepthTextureMode.Depth;
            camera.targetTexture = source;
            camera.aspect = 128f / 96;
            source.Create();
            layer.Create();
            processed.Create();
            var colliders = new[]
            {
                (Collider)groundCollider,
                wall.GetComponent<Collider>(),
                upper.GetComponent<Collider>(),
                pipe.GetComponent<Collider>(),
                foliage.GetComponent<Collider>(),
                grass.GetComponent<Collider>(),
            };
            Physics.SyncTransforms();
            camera.AddCommandBuffer(UnityEngine.Rendering.CameraEvent.BeforeForwardAlpha, commands);
            foreach (var rendering in new[] { RenderingPath.Forward, RenderingPath.DeferredShading })
            foreach (var pose in new[] { new Vector3(4, 8, -6), new Vector3(2, 1, -1) })
            foreach (var floor in new[] { false, true })
            foreach (var nativeFlip in new[] { false, true })
            {
                camera.renderingPath = rendering;
                camera.transform.position = pose;
                camera.transform.LookAt(new Vector3(4, .6f, 4));
                camera.ResetProjectionMatrix();
                material.SetVector("_FloorBand", floor ? new Vector4(.5f, 1.1f, 0, 0) : new Vector4(-100000, 100000, 0, 0));
                floorMaterial.SetVector("_FloorBand", floor ? new Vector4(.5f, 1.1f, 0, 0) : new Vector4(-100000, 100000, 0, 0));
                commands.Clear();
                commands.DrawMesh(footprint, Matrix4x4.identity, material, 0, material.FindPass("GroundProjection"));
                commands.DrawMesh(floorFootprint, Matrix4x4.identity, floorMaterial, 0, floorMaterial.FindPass("GroundProjection"));
                camera.Render();
                // Exercise the final composition, including a native image-effect
                // Y flip and a 2x -> docked viewport resize. Coverage must follow
                // the same pixels as the physical scene through both operations.
                if (nativeFlip)
                    Graphics.Blit(source, processed, copy);
                else
                    Graphics.Blit(source, processed);
                Graphics.Blit(processed, layer, copy);
                RenderTexture.active = layer;
                readback.ReadPixels(new Rect(0, 0, 128, 96), 0, 0);
                readback.Apply();
                var pixels = readback.GetPixels();
                int raisedSamples = 0,
                    raisedTint = 0,
                    floorSamples = 0,
                    floorMisses = 0;
                int expected = 0,
                    missed = 0,
                    forbidden = 0,
                    leaked = 0;
                for (var y = 1; y < 95; y++)
                for (var x = 1; x < 127; x++)
                {
                    var ray = camera.ViewportPointToRay(new Vector3((x + .5f) / 128, (y + .5f) / 96, 0));
                    RaycastHit closest = default;
                    var distance = float.PositiveInfinity;
                    foreach (var collider in colliders)
                        if (collider.Raycast(ray, out var hit, 100) && hit.distance < distance)
                        {
                            closest = hit;
                            distance = hit.distance;
                        }
                    var presentedY = SystemInfo.graphicsUVStartsAtTop && !nativeFlip ? 95 - y : y;
                    var pixel = pixels[presentedY * 128 + x];
                    var painted = pixel.b - pixel.r > .15f;
                    if (float.IsPositiveInfinity(distance))
                    {
                        forbidden++;
                        if (painted)
                            leaked++;
                        continue;
                    }
                    var p = closest.point;
                    // Ignore raster boundaries in the comparison, not whole missing areas.
                    if (Mathf.Min(Mathf.Abs(p.x - 3), Mathf.Abs(p.x - 5), Mathf.Abs(p.z - 3), Mathf.Abs(p.z - 5)) < .12f)
                        continue;
                    if (p.x < .12f || p.x > 7.88f || p.z < .12f || p.z > 7.88f)
                        continue;
                    if (floor && (Mathf.Abs(p.y - .5f) < .08f || Mathf.Abs(p.y - 1.1f) < .08f))
                        continue;
                    var raisedObject =
                        closest.collider.gameObject == pipe
                        || closest.collider.gameObject == foliage
                        || closest.collider.gameObject == grass;
                    if (raisedObject)
                    {
                        raisedSamples++;
                        if (painted)
                            raisedTint++;
                    }
                    var floorTop = closest.collider.gameObject == upper && closest.normal.y > .9f && !floor;
                    if (floorTop && p.x > 1.1f && p.x < 1.9f && p.z > 2.6f && p.z < 3.4f)
                    {
                        floorSamples++;
                        if (!painted)
                            floorMisses++;
                    }
                    var wanted =
                        (floorTop || (closest.collider == groundCollider && !(p.x > 3 && p.x < 5 && p.z > 3 && p.z < 5) && p.y < 2))
                        && Mathf.Abs(closest.normal.y) > .45f
                        && (!floor || (p.y >= .5f && p.y <= 1.1f));
                    if (wanted)
                    {
                        expected++;
                        if (!painted)
                            missed++;
                    }
                    else
                    {
                        forbidden++;
                        if (painted)
                        {
                            leaked++;
                        }
                    }
                }
                Debug.Log(
                    $"Receiver rejection: {raisedTint}/{raisedSamples} raised-object pixels tinted; {floorSamples - floorMisses}/{floorSamples} elevated floor pixels covered."
                );
                if (raisedTint > 3 || floorMisses > 2 || (pose.y > 5 && (raisedSamples < 10 || (!floor && floorSamples < 2))))
                    throw new InvalidOperationException("Surface receiver regression: foliage/pipe tint or missing elevated floor.");
                if (expected < 30 || missed > expected * .03f || leaked > forbidden * .01f + 5)
                    throw new InvalidOperationException(
                        $"Ground projection failed {rendering}/{pose}/floor={floor}/nativeFlip={nativeFlip}: expected={expected}, missed={missed}, forbidden={forbidden}, leaked={leaked}."
                    );
                Debug.Log(
                    $"Ground projection GPU: {rendering}, camera={pose}, floor={floor}, nativeFlip={nativeFlip}, covered={expected - missed}/{expected}, leaks={leaked}/{forbidden}; curved ground, real hole, wall, upper floor, foliage, grass, pipe and final viewport composition from a 2x native target."
                );
            }
        }
        finally
        {
            RenderTexture.active = previous;
            commands.Release();
            UnityEngine.Object.DestroyImmediate(host);
            UnityEngine.Object.DestroyImmediate(ground);
            UnityEngine.Object.DestroyImmediate(wall);
            UnityEngine.Object.DestroyImmediate(upper);
            UnityEngine.Object.DestroyImmediate(pipe);
            UnityEngine.Object.DestroyImmediate(foliage);
            UnityEngine.Object.DestroyImmediate(grass);
            UnityEngine.Object.DestroyImmediate(terrainData);
            UnityEngine.Object.DestroyImmediate(hill);
            UnityEngine.Object.DestroyImmediate(footprint);
            UnityEngine.Object.DestroyImmediate(floorFootprint);
            UnityEngine.Object.DestroyImmediate(floorMaterial);
            UnityEngine.Object.DestroyImmediate(opaque);
            UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(copy);
            processed.Release();
            UnityEngine.Object.DestroyImmediate(processed);
            UnityEngine.Object.DestroyImmediate(readback);
            source.Release();
            layer.Release();
            UnityEngine.Object.DestroyImmediate(source);
            UnityEngine.Object.DestroyImmediate(layer);
        }
    }

    private static void ValidateNavigationFloors(Shader shader)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            return;
        var previous = RenderTexture.active;
        var objects = new List<GameObject>();
        var meshes = new List<Mesh>();
        var receivers = new HashSet<Collider>();
        var colliders = new List<Collider>();
        var opaque = new Material(
            AssetDatabase.LoadAssetAtPath<Shader>("Assets/Mods/WTT-Campaigns.Assets/EditorToolkit/NavigationProjectionTest.shader")
        );
        var surface = new Material(shader) { color = new Color(.2f, .65f, 1, .3f) };
        var commands = new UnityEngine.Rendering.CommandBuffer();
        var target = new RenderTexture(512, 384, 24);
        var readback = new Texture2D(512, 384, TextureFormat.RGBA32, false, true);
        var vertices = new List<Vector3>();
        var indices = new List<int>();
        GameObject Box(string name, Vector3 center, Vector3 size, bool receiver)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            objects.Add(obj);
            obj.name = name;
            obj.layer = 31;
            obj.transform.position = center;
            obj.transform.localScale = size;
            obj.GetComponent<MeshRenderer>().sharedMaterial = opaque;
            var collider = obj.GetComponent<Collider>();
            colliders.Add(collider);
            if (receiver)
                receivers.Add(collider);
            return obj;
        }
        void Coverage(float x0, float x1, float z0, float z1, float y0, float y1)
        {
            var first = vertices.Count;
            vertices.AddRange(new[] { new Vector3(x0, y0, z0), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), new Vector3(x1, y0, z0) });
            indices.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
        }
        try
        {
            var floor = Box("Offset indoor floor", new Vector3(0, -.1f, 6), new Vector3(18, .2f, 16), true);
            Coverage(-9, 9, -2, 14, .18f, .18f);
            // Two triangle ramp, with the baked navigation surface lifted by a voxel.
            var ramp = new GameObject("Sloping ramp") { layer = 31 };
            objects.Add(ramp);
            var rampMesh = new Mesh();
            meshes.Add(rampMesh);
            rampMesh.vertices = new[] { new Vector3(-3, 0, 2), new Vector3(-3, 3, 8), new Vector3(0, 3, 8), new Vector3(0, 0, 2) };
            rampMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            rampMesh.RecalculateNormals();
            rampMesh.RecalculateBounds();
            ramp.AddComponent<MeshFilter>().sharedMesh = rampMesh;
            ramp.AddComponent<MeshRenderer>().sharedMaterial = opaque;
            var rampCollider = ramp.AddComponent<MeshCollider>();
            rampCollider.sharedMesh = rampMesh;
            colliders.Add(rampCollider);
            receivers.Add(rampCollider);
            Coverage(-3, 0, 2, 8, .18f, 3.18f);
            // Twelve discrete treads represented by one smooth navigation ramp.
            for (var step = 0; step < 12; step++)
                Box(
                    "Stair tread",
                    new Vector3(3.5f, (step + 1) * .125f, 2 + (step + .5f) * .5f),
                    new Vector3(3, (step + 1) * .25f, .5f),
                    true
                );
            Coverage(2, 5, 2, 8, .15f, 3.15f);
            var upper = Box("Upper storey", new Vector3(-5, 3.9f, 5), new Vector3(3, .2f, 4), true);
            Coverage(-6.5f, -3.5f, 3, 7, 4.18f, 4.18f);
            Box("Furniture", new Vector3(7, .5f, 5), new Vector3(1.5f, 1, 2), false);
            Box("Unmapped overhead slab", new Vector3(0, 1.1f, 11), new Vector3(3, .2f, 2), false);
            Box("Wall", new Vector3(-7, 1.5f, 10), new Vector3(.3f, 3, 3), false);
            var footprint = new Mesh();
            meshes.Add(footprint);
            footprint.SetVertices(vertices);
            footprint.SetTriangles(indices, 0);
            footprint.RecalculateBounds();
            var host = new GameObject("Indoor navigation validation camera");
            objects.Add(host);
            var camera = host.AddComponent<Camera>();
            camera.enabled = false;
            camera.cullingMask = 1 << 31;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 200;
            camera.allowHDR = camera.allowMSAA = false;
            camera.depthTextureMode = DepthTextureMode.Depth;
            camera.targetTexture = target;
            target.Create();
            Physics.SyncTransforms();
            surface.SetFloat("_ProjectGround", 1);
            camera.AddCommandBuffer(UnityEngine.Rendering.CameraEvent.BeforeForwardAlpha, commands);
            foreach (var path in new[] { RenderingPath.Forward, RenderingPath.DeferredShading })
            foreach (var pose in new[] { new Vector3(12, 14, -8), new Vector3(-11, 9, 16) })
            foreach (var strict in new[] { false, true })
            {
                camera.renderingPath = path;
                camera.transform.position = pose;
                camera.transform.LookAt(new Vector3(0, 1, 6));
                surface.SetVector("_MeshContactRange", strict ? new Vector4(.06f, .06f, 0, .45f) : new Vector4(.64f, .16f, .38f, .6428f));
                commands.Clear();
                commands.DrawMesh(footprint, Matrix4x4.identity, surface, 0, surface.FindPass("GroundProjection"));
                camera.Render();
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, 512, 384), 0, 0);
                readback.Apply();
                var pixels = readback.GetPixels();
                int expected = 0,
                    painted = 0,
                    forbidden = 0,
                    leaked = 0,
                    stairSamples = 0,
                    stairPainted = 0,
                    rampSamples = 0,
                    rampPainted = 0;
                for (var y = 1; y < 383; y++)
                for (var x = 1; x < 511; x++)
                {
                    var ray = camera.ViewportPointToRay(new Vector3((x + .5f) / 512, (y + .5f) / 384, 0));
                    RaycastHit closest = default;
                    var distance = float.PositiveInfinity;
                    foreach (var collider in colliders)
                        if (collider.Raycast(ray, out var hit, 200) && hit.distance < distance)
                        {
                            closest = hit;
                            distance = hit.distance;
                        }
                    if (float.IsPositiveInfinity(distance))
                        continue;
                    var p = closest.point;
                    // Omit geometry silhouettes and tread lips, where pixel coverage is mixed.
                    var b = closest.collider.bounds;
                    if (
                        Mathf.Min(Mathf.Abs(p.x - b.min.x), Mathf.Abs(p.x - b.max.x), Mathf.Abs(p.z - b.min.z), Mathf.Abs(p.z - b.max.z))
                        < .06f
                    )
                        continue;
                    if (closest.collider == rampCollider || closest.collider.gameObject.name == "Stair tread")
                    {
                        // A world-space margin alone is insufficient at grazing
                        // angles. Exclude pixels whose immediate screen neighbors
                        // see a different surface or a riser instead of this tread.
                        var interior = true;
                        for (var side = 0; side < 4 && interior; side++)
                        {
                            var nx =
                                x
                                + (
                                    side == 0 ? -1
                                    : side == 1 ? 1
                                    : 0
                                );
                            var ny =
                                y
                                + (
                                    side == 2 ? -1
                                    : side == 3 ? 1
                                    : 0
                                );
                            var neighborRay = camera.ViewportPointToRay(new Vector3((nx + .5f) / 512, (ny + .5f) / 384, 0));
                            RaycastHit neighbor = default;
                            var neighborDistance = float.PositiveInfinity;
                            foreach (var collider in colliders)
                                if (collider.Raycast(neighborRay, out var hit, 200) && hit.distance < neighborDistance)
                                {
                                    neighbor = hit;
                                    neighborDistance = hit.distance;
                                }
                            interior = neighbor.collider == closest.collider && Vector3.Dot(neighbor.normal, closest.normal) > .98f;
                        }
                        if (!interior)
                            continue;
                    }
                    var color = pixels[y * 512 + x];
                    var tinted = color.b - color.r > .15f;
                    var wanted = receivers.Contains(closest.collider) && closest.normal.y > .64f;
                    if (wanted)
                    {
                        expected++;
                        if (tinted)
                            painted++;
                    }
                    else
                    {
                        forbidden++;
                        if (tinted)
                            leaked++;
                    }
                    if (closest.collider.gameObject.name == "Stair tread" && closest.normal.y > .9f)
                    {
                        stairSamples++;
                        if (tinted)
                            stairPainted++;
                    }
                    if (closest.collider == rampCollider)
                    {
                        rampSamples++;
                        if (tinted)
                            rampPainted++;
                    }
                }
                Debug.Log(
                    $"Indoor navigation GPU: {path}, camera={pose}, strict={strict}, floor coverage={painted}/{expected}, stair treads={stairPainted}/{stairSamples}, ramp={rampPainted}/{rampSamples}, forbidden tint={leaked}/{forbidden}."
                );
                if (
                    expected < 100
                    || stairSamples < 40
                    || rampSamples < 40
                    || (
                        strict
                            ? painted > expected * .7f
                            : painted < expected * .97f || stairPainted < stairSamples * .97f || rampPainted < rampSamples * .97f
                    )
                    || leaked > forbidden * .01f + 3
                )
                    throw new InvalidOperationException("Indoor floor/ramp/stair receiver regression.");
            }
        }
        finally
        {
            RenderTexture.active = previous;
            commands.Release();
            foreach (var obj in objects)
                UnityEngine.Object.DestroyImmediate(obj);
            foreach (var mesh in meshes)
                UnityEngine.Object.DestroyImmediate(mesh);
            UnityEngine.Object.DestroyImmediate(opaque);
            UnityEngine.Object.DestroyImmediate(surface);
            UnityEngine.Object.DestroyImmediate(readback);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    private static void ValidateNavigationDistance(Shader shader)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            return;
        var previous = RenderTexture.active;
        var host = new GameObject("Navigation distance camera");
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var terrain = new TerrainData { heightmapResolution = 33, size = new Vector3(600, 4, 600) };
        var footprint = new Mesh();
        var surface = new Material(shader) { color = new Color(.2f, .65f, 1, .3f) };
        var opaque = new Material(
            AssetDatabase.LoadAssetAtPath<Shader>("Assets/Mods/WTT-Campaigns.Assets/EditorToolkit/NavigationProjectionTest.shader")
        );
        var commands = new UnityEngine.Rendering.CommandBuffer();
        var target = new RenderTexture(512, 384, 24);
        var readback = new Texture2D(512, 384, TextureFormat.RGBA32, false, true);
        try
        {
            ground.layer = obstacle.layer = 31;
            ground.transform.position = new Vector3(0, -.05f, 0);
            ground.transform.localScale = new Vector3(600, .1f, 600);
            obstacle.transform.position = new Vector3(40, .4f, 40);
            obstacle.transform.localScale = new Vector3(40, .8f, 40);
            ground.GetComponent<MeshRenderer>().sharedMaterial = opaque;
            obstacle.GetComponent<MeshRenderer>().sharedMaterial = opaque;
            // A coarse distant terrain patch omits height detail retained by its
            // native height texture. The old fixed 6cm gate rejects this patch.
            var heights = new float[33, 33];
            for (var z = 0; z < 33; z++)
            for (var x = 0; x < 33; x++)
                heights[z, x] = (.16f + .08f * Mathf.Sin(x * .4f) * Mathf.Sin(z * .4f)) / 4;
            terrain.SetHeights(0, 0, heights);
            surface.SetFloat("_ProjectGround", 1);
            surface.SetFloat("_TerrainReceiver", 1);
            surface.SetTexture("_TerrainHeightmap", terrain.heightmapTexture);
            surface.SetVector("_TerrainRegion", new Vector4(-300, -300, 1f / 600, 1f / 600));
            surface.SetVector("_TerrainHeight", new Vector4(0, 4 * (65535f / 32766f), 32f / 33, .5f / 33));
            footprint.vertices = new[]
            {
                new Vector3(-300, 0, -300),
                new Vector3(-300, 0, 300),
                new Vector3(300, 0, 300),
                new Vector3(300, 0, -300),
            };
            footprint.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            footprint.RecalculateBounds();
            var camera = host.AddComponent<Camera>();
            camera.enabled = false;
            camera.cullingMask = 1 << 31;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = .03f;
            camera.farClipPlane = 3000;
            camera.allowHDR = camera.allowMSAA = false;
            camera.depthTextureMode = DepthTextureMode.Depth;
            camera.targetTexture = target;
            target.Create();
            camera.AddCommandBuffer(UnityEngine.Rendering.CameraEvent.BeforeForwardAlpha, commands);
            var groundCollider = ground.GetComponent<Collider>();
            var obstacleCollider = obstacle.GetComponent<Collider>();
            Physics.SyncTransforms();
            foreach (var rendering in new[] { RenderingPath.Forward, RenderingPath.DeferredShading })
            foreach (var pixelError in new[] { 5f, 40f })
            foreach (var distance in new[] { 10f, 20f, 25f, 35f, 50f, 250f, 750f, 1500f })
            foreach (var fixedGate in new[] { false, true })
            {
                if (pixelError == 5 && distance < 50)
                    continue;
                if (fixedGate && distance != 250 && distance != 20)
                    continue;
                camera.renderingPath = rendering;
                camera.transform.position = new Vector3(0, distance, -distance * .5f);
                camera.transform.LookAt(Vector3.zero);
                surface.SetFloat("_TerrainPixelError", fixedGate ? 0 : pixelError);
                surface.SetFloat("_ReceiverDepthPrecision", fixedGate ? 0 : 1);
                var obstacleHeight = distance >= 750 ? 12f : .8f;
                obstacle.transform.position = new Vector3(40, obstacleHeight / 2, 40);
                obstacle.transform.localScale = new Vector3(40, obstacleHeight, 40);
                Physics.SyncTransforms();
                commands.Clear();
                commands.DrawMesh(footprint, Matrix4x4.identity, surface, 0, surface.FindPass("GroundProjection"));
                camera.Render();
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, 512, 384), 0, 0);
                readback.Apply();
                var pixels = readback.GetPixels();
                int expected = 0,
                    painted = 0,
                    propPixels = 0,
                    propTint = 0;
                for (var y = 1; y < 383; y++)
                for (var x = 1; x < 511; x++)
                {
                    var ray = camera.ViewportPointToRay(new Vector3((x + .5f) / 512, (y + .5f) / 384, 0));
                    if (!groundCollider.Raycast(ray, out var hit, 3000))
                        continue;
                    if (Mathf.Abs(hit.point.x) > 298 || Mathf.Abs(hit.point.z) > 298)
                        continue;
                    var color = pixels[y * 512 + x];
                    var tinted = color.b - color.r > .15f;
                    if (obstacleCollider.Raycast(ray, out var obstacleHit, hit.distance))
                    {
                        // Exclude a one-world-unit silhouette band from raster checks.
                        if (obstacleHit.point.x > 21 && obstacleHit.point.x < 59 && obstacleHit.point.z > 21 && obstacleHit.point.z < 59)
                        {
                            propPixels++;
                            if (tinted)
                                propTint++;
                        }
                        continue;
                    }
                    expected++;
                    if (tinted)
                        painted++;
                }
                Debug.Log(
                    $"Navigation distance GPU: {rendering}, height={distance}m, pixelError={pixelError}, fixedGate={fixedGate}, coverage={painted}/{expected}, prop tint={propTint}/{propPixels}."
                );
                if (expected < 100 || (fixedGate ? painted > expected * .9f : painted < expected * .97f) || propTint > 2)
                    throw new InvalidOperationException("Navigation distance/terrain LOD regression.");
            }
        }
        finally
        {
            RenderTexture.active = previous;
            commands.Release();
            UnityEngine.Object.DestroyImmediate(host);
            UnityEngine.Object.DestroyImmediate(ground);
            UnityEngine.Object.DestroyImmediate(obstacle);
            UnityEngine.Object.DestroyImmediate(terrain);
            UnityEngine.Object.DestroyImmediate(footprint);
            UnityEngine.Object.DestroyImmediate(surface);
            UnityEngine.Object.DestroyImmediate(opaque);
            UnityEngine.Object.DestroyImmediate(readback);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    private static void ValidateViewportCopy(Shader shader)
    {
        if (!shader || ShaderUtil.ShaderHasError(shader))
            throw new InvalidOperationException("Viewport copy shader failed to compile.");
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            return;
        var previous = RenderTexture.active;
        var srgbWrite = GL.sRGBWrite;
        var material = new Material(shader);
        var source = new Texture2D(4, 4, TextureFormat.RGBAFloat, false, true);
        var readback = new Texture2D(4, 4, TextureFormat.RGBAFloat, false, true);
        var linear = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        try
        {
            linear.Create();
            foreach (var alpha in new[] { 0f, .05f, 1f })
            foreach (var encoding in new[] { RenderTextureReadWrite.Linear, RenderTextureReadWrite.sRGB })
            {
                var colors = new Color[16];
                // Distinct rows AND columns detect inversion, mirroring and rotation.
                // A uniform image can check opacity but cannot detect an upside-down frame.
                for (var y = 0; y < 4; y++)
                for (var x = 0; x < 4; x++)
                    colors[y * 4 + x] = new Color(.1f + x * .2f, .1f + y * .2f, .75f, alpha);
                source.filterMode = FilterMode.Point;
                source.SetPixels(colors);
                source.Apply();
                var target = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32, encoding);
                try
                {
                    target.Create();
                    GL.sRGBWrite = target.sRGB;
                    Graphics.Blit(source, target, material);
                    GL.sRGBWrite = false;
                    Graphics.Blit(target, linear);
                    RenderTexture.active = linear;
                    readback.ReadPixels(new Rect(0, 0, 4, 4), 0, 0);
                    readback.Apply();
                    var actual = readback.GetPixels();
                    for (var y = 0; y < 4; y++)
                    for (var x = 0; x < 4; x++)
                    {
                        var pixel = actual[y * 4 + x];
                        var sourceY = SystemInfo.graphicsUVStartsAtTop ? 3 - y : y;
                        var expected = colors[sourceY * 4 + x];
                        if (
                            Mathf.Abs(pixel.r - expected.r) > .012f
                            || Mathf.Abs(pixel.g - expected.g) > .012f
                            || Mathf.Abs(pixel.b - expected.b) > .012f
                            || pixel.a < .99f
                        )
                            throw new InvalidOperationException(
                                $"Viewport orientation/color/opacity regression: {encoding}, source alpha={alpha}, pixel=({x},{y}), expected={expected}, actual={pixel}."
                            );
                    }
                }
                finally
                {
                    target.Release();
                    UnityEngine.Object.DestroyImmediate(target);
                }
            }
            Debug.Log(
                "Viewport GPU regression: asymmetric rows/columns have the correct presentation orientation; RGB preserved and output opaque for zero, partial and full source alpha in linear and sRGB targets."
            );
        }
        finally
        {
            RenderTexture.active = previous;
            GL.sRGBWrite = srgbWrite;
            linear.Release();
            UnityEngine.Object.DestroyImmediate(linear);
            UnityEngine.Object.DestroyImmediate(source);
            UnityEngine.Object.DestroyImmediate(readback);
            UnityEngine.Object.DestroyImmediate(material);
        }
    }

    public static void Build()
    {
        const string folder = "Assets/Mods/WTT-Campaigns.Assets/EditorToolkit";
        var project = Path.GetFullPath(Path.Combine(Application.dataPath, "../../SeasonalPerks"));
        Directory.CreateDirectory(folder);
        var sources = new List<SourceHash>();
        foreach (var file in Directory.GetFiles(Path.Combine(project, "tools/unity/EditorToolkit")))
        {
            if (Path.GetExtension(file) != ".uxml" && Path.GetExtension(file) != ".uss" && Path.GetExtension(file) != ".shader")
                continue;
            File.Copy(file, folder + "/" + Path.GetFileName(file), true);
            sources.Add(new SourceHash { file = "EditorToolkit/" + Path.GetFileName(file), sha256 = Hash(file) });
        }
        sources.Add(
            new SourceHash
            {
                file = "CampaignsEditorToolkitBuilder.cs",
                sha256 = Hash(Path.Combine(project, "tools/unity/CampaignsEditorToolkitBuilder.cs")),
            }
        );
        File.WriteAllText(folder + "/Editor.tss", "@import url(\"unity-theme://default\");\n");
        AssetDatabase.Refresh();
        var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(folder + "/Editor.tss");
        var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(folder + "/Editor.uxml");
        if (!theme || !tree || tree.CloneTree().Q("surface") == null)
            throw new InvalidOperationException("Editor Toolkit theme or visual tree failed to import.");
        ValidateTemplates(name => AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(folder + "/" + name + ".uxml"));
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.themeStyleSheet = theme;
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        var path = folder + "/EditorPanel.asset";
        if (AssetDatabase.LoadAssetAtPath<PanelSettings>(path))
            AssetDatabase.DeleteAsset(path);
        // Built-in shader references can point to assets stripped from EFT. Clone the
        // compiled engine shaders into owned assets and prove they survive the bundle.
        var serialized = new SerializedObject(settings);
        foreach (var property in new[] { "m_AtlasBlitShader", "m_RuntimeShader", "m_RuntimeWorldShader" })
        {
            var field = serialized.FindProperty(property);
            if (field == null || !field.objectReferenceValue)
                throw new Exception("Missing panel shader: " + property);
            var shader = UnityEngine.Object.Instantiate(field.objectReferenceValue);
            var shaderPath = folder + "/" + property + ".asset";
            if (AssetDatabase.LoadMainAssetAtPath(shaderPath))
                AssetDatabase.DeleteAsset(shaderPath);
            AssetDatabase.CreateAsset(shader, shaderPath);
            field.objectReferenceValue = shader;
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.CreateAsset(settings, path);
        // Serialized PanelSettings includes the engine's UI shaders so the mod does not
        // depend on EFT having kept unused runtime Toolkit shaders in its player build.
        AssetDatabase.SaveAssets();
        var output = Path.Combine(project, "Client/Resources");
        const string bundleName = "wtt_campaigns_editor_toolkit.bundle";
        var assets = new List<string>
        {
            path,
            folder + "/Editor.uxml",
            folder + "/Editor.uss",
            folder + "/ViewportCopy.shader",
            folder + "/NavigationSurface.shader",
            folder + "/m_RuntimeShader.asset",
            folder + "/m_RuntimeWorldShader.asset",
            folder + "/m_AtlasBlitShader.asset",
            "Assets/Mods/WTT-Campaigns.Assets/Fonts/bender.ttf",
            "Assets/Mods/WTT-Campaigns.Assets/RaidEditor/CampaignScenePreview.shader",
        };
        foreach (var name in Templates)
            assets.Add(folder + "/" + name + ".uxml");
        var manifest = BuildPipeline.BuildAssetBundles(
            output,
            new[]
            {
                new AssetBundleBuild { assetBundleName = bundleName, assetNames = assets.ToArray() },
            },
            BuildAssetBundleOptions.ForceRebuildAssetBundle,
            BuildTarget.StandaloneWindows64
        );
        if (!manifest || manifest.GetAllDependencies(bundleName).Length != 0)
            throw new InvalidOperationException("Editor Toolkit bundle must be self-contained.");
        var bundle = AssetBundle.LoadFromFile(Path.Combine(output, bundleName));
        if (!bundle || !bundle.LoadAsset<PanelSettings>(path) || !bundle.LoadAsset<VisualTreeAsset>(folder + "/Editor.uxml"))
            throw new InvalidOperationException("Editor Toolkit bundle reload failed.");
        if (bundle.LoadAllAssets<Shader>().Length < 3)
            throw new InvalidOperationException("Runtime UI Toolkit shaders were not embedded in the bundle.");
        ValidateTemplates(name => bundle.LoadAsset<VisualTreeAsset>(folder + "/" + name + ".uxml"));
        ValidateGroundProjection(bundle.LoadAsset<Shader>(folder + "/NavigationSurface.shader"));
        ValidateNavigationDistance(bundle.LoadAsset<Shader>(folder + "/NavigationSurface.shader"));
        ValidateNavigationFloors(bundle.LoadAsset<Shader>(folder + "/NavigationSurface.shader"));
        ValidateViewportCopy(bundle.LoadAsset<Shader>(folder + "/ViewportCopy.shader"));
        ValidateNavigationShader(bundle.LoadAsset<Shader>(folder + "/NavigationSurface.shader"));
        bundle.Unload(true);
        File.WriteAllText(
            Path.Combine(output, "editor-toolkit-validation.json"),
            JsonUtility.ToJson(
                new Validation
                {
                    unity = Application.unityVersion,
                    sha256 = Hash(Path.Combine(output, bundleName)),
                    sources = sources.ToArray(),
                },
                true
            )
        );
        Debug.Log("Editor Toolkit assets imported, bundled and reloaded successfully.");
    }
}
