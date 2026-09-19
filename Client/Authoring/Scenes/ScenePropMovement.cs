using AmplifyImpostors;
using EFT.Interactive;
using UnityEngine;
using UnityEngine.AI;

namespace WTT.Campaigns.Client.Authoring.Scenes;

internal static class ScenePropMovement
{
    private static bool Inside(Transform root, Component? part) => !part || part!.transform.IsChildOf(root);

    private static bool AllInside<T>(Transform root, IEnumerable<T>? parts)
        where T : Component
    {
        if (parts != null)
            foreach (var part in parts)
                if (!Inside(root, part))
                    return false;
        return true;
    }

    private static bool ObjectsInside(Transform root, IEnumerable<GameObject>? parts)
    {
        if (parts != null)
            foreach (var part in parts)
                if (part && !part.transform.IsChildOf(root))
                    return false;
        return true;
    }

    internal static string Restriction(Transform root, Component component)
    {
        if (
            component is AreaLight area
            && (!Inside(root, area.ShadowCube) || !Inside(root, area.InvertedShadowCube) || !Inside(root, area.ShadowRendererToDraw))
        )
            return "Select the whole area light, including its shadow geometry.";
        if (component is EFT.Impostors.AmplifyImpostorsArrayElement element)
        {
            var lod = element.GetComponent<LODGroup>() ?? element.GetComponentInParent<LODGroup>();
            if (!Inside(root, lod))
                return "Select the whole tree so its impostor and LOD group move together.";
        }
        if (component is VolumetricLight volume && !Inside(root, volume.Light))
            return "Select the whole light, including its volumetric effect.";
        if (component is TreeInteractive treeTrigger && treeTrigger.GetComponentsInChildren<Collider>(true).Length == 0)
            return "Select a tree interaction with its own collision geometry.";
        if (component is LampController lamp)
        {
            if (
                !AllInside(root, lamp.Lights)
                || !AllInside(root, lamp.AreaAndTubeLights)
                || !AllInside(root, lamp.CustomLights)
                || !AllInside(root, lamp.MultiFlareLights)
                || !ObjectsInside(root, lamp.OnObjects)
                || !ObjectsInside(root, lamp.OffObjects)
                || !ObjectsInside(root, lamp.DestroyedObjects)
                || !Inside(root, lamp.AudioSource)
                || !Inside(root, lamp.BallisticCollider)
                || !Inside(root, lamp.SparksEmmiterTransform)
                || !Inside(root, lamp.DestroyEffectPivot)
            )
                return "Select the whole lamp assembly, including its lights, effects and switched objects.";
            if (lamp._materialsWithEmission != null)
                foreach (var material in lamp._materialsWithEmission)
                    if (material != null && !Inside(root, material.Renderer))
                        return "This lamp controls a renderer outside the selected prop; select their common parent.";
            if (lamp._legacyMaterialsWithEmission != null)
                foreach (var material in lamp._legacyMaterialsWithEmission)
                    if (material != null && !Inside(root, material.Renderer))
                        return "This lamp controls a renderer outside the selected prop; select their common parent.";
        }
        if (
            component is AmplifyImpostor impostor
            && (
                !Inside(root, impostor.RootTransform)
                || !Inside(root, impostor.LodGroup)
                || !AllInside(root, impostor.Renderers)
                || (impostor.m_lastImpostor && !impostor.m_lastImpostor.transform.IsChildOf(root))
            )
        )
            return "Select the whole tree, including its LOD renderers and impostor.";
        if (
            component is CullingLightObject light
            && (
                !Inside(root, light.GetTransform())
                || !Inside(root, light._light)
                || !AllInside(root, light._componentsToTurnOff)
                || !ObjectsInside(root, light._gameObjectsToTurnOff)
            )
        )
            return "This light culls parts outside the selected prop; select their common parent.";
        if (component is WindowBreaker window)
        {
            if (window.IsDamaged || window.HasPieces)
                return "Only intact windows can be relocated; this window already has broken glass pieces.";
            if (
                !window.Renderer
                || !window.GlassBallisticCollider
                || !Inside(root, window.Renderer)
                || !Inside(root, window.GlassBallisticCollider)
                || !Inside(root, window.ObstructiveCollider)
            )
                return "Select the whole window, including its glass renderer and collision geometry.";
            // Native fracture coordinates are local to the WindowBreaker transform.
            var rendererFrame = window.transform.worldToLocalMatrix * window.Renderer.transform.localToWorldMatrix;
            if (!AlignedFrame(rendererFrame))
                return "This window uses a separate renderer coordinate frame and cannot be relocated safely.";
        }
        if (component is TreeInteractivePart tree && tree.InteractivePart && !tree.InteractivePart.transform.IsChildOf(root))
            return "Select the whole tree, including its interactive part.";
        if (component is DoorHandle && component.transform == root)
            return "Select the owning prop so the handle keeps its local animation coordinates.";
        if (component is WorldInteractiveObject interaction)
        {
            // Native animations write local positions/rotations. Move their parent,
            // preserving the hinge/drawer coordinate system and registered identity.
            if (interaction.transform == root)
                return "Select the whole prop, including the parent of its animated interaction.";
            if (string.IsNullOrEmpty(interaction.Id))
                return "This interaction has no stable native identity.";
            if (
                !Inside(root, interaction.LockHandle)
                || !Inside(root, interaction._handle)
                || !Inside(root, interaction.Obstacle)
                || !Inside(root, interaction.Collider)
            )
                return "This interaction has linked parts outside the selected prop; select their common parent.";
            if (interaction.TriggersMap is { Length: > 0 })
                return "This interaction drives map triggers and cannot be relocated as a prop.";
            if (interaction._mboitRenderers != null)
                foreach (var entry in interaction._mboitRenderers)
                    if (entry != null && !Inside(root, entry.renderer))
                        return "This interaction has linked glass outside the selected prop.";
        }
        if (component is LootPoint point && point.GroupPositions != null)
            foreach (var group in point.GroupPositions)
                if (!Inside(root, group))
                    return "Select the whole loot-point group so its spawn positions move together.";
        if (component is NavMeshObstacle obstacle)
        {
            var owner = obstacle.GetComponentInParent<WorldInteractiveObject>();
            if (!owner || !Inside(root, owner) || owner.Obstacle != obstacle)
                return "This navigation obstacle is not owned by an interaction in the selected prop.";
        }
        return "";
    }

    private static bool AlignedFrame(Matrix4x4 frame)
    {
        for (var row = 0; row < 4; row++)
        for (var column = 0; column < 4; column++)
            if (Mathf.Abs(frame[row, column] - (row == column ? 1f : 0f)) > .0001f)
                return false;
        return true;
    }

    internal static void Refresh(WorldInteractiveObject[] interactions)
    {
        if (!WindowsManager.InstanceIsActive())
            return;
        foreach (var interaction in interactions)
            if (interaction && interaction._mboitRenderers != null)
                foreach (var entry in interaction._mboitRenderers)
                    if (entry != null && entry.renderer)
                        WindowsManager.Instance.UpdateInteractableTransform(entry.id, entry.renderer.transform);
    }
}
