using WTT.Campaigns.Client.Authoring;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Tests;

internal static class SceneSelectionChecks
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (var component in new[] { "UnityEngine.Rigidbody", "UnityEngine.AudioSource", "UnityEngine.Light", "BaseBallistic", "EFT.Ballistics.BallisticColliderComposer", "HotObject", "StaticDeferredDecal", "StencilShadow" })
            check(ScenePropSupport.PreservedComponent(component), "Original movement preserves supported component: " + component);
        foreach (var component in new[] { "UnityEngine.MonoBehaviour", "UnityEngine.Animator", "EFT.Interactive.Door", "EFT.Interactive.BarbedWire", "EFT.Interactive.WindowBreaker", "EFT.Interactive.ColliderReporter", "CustomStaticBatching", "SomeMod.HotObject" })
            check(!ScenePropSupport.PreservedComponent(component), "Unknown or gameplay components require their own movement adapter: " + component);
        check(ScenePropSupport.Restriction("EFT.Interactive.ColliderReporter").Contains("owning prop"), "Reporter restrictions explain which object to select");
        var selection = new MapObjectEdit
        {
            Id = "selected", Name = "Original crate", Operation = "Move",
            Position = new SpatialVector { X = 3, Y = 2, Z = 1 },
            Scale = new SpatialVector { X = 2, Y = 3, Z = 4 },
            Target = new MapTarget { Path = "Scene/crate", Fingerprint = "original" },
        };
        check(SceneSelectionEdit.Prepare(selection, _ => { }) == null, "Clicking or changing tools creates no scene edit");
        check(SceneSelectionEdit.Prepare(selection, p => p.Position.X = 3) == null, "An unchanged property creates no scene edit");
        var rotated = SceneSelectionEdit.Prepare(selection, p => p.Rotation.Y = 45)!;
        check(rotated.Operation == "Move" && rotated.Rotation.Y == 45 && selection.Rotation.Y == 0,
            "First rotation produces a detached original-prop edit");
        var resized = SceneSelectionEdit.Prepare(selection, p => ((MapObjectEdit)p).Scale.X = 5)!;
        check(resized.Scale.X == 5 && selection.Scale.X == 2 && resized.Target.Fingerprint == "original",
            "First resize preserves original selection and saved target binding");
        resized.Target.Path = "changed copy";
        check(selection.Target.Path == "Scene/crate", "Prepared edits never alias selection bindings");
        check(SceneSelectionEdit.Prepare(selection, p => { p.Position.X = 8; p.Position.X = 3; }) == null,
            "Returning a drag to its original pose creates no scene edit");
        try { SceneSelectionEdit.Prepare(selection, p => { p.Position.X = 99; throw new InvalidOperationException(); }); }
        catch (InvalidOperationException) { }
        check(selection.Position.X == 3, "Failed first edit leaves the selection pose untouched");
    }
}
