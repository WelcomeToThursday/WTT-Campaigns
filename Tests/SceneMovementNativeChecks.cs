using Mono.Cecil;

namespace WTT.Campaigns.Tests;

internal static class SceneMovementNativeChecks
{
    internal static void Run(Dictionary<string, TypeDefinition> types)
    {
        void Require(bool valid, string message)
        {
            if (!valid)
                throw new InvalidOperationException("Scene movement native contract: " + message);
        }
        bool Calls(string type, string method, string called) =>
            types[type]
                .Methods.Where(m => m.Name == method && m.HasBody)
                .Any(m => m.Body.Instructions.Any(i => i.Operand is MethodReference r && r.Name == called));

        // These adapters depend on local animation frames and live world-space queries.
        // Fail validation if a game update introduces a different movement contract.
        Require(
            types["TreeInteractivePart"].Fields.Any(f => f.Name == "InteractivePart" && f.FieldType.FullName == "UnityEngine.GameObject"),
            "tree ownership reference is available"
        );
        Require(
            types["EFT.Interactive.Trunk"].BaseType.FullName == "EFT.Interactive.WorldInteractiveObject",
            "trunk uses native interaction frame"
        );
        Require(
            Calls("EFT.Interactive.LootableContainer", "SmoothDoorOpenCoroutine", "set_localPosition")
                || types["EFT.Interactive.LootableContainer"]
                    .NestedTypes.Any(t =>
                        t.Methods.Any(m =>
                            m.HasBody && m.Body.Instructions.Any(i => i.Operand is MethodReference r && r.Name == "set_localPosition")
                        )
                    ),
            "container animation retains local drawer positions"
        );
        Require(
            Calls("DoorHandle", "DefPos", "set_localPosition") && Calls("DoorHandle", "DefPos", "set_localRotation"),
            "handle animation remains relative to its parent"
        );
        Require(Calls("GripPose", "get_Position", "get_position"), "grips query their current world position");
        Require(Calls("EFT.Interactive.LootPoint", "AsLootPointParameters", "get_position"), "loot markers export current positions");
        Require(
            Calls("EFT.Interactive.WorldInteractiveObject", "set_CurrentAngle", "UpdateInteractableTransform"),
            "native glass rendering uses the same transform refresh as the movement adapter"
        );
        Require(Calls("EFT.Interactive.LampController", "TryToBlowUp", "get_position"), "lamp damage queries current position");
        Require(Calls("EFT.Interactive.LampController", "Switch", "TransformDirection"), "lamp effects follow the current transform");
        Require(Calls("EFT.Interactive.WindowBreaker", "Break", "InverseTransformPoint"), "window hits use the current local frame");
        Require(
            Calls("EFT.Interactive.WindowBreaker", "GetStuckPieceDescription", "set_localPosition"),
            "window pieces remain local to the window"
        );
        Require(Calls("CullingObject", "CustomUpdate", "UpdateSphere"), "culling refreshes its registered sphere");
        Require(
            types["CullingObject"].Fields.Any(f => f.Name == "_safeMultithreadedPosition" && f.FieldType.FullName == "UnityEngine.Vector3"),
            "light position cache is available"
        );
        Require(
            types["CullingManager"]
                .Fields.Any(f => f.Name == "_objectsData" && f.FieldType.FullName == "CullingManager/CullingObjectData[]"),
            "light registration can be checked before sphere updates"
        );
        foreach (var field in new[] { "m_rootTransform", "m_lodGroup", "m_renderers", "m_lastImpostor" })
            Require(types["AmplifyImpostors.AmplifyImpostor"].Fields.Any(f => f.Name == field), "impostor ownership field " + field);
        Require(
            !types["AmplifyImpostors.AmplifyImpostor"].Methods.Any(m => m.Name is "Update" or "LateUpdate" or "Awake" or "OnEnable"),
            "impostor component has no runtime pose cache lifecycle"
        );
        foreach (var field in new[] { "_breakerIdInstanceId", "_instancesGeometry", "_renderData", "_instancesEnable" })
            Require(types["WindowsManager"].Fields.Any(f => f.Name == field), "window rendering field " + field);
        Require(
            types["EFT.Interactive.WindowBreaker"]
                .Fields.Any(f => f.Name == "_pieces" && f.FieldType.FullName == "EFT.Interactive.WindowBreaker/Piece[]"),
            "window restoration can refresh surviving fragment bounds"
        );
        Require(
            Calls("WindowsManager", "BreakWindow", "SetData") && Calls("WindowsManager", "UpdatePieceTransform", "SetData"),
            "window visibility and pose use native instance buffers"
        );
        Require(
            Calls("VolumetricLight", "SetDynamicLightValues", "SetDynamicSpotLightValues")
                && Calls("VolumetricLight", "SetDynamicLightValues", "SetDynamicPointLightValues"),
            "volumetric movement refresh covers spot and point lights"
        );
        Require(
            Calls("VolumetricLight", "SetDynamicSpotLightValues", "get_position")
                && Calls("VolumetricLight", "SetDynamicPointLightValues", "get_position"),
            "volumetric matrices use current positions"
        );
        Require(
            Calls("EFT.Interactive.TreeInteractive", "OnTriggerExit", "ExitTree")
                && Calls("EFT.Interactive.TreeInteractive", "OnTriggerExit", "VolumeFadeOut"),
            "tree movement releases native AI occupancy and rustling audio"
        );
        Require(
            types["EFT.Interactive.TreeInteractive"]
                .Fields.Any(f =>
                    f.Name == "_sources"
                    && f.FieldType.FullName == "System.Collections.Generic.Dictionary`2<UnityEngine.Collider,BetterSource>"
                ),
            "tree audio contacts can be released before movement"
        );
        Require(
            types["EFT.Interactive.TreeInteractive"].Fields.Any(f => f.Name == "_colliderPlayers"),
            "tree player contacts can be cleared after native exits"
        );
        Require(Calls("EFT.SpeedTree.TreeWind", "SetParams", "SetPropertyBlock"), "tree wind uses renderer property data");
        Require(
            Calls("AreaLight", "CalculateDefaultValues", "get_localToWorldMatrix")
                && Calls("AreaLight", "CalculateDefaultValues", "GetProjectionMatrix"),
            "area lights cache pose and projection together"
        );
        foreach (var field in new[] { "bool_0", "bool_4", "_shadowCubePlanes", "_invertedShadowCubePlanes" })
            Require(types["AreaLight"].Fields.Any(f => f.Name == field), "area light cache field " + field);
        Require(
            types["AreaLight"]
                .Methods.Single(m => m.Name == "CalculateDefaultValues")
                .Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "bool_4"),
            "area light pose refresh invalidates the actual cache gate"
        );
        Require(
            types["EFT.Impostors.ImpostorsRenderer"]
                .Fields.Any(f => f.Name == "_drawInstance" && f.FieldType.FullName == "EFT.Impostors.ImpostorsDrawInstance"),
            "impostor draw registry is available"
        );
        Require(
            Calls("EFT.Impostors.ImpostorsDrawInstance", "UpdateMatrixBuffers", "get_localToWorldMatrix")
                && Calls("EFT.Impostors.ImpostorsDrawInstance", "UpdateMatrixBuffers", "get_worldToLocalMatrix"),
            "impostor rendering stores both pose matrices"
        );
        Require(
            Calls("EFT.Impostors.ImpostorsMainCameraContext", "RefreshImpostorsBuffer", "get_position")
                && Calls("EFT.Impostors.ImpostorsMainCameraContext", "RefreshImpostorsBuffer", "get_lossyScale"),
            "impostor culling requires position and scale updates as well as render matrices"
        );
        Require(
            types["EFT.Impostors.ImpostorsMainCameraContext"]
                .Methods.Single(m => m.Name == "OnPreCull")
                .Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "_refreshed"),
            "impostor updates force reculling for stationary cameras"
        );
        Require(
            types["EFT.World"]
                .Fields.Any(f =>
                    f.Name == "_interactiveObjectsDictionary"
                    && f.FieldType.FullName
                        == "System.Collections.Generic.Dictionary`2<System.String,EFT.Interactive.WorldInteractiveObject>"
                ),
            "placed doors have an owned native registry entry with cleanup access"
        );
        Require(
            Calls("EFT.World", "RegisterWorldInteractionObject", "Add"),
            "placed door registration uses the native identity dictionary"
        );
        Require(
            types["SmartGrip"].BaseType.FullName == "GripPose"
                && Calls("SmartGrip", "Awake", "GetComponentsInChildren")
                && Calls("SmartGrip", "Awake", "set_parent"),
            "door smart grips discover cloned finger solvers and require an owned pivot before activation"
        );
        foreach (var field in new[] { "Pivot", "Targets", "DEBUG_TARGET" })
            Require(
                types["SmartGrip"].Fields.Any(f => f.Name == field && f.FieldType.FullName == "UnityEngine.Transform"),
                "door grip ownership reference " + field
            );
        Require(
            types["EFT.InventoryLogic.KeyTemplate"]
                .Methods.Where(m => m.HasBody)
                .Any(m =>
                    m.Name.EndsWith("get_KeyId")
                    && m.Body.Instructions.Any(i =>
                        i.Operand is MethodReference r
                        && r.Name == "get__id"
                        && r.DeclaringType.FullName == "EFT.InventoryLogic.ItemTemplate"
                    )
                ),
            "searchable key catalogue IDs match the actual native unlock key identity"
        );
        Require(
            types["EFT.Interactive.WorldInteractiveObject"]
                .Methods.Single(m => m.Name == "UnlockOperation")
                .Body.Instructions.Any(i => i.Operand is MethodReference r && r.Name == "get_KeyId"),
            "door unlock preserves native key checks and consumption"
        );
        Console.WriteLine(
            "Scene movement: native interaction, lamp, volumetric light, tree wind/contact, culling, impostor and window contracts verified offline."
        );
    }
}
