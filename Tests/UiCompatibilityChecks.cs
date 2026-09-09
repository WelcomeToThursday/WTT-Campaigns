using Mono.Cecil;

namespace SeasonalPerks.Tests;

internal static class UiCompatibilityChecks
{
    internal static void Run(string path, string? clientPath = null)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(path);
        var types = assembly.MainModule.GetTypes().ToDictionary(type => type.FullName);
        var count = 0;
        void Check(bool value, string description)
        {
            count += value ? 1 : throw new InvalidOperationException(description);
        }
        var appearance = types["EFT.UI.HeadSelectionState"];
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
            "Native loading indicator is available to the seasonal creation overlay"
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
            var zoneBridge = client.MainModule.GetType("SeasonalPerks.Client.Spatial.NativeZoneBridge");
            var unityStay = zoneBridge.Methods.Single(m => m.Name == "OnTriggerStay");
            Check(
                unityStay.Parameters.Count == 1 && unityStay.Parameters[0].ParameterType.FullName == "UnityEngine.Collider",
                "Seasonal zone stay callback has a valid Unity message signature"
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
            var storyAccept = client.MainModule.GetType("SeasonalPerks.Client.Story.StoryClient").Methods.Single(m => m.Name == "Accept");
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
            var chapterNotification = client.MainModule.GetType("SeasonalPerks.Client.Story.StoryChapterNotification");
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
            var chapterView = client.MainModule.GetType("SeasonalPerks.Client.Story.StoryChapterNotificationView");
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
                    .Body.Instructions.Any(i => Equals(i.Operand, "seasonal_story_notifications.bundle")),
                "Chapter notification GameObjects load from their dedicated bundle"
            );
            Check(
                types["EFT.UI.BaseNotificationView"].Methods.Any(m => m.Name == "OnAnimationDone" && m.Parameters.Count == 0),
                "Bundled notification animation completion binds the native dismissal callback"
            );
            var startup = client
                .MainModule.GetType("SeasonalPerks.Client.Patches.Session.BackendIdentity")
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
                .MainModule.GetType("SeasonalPerks.Client.Story.StoryClient")
                .Methods.Single(m => m.Name == "get_Available")
                .Body.Instructions;
            Check(
                storyAvailability.Any(i => Equals(i.Operand, "seasonal"))
                    && storyAvailability.Any(i => i.Operand is MethodReference method && method.Name == "get_EffectiveProfileId")
                    && !storyAvailability.Any(i => i.Operand is MethodReference method && method.Name == "get_HasStory"),
                "Seasonal story interface requires the loaded character, not authored story content"
            );
            var hub = client.MainModule.GetType("SeasonalPerks.Client.Hub.SeasonHubUi");
            Check(hub != null, "Season hub client adapter is packaged");
            var availability = hub!.Methods.Single(m => m.Name == "get_Available").Body.Instructions;
            Check(
                availability.Any(i => Equals(i.Operand, "seasonal"))
                    && availability.Any(i => i.Operand is MethodReference method && method.Name == "get_InRaid")
                    && availability.Any(i => i.Operand is FieldReference field && field.Name == "Busy"),
                "Hub visibility checks Seasonal, raid and busy state"
            );
            var menu = client.MainModule.GetType("SeasonalPerks.Client.Patches.UI.MenuEntry").Methods.Single(m => m.Name == "Postfix");
            Check(
                menu.Body.Instructions.Any(i => i.Operand is MethodReference method && method.Name == "AttachMenu")
                    && menu.Body.ExceptionHandlers.Any(h => h.CatchType?.FullName == "System.Exception"),
                "Optional hub entry cannot throw through native menu initialization"
            );
            var input = client.MainModule.GetType("SeasonalPerks.Client.UI.SeasonUi").Methods.Single(m => m.Name == "get_InputBlocked");
            Check(
                input.Body.Instructions.Any(i => i.Operand is MethodReference method && method.DeclaringType.Name == "SeasonHubUi"),
                "Native input guard includes hub visibility"
            );
            var video = client.MainModule.GetType("SeasonalPerks.Client.Hub.HubVideo").Methods.Single(m => m.Name == "OnDisable");
            Check(
                video.Body.Instructions.Any(i => i.Operand is MethodReference method && method.Name == "Stop"),
                "Hidden hub videos stop their decoder"
            );
            var patch = client.MainModule.GetType("SeasonalPerks.Client.Patches.UI.SkillsTabPatch");
            var initialization = patch.Methods.Single(method =>
                method.HasBody
                && method.Body.Instructions.Any(instruction =>
                    instruction.Operand is MethodReference called
                    && called.DeclaringType.FullName == "SeasonalPerks.Client.UI.SeasonalSkillsTab"
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
