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
        ValidateViewportCopy(bundle.LoadAsset<Shader>(folder + "/ViewportCopy.shader"));
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
