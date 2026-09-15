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
        Console.WriteLine("Scene movement: native trunk, drawer, handle, grip, tree, loot-marker and glass contracts verified offline.");
    }
}
