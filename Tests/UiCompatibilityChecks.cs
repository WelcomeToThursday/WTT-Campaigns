using Mono.Cecil;

namespace WTT.Campaigns.Tests;

internal static class UiCompatibilityChecks
{
    internal static void Run(string path, string? clientPath = null)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(path);
        PreviewNativeChecks.Run(assembly.MainModule);
        var types = assembly.MainModule.GetTypes().ToDictionary(type => type.FullName);
        var count = 0;
        void Check(bool value, string description)
        {
            count += value ? 1 : throw new InvalidOperationException(description);
        }
        var raidLoadingScreen = types["EFT.UI.Matchmaker.MatchmakerTimeHasCome"];
        Check(
            raidLoadingScreen.Methods.Any(m =>
                m.Name == "ChangeStatus"
                && m.Parameters.Count == 2
                && m.Parameters[0].Name == "status"
                && m.Parameters[0].ParameterType.FullName == "System.String"
                && m.Parameters[1].Name == "progress"
                && m.Parameters[1].ParameterType.FullName == "System.Nullable`1<System.Single>"
            ) && raidLoadingScreen.Methods.Any(m => m.Name == "OnDestroy" && m.Parameters.Count == 0),
            "Campaign loading captions preserve the native status, progress and screen destruction contracts"
        );
        Check(
            types["EFT.ObjectsFactory"]
                .Methods.Any(m =>
                    m.Name == "CreateItemAsync"
                    && m.IsPublic
                    && m.Parameters.Count == 6
                    && m.ReturnType.FullName == "System.Threading.Tasks.Task`1<UnityEngine.GameObject>"
                ),
            "Scene models use the native asynchronous item factory"
        );
        Check(
            types["EFT.ObjectsFactory"]
                .Methods.Any(m =>
                    m.Name == "LoadBundlesAndCreatePools"
                    && m.IsPublic
                    && m.Parameters.Count == 6
                    && m.Parameters[5].ParameterType.FullName == "System.Threading.CancellationToken"
                ),
            "Scene asset loading supports cancellation"
        );
        Check(
            types["EFT.AssetsManager.AssetPoolObject"]
                .Methods.Any(m =>
                    m.Name == "ReturnToPool"
                    && m.IsPublic
                    && m.IsStatic
                    && m.Parameters.Count == 2
                    && m.Parameters[0].ParameterType.FullName == "UnityEngine.GameObject"
                ),
            "Scene cleanup returns native models to their pool"
        );
        foreach (var name in new[] { "StaticId", "TemplateId" })
            Check(
                types["EFT.Interactive.LootItem"].Fields.Any(f => f.Name == name && f.IsPublic && f.FieldType.FullName == "System.String"),
                "Loose loot binding uses native " + name
            );
        Check(
            types["EFT.Interactive.LootableContainer"]
                .Methods.Any(m => m.Name == "get_Id" && m.IsPublic && m.ReturnType.FullName == "System.String"),
            "Container binding uses native identity"
        );
        foreach (var name in new[] { "RegisterInCullingObject", "UnregisterFromCullingObject" })
            Check(
                types["EFT.Interactive.LootItem"].Methods.Any(m => m.Name == name && m.IsPublic && m.Parameters.Count == 0),
                "Loot edits preserve native culling registration: " + name
            );
        foreach (var name in new[] { "_buyTab", "_sellTab" })
        {
            Check(
                types["EFT.UI.TraderDealScreen"].Fields.Any(f => f.Name == name && f.IsPublic && f.FieldType.Name == "Tab"),
                "Visit shares the native trading tab row: " + name
            );
        }
        Check(
            types["EFT.UI.TraderScreensGroup"].Fields.Any(f => f.Name == "_traderDealScreen" && f.IsPublic),
            "Visit can return to the active native trading screen"
        );
        Check(
            types["Tab"].Methods.Any(m => m.Name == "OnPointerClick" && m.IsPublic && m.Parameters.Count == 1),
            "Visit returns through native tab selection so mode and highlights stay synchronized"
        );
        var appearance = types["EFT.UI.HeadSelectionState"];
        Check(
            types["EFT.UI.InventoryScreen"]
                .Methods.Any(m =>
                    m.Name == "Show" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.Name == "InventoryScreenController"
                ),
            "Customization binds the controller Show overload before native tab registration"
        );
        Check(
            types["EFT.UI.InventoryScreen/InventoryScreenController"]
                .Methods.Any(m =>
                    m.Name == "CloseScreenInterruption" && m.ReturnType.FullName == "System.Threading.Tasks.Task`1<System.Boolean>"
                ),
            "Customization can flush saves before navigation closes the screen"
        );
        foreach (var method in new[] { "UpdatePreview", "PlayVoice" })
        {
            Check(
                appearance.Methods.Count(m => m.Name == method && m.ReturnType.FullName == "System.Threading.Tasks.Task") == 1,
                "Customization tracks the native asynchronous " + method + " operation"
            );
        }
        Check(
            types["Arena.UI.FaceCardView"].Fields.Any(f => f.Name == "_toggle" && f.IsPublic),
            "Failed appearance saves can restore the native card highlight"
        );
        Check(
            appearance.Fields.Any(field => field.Name == "_faceCards" && field.IsPublic && field.IsNotSerialized),
            "Native appearance card tracking is runtime-only and is not inherited by screen clones"
        );
        foreach (var fieldName in new[] { "_faceCardsViewPort", "_faceCardPrefab" })
        {
            Check(
                appearance.Fields.Any(field => field.Name == fieldName && field.IsPublic && !field.IsNotSerialized),
                "Native appearance clone retains its serialized " + fieldName
            );
        }
        Check(
            appearance
                .Methods.Single(method => method.Name == "PrepareFaceSelector")
                .Body.Instructions.Any(instruction => instruction.Operand is MethodReference method && method.Name == "Instantiate"),
            "Native appearance builds fresh faction head cards from its prefab"
        );
        foreach (var name in new[] { "EFT.UI.MenuScreen", "EFT.UI.SkillsAndMasteringScreen" })
        {
            var method = types[name]
                .Methods.Single(value =>
                    value.Name == "Show" && value.Parameters.FirstOrDefault()?.ParameterType.FullName == "EFT.Profile"
                );
            Check(
                method.Parameters[0].Name == "profile" && method.Parameters[0].ParameterType.FullName == "EFT.Profile",
                name + " Show profile binding"
            );
        }
        Check(
            types["EFT.UI.PreloaderUI"]
                .Fields.Any(field => field.Name == "_loader" && field.FieldType.FullName == "UnityEngine.GameObject" && field.IsPublic),
            "Native loading indicator is available to the campaign creation overlay"
        );
        Check(
            types["EFT.UI.PreloaderUI"]
                .Fields.Any(field =>
                    field.Name == "_pveLoadingScreen"
                    && field.FieldType.FullName == "EFT.Hideout.PveGameModeLoadingScreen"
                    && field.IsPublic
                ),
            "Native full-screen loading artwork is available before character switching"
        );
        var loadingScreen = types["EFT.Hideout.PveGameModeLoadingScreen"];
        foreach (var field in new[] { "_logoGroup", "_screenAnimator" })
        {
            Check(loadingScreen.Fields.Any(value => value.Name == field && value.IsPublic), "Immediate loading screen binding: " + field);
        }
        Check(
            loadingScreen.Methods.Any(method => method.HasBody && method.Body.Instructions.Any(i => Equals(i.Operand, "LoadingState"))),
            "Native loading animation state is available"
        );
        foreach (var field in new[] { "_masteringTab", "_skillsScreen", "_skillMasterTabGroup" })
        {
            Check(types["EFT.UI.SkillsAndMasteringScreen"].Fields.Any(value => value.Name == field), "Native skills field " + field);
        }
        var skillsScreen = types["EFT.UI.SkillsAndMasteringScreen"];
        Check(
            skillsScreen
                .Methods.Single(method => method.Name == "Awake")
                .Body.Instructions.Any(instruction =>
                    instruction.OpCode == Mono.Cecil.Cil.OpCodes.Stfld
                    && instruction.Operand is FieldReference field
                    && field.Name == "_skillMasterTabGroup"
                ),
            "Native Awake initializes the skills tab group"
        );
        var showInstructions = skillsScreen.Methods.Single(method => method.Name == "Show").Body.Instructions;
        var activation = showInstructions.First(instruction =>
            instruction.Operand is MethodReference method && method.Name == "ShowGameObject"
        );
        var groupAccess = showInstructions.First(instruction =>
            instruction.Operand is FieldReference field && field.Name == "_skillMasterTabGroup"
        );
        Check(activation.Offset < groupAccess.Offset, "Native Show activates the screen before accessing its tab group");
        if (clientPath != null)
        {
            using var client = AssemblyDefinition.ReadAssembly(clientPath);
            PreviewNativeChecks.CheckEscape(assembly.MainModule, client.MainModule);
            EditorOpenChecks.Client(client, Check);
            AiControlsChecks.Run(client, Check);
            var editor = client.MainModule.GetType("WTT.Campaigns.Client.Authoring.RaidEditor");
            var geometry = editor.Methods.Single(m => m.Name == "GeometryInput");
            Check(
                geometry.Body.Instructions.Count(i =>
                    i.Operand is MethodReference m && m.DeclaringType.Name == "ScenePicking" && m.Name == "Dispatch"
                ) == 2,
                "Ordinary clicks and armed picking both use Maps/Scene selection routing"
            );
            var pick = editor.Methods.Single(m => m.Name == "PickScene");
            var calls = pick
                .Body.Instructions.Where(i => i.Operand is MethodReference)
                .Select(i => ((MethodReference)i.Operand).Name)
                .ToList();
            Check(
                calls.IndexOf("EnterSceneSelection") >= 0 && calls.IndexOf("EnterSceneSelection") < calls.IndexOf("SelectSceneTarget"),
                "A Maps world hit enters Scene before resolving its selection and inspector"
            );
            var row = editor.Methods.Single(m => m.Name == "SelectRow");
            Check(
                row.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "EnterSceneSelection"),
                "Selecting a prop in the Maps library opens the unified object inspector"
            );

            foreach (
                var (type, method, arguments) in new[]
                {
                    ("HotObject", "SyncPosition", 0),
                    ("StencilShadow", "set_Bounds", 1),
                    ("StaticDeferredDecalRenderer", "RegisterDecal", 2),
                    ("StaticDeferredDecalRenderer", "UnregisterDecal", 2),
                }
            )
                Check(
                    types[type].Methods.Any(m => m.Name == method && m.IsPublic && m.Parameters.Count == arguments),
                    "Original-prop movement uses the installed native render adapter: " + type + "." + method
                );
            var sceneAdapter = client.MainModule.GetType("WTT.Campaigns.Client.Authoring.Scenes.MapSceneAdapter");
            var resolveCalls = sceneAdapter
                .Methods.Single(m => m.Name == "Resolve")
                .Body.Instructions.Select(i => i.Operand)
                .OfType<MethodReference>()
                .Select(m => m.Name)
                .ToList();
            Check(
                resolveCalls.Contains("FindTargetPath")
                    && resolveCalls.Contains("Capture")
                    && !resolveCalls.Contains("FindObjectsOfTypeAll"),
                "Scene binding follows the saved hierarchy without a per-prop global scan and retains fingerprint validation"
            );
            var previewModelCalls = AsyncBody("WTT.Campaigns.Client.Authoring.Scenes.SceneLootModel", "Create")
                .Body.Instructions.Select(i => i.Operand)
                .OfType<MethodReference>()
                .Select(m => m.Name)
                .ToList();
            foreach (var pose in new[] { "set_localPosition", "set_localRotation", "set_localScale" })
                Check(
                    previewModelCalls.IndexOf(pose) > previewModelCalls.IndexOf("SetParent")
                        && previewModelCalls.IndexOf(pose) < previewModelCalls.IndexOf("get_bounds"),
                    "Loot preview clears the pooled pose before measuring placement bounds: " + pose
                );
            var equipCalls = AsyncBody("WTT.Campaigns.Client.Authoring.Preview.EditorPreviewPlayer", "Equip")
                .Body.Instructions.Select(i => i.Operand)
                .OfType<MethodReference>()
                .Select(m => m.Name)
                .ToList();
            Check(
                equipCalls.IndexOf("UncoverContent") > equipCalls.LastIndexOf("Replace"),
                "Fresh preview gear receives native search knowledge after entering equipped slots"
            );
            var restoreGearCalls = AsyncBody("WTT.Campaigns.Client.Authoring.Preview.EditorPreviewPlayer", "Restore")
                .Body.Instructions.Select(i => i.Operand)
                .OfType<MethodReference>()
                .Select(m => m.Name)
                .ToList();
            Check(
                restoreGearCalls.IndexOf("StopSearching") >= 0
                    && restoreGearCalls.IndexOf("StopSearching") < restoreGearCalls.IndexOf("Replace")
                    && restoreGearCalls.Contains("ForgetItem"),
                "Preview reset stops active searches before removing their items and retires temporary search knowledge"
            );
            var preparePreview = AsyncBody(editor.FullName, "BeginAiPreview")
                .Body.Instructions.Select(i => i.Operand)
                .OfType<MethodReference>()
                .Select(m => m.Name)
                .ToList();
            Check(
                preparePreview.IndexOf("ApplyAsync") >= 0
                    && preparePreview.IndexOf("ApplyAsync") < preparePreview.IndexOf("Equip")
                    && preparePreview.IndexOf("Apply") > preparePreview.IndexOf("Equip"),
                "Mission and AI preview wait for scene preparation before equipping the player"
            );
            var previewLootCalls = AsyncBody(editor.FullName, "BeginAiPreview")
                .Body.Instructions.Select(i => i.Operand)
                .OfType<MethodReference>()
                .ToList();
            Check(
                previewLootCalls.Any(m =>
                    m.DeclaringType.FullName == "WTT.Campaigns.Client.Missions.MissionLoot" && m.Name == "ApplyAsync"
                ),
                "Playable AI previews create native collectable loot after preparing the scene"
            );
            var missionLootCalls = AsyncBody("WTT.Campaigns.Client.Missions.MissionLoot", "ApplyAsync")
                .Body.Instructions.Select(i => i.Operand)
                .OfType<MethodReference>()
                .ToList();
            var lootOwner = missionLootCalls.FindIndex(m =>
                m.Name == ".ctor" && m.DeclaringType.FullName == "EFT.InventoryLogic.ItemController"
            );
            Check(
                lootOwner >= 0
                    && lootOwner < missionLootCalls.FindIndex(m => m.Name == "CreateLootPrefab")
                    && lootOwner < missionLootCalls.FindIndex(m => m.Name == "CreateStaticLoot"),
                "Mission loot receives a native root owner before prefab creation and world registration"
            );
            var prepareScene = AsyncBody(sceneAdapter.FullName, "ApplyAsync")
                .Body.Instructions.Select(i => i.Operand)
                .OfType<MethodReference>()
                .Select(m => m.Name)
                .ToList();
            Check(
                prepareScene.IndexOf("Reconcile") >= 0
                    && prepareScene.IndexOf("Reconcile") < prepareScene.IndexOf("get_Loading")
                    && prepareScene.IndexOf("Delay") < prepareScene.IndexOf("Apply")
                    && prepareScene.Count(m => m == "ThrowIfCancellationRequested") >= 2
                    && prepareScene.Contains("GetResult"),
                "Scene preparation starts model loads, awaits readiness with cancellation, and then validates the applied layout"
            );
            var scaleRestriction = sceneAdapter.Methods.Single(m => m.Name == "ScaleRestriction").Body.Instructions;
            Check(
                scaleRestriction.Any(i =>
                    i.Operand is GenericInstanceMethod m
                    && m.Name == "GetComponentsInChildren"
                    && m.GenericArguments.Single().FullName == "UnityEngine.MeshCollider"
                    && i.Previous.OpCode == Mono.Cecil.Cil.OpCodes.Ldc_I4_1
                ),
                "Resize checks every child collision mesh, including disabled children"
            );
            Check(
                scaleRestriction.Any(i =>
                    i.Operand is MethodReference m && m.DeclaringType.FullName == "UnityEngine.Mesh" && m.Name == "get_isReadable"
                ),
                "Resize eligibility uses native collision mesh readability"
            );
            Check(
                editor
                    .Methods.Single(m => m.Name == "CanTransformScene")
                    .Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "ScaleRestriction"),
                "Resize handles and numeric properties share the collision restriction"
            );
            var reconcileCalls = sceneAdapter
                .Methods.Single(m => m.Name == "Reconcile")
                .Body.Instructions.Where(i => i.Operand is MethodReference)
                .Select(i => ((MethodReference)i.Operand).Name)
                .ToList();
            var guard = reconcileCalls.IndexOf("ScaleRestriction");
            foreach (var mutation in new[] { "CopyProp", "SetActive", "Pose", "WorldScale" })
                Check(
                    guard >= 0 && guard < reconcileCalls.IndexOf(mutation),
                    "Saved or remote resize is checked before scene mutation: " + mutation
                );
            var original = sceneAdapter.NestedTypes.Single(t => t.Name == "Original");
            var visualUpdates = original.Methods.Single(m => m.Name == "RefreshVisuals").Body.Instructions;
            foreach (var name in new[] { "SyncPosition", "set_Bounds", "UnregisterDecal", "RegisterDecal" })
                Check(visualUpdates.Any(i => i.Operand is MethodReference m && m.Name == name), "Moved originals refresh " + name);
            var restore = original.Methods.Single(m => m.Name == "Restore").Body.Instructions;
            foreach (var name in new[] { "set_localPosition", "set_localRotation" })
                Check(
                    restore.Any(i => i.Operand is MethodReference m && m.Name == name),
                    "Scene restore preserves exact local transforms: " + name
                );
            Check(
                editor
                    .Methods.Single(m => m.Name == "LateUpdate")
                    .Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "FlushVisuals"),
                "Render adapters receive the final anchored pose once per frame"
            );
            EditorHomeCompatibilityChecks.Run(assembly, client);
            EditorRenderChecks.Native(assembly, client);
            EditorEnvironmentChecks.Native(assembly, client);
            EditorDiagnosticChecks.Native(assembly, client);
            EditorMemoryChecks.Native(assembly, client);
            EditorRouteChecks.Native(assembly, client);
            EditorBarrierChecks.Native(assembly, client);
            EditorLoadingChecks.Native(assembly, client);
            EditorSceneVisibilityChecks.Native(assembly, client);
            var faceIcon = client
                .MainModule.Resources.OfType<EmbeddedResource>()
                .SingleOrDefault(r => r.Name == "WTT.Campaigns.Customization.face.png");
            Check(
                faceIcon != null
                    && Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(faceIcon.GetResourceData()))
                        == "42544B972D804E148E99230710247817B3E907D91A37E1F7DF60000735A858AC",
                "Customization embeds the recovered live face icon, with no placeholder"
            );
            MethodDefinition AsyncBody(string typeName, string methodName)
            {
                var method = client.MainModule.GetType(typeName).Methods.Single(m => m.Name == methodName);
                var machine = (TypeReference)
                    method.CustomAttributes.Single(a => a.AttributeType.Name == "AsyncStateMachineAttribute").ConstructorArguments[0].Value;
                return machine.Resolve().Methods.Single(m => m.Name == "MoveNext");
            }
            var visitOpen = AsyncBody("WTT.Campaigns.Client.Story.StoryVisitRuntime", "Open").Body.Instructions;
            var campaignReconnect = AsyncBody("WTT.Campaigns.Client.Plugin", "Reload").Body.Instructions;
            Check(
                campaignReconnect.Any(i => i.Operand is MethodReference m && m.DeclaringType.Name == "ProfileReconnect" && m.Name == "Run"),
                "Every campaign backend reconnect uses the native profile-creation handoff"
            );
            var creationOverlay = client
                .MainModule.GetType("WTT.Campaigns.Client.UI.SeasonUi")
                .Methods.Single(m => m.Name == "SetReconnectOverlayVisible")
                .Body.Instructions;
            foreach (var required in new[] { "HideSwitchLoader", "SetCreationLoader", "SetActive" })
            {
                Check(
                    creationOverlay.Any(i => i.Operand is MethodReference m && m.Name == required),
                    "Native profile creation releases campaign overlay: " + required
                );
            }
            var profileOpen = AsyncBody("WTT.Campaigns.Client.UI.SeasonUi", "ShowSwitchLoader").Body.Instructions;
            foreach (var body in new[] { visitOpen, profileOpen })
                Check(
                    body.Any(i => i.Operand is MethodReference m && m.DeclaringType.Name == "CampaignLoadingScreen" && m.Name == "Show"),
                    "Profile and trader loading share the same native loading screen implementation"
                );
            var showLoader = visitOpen.First(i =>
                i.Operand is MethodReference m && m.DeclaringType.Name == "CampaignLoadingScreen" && m.Name == "Show"
            );
            var loadStory = visitOpen.First(i =>
                i.Operand is MethodReference m && m.DeclaringType.Name == "StoryClient" && m.Name == "Load"
            );
            Check(showLoader.Offset < loadStory.Offset, "Trader loader appears before story/network preparation");
            var loaderBody = AsyncBody("WTT.Campaigns.Client.UI.CampaignLoadingScreen", "Show").Body.Instructions;
            Check(
                loaderBody.Count(i => i.Operand is MethodReference m && m.Name == "NextFrame") == 2,
                "Shared loader receives render frames before blocking preparation"
            );
            var roomBody = AsyncBody("WTT.Campaigns.Client.Story.StoryVisitRuntime", "LoadRoom").Body;
            Check(
                roomBody.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "LoadAssetAsync")
                    && roomBody.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "ThrowIfCancellationRequested")
                    && roomBody.Instructions.Any(i =>
                        i.Operand is MethodReference m && m.DeclaringType.Name == "StoryMediaStore" && m.Name == "Close"
                    ),
                "Cancelled room loads retain ownership until the async request completes, then release the bundle"
            );
            var bundleBody = AsyncBody("WTT.Campaigns.Client.Story.StoryMediaStore", "OpenTraderAsync").Body.Instructions;
            Check(
                bundleBody.Any(i => i.Operand is MethodReference m && m.Name == "LoadFromFileAsync")
                    && bundleBody.Any(i =>
                        i.Operand is MethodReference m && m.DeclaringType.FullName == "System.Threading.Tasks.Task" && m.Name == "Run"
                    ),
                "Trader checksum work runs in the background and Unity loads bundles asynchronously"
            );
            var visitClear = client
                .MainModule.GetType("WTT.Campaigns.Client.Story.StoryVisitRuntime")
                .Methods.Single(m => m.Name == "Clear");
            Check(
                visitClear.Body.Instructions.Any(i =>
                    i.Operand is MethodReference m && m.DeclaringType.Name == "CampaignLoadingScreen" && m.Name == "Dispose"
                )
                    && visitClear.Body.Instructions.Any(i =>
                        i.Operand is MethodReference m && m.DeclaringType.Name == "CancellationTokenSource" && m.Name == "Cancel"
                    ),
                "Trader closure cancels pending load ownership and removes the loading overlay"
            );
            var zoneBridge = client.MainModule.GetType("WTT.Campaigns.Client.Spatial.NativeZoneBridge");
            var unityStay = zoneBridge.Methods.Single(m => m.Name == "OnTriggerStay");
            Check(
                unityStay.Parameters.Count == 1 && unityStay.Parameters[0].ParameterType.FullName == "UnityEngine.Collider",
                "Campaign zone stay callback has a valid Unity message signature"
            );
            var eftStay = zoneBridge.Methods.Single(m => m.Overrides.Any(o => o.Name == "OnTriggerStay"));
            Check(
                eftStay.Name != "OnTriggerStay" && eftStay.Parameters.Count == 2,
                "EFT two-collider dispatcher is implemented explicitly without colliding with Unity messages"
            );
            Check(
                new[] { unityStay, eftStay }.All(m =>
                    m.Body.Instructions.Any(i => i.Operand is MethodReference call && call.Name == "OnTriggerEnter")
                ),
                "Both physics paths recover occupancy for players already inside the zone"
            );
            var storyAccept = client.MainModule.GetType("WTT.Campaigns.Client.Story.StoryClient").Methods.Single(m => m.Name == "Accept");
            Check(
                storyAccept.Body.Instructions.Any(i =>
                    i.Operand is MethodReference call && call.DeclaringType.Name == "StoryChapterChanges" && call.Name == "Accept"
                ),
                "All accepted story snapshots feed chapter notifications, including passive raid refreshes"
            );
            Check(
                types["EFT.Communications.NotificationManager"].Methods.Any(m => m.Name == "DisplayNotification" && m.IsStatic),
                "Chapter notifications bind the installed native notification queue"
            );
            var chapterNotification = client.MainModule.GetType("WTT.Campaigns.Client.Story.StoryChapterNotification");
            Check(
                chapterNotification
                    .Methods.Single(m => m.Name == "CreateView")
                    .Body.Instructions.Any(i =>
                        i.Operand is MethodReference call
                        && call.DeclaringType.Name == "StoryChapterNotificationView"
                        && call.Name == "Create"
                    ),
                "Story chapters use a dedicated notification view rather than the generic toast"
            );
            var chapterView = client.MainModule.GetType("WTT.Campaigns.Client.Story.StoryChapterNotificationView");
            Check(
                chapterView.BaseType.FullName == "EFT.UI.BaseNotificationView"
                    && chapterView
                        .Methods.Single(m => m.Name == "get_ReturnToPool")
                        .Body.Instructions.Any(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldc_I4_0),
                "The dedicated banner participates in native dismissal without contaminating the generic notification pool"
            );
            Check(
                chapterView
                    .Methods.Single(m => m.Name == "Create")
                    .Body.Instructions.Any(i => Equals(i.Operand, "wtt_campaigns_story_notifications.bundle")),
                "Chapter notification GameObjects load from their dedicated bundle"
            );
            Check(
                types["EFT.UI.BaseNotificationView"].Methods.Any(m => m.Name == "OnAnimationDone" && m.Parameters.Count == 0),
                "Bundled notification animation completion binds the native dismissal callback"
            );
            var startup = client
                .MainModule.GetType("WTT.Campaigns.Client.Patches.Session.BackendIdentity")
                .Methods.Single(m => m.Name == "Prefix")
                .Body.Instructions;
            var protocol = startup.Single(i => i.Operand is MethodReference m && m.Name == "set_ProtocolVersion");
            var serialize = startup.Single(i => i.Operand is MethodReference m && m.Name == "SerializeObject");
            var post = startup.Single(i => i.Operand is MethodReference m && m.Name == "PostJson");
            Check(
                protocol.Previous.OpCode == Mono.Cecil.Cil.OpCodes.Ldc_I4_2
                    && protocol.Offset < serialize.Offset
                    && serialize.Offset < post.Offset
                    && !startup.Any(i => Equals(i.Operand, "{}")),
                "Initial backend snapshot sends creator protocol 2 before any game session exists"
            );
            var storyAvailability = client
                .MainModule.GetType("WTT.Campaigns.Client.Story.StoryClient")
                .Methods.Single(m => m.Name == "get_Available")
                .Body.Instructions;
            Check(
                storyAvailability.Any(i => Equals(i.Operand, "seasonal"))
                    && storyAvailability.Any(i => i.Operand is MethodReference method && method.Name == "get_EffectiveProfileId")
                    && !storyAvailability.Any(i => i.Operand is MethodReference method && method.Name == "get_HasStory"),
                "Campaign story interface requires the loaded character, not authored story content"
            );
            var hub = client.MainModule.GetType("WTT.Campaigns.Client.Hub.SeasonHubUi");
            Check(hub != null, "Campaign hub client adapter is packaged");
            var availability = hub!.Methods.Single(m => m.Name == "get_Available").Body.Instructions;
            Check(
                availability.Any(i => Equals(i.Operand, "seasonal"))
                    && availability.Any(i => i.Operand is MethodReference method && method.Name == "get_InRaid")
                    && availability.Any(i => i.Operand is FieldReference field && field.Name == "Busy"),
                "Hub visibility checks Campaign, raid and busy state"
            );
            var menu = client.MainModule.GetType("WTT.Campaigns.Client.Patches.UI.MenuEntry").Methods.Single(m => m.Name == "Postfix");
            Check(
                menu.Body.Instructions.Any(i => i.Operand is MethodReference method && method.Name == "AttachMenu")
                    && menu.Body.ExceptionHandlers.Any(h => h.CatchType?.FullName == "System.Exception"),
                "Optional hub entry cannot throw through native menu initialization"
            );
            var input = client.MainModule.GetType("WTT.Campaigns.Client.UI.SeasonUi").Methods.Single(m => m.Name == "get_InputBlocked");
            Check(
                input.Body.Instructions.Any(i => i.Operand is MethodReference method && method.DeclaringType.Name == "SeasonHubUi"),
                "Native input guard includes hub visibility"
            );
            var video = client.MainModule.GetType("WTT.Campaigns.Client.Hub.HubVideo").Methods.Single(m => m.Name == "OnDisable");
            Check(
                video.Body.Instructions.Any(i => i.Operand is MethodReference method && method.Name == "Stop"),
                "Hidden hub videos stop their decoder"
            );
            var patch = client.MainModule.GetType("WTT.Campaigns.Client.Patches.UI.SkillsTabPatch");
            var initialization = patch.Methods.Single(method =>
                method.HasBody
                && method.Body.Instructions.Any(instruction =>
                    instruction.Operand is MethodReference called
                    && called.DeclaringType.FullName == "WTT.Campaigns.Client.UI.CampaignSkillsTab"
                    && called.Name == "Initialize"
                )
            );
            Check(
                initialization.CustomAttributes.Any(attribute => attribute.AttributeType.Name == "PatchPostfixAttribute")
                    && !patch.Methods.Any(method =>
                        method.CustomAttributes.Any(attribute => attribute.AttributeType.Name == "PatchPrefixAttribute")
                    ),
                "Perks tab initializes after native Show, including first activation"
            );
            Check(
                initialization.Body.ExceptionHandlers.Any(handler => handler.CatchType?.FullName == "System.Exception"),
                "Optional perks initialization cannot throw through native Skills Show"
            );
        }
        Check(
            types["EFT.UI.MenuScreen"].Fields.Any(value => value.Name == "_playerButton" && value.FieldType.Name == "DefaultUIButton"),
            "Native menu button"
        );
        Check(
            types["EFT.UI.TabGroup"].Fields.Any(value => value.Name == "_tabs" && value.FieldType.FullName == "Tab[]"),
            "Extensible native tabs"
        );
        Check(
            types["EFT.UI.TabGroup"].Methods.Any(value => value.Name == "SelectionChangedHandler" && value.IsPublic),
            "Native tab selection handler"
        );
        Check(types["Tab"].Events.Any(value => value.Name == "OnSelectionChanged"), "Native tab selection event");
        foreach (var name in new[] { "Show", "TryHide" })
        {
            Check(types["EFT.UI.ITabController"].Methods.Any(value => value.Name == name), "Tab controller " + name);
        }
        var translate = types["EFT.InputSystem.InputNode"].Methods.Single(value => value.Name == "TranslateInput");
        Check(
            translate.Parameters.Select(value => value.Name).SequenceEqual(new[] { "commands", "axes", "shouldLockCursor" }),
            "Input patch argument names"
        );
        Check(
            translate.Parameters[1].ParameterType.IsByReference && translate.Parameters[2].ParameterType.IsByReference,
            "Input patch reference arguments"
        );
        Check(types["EFT.InputSystem.UIInputRoot"].BaseType.FullName == "EFT.InputSystem.InputNode", "UI root input interception");
        Check(types["EFT.InputSystem.ECursorResult"].Fields.Any(value => value.Name == "ShowCursor"), "Overlay cursor state");
        Check(types["EFT.InputSystem.ECursorResult"].Fields.Any(value => value.Name == "LockCursor"), "Editor flight cursor state");
        Check(
            types["EFT.Player"].Methods.Any(value => value.Name == "get_CameraPosition" && value.ReturnType.FullName == "UnityEngine.Transform")
                && types["EFT.Player"].Methods.Any(value => value.Name == "get_LookDirection" && value.ReturnType.FullName == "UnityEngine.Vector3"),
            "Editor initial pose uses the current player's native camera anchor and facing"
        );
        Check(
            types["EFT.UI.PlayerModelView"]
                .Methods.Any(value =>
                    value.Name == "Show" && value.Parameters[0].ParameterType.FullName == "EFT.PlayerVisualRepresentation"
                ),
            "Independent equipment preview overload"
        );
        Check(types["EFT.UI.PlayerModelView"].Fields.Any(value => value.Name == "_progressSpinner"), "Model loading indicator binding");
        Check(types["EFT.UI.CameraImage"].Methods.Any(value => value.Name == "InitCamera"), "Native render texture lifecycle");
        Check(
            types["EFT.InventoryEquipmentDescriptor"].Methods.Any(value => value.Name == "OnJSONDeserialized"),
            "Native equipment-tree deserialization"
        );
        Check(
            types["EFT.PlayerVisualRepresentationDescriptor"]
                .Fields.Any(value => value.Name == "Equipment" && value.FieldType.FullName == "EFT.InventoryEquipmentDescriptor"),
            "Appearance-only profile descriptor"
        );
        var reconnect = types["EFT.TarkovApplication"].Methods.Single(method => method.Name == "RecreateBackend");
        foreach (var name in new[] { "MergeResult", "SplitResult", "TransferResult" })
        {
            var method = types["EFT.InventoryLogic." + name].Methods.Single(m => m.Name == "RaiseEvents");
            Check(
                method.Parameters.Select(p => p.Name).SequenceEqual(new[] { "controller", "status" }),
                "Document provenance event arguments: " + name
            );
            Check(method.Parameters[1].ParameterType.Name == "CommandStatus", "Successful native operation status: " + name);
        }
        Check(
            types["EFT.InventoryLogic.MergeResult"].Fields.Any(f => f.Name == "_transferResult" && f.FieldType.Name == "TransferResult"),
            "Merge exposes exact transferred quantity"
        );
        Check(
            types["EFT.InventoryLogic.TransferResult"].Fields.Any(f => f.Name == "Count" && f.FieldType.Name == "Int32"),
            "Transfer exposes exact quantity"
        );
        Check(
            types["EFT.InventoryLogic.InternalSplitResult"].Properties.Any(p => p.Name == "ResultItem"),
            "Split exposes the new item identity"
        );
        Check(
            reconnect.IsPublic
                && reconnect.Parameters.Count == 2
                && reconnect.Parameters[1].Name == "force"
                && reconnect.Parameters[1].ParameterType.FullName == "System.Boolean",
            "Native reconnect supports forced same-mode profile switching"
        );
        foreach (var name in new[] { "LocalRaidStarted", "LocalRaidEnded" })
        {
            Check(
                types["EFT.EftClientBackendSession"].Methods.Count(m => m.Name == name) == 1,
                "Document journal flush binds native backend " + name
            );
        }
        Console.WriteLine($"UI compatibility: {count} checks passed against {Path.GetFileName(path)}.");
    }
}
