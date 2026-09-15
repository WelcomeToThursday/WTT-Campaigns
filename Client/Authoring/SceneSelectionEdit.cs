using System;
using Newtonsoft.Json.Linq;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Authoring;

// A selection snapshot never belongs to the draft. Commit a detached changed copy only.
internal static class SceneSelectionEdit
{
    internal static void SetAxis(SpatialCapture point, string field, int axis, float number)
    {
        if (axis < 0 || axis > 2 || float.IsNaN(number) || float.IsInfinity(number) || field == "Size" && number <= 0)
            return;
        var vector =
            field == "Position" ? point.Position
            : field == "Rotation" ? point.Rotation
            : field == "Size" && point is MapVolume volume ? volume.Size
            : field == "Size" && point is MapObjectEdit edit ? edit.Scale
            : null;
        if (vector == null)
            return;
        if (axis == 0)
            vector.X = number;
        else if (axis == 1)
            vector.Y = number;
        else
            vector.Z = number;
        if (field == "Size" && point is MapVolume { Shape: "Sphere" } sphere)
        {
            sphere.Radius = number / 2;
            sphere.Size = new SpatialVector
            {
                X = number,
                Y = number,
                Z = number,
            };
        }
    }

    internal static MapObjectEdit? Prepare(MapObjectEdit selection, Action<SpatialCapture> change)
    {
        var after = SeasonCompiler.Copy(selection);
        change(after);
        return JToken.DeepEquals(JObject.FromObject(selection), JObject.FromObject(after)) ? null : after;
    }
}
