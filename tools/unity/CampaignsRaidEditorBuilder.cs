using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WTT.Campaigns.UI.Controls;
using WTT.Campaigns.UI.Screens;

public static class CampaignsRaidEditorBuilder
{
    private const string Root = "Assets/Mods/WTT-Campaigns.Assets";

    private static GameObject Create()
    {
        var font = AssetDatabase.LoadAssetAtPath<Font>(Root + "/Fonts/bender.ttf");
        var border = AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/SelectionArtwork/confirmation-border.png");
        var header = AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/SelectionArtwork/footer-gradient.png");
        if (!font || !border || !header)
            throw new InvalidOperationException("Recovered EFT font and window sprites are required.");
        return RaidEditorLayout.Build(font, border, header);
    }

    [MenuItem("SDK/WTT-Campaigns/Build raid editor")]
    public static void Build()
    {
        var root = Create();
        Directory.CreateDirectory(Root + "/RaidEditor");
        var prefab = Root + "/RaidEditor/SeasonalRaidEditor.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, prefab);
        UnityEngine.Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        var project = Path.GetFullPath(Path.Combine(Application.dataPath, "../../SeasonalPerks"));
        var output = Path.Combine(project, "Client/Resources");
        var manifest = BuildPipeline.BuildAssetBundles(
            output,
            new[]
            {
                new AssetBundleBuild
                {
                    assetBundleName = "wtt_campaigns_raid_editor.bundle",
                    assetNames = new[] { prefab, Root + "/RaidEditor/CampaignScenePreview.shader" },
                },
            },
            BuildAssetBundleOptions.ForceRebuildAssetBundle,
            BuildTarget.StandaloneWindows64
        );
        if (!manifest)
            throw new InvalidOperationException("Raid editor bundle build failed.");
        if (manifest.GetAllDependencies("wtt_campaigns_raid_editor.bundle").Length != 0)
            throw new Exception("Raid editor bundle must be self-contained.");
        Preview();
        var bundle = AssetBundle.LoadFromFile(Path.Combine(output, "wtt_campaigns_raid_editor.bundle"));
        if (!bundle)
            throw new Exception("Cannot reload editor bundle.");
        var previewCheck = RenderPreviewFixture(
            bundle.LoadAsset<Shader>((Root + "/RaidEditor/CampaignScenePreview.shader").ToLowerInvariant())
        );
        UnityEngine.Object.DestroyImmediate(previewCheck);
        var loaded = UnityEngine.Object.Instantiate(bundle.LoadAsset<GameObject>(prefab.ToLowerInvariant()));
        Validate(loaded);
        UnityEngine.Object.DestroyImmediate(loaded);
        bundle.Unload(true);
        using (var sha = System.Security.Cryptography.SHA256.Create())
        {
            var hash = BitConverter
                .ToString(sha.ComputeHash(File.ReadAllBytes(Path.Combine(output, "wtt_campaigns_raid_editor.bundle"))))
                .Replace("-", "");
            File.WriteAllText(
                Path.Combine(output, "raid-editor-validation.json"),
                "{\"schema\":2,\"sha256\":\"" + hash + "\",\"validated\":true}"
            );
        }
    }

    private static void Validate(GameObject root)
    {
        void Check(bool value, string message)
        {
            if (!value)
                throw new Exception(message);
        }
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
            Check(component && component.GetType().Namespace == "UnityEngine.UI", "Bundle contains a missing or non-native UI script.");
        // Replay the actual client startup sequence on the serialized, already-migrated bundle.
        // The preview used to skip these upgrades and missed stale hierarchy paths at runtime.
        var beforePrepare = root.GetComponentsInChildren<Transform>(true).Length;
        RaidEditorLayout.Prepare(root);
        RaidEditorLayout.Prepare(root);
        Check(
            root.GetComponentsInChildren<Transform>(true).Length == beforePrepare,
            "Client startup duplicated controls in a modern tool-window bundle."
        );
        Check(root.transform.Find("ToolWindows/Library/CategoryRail/Routes"), "Routes must remain in the floating Browser.");
        Check(root.transform.Find("ToolWindows/EnvironmentMenu"), "Environment must remain a floating window after client startup.");
        foreach (
            var control in new[]
            {
                "EditorHome",
                "EditorMapToolbar",
                "MapInspector",
                "EditorOpen",
                "EditorReturn",
                "EditorWalk",
                "MapNew",
                "MapRebind",
                "SceneTabs",
                "SceneInspector",
                "LibraryScroll",
                "ScenePlace",
                "SceneRemove",
                "SceneRestore",
            }
        )
            Check(
                Array.Exists(root.GetComponentsInChildren<Transform>(true), t => t.name == control),
                "Missing editor control: " + control
            );
        var controls = root.GetComponentsInChildren<Transform>(true);
        foreach (var button in root.GetComponentsInChildren<Button>(true))
            Check(
                Array.FindAll(controls, t => t.name == button.name && t.GetComponent<Button>()).Length == 1,
                "Duplicate bound button: " + button.name
            );
        foreach (var field in root.GetComponentsInChildren<InputField>(true))
            Check(
                Array.FindAll(controls, t => t.name == field.name && t.GetComponent<InputField>()).Length == 1,
                "Duplicate bound input: " + field.name
            );

        foreach (
            var name in new[]
            {
                "EditorDraft",
                "EditorLayout",
                "EditorMap",
                "EditorOpen",
                "EditorRefresh",
                "EditorRetry",
                "EditorReturn",
                "EditorStartup",
                "EditorWeb",
                "EditorWalk",
                "EditorReset",
                "EditorUnload",
                "MapNew",
                "MapCopy",
                "MapDelete",
                "MapStart",
                "MapCheckpoint",
                "MapExit",
                "MapBarrier",
                "MapShape",
                "MapMoveObject",
                "MapCopyObject",
                "MapHideObject",
                "MapDoor",
                "MapRebind",
                "MapAtPlayer",
                "MapEarlier",
                "MapLater",
                "MapWalkStart",
            }
        )
            Check(
                Array.FindAll(controls, t => t.name == name && t.GetComponent<Button>()).Length == 1,
                "Missing or duplicate bound editor button: " + name
            );
        foreach (var group in new[] { "Position", "Rotation", "Size" })
        foreach (var axis in "XYZ")
            Check(
                Array.FindAll(controls, t => t.name == "Map" + group + axis && t.GetComponent<InputField>()).Length == 1,
                "Missing transform input: " + group + axis
            );
        foreach (var name in new[] { "Maps", "Zones", "Bindings", "Captures", "Scene" })
        {
            var tab = Array.Find(controls, t => t.name == name).GetComponentInChildren<Text>();
            Check(tab.preferredWidth <= ((RectTransform)tab.transform).rect.width + .5f, "Module label does not fit: " + name);
        }
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        Canvas.ForceUpdateCanvases();
        var host = root.AddComponent<RaidEditorWindows>();
        host.Initialize();
        foreach (
            var id in new[]
            {
                "Maps",
                "Routes",
                "Zones",
                "Bindings",
                "Captures",
                "Scene",
                "Undo",
                "Redo",
                "Move",
                "Rotate",
                "Scale",
                "Snap",
            }
        )
        {
            var button = Array.Find(controls, t => t.name == id);
            Check(button.Find("ToolIcon") && button.Find("ToolIcon").GetComponent<Image>().sprite, "Tool icon was not preserved: " + id);
            Check(button.GetComponentInChildren<Text>(true).enabled, "Labeled tool lost its caption: " + id);
        }
        host.Present("Zones", "Box", true, false, false, true, false);
        host.Select("Zones", "zone-1");
        foreach (var module in RaidEditorLayout.Modules)
        {
            host.ShowPanel(module, true);
            var panel = Array.Find(controls, t => t.name == module);
            var field = panel.GetComponentInChildren<InputField>(true);
            var value = field.text;
            field.text = "Unsaved draft 42";
            Check(panel.parent.name == "ToolWindows", "Tool window is not floating: " + module);
            var drag = panel.GetComponentInChildren<EditorWindowDrag>();
            ((RectTransform)panel).anchoredPosition = new Vector2(99999, -99999);
            drag.Clamp();
            var saved = host.CaptureLayout();
            host.ResetLayout();
            host.RestoreLayout(saved);
            Check(field.text == "Unsaved draft 42", "Layout restore lost draft input.");
            var corners = new Vector3[4];
            ((RectTransform)panel).GetWorldCorners(corners);
            foreach (var corner in corners)
                Check(
                    ((RectTransform)root.transform).rect.Contains(root.transform.InverseTransformPoint(corner) * .999f),
                    "Window left canvas bounds."
                );
            var resize = panel.GetComponentInChildren<EditorWindowResize>();
            Check(resize && resize.Minimum.x >= 360, "Window has no bounded resize handle.");
            host.ResetLayout();
            host.ShowPanel(module, true);
            var initialSize = ((RectTransform)panel).sizeDelta;
            var eventObject = new GameObject("Window resize checks");
            var eventSystem = eventObject.AddComponent<EventSystem>();
            var pointer = new PointerEventData(eventSystem)
            {
                button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(null, resize.transform.position),
            };
            resize.OnBeginDrag(pointer);
            Check(host.Interacting, "Resizing must suspend scene keyboard/camera input.");
            pointer.position += new Vector2(-24, 32);
            resize.OnDrag(pointer);
            resize.OnEndDrag(pointer);
            Check(
                !host.Interacting && ((RectTransform)panel).sizeDelta != initialSize,
                "Resize did not change dimensions or release input: "
                    + module
                    + " initial="
                    + initialSize
                    + " after="
                    + ((RectTransform)panel).sizeDelta
                    + " root="
                    + ((RectTransform)root.transform).rect
                    + " busy="
                    + host.Interacting
            );
            Check(field.text == "Unsaved draft 42", "Resizing lost the active draft value.");
            UnityEngine.Object.DestroyImmediate(eventObject);
            host.ResetLayout();
            Check(panel.parent.name == "ToolWindows", "Reset did not restore floating panel.");
            field.text = value;
        }
        Transform Find(string name) => Array.Find(controls, t => t.name == name);
        ValidateSceneWorkflow(root, host, Find, Check);
        host.Present("Zones", "Sphere", true, false, false, true, false);
        Check(
            Find("RadiusGroup").gameObject.activeSelf && !Find("SizeGroup").gameObject.activeSelf,
            "Sphere inspector must show radius only."
        );
        host.Present("Bindings", "Point", true, false, false, true, false);
        Check(
            Find("EventKindGroup").gameObject.activeSelf && !Find("PositionGroup").gameObject.activeSelf,
            "Event inspector leaked transform fields."
        );
        host.Present("Captures", "Point", true, true, false, true, false);
        Check(
            Find("Capture").gameObject.activeInHierarchy && Find("Complete").gameObject.activeInHierarchy,
            "Capture request must retain capture and completion controls."
        );
        host.Present("Captures", "Scene", true, true, true, true, false);
        host.Select("Captures", "picked:42");
        Check(
            Find("Inspector").gameObject.activeSelf
                && Find("UseObject").gameObject.activeInHierarchy
                && !Find("PositionGroup").gameObject.activeSelf,
            "A picked object without a captured record must expose Use scene target, without editable transforms."
        );
        host.Present("Maps", "Door", true, false, true, true, false);
        Check(
            !Find("MapPositionGroup").gameObject.activeSelf && Find("MapRebind").gameObject.activeSelf,
            "Door inspector must offer rebind without transforms."
        );
        host.Present("Zones", "Box", true, false, false, true, false);
        host.Select("Zones", "zone-1");
        host.ShowPanel("Inspector", false);
        host.Select("Zones", "zone-1");
        Check(!Find("Inspector").gameObject.activeSelf, "Refresh reopened a manually collapsed panel.");
        host.Select("Zones", "zone-2");
        Check(Find("Inspector").gameObject.activeSelf, "New selection did not open Properties.");
        host.ToggleDock("Inspector");
        var floated = (RectTransform)Find("Inspector");
        var savedPosition = floated.anchoredPosition;
        host.SetWalkthrough(true);
        Check(
            !floated.gameObject.activeSelf && !Find("Workspace").gameObject.activeSelf && Find("EditorWalkStatus").gameObject.activeSelf,
            "Walkthrough left editing controls visible."
        );
        host.SetWalkthrough(false);
        Check(
            floated.gameObject.activeSelf && floated.parent.name == "ToolWindows" && floated.anchoredPosition == savedPosition,
            "Walkthrough lost the floating panel state."
        );
        host.ToggleDock("Inspector");
        Find("WindowsToggle").GetComponent<Button>().onClick.Invoke();
        Check(host.HasMenu && host.DismissMenus() && !host.HasMenu, "Escape menu dismissal contract failed.");
        Check(
            !Find("Workspace").GetComponent<UnityEngine.UI.Graphic>() && !Find("DockArea").GetComponent<UnityEngine.UI.Graphic>(),
            "Workspace blocks the world viewport."
        );
        var shield = root.transform.Find("ConflictShield");
        shield.gameObject.SetActive(true);
        host.KeepModalOnTop();
        Check(
            shield.GetSiblingIndex() == root.transform.childCount - 1 && shield.GetComponent<Image>().raycastTarget,
            "Conflict must block every window."
        );
        shield.gameObject.SetActive(false);
        UnityEngine.Object.DestroyImmediate(host);
        foreach (var drag in root.GetComponentsInChildren<EditorWindowDrag>(true))
            UnityEngine.Object.DestroyImmediate(drag);
        Debug.Log(
            "Raid editor: unique controls, contextual fields, capture workflow, selection, walkthrough, menus, floating windows, persistence, draft retention, reset, bounds and modal checks passed."
        );
    }

    private static Texture2D RenderPreviewFixture(Shader shader)
    {
        if (!shader || !shader.isSupported)
            throw new Exception("Bundled prop preview shader is unavailable.");
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var rig = new GameObject("Preview fixture camera");
        var material = new Material(shader);
        var depth = SceneHandleMath.LineMaterial(false);
        var overlay = SceneHandleMath.LineMaterial(true);
        var target = new RenderTexture(192, 192, 24);
        var image = new Texture2D(192, 192, TextureFormat.RGBA32, false);
        var previous = RenderTexture.active;
        try
        {
            cube.layer = 31;
            cube.transform.position = new Vector3(0, -10000, 0);
            cube.transform.localScale = new Vector3(1.6f, 1, 1);
            material.SetColor("_Color", new Color(.45f, .7f, .3f));
            cube.GetComponent<Renderer>().sharedMaterial = material;
            var camera = rig.AddComponent<Camera>();
            camera.enabled = false;
            camera.cullingMask = 1 << 31;
            camera.orthographic = true;
            camera.orthographicSize = 1.25f;
            camera.transform.position = cube.transform.position + new Vector3(3, 2, -3);
            camera.transform.LookAt(cube.transform.position);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.09f, .095f, .09f);
            camera.targetTexture = target;
            target.Create();
            void Read()
            {
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 192, 192), 0, 0);
                image.Apply();
            }
            Read();
            var pixels = image.GetPixels();
            var visible = Array.FindAll(pixels, c => c.g > c.r * 1.25f && c.g > .15f).Length;
            if (visible < 1000)
                throw new Exception("Prop preview shader produced a blank or unreadable image.");

            var lines = new GameObject("Occluded handle fixture");
            lines.transform.SetParent(rig.transform, false);
            lines.layer = 31;
            var line = lines.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            var behind = cube.transform.position + camera.transform.forward;
            line.SetPositions(new[] { behind - camera.transform.right * .25f, behind + camera.transform.right * .25f });
            line.startWidth = line.endWidth = .1f;
            line.startColor = line.endColor = Color.magenta;
            line.sharedMaterial = depth;
            Read();
            var hidden = image.GetPixel(96, 96);
            line.sharedMaterial = overlay;
            Read();
            var shown = image.GetPixel(96, 96);
            if (hidden.r > .8f || shown.r < .8f || shown.b < .8f || shown.g > .2f)
                throw new Exception("Overlay handle failed the occluded-object render check.");
            lines.SetActive(false);
            Read();
            Debug.Log(
                "Scene rendering: bundled prop shader has visible geometry; overlay handles render through an occluder while depth-tested lines stay hidden."
            );
            return image;
        }
        catch
        {
            UnityEngine.Object.DestroyImmediate(image);
            throw;
        }
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(cube);
            UnityEngine.Object.DestroyImmediate(rig);
            UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(depth);
            UnityEngine.Object.DestroyImmediate(overlay);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    private static void ValidateSceneWorkflow(
        GameObject root,
        RaidEditorWindows host,
        Func<string, Transform> find,
        Action<bool, string> check
    )
    {
        var events = new GameObject("Scene editor check events").AddComponent<EventSystem>();
        try
        {
            host.Present("Scene", "Copy", true, false, false, true, false, true);
            host.PresentScene(true, "Existing", "Copy", true, true, true, false);
            host.Select("Scene/Existing", "crate");
            var button = find("SceneMove").GetComponent<Button>();
            var pointer = new PointerEventData(events) { button = PointerEventData.InputButton.Left };
            button.OnPointerDown(pointer);
            var pressed = typeof(Selectable).GetMethod(
                "IsPressed",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
            );
            check((bool)pressed.Invoke(button, null), "Scene move did not receive pointer-down.");
            for (var i = 0; i < 20; i++)
            {
                host.Present("Scene", "Copy", true, false, false, true, false, true);
                host.PresentScene(true, "Existing", "Copy", true, true, true, false);
                host.Select("Scene/Existing", "crate");
                check((bool)pressed.Invoke(button, null), "Unchanged scene refresh interrupted a pressed button.");
            }
            var clicks = 0;
            UnityEngine.Events.UnityAction clicked = () => clicks++;
            button.onClick.AddListener(clicked);
            button.OnPointerUp(pointer);
            button.OnPointerClick(pointer);
            button.onClick.RemoveListener(clicked);
            check(clicks == 1, "Scene click must execute exactly once across refreshes.");
            var field = find("MapPositionX").GetComponent<InputField>();
            field.text = "123. unfinished";
            events.SetSelectedGameObject(field.gameObject);
            var scroll = find("PropertyScroll").GetComponent<ScrollRect>();
            scroll.verticalNormalizedPosition = .4f;
            for (var i = 0; i < 20; i++)
            {
                host.Present("Scene", "Copy", true, false, false, true, false, true);
                host.PresentScene(true, "Existing", "Copy", true, true, true, false);
                host.Select("Scene/Existing", "crate");
            }
            check(
                events.currentSelectedGameObject == field.gameObject && field.text == "123. unfinished",
                "Unchanged scene refresh lost field selection or unfinished text."
            );
            check(Mathf.Abs(scroll.verticalNormalizedPosition - .4f) < .001f, "Refresh reset inspector scroll.");
            events.SetSelectedGameObject(null);
            host.ShowPanel("Library", true);
            var row = find("Row0").gameObject.AddComponent<EditorRowSelection>();
            row.Identity = "original crate";
            row.OnPointerDown(pointer);
            row.Identity = "newly indexed barrel";
            row.OnPointerUp(pointer);
            check(row.Consume() == "original crate", "Pointer-down identity changed during indexing.");
            check(row.Consume() == "newly indexed barrel", "Keyboard selection retained a stale pointer identity.");
            UnityEngine.Object.DestroyImmediate(row);
            var label = find("Row0").GetComponentInChildren<Text>(true);
            UiElements.Stretch(label.rectTransform, 54, 8, 2, 2);
            Canvas.ForceUpdateCanvases();
            check(
                label.rectTransform.rect.width > 100 && label.rectTransform.rect.height >= 40,
                "Icon margins collapsed the library label."
            );

            var position = new Vector3(10, 2, -5);
            var rotation = Quaternion.Euler(20, 35, 10);
            var scale = new Vector3(2, 3, .5f);
            var localCenter = new Vector3(1, .5f, -2);
            var anchor = position + rotation * Vector3.Scale(scale, localCenter);
            var nextRotation = Quaternion.Euler(-5, 90, 40);
            var nextScale = new Vector3(4, 1, 2);
            var result = SceneHandleMath.PositionAroundAnchor(position, rotation, scale, nextRotation, nextScale, anchor);
            check(
                Vector3.Distance(result + nextRotation * Vector3.Scale(nextScale, localCenter), anchor) < .0001f,
                "Center rotation/scaling moved the visible anchor."
            );
            check(
                Vector3.Distance(
                    SceneHandleMath.PositionAroundAnchor(position, rotation, scale, nextRotation, nextScale, position),
                    position
                ) < .0001f,
                "Pivot mode changed the original origin."
            );
            var camera = events.gameObject.AddComponent<Camera>();
            camera.enabled = false;
            var near = SceneHandleMath.MetresPerPixel(camera, camera.transform.forward * 10);
            var far = SceneHandleMath.MetresPerPixel(camera, camera.transform.forward * 100);
            check(Mathf.Abs(far / near - 10) < .001f, "Handle screen size changes with distance.");
            host.PresentScene(true, "Existing", "Move", true, true, true, true, false);
            host.ShowPanel("Inspector", true);
            check(
                find("MapInspector").gameObject.activeInHierarchy
                    && find("MapPositionGroup").gameObject.activeInHierarchy
                    && find("MapRotationGroup").gameObject.activeInHierarchy
                    && find("MapSizeGroup").gameObject.activeInHierarchy,
                "An original selection must expose transform properties before its first saved edit."
            );
            field.readOnly = true;
            events.SetSelectedGameObject(field.gameObject);
            for (var i = 0; i < 20; i++)
                host.PresentScene(true, "Existing", "Move", true, true, false, true, false);
            check(
                field.readOnly && field.interactable && events.currentSelectedGameObject == field.gameObject,
                "Restricted objects must retain selectable read-only properties."
            );
            field.readOnly = false;
            events.SetSelectedGameObject(null);
            ValidateScenePickingAndTransforms(check, camera);
            Debug.Log(
                "Scene workflow: Maps/Scene world-click routing, original physics restoration, pointer/focus retention, row identity, selection before edits, read-only inspection, trigger-transparent picking, collider-free placements, rotation rings, parent scale and center/pivot math passed."
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(events.gameObject);
        }
    }

    private static void ValidateScenePickingAndTransforms(Action<bool, string> check, Camera camera)
    {
        var fixture = new GameObject("Scene selection regression fixture");
        fixture.transform.position = new Vector3(10000, 10000, 10000);
        try
        {
            var solid = GameObject.CreatePrimitive(PrimitiveType.Cube);
            solid.transform.SetParent(fixture.transform, false);
            solid.transform.localPosition = Vector3.forward * 10;
            var body = solid.AddComponent<Rigidbody>();
            body.useGravity = true;
            body.velocity = new Vector3(1, 2, 3);
            body.angularVelocity = new Vector3(0, 2, 0);
            var bodyState = new WTT.Campaigns.Client.Authoring.SceneBodyState(body);
            for (var i = 0; i < 20; i++)
                bodyState.Freeze();
            check(body.isKinematic && !body.useGravity, "Original physics prop did not stay frozen during editing.");
            solid.transform.position += Vector3.right * 3;
            bodyState.Restore();
            check(
                !body.isKinematic
                    && body.useGravity
                    && Vector3.Distance(body.velocity, new Vector3(1, 2, 3)) < .001f
                    && Vector3.Distance(body.angularVelocity, new Vector3(0, 2, 0)) < .001f,
                "Restoring an original prop lost native physics state."
            );
            solid.transform.localPosition = Vector3.forward * 10;
            UnityEngine.Object.DestroyImmediate(body);
            bodyState.Restore(); // Destroyed objects are harmless during map teardown.
            var trigger = new GameObject("Trigger volume");
            trigger.transform.SetParent(fixture.transform, false);
            trigger.transform.localPosition = Vector3.forward * 3;
            trigger.AddComponent<BoxCollider>().isTrigger = true;
            var ray = new Ray(fixture.transform.position, Vector3.forward);
            foreach (var mode in new[] { "Maps", "Scene" })
            {
                Transform picked = null;
                var calls = 0;
                check(
                    WTT.Campaigns.Client.Authoring.ScenePicking.Dispatch(
                        true,
                        mode,
                        () =>
                        {
                            calls++;
                            picked = WTT.Campaigns.Client.Authoring.ScenePicking.Pick(ray, Array.Empty<Renderer>());
                        }
                    )
                        && calls == 1
                        && picked == solid.transform,
                    mode + " world click must select the visible object without requiring a category switch or armed picker."
                );
            }
            foreach (var mode in new[] { "Maps", "Scene", "Zones", "Bindings", "Captures" })
            {
                var calls = 0;
                check(
                    !WTT.Campaigns.Client.Authoring.ScenePicking.Dispatch(false, mode, () => calls++) && calls == 0,
                    "Scene editing must not take over ordinary raid capture input: " + mode
                );
                if (mode != "Maps" && mode != "Scene")
                    check(
                        !WTT.Campaigns.Client.Authoring.ScenePicking.Dispatch(true, mode, () => calls++) && calls == 0,
                        "Scene picking must preserve the other authoring tools: " + mode
                    );
            }
            check(
                WTT.Campaigns.Client.Authoring.ScenePicking.Pick(ray, Array.Empty<Renderer>()) == solid.transform,
                "A trigger volume blocked selection of a visible prop."
            );
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "CampaignEditor prop";
            visual.transform.SetParent(fixture.transform, false);
            visual.transform.localPosition = Vector3.forward * 5;
            UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());
            var renderer = visual.GetComponent<MeshRenderer>();
            check(
                WTT.Campaigns.Client.Authoring.ScenePicking.Pick(ray, new[] { renderer }) == solid.transform,
                "An unowned editor helper was pickable."
            );
            check(
                WTT.Campaigns.Client.Authoring.ScenePicking.Pick(ray, new[] { renderer }, t => t == visual.transform) == visual.transform,
                "A placed prop without colliders could not be selected."
            );
            visual.name = "Independent renderer-only scenery";
            check(
                WTT.Campaigns.Client.Authoring.ScenePicking.Pick(ray, new[] { renderer }) == visual.transform,
                "Renderer-only scenery was not selectable for inspection."
            );
            renderer.enabled = false;
            check(
                WTT.Campaigns.Client.Authoring.ScenePicking.Pick(ray, new[] { renderer }) == solid.transform,
                "An invisible renderer stole a click."
            );
            renderer.enabled = true;
            visual.transform.localPosition = Vector3.forward * 15;
            check(
                WTT.Campaigns.Client.Authoring.ScenePicking.Pick(ray, new[] { renderer }) == solid.transform,
                "Renderer fallback selected scenery behind a nearer solid object."
            );
            var collision = new GameObject("Collision only child");
            collision.transform.SetParent(solid.transform, false);
            check(
                SceneSelectionGeometry.VisualRoot(collision.transform) == solid.transform,
                "Collision-only child did not resolve to its visible parent."
            );
            var lodRoot = new GameObject("LOD prop");
            lodRoot.transform.SetParent(fixture.transform, false);
            solid.transform.SetParent(lodRoot.transform, true);
            lodRoot.AddComponent<LODGroup>().SetLODs(new[] { new LOD(.01f, new[] { solid.GetComponent<Renderer>() }) });
            check(
                SceneSelectionGeometry.VisualRoot(solid.transform) == lodRoot.transform,
                "Clicking a LOD mesh selected only one detail level."
            );
            lodRoot.transform.localScale = new Vector3(2, 3, 4);
            solid.transform.localScale = new Vector3(.5f, .75f, 1.25f);
            solid.transform.localRotation = Quaternion.Euler(20, 30, 10);
            var selectedCenter = solid.transform.TransformPoint(new Vector3(1, .5f, -2));
            var localCenter = solid.transform.InverseTransformPoint(selectedCenter);
            var nextRotation = Quaternion.Euler(15, 80, 45);
            var nextScale = new Vector3(4, 2, 3);
            solid.transform.rotation = nextRotation;
            SceneSelectionGeometry.WorldScale(solid.transform, nextScale);
            solid.transform.position = SceneSelectionGeometry.PositionForAnchor(solid.transform, localCenter, selectedCenter);
            check(
                Vector3.Distance(solid.transform.TransformPoint(localCenter), selectedCenter) < .005f,
                "Center rotation and resize drifted under a nonuniformly scaled parent: "
                    + (solid.transform.TransformPoint(localCenter) - selectedCenter).ToString("F6")
                    + "; scale "
                    + solid.transform.lossyScale.ToString("F6")
            );
            var localBefore = solid.transform.localScale;
            var worldBefore = solid.transform.lossyScale;
            var desired = new Vector3(4, 5, 6);
            SceneSelectionGeometry.WorldScale(solid.transform, desired);
            check(Vector3.Distance(solid.transform.lossyScale, desired) < .0001f, "Original prop resize ignored parent scaling.");
            solid.transform.localScale = localBefore;
            check(
                Vector3.Distance(solid.transform.lossyScale, worldBefore) < .0001f,
                "Cancelling resize did not restore parent-relative scale."
            );
            check(
                Mathf.Abs(SceneSelectionGeometry.Resize(2.34f, 0, true) - 2.34f) < .0001f,
                "Clicking a resize handle without dragging changed the object's size."
            );
            var before = Quaternion.Euler(20, 30, 40);
            var turned = SceneSelectionGeometry.Rotation(before, 1, 90);
            check(
                Vector3.Distance(turned * Vector3.forward, Quaternion.AngleAxis(90, Vector3.up) * (before * Vector3.forward)) < .0001f,
                "Rotation ring did not rotate around its displayed world axis."
            );
            check(
                Mathf.Abs(SceneSelectionGeometry.Resize(2, 90, false) - 4) < .0001f
                    && Mathf.Abs(SceneSelectionGeometry.Resize(2, -90, false) - 1) < .0001f,
                "Resize must respond equally to screen movement on tiny and large objects."
            );
            var ring = new Vector3[65];
            for (var i = 0; i < ring.Length; i++)
            {
                var angle = i / 64f * Mathf.PI * 2;
                ring[i] =
                    camera.transform.position
                    + camera.transform.forward * 10
                    + camera.transform.right * Mathf.Cos(angle)
                    + camera.transform.up * Mathf.Sin(angle);
            }
            var mouse = (Vector2)camera.WorldToScreenPoint(ring[8]);
            check(
                SceneHandleMath.HitPath(camera, ring, mouse, out var tangent) < .01f && tangent.sqrMagnitude > .99f,
                "Displayed rotation ring did not match its screen hit target."
            );
            check(
                SceneHandleMath.HitPath(
                    camera,
                    ring,
                    camera.WorldToScreenPoint(camera.transform.position + camera.transform.forward * 10),
                    out _
                ) > 12,
                "Rotation ring captured clicks far from the displayed handle."
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fixture);
        }
    }

    [MenuItem("SDK/WTT-Campaigns/Preview raid editor")]
    public static void Preview()
    {
        var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../SeasonalPerks/Research/UI/RaidEditor"));
        Directory.CreateDirectory(output);
        var propPreview = RenderPreviewFixture(AssetDatabase.LoadAssetAtPath<Shader>(Root + "/RaidEditor/CampaignScenePreview.shader"));
        foreach (
            var size in new[]
            {
                new Vector2Int(1920, 1080),
                new Vector2Int(2560, 1440),
                new Vector2Int(3440, 1440),
                new Vector2Int(1280, 1024),
                new Vector2Int(1280, 720),
            }
        )
        {
            var root = Create();
            var cameraObject = new GameObject("Raid editor preview camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.17f, .18f, .15f);
            var target = new RenderTexture(size.x, size.y, 24);
            camera.targetTexture = target;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1;
            Canvas.ForceUpdateCanvases();
            var host = root.AddComponent<RaidEditorWindows>();
            host.Initialize();
            var controls = root.GetComponentsInChildren<Transform>(true);
            Transform Find(string name) => Array.Find(controls, t => t.name == name);
            void Text(string name, string value) => Find(name).GetComponent<Text>().text = value;
            void Input(string name, string value) => Find(name).GetComponent<InputField>().text = value;
            foreach (
                var state in new[]
                {
                    "browser",
                    "zones",
                    "sphere",
                    "events",
                    "captures",
                    "scene",
                    "scene-catalog",
                    "scene-empty",
                    "scene-loading",
                    "scene-removed",
                    "scene-failed",
                    "scene-popout",
                    "scene-edit",
                    "scene-picked",
                    "scene-readonly",
                    "maps",
                    "map-door",
                    "popout",
                    "capture-request",
                    "conflict",
                    "home",
                    "walkthrough",
                    "environment",
                    "help",
                    "resized",
                }
            )
            {
                host.SetWalkthrough(false);
                Find("Workspace").gameObject.SetActive(true);
                Find("EditorHome").gameObject.SetActive(false);
                Find("ConflictShield").gameObject.SetActive(false);
                var mode =
                    state == "events" ? "Bindings"
                    : state == "captures" || state == "capture-request" ? "Captures"
                    : state.StartsWith("scene") ? "Scene"
                    : state.StartsWith("map") ? "Maps"
                    : "Zones";
                var kind =
                    state == "sphere" ? "Sphere"
                    : state == "maps" ? "Checkpoint"
                    : state == "map-door" ? "Door"
                    : "Box";
                var selected = state != "browser";
                host.Present(
                    mode,
                    kind,
                    selected,
                    state == "capture-request",
                    mode == "Scene" || mode == "Maps",
                    true,
                    false,
                    state.StartsWith("scene-")
                );
                host.Select(mode, selected ? "preview-record" : "");
                host.ResetLayout();
                var picked = state == "scene-picked" || state == "scene-readonly";
                host.PresentScene(
                    state.StartsWith("scene-"),
                    state == "scene-removed" ? "Changes"
                        : state == "scene-edit" || picked ? "Existing"
                        : "Catalog",
                    state == "scene-removed" ? "Hide"
                        : picked ? "Move"
                        : state == "scene-edit" ? "Copy"
                        : "Loot",
                    state != "scene-empty",
                    state == "scene-removed" || state == "scene-edit" || picked,
                    state != "scene-readonly",
                    picked,
                    !picked
                );
                if (state.StartsWith("scene-"))
                {
                    var ready = state == "scene-catalog" || state == "scene-popout";
                    var selectedPreview = Find("ScenePreview").GetComponent<RawImage>();
                    selectedPreview.texture = ready ? propPreview : null;
                    selectedPreview.color = ready ? Color.white : Color.clear;
                    selectedPreview.rectTransform.sizeDelta = new Vector2(180, 180);
                    Find("ScenePreviewStatus").gameObject.SetActive(!ready);
                    Text("ScenePreviewStatus", state == "scene-failed" ? "Preview unavailable" : "Loading preview…");
                    Find("ScenePreviewRetryGroup").gameObject.SetActive(state == "scene-failed");
                    Text(
                        "SceneHeading",
                        state == "scene-removed" ? "Weapon box — removed"
                            : ready || state == "scene-edit" ? "Storage crate — preview fixture"
                            : "Kalashnikov AK-74M 5.45x39 assault rifle with long installed preset name"
                    );
                    Text(
                        "SceneInfo",
                        state == "scene-loading" ? "Loading installed item models…"
                            : state == "scene-failed" ? "Missing item model. Install the required content and reload the map."
                            : state == "scene-removed" ? "Removed from this layout. Restore original returns the object."
                            : "Place on a surface, then refine with the transform handles. Escape cancels."
                    );
                    host.ShowPanel("Library", true);
                    if (size.x >= 1600)
                        host.ShowPanel("Inspector", true);
                }
                Text("Connection", "Operation Northwind / Customs / Warehouse approach");
                Text("Status", "Connected · All changes saved");
                Text("Request", "RMB fly · Ctrl+F8 close · EDITOR / Gameplay disabled");
                Text("LibraryCount", "4 records · Page 1 / 1");
                Text("Identity", mode == "Scene" ? "warehouse_loading_door" : "customs_warehouse_approach");
                Text(
                    "Details",
                    mode == "Scene"
                        ? "Customs / Warehouse / LoadingDoor\nInteractable door · Scene target resolved"
                        : "Warehouse approach\nDraft preview · No gameplay changes"
                );
                Text("MapDetails", "Picked: warehouse_loading_door\nAll map edits belong to Warehouse approach.");
                Text("CaptureRequest", "Transform capture · Position the camera, then capture and complete.");
                Input("Name", "Warehouse approach");
                Input("MapName", state == "map-door" ? "Loading door" : "Warehouse checkpoint");
                foreach (var prefix in new[] { "", "Map" })
                foreach (var group in new[] { "Position", "Rotation", "Size" })
                foreach (var axis in "XYZ")
                    Input(
                        prefix + group + axis,
                        group == "Position"
                                ? (
                                    axis == 'X' ? "124.65"
                                    : axis == 'Y' ? "2.4"
                                    : "-86.12"
                                )
                            : group == "Rotation" ? "0"
                            : "4"
                    );
                Input("Radius", "3.5");
                for (var i = 0; i < 10; i++)
                {
                    Find("Row" + i).gameObject.SetActive(i < 4);
                    if (i < 4)
                        Find("Row" + i).GetComponentInChildren<Text>().text = new[]
                        {
                            "Warehouse approach",
                            "Loading door",
                            "Courtyard checkpoint",
                            "Extraction boundary",
                        }[i];
                    Find("Row" + i).GetComponent<Button>().targetGraphic.color =
                        i == 0 && selected ? new Color(.36f, .33f, .23f) : new Color(.18f, .18f, .15f);
                }
                if (state.StartsWith("scene-"))
                {
                    Text(
                        "LibraryCount",
                        state == "scene-empty" ? "No matching objects"
                            : state == "scene-loading" ? "Loading catalog…"
                            : "4 objects · 1 / 1"
                    );
                    for (var i = 0; i < 10; i++)
                    {
                        Find("Row" + i).gameObject.SetActive(state != "scene-empty" && i < 4);
                        if (i < 4)
                            Find("Row" + i).GetComponentInChildren<Text>().text = new[]
                            {
                                "AK-74M 5.45x39 assault rifle — installed preset",
                                "Salewa first aid kit",
                                "Corrugated warehouse storage crate",
                                "Weapon repair kit",
                            }[i];
                        var icon = Find("SceneIcon" + i).GetComponent<RawImage>();
                        var catalogRow = state != "scene-removed" && state != "scene-edit" && !picked;
                        var ready = i == 0 && (state == "scene-catalog" || state == "scene-popout");
                        icon.gameObject.SetActive(i < 4 && catalogRow);
                        icon.texture = ready ? propPreview : null;
                        icon.color = ready ? Color.white : Color.clear;
                        if (ready)
                            Find("Row0").GetComponentInChildren<Text>().text = "Storage crate — preview fixture";
                        Find("SceneIconStatus" + i).gameObject.SetActive(i < 4 && catalogRow && !ready);
                        Text("SceneIconStatus" + i, state == "scene-failed" ? "N/A" : "…");
                        UiElements.Stretch(Find("Row" + i).GetComponentInChildren<Text>(true).rectTransform, catalogRow ? 54 : 8, 8, 2, 2);
                    }
                    if (state == "scene-popout")
                        host.ToggleDock("Library");
                    if (picked)
                    {
                        Text(
                            "SceneHeading",
                            state == "scene-readonly" ? "Warehouse structural wall" : "Corrugated warehouse storage crate"
                        );
                        Text(
                            "SceneInfo",
                            state == "scene-readonly"
                                ? "Selected for inspection. Combined static geometry cannot be moved safely. Choose an independent prop."
                                : "Drag a handle or edit a property. Changes are saved only when you edit; Esc cancels a drag."
                        );
                        Text("MapDetails", "Customs:/Warehouse/Storage/crate\nTransform, MeshFilter, MeshRenderer, BoxCollider, LODGroup");
                        Input("MapName", state == "scene-readonly" ? "Warehouse structural wall" : "Corrugated warehouse storage crate");
                        foreach (
                            var action in new[] { "Move", "Rotate", "Scale", "SceneMove", "SceneRotate", "SceneRemove", "MapAtPlayer" }
                        )
                            Find(action).GetComponent<Button>().interactable = state != "scene-readonly";
                        foreach (var group in new[] { "Position", "Rotation", "Size" })
                        foreach (var axis in "XYZ")
                            Find("Map" + group + axis).GetComponent<InputField>().readOnly = state == "scene-readonly";
                        Find("SceneRestore").GetComponent<Button>().interactable = false;
                        Find("SceneRebind").GetComponent<Button>().interactable = false;
                        host.ShowPanel("Inspector", true);
                        if (state == "scene-readonly")
                            host.ToggleDock("Inspector");
                    }
                }
                foreach (var category in new[] { "Maps", "Zones", "Bindings", "Captures", "Scene" })
                    Find(category).GetComponent<Button>().targetGraphic.color =
                        category == mode ? new Color(.36f, .33f, .23f) : new Color(.18f, .18f, .15f);
                if (state == "popout")
                    host.ToggleDock("Inspector");
                if (state == "conflict")
                {
                    Find("ConflictShield").gameObject.SetActive(true);
                    host.KeepModalOnTop();
                }
                if (state == "home")
                {
                    Find("Workspace").gameObject.SetActive(false);
                    host.ShowPanel("Inspector", false);
                    host.ShowPanel("Library", false);
                    Find("EditorHome").gameObject.SetActive(true);
                }
                if (state == "walkthrough")
                    host.SetWalkthrough(true);
                if (state == "help")
                    Find("HelpToggle").GetComponent<Button>().onClick.Invoke();
                if (state == "resized")
                {
                    var layout = host.CaptureLayout();
                    foreach (var entry in layout.Windows)
                        if (entry.Id == "Library" || entry.Id == "Inspector")
                        {
                            entry.Width = entry.Id == "Library" ? 500 : 460;
                            entry.Height = 440;
                            entry.X = entry.Id == "Library" ? -.26f : .26f;
                            entry.Y = 0;
                        }
                    host.RestoreLayout(layout);
                    Find("Row0").GetComponentInChildren<Text>().text = "Warehouse storage crate beside the maintenance checkpoint";
                }
                if (state == "environment")
                {
                    Find("EnvironmentToggle").GetComponent<Button>().onClick.Invoke();
                    if (Find("EnvironmentMenuScroll").GetComponent<ScrollRect>().verticalNormalizedPosition < .99f)
                        throw new Exception("Environment must initially show its time-of-day controls.");
                    Input("EnvironmentHour", "18:30");
                    Text("EnvironmentStatus", "Preview time held. Closing restores raid time.");
                    foreach (var field in new[] { "Clouds", "Rain", "Fog", "Wind", "Thunder" })
                        Input("Weather" + field, "50");
                    Text("WeatherStatus", "Weather preview active. Closing restores raid weather.");
                    Canvas.ForceUpdateCanvases();
                    var menu = (RectTransform)Find("EnvironmentMenu");
                    var corners = new Vector3[4];
                    menu.GetWorldCorners(corners);
                    foreach (var corner in corners)
                        if (!((RectTransform)root.transform).rect.Contains(root.transform.InverseTransformPoint(corner) * .999f))
                            throw new Exception(size + " environment menu exceeds the screen.");
                    foreach (var text in menu.GetComponentsInChildren<Text>())
                        if (text.preferredHeight > text.rectTransform.rect.height + 1)
                            throw new Exception(size + " environment text clipped: " + text.name);
                }
                host.RefreshBounds();
                Canvas.ForceUpdateCanvases();
                if (state != "home" && state != "walkthrough")
                {
                    var rootRect = (RectTransform)root.transform;
                    foreach (var id in new[] { "WorkspaceTitleBar", "TransformToolbar", "StatusBar", "ActionBar" })
                    {
                        var bar = (RectTransform)Find(id);
                        var barCorners = new Vector3[4];
                        bar.GetWorldCorners(barCorners);
                        var left = rootRect.InverseTransformPoint(barCorners[0]).x;
                        var right = rootRect.InverseTransformPoint(barCorners[2]).x;
                        if (Mathf.Abs(left - rootRect.rect.xMin) > .1f || Mathf.Abs(right - rootRect.rect.xMax) > .1f)
                            throw new Exception(size + " bar does not span the viewport: " + id);
                        var frame = bar.Find("ToolFrame");
                        if (!frame)
                            continue;
                        foreach (RectTransform edge in frame)
                        {
                            edge.GetWorldCorners(barCorners);
                            foreach (var corner in barCorners)
                            {
                                var point = bar.InverseTransformPoint(corner);
                                if (
                                    point.x < bar.rect.xMin - .1f
                                    || point.x > bar.rect.xMax + .1f
                                    || point.y < bar.rect.yMin - .1f
                                    || point.y > bar.rect.yMax + .1f
                                )
                                    throw new Exception(size + " border extends beyond bar: " + id);
                            }
                        }
                    }
                    if (Find("WorkspaceTitleBar").GetComponent<Image>().sprite)
                        throw new Exception("Top bar retained the decorative footer outline.");
                    foreach (var id in RaidEditorLayout.Modules)
                    {
                        var panel = Find(id).GetComponent<RectTransform>();
                        if (!panel.gameObject.activeInHierarchy)
                            continue;
                        var corners = new Vector3[4];
                        panel.GetWorldCorners(corners);
                        foreach (var corner in corners)
                            if (!rootRect.rect.Contains(rootRect.InverseTransformPoint(corner) * .999f))
                                throw new Exception(size + " " + state + " panel out of bounds: " + id);
                    }
                    foreach (var button in root.GetComponentsInChildren<Button>())
                    {
                        if (button.GetComponentInParent<ScrollRect>())
                            continue;
                        var label = button.GetComponentInChildren<Text>();
                        if (label && label.preferredHeight > label.rectTransform.rect.height + 1)
                            throw new Exception(size + " " + state + " clipped button label: " + button.name);
                    }
                }
                camera.Render();
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                var texture = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
                texture.Apply();
                File.WriteAllBytes(Path.Combine(output, size.x + "x" + size.y + "-" + state + ".png"), texture.EncodeToPNG());
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(texture);
            }
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(target);
        }
        Debug.Log("Campaign raid editor previews: " + output);
        UnityEngine.Object.DestroyImmediate(propPreview);
    }
}
