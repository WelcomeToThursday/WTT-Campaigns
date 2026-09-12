using Newtonsoft.Json.Linq;

namespace WTT.Campaigns.Server.Progression;

internal static class QuestBackportCompatibility
{
    // These are the stages handled by both the installed SPT models and the beta client.
    internal static readonly string[] ConditionStages = ["AvailableForStart", "AvailableForFinish", "Fail"];
    internal static readonly string[] RewardStages = ["Started", "Success", "Fail"];

    internal static string? Blocker(JObject definition)
    {
        foreach (var (name, supported) in new[] { ("conditions", ConditionStages), ("rewards", RewardStages) })
        {
            if (definition[name] is not JObject stages)
            {
                return "Missing quest stage dictionary: " + name;
            }
            foreach (var stage in stages.Properties())
            {
                if (stage.Value is not JArray entries)
                {
                    return "Quest stage must be an array: " + name + "/" + stage.Name;
                }
                if (!supported.Contains(stage.Name) && entries.Count != 0)
                {
                    return "Unsupported nonempty quest stage: " + name + "/" + stage.Name;
                }
            }
        }
        return null;
    }

    internal static void Normalize(JObject definition)
    {
        if (Blocker(definition) is { } blocker)
        {
            throw new InvalidDataException(blocker);
        }
        foreach (var (name, supported) in new[] { ("conditions", ConditionStages), ("rewards", RewardStages) })
        {
            // Even empty AutoStart arrays crash the client's enum-keyed dictionary reader.
            // Only empty unsupported stages can be omitted without removing quest behavior.
            foreach (var stage in ((JObject)definition[name]!).Properties().Where(p => !supported.Contains(p.Name)).ToArray())
            {
                stage.Remove();
            }
        }
    }
}
