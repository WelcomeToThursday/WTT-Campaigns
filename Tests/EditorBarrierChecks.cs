using Mono.Cecil;
using Mono.Cecil.Cil;

namespace WTT.Campaigns.Tests;

internal static class EditorBarrierChecks
{
    internal static void Native(AssemblyDefinition native, AssemblyDefinition client)
    {
        void Require(bool valid, string message)
        {
            if (!valid)
                throw new InvalidOperationException("Editor barriers: " + message);
        }

        // The installed culling sampler owns this layer name.  Keeping the
        // native contract in the offline suite catches a changed game build
        // before a walkthrough can silently create ineffective blockers.
        var sampler = native.MainModule.GetType("Koenigz.PerfectCulling.EFT.PerfectCullingCrossSceneSampler");
        Require(
            sampler.Fields.Any(f => f.Name == "LOW_POLY_COLLIDER_LAYER" && f.IsLiteral && Equals(f.Constant, "LowPolyCollider")),
            "The installed culling sampler must retain the LowPolyCollider layer contract."
        );
        var masks = native.MainModule.GetType("LayersMaskController").Methods.Single(m => m.Name == ".cctor").Body.Instructions;
        var playerMask = masks.Single(i =>
            i.OpCode == OpCodes.Stsfld && i.Operand is FieldReference field && field.Name == "PlayerCollisionTestMask"
        );
        var maskStart = playerMask.Previous;
        while (maskStart.Previous != null && maskStart.Previous.OpCode != OpCodes.Stsfld)
            maskStart = maskStart.Previous;
        var maskBody = masks.Skip(masks.IndexOf(maskStart)).Take(masks.IndexOf(playerMask) - masks.IndexOf(maskStart)).ToArray();
        Require(
            maskBody.Any(i => i.OpCode == OpCodes.Ldstr && Equals(i.Operand, "LowPolyCollider"))
                && maskBody.Any(i => i.Operand is MethodReference method && method.Name == "GetMask"),
            "EFT's installed player collision test mask must include LowPolyCollider."
        );

        var adapter = client.MainModule.GetType("WTT.Campaigns.Client.Authoring.Scenes.MapSceneAdapter");
        var volume = adapter.Methods.Single(m => m.Name == "Volume");
        var volumeCalls = volume
            .Body.Instructions.Where(i => i.Operand is MethodReference)
            .Select(i => (MethodReference)i.Operand)
            .ToArray();
        var allAdapterCalls = adapter
            .Methods.Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions)
            .Where(i => i.Operand is MethodReference)
            .Select(i => (MethodReference)i.Operand)
            .ToArray();

        Require(
            volume.Body.Instructions.Any(i =>
                i.OpCode == OpCodes.Newobj && i.Operand is MethodReference m && m.DeclaringType.FullName == "UnityEngine.GameObject"
            ),
            "Applied barriers must start from a plain GameObject without a renderer."
        );
        Require(
            volumeCalls.Any(m =>
                m.Name == "AddComponent"
                && m is GenericInstanceMethod g
                && g.GenericArguments.Single().FullName == "UnityEngine.BoxCollider"
            )
                && volumeCalls.Any(m =>
                    m.Name == "AddComponent"
                    && m is GenericInstanceMethod g
                    && g.GenericArguments.Single().FullName == "UnityEngine.SphereCollider"
                ),
            "Applied barriers must use native box and sphere colliders."
        );
        Require(
            volumeCalls.Any(m => m.Name == "set_size" && m.DeclaringType.FullName == "UnityEngine.BoxCollider")
                && volumeCalls.Any(m => m.Name == "set_radius" && m.DeclaringType.FullName == "UnityEngine.SphereCollider"),
            "Barrier dimensions must be assigned to the collider shape."
        );
        foreach (var trigger in volume.Body.Instructions.Where(i => i.Operand is MethodReference { Name: "set_isTrigger" }))
            Require(trigger.Previous?.OpCode == OpCodes.Ldc_I4_0, "Applied barriers must be non-trigger colliders.");
        Require(
            volume.Body.Instructions.Any(i =>
                i.Operand is MethodReference { Name: "NameToLayer", DeclaringType.FullName: "UnityEngine.LayerMask" }
            ) && volume.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldstr && Equals(i.Operand, "LowPolyCollider")),
            "Applied barriers must require and assign the explicit LowPolyCollider layer."
        );
        Require(
            allAdapterCalls.Any(m =>
                m.Name == "MoveGameObjectToScene" && m.DeclaringType.FullName == "UnityEngine.SceneManagement.SceneManager"
            ),
            "Applied barriers must enter the captured loaded scene."
        );
        Require(
            allAdapterCalls.Any(m => m.Name == "get_isLoaded" && m.DeclaringType.FullName == "UnityEngine.SceneManagement.Scene")
                && allAdapterCalls.Any(m =>
                    m.Name == "FindObjectsOfTypeAll"
                    && m is GenericInstanceMethod g
                    && g.GenericArguments.Single().FullName == "UnityEngine.Transform"
                ),
            "Barrier scene lookup must verify ordinary and persistent loaded scenes."
        );
        Require(
            volumeCalls.Any(m => m.Name == "CreatePrimitive")
                && volumeCalls.Any(m => m.Name == "Destroy" && m.DeclaringType.FullName == "UnityEngine.Object"),
            "Ghost previews must remain render-only and clean up their temporary primitive."
        );
        Require(
            volumeCalls.Any(m => m.Name == "set_localScale" && m.DeclaringType.FullName == "UnityEngine.Transform"),
            "Ghost previews must retain the authored box size and sphere diameter."
        );
        Require(
            adapter
                .Methods.Single(m => m.Name == "Apply")
                .Body.Instructions.Any(i => i.Operand is MethodReference { Name: "Dispose", DeclaringType.Name: "SceneEditTransaction" }),
            "Repeated walkthrough apply must release the previous barrier transaction."
        );
        Require(
            volume.Body.Instructions.Any(i => i.Operand is MethodReference { Name: "Remove", DeclaringType.Name: "MapSceneAdapter" }),
            "Barrier creation must clean up a partially created object when scene setup fails."
        );

        Console.WriteLine(
            "Editor barriers: native layer, collider shape, scene placement, ghost and transaction contracts passed offline."
        );
    }
}
