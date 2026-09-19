namespace WTT.Campaigns.Client.Authoring.Scenes;

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
                or "StencilShadow"
                or "EFT.SpeedTree.TreeWind";

    // These require ownership checks as well as preserving their native components.
    internal static bool OwnedComponent(string name) =>
        name
            is "TreeInteractivePart"
                or "EFT.Interactive.TreeInteractive"
                or "VolumetricLight"
                or "AreaLight"
                or "EFT.Impostors.AmplifyImpostorsArrayElement"
                or "EFT.Interactive.LampController"
                or "AmplifyImpostors.AmplifyImpostor"
                or "CullingLightObject"
                or "EFT.Interactive.WindowBreaker"
                or "DoorHandle"
                or "GripPose"
                or "EFT.Interactive.Trunk"
                or "EFT.Interactive.LootableContainer"
                or "EFT.Interactive.LootPointViewer"
                or "EFT.Interactive.LootPoint"
                or "EFT.Interactive.GroupLootPoint"
                or "UnityEngine.AI.NavMeshObstacle";

    internal static string Restriction(string name) =>
        name switch
        {
            "GPUInstancer.GPUInstancerTerrainProxy" or "UnityEngine.Terrain" or "UnityEngine.TerrainCollider" =>
                "This terrain tile owns baked terrain, vegetation and navigation data; it cannot be moved as a prop.",
            "UnityEngine.OcclusionPortal" =>
                "This object contains a baked occlusion portal. Moving its transform cannot relocate the map's visibility portal.",
            "EFT.Interactive.Door" =>
                "This prop contains a door with map navigation and audio links. Use door controls for its state; relocating the whole assembly is unsupported.",
            "EFT.Interactive.ColliderReporter" => "This collider reports interactions for another object; select its owning prop.",
            "EFT.Interactive.BarbedWire" => "This is a damage trigger, so its gameplay area cannot be moved as a plain prop.",
            "MetaXRAcousticGeometry" => "This object contains baked acoustic geometry that does not follow ordinary transforms.",
            _ => "This component needs a movement adapter: " + name + ". Its properties remain available for inspection.",
        };
}
