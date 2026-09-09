using EFT.Communications;
using EFT.UI;
using SeasonalPerks.Shared.Story;
using UnityEngine;

namespace SeasonalPerks.Client.Story;

// Backport live's dedicated chapter view into 4.1's notification manager.
internal sealed class StoryChapterNotification : Notification
{
    private readonly string _description;
    internal readonly string Status;
    internal readonly string Title;
    internal readonly string Artwork;

    internal StoryChapterNotification(StoryChapter chapter, string status)
    {
        Status = status;
        Title = Plugin.Localized(chapter.Id + " name", chapter.Name);
        Artwork = chapter.Icon.Length > 0 ? chapter.Icon : chapter.Image;
        _description = Plugin.Localized(
            "Quest/MainQuest/Notification/Chapter" + (status == "Complete" ? "Success" : status),
            status switch
            {
                "Complete" => "Chapter complete",
                "Failed" => "Chapter failed",
                _ => "Chapter started",
            }
        );
        Duration = ENotificationDurationType.Long;
        // The bundle contains the actual chapter clips; 4.1's sound enum lacks these entries.
        SoundType = null;
    }

    public override string Description => _description;
    public override ENotificationIconType Icon => ENotificationIconType.Quest;
    public override Color? TextColor => new Color32(182, 229, 243, 255);
    public override Color? BackgroundColor => Color.white;

    public override BaseNotificationView CreateView(INotificationViewFactory viewFactory)
    {
        return StoryChapterNotificationView.Create((NotifierView)viewFactory, this);
    }
}
