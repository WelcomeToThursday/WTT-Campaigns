using EFT.Interactive;
using UnityEngine;
using UnityEngine.AI;

namespace WTT.Campaigns.Client.Authoring.Scenes;

internal static class ScenePropMovement
{
    private static bool Inside(Transform root, Component? part) => !part || part!.transform.IsChildOf(root);

    internal static string Restriction(Transform root, Component component)
    {
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
            if (!Inside(root, interaction.LockHandle) || !Inside(root, interaction._handle)
                || !Inside(root, interaction.Obstacle) || !Inside(root, interaction.Collider))
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
