namespace WTT.Campaigns.Client.Authoring;

// These components remain attached to an edited original. Copying them needs its own adapter.
internal static class ScenePropSupport
{
    internal static bool PreservedComponent(string name) =>
        name
            is "UnityEngine.Rigidbody"
                or "UnityEngine.AudioSource"
                or "UnityEngine.Light"
                or "BaseBallistic"
                or "EFT.Ballistics.BallisticColliderComposer"
                or "HotObject"
                or "StaticDeferredDecal"
                or "StencilShadow";

    internal static string Restriction(string name) =>
        name switch
        {
            "EFT.Interactive.ColliderReporter" => "This collider reports interactions for another object; select its owning prop.",
            "EFT.Interactive.WindowBreaker" => "This is a destructible window. Moving it requires updating its breakable geometry.",
            "EFT.Interactive.BarbedWire" => "This is a damage trigger, so its gameplay area cannot be moved as a plain prop.",
            "MetaXRAcousticGeometry" => "This object contains baked acoustic geometry that does not follow ordinary transforms.",
            _ => "This component needs a movement adapter: " + name + ". Its properties remain available for inspection.",
        };
}
