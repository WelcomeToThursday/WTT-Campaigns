using System.Globalization;
using EFT.Interactive;
using UnityEngine;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring.Scenes;

internal sealed partial class MapSceneAdapter
{
    private readonly Dictionary<string, Transform> _doorSources = new();

    private static MapTarget CaptureDoor(Transform target) =>
        new()
        {
            Kind = "Door",
            Scene = target.gameObject.scene.name,
            Path = PathOf(target),
            NativeId = target.GetComponent<Door>().Id,
            Fingerprint = DoorFingerprints(target, legacy: false)[0],
        };

    private static bool MatchesDoorFingerprint(Transform target, string saved) => DoorFingerprints(target, legacy: true).Contains(saved);

    private static List<string> DoorFingerprints(Transform target, bool legacy)
    {
        var door = target.GetComponent<Door>();
        var components = target.GetComponentsInChildren<Component>(true);
        string Name(Component c) => c ? c.GetType().FullName + ":" + c.name : "missing";
        bool Diagnostic(Component c) => c && c.GetType().FullName == SceneDoorFingerprint.DiagnosticType;
        var actual = components.AsValueEnumerable().Select(Name).JoinToString("|");
        var clean = components.AsValueEnumerable().Where(c => !Diagnostic(c)).Select(Name).JoinToString("|");
        var rootCount = target.GetComponents<Component>().AsValueEnumerable().Count(c => !Diagnostic(c));
        var meshes = target
            .GetComponentsInChildren<MeshFilter>(true)
            .AsValueEnumerable()
            .Select(f => f.sharedMesh ? f.sharedMesh.name + ":" + f.sharedMesh.vertexCount : "missing")
            .JoinToString("|");
        var hashes = new List<string>();
        var localRotation = target.localRotation;
        var hinge =
            door.DoorAxis
            + "|"
            + door.CloseAngle.ToString("R", CultureInfo.InvariantCulture)
            + "|"
            + door.OpenAngle.ToString("R", CultureInfo.InvariantCulture);
        try
        {
            if (legacy)
                AddLegacy();
            // Evaluate the native closed pose without invoking CurrentAngle,
            // door synchronization, animation, occlusion, or gameplay callbacks.
            // Restore the exact local quaternion before leaving this synchronous call.
            target.rotation = door.GetDoorRotation(door.CloseAngle) * target.parent.rotation;
            hashes.Add(
                SceneDoorFingerprint.Stable(
                    clean,
                    target.position.ToString("R"),
                    target.rotation.ToString("R"),
                    target.lossyScale.ToString("R"),
                    meshes,
                    hinge
                )
            );
            if (legacy)
            {
                AddLegacy();
                target.rotation = door.GetDoorRotation(door.OpenAngle) * target.parent.rotation;
                AddLegacy();
            }
        }
        finally
        {
            target.localRotation = localRotation;
        }
        return hashes;

        void AddLegacy()
        {
            foreach (var shape in SceneDoorFingerprint.LegacyComponents(actual, clean, rootCount, target.name))
                hashes.Add(
                    SceneDoorFingerprint.Legacy(
                        shape,
                        target.position.ToString("R"),
                        target.rotation.ToString("R"),
                        target.lossyScale.ToString("R"),
                        meshes
                    )
                );
        }
    }

    private void ResolveDoorSources(MapLayout layout)
    {
        var required = new HashSet<string>();
        // Resolve every source before changing any door state or creating copies.
        // A map door may also be the source for one or more placed doors.
        foreach (var edit in layout.Doors)
        {
            var key = Key(edit.Target);
            required.Add(key);
            if (_doorSources.TryGetValue(key, out var source) && source)
                continue;
            try
            {
                _doorSources[key] = Resolve(edit.Target, true);
            }
            catch (Exception exception)
            {
                TargetErrors.Add(edit.Name + ": " + exception.Message);
            }
        }
        foreach (var key in _doorSources.Keys.AsValueEnumerable().Where(k => !required.Contains(k)).ToArray())
            _doorSources.Remove(key);
    }
}
