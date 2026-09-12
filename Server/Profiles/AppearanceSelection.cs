using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace WTT.Campaigns.Server.Profiles;

public static class AppearanceSelection
{
    public const string HeadParent = "5cc085e214c02e000c6bea67";
    public const string VoiceParent = "5fc100cf95572123ae738483";

    public static async Task Apply(
        Dictionary<MongoId, CustomizationItem> templates,
        string side,
        Customization customization,
        string headId,
        string voiceId,
        Func<Task> persist
    )
    {
        var head = Validate(templates, side, HeadParent, headId);
        var voice = Validate(templates, side, VoiceParent, voiceId);
        var oldHead = customization.Head;
        var oldVoice = customization.Voice;
        try
        {
            customization.Head = head;
            customization.Voice = voice;
            await persist();
        }
        catch
        {
            customization.Head = oldHead;
            customization.Voice = oldVoice;
            // SPT caches the serialized hash before writing. Re-save the rollback so a retry
            // cannot mistake a failed write for an already persisted appearance.
            try
            {
                await persist();
            }
            catch
            { /* Preserve the original save failure and the restored in-memory appearance. */
            }
            throw;
        }
    }

    public static MongoId Validate(Dictionary<MongoId, CustomizationItem> templates, string side, string parent, string requested)
    {
        if (
            !MongoId.IsValidMongoId(requested)
            || !templates.TryGetValue(new MongoId(requested), out var item)
            || item.Parent != parent
            || item.Properties?.AvailableAsDefault != true
            || item.Properties.Side?.Contains(side) != true
        )
        {
            throw new InvalidOperationException("The selected head or voice is unavailable for this faction.");
        }
        return item.Id;
    }
}
