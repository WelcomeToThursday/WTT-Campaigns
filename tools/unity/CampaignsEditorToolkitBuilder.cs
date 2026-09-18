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
                name == "HomePicker" ? new[] { "Heading:Label", "Close:Button", "Choices:ListView", "Empty:Label" }
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
            if (name == "Inspector")
            {
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
                    || first.Q<Button>("MapNormalRaid") == null
                )
            )
                throw new InvalidOperationException("Properties template is missing typed runtime slots.");
            if (name == "EnvironmentMenu")
            {
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

    public static void Build()
    {
        const string folder = "Assets/Mods/WTT-Campaigns.Assets/EditorToolkit";
        var project = Path.GetFullPath(Path.Combine(Application.dataPath, "../../SeasonalPerks"));
        Directory.CreateDirectory(folder);
        var sources = new List<SourceHash>();
        foreach (var file in Directory.GetFiles(Path.Combine(project, "tools/unity/EditorToolkit")))
        {
            if (Path.GetExtension(file) != ".uxml" && Path.GetExtension(file) != ".uss")
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
