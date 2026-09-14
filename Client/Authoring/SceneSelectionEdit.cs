using System;
using Newtonsoft.Json.Linq;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Authoring;

// A selection snapshot never belongs to the draft. Commit a detached changed copy only.
internal static class SceneSelectionEdit
{
    internal static MapObjectEdit? Prepare(MapObjectEdit selection, Action<SpatialCapture> change)
    {
        var after = SeasonCompiler.Copy(selection);
        change(after);
        return JToken.DeepEquals(JObject.FromObject(selection), JObject.FromObject(after)) ? null : after;
    }
}
