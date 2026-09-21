using EFT.Quests;
using EFT.UI;
using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.Client.UI;
using WTT.Campaigns.Shared.Missions;

namespace WTT.Campaigns.Client.Story;

public sealed class StoryChapterNotificationView : BaseNotificationView
{
    private static AssetBundle? _bundle;
    private Sprite? _artwork;
    private readonly CancellationTokenSource _imageCancellation = new();
    public override bool ReturnToPool => false;

    internal static StoryChapterNotificationView Create(NotifierView notifier, StoryChapterNotification notification)
    {
        // Unity resolves the prefab's shared sprites through the already loaded main UI bundle.
        _ = SeasonUi.Instance.UiBundle;
        _bundle ??=
            AssetBundle.LoadFromFile(Path.Combine(Plugin.Folder, "wtt_campaigns_story_notifications.bundle"))
            ?? throw new InvalidDataException("Missing Campaign story notification bundle.");
        var asset =
            "assets/mods/wtt-campaigns.assets/storynotifications/seasonalchapter" + notification.Status.ToLowerInvariant() + ".prefab";
        var prefab =
            _bundle.LoadAsset<GameObject>(asset) ?? throw new InvalidDataException("Missing chapter notification prefab: " + asset);
        var root = Instantiate(prefab, notifier._container, false);
        var view = root.AddComponent<StoryChapterNotificationView>();
        view._icon = root.transform.Find("Content/Left/Icon").GetComponent<Image>();
        view._text = root.transform.Find("Content/Text group/Text/Text").GetComponent<TMPro.TMP_Text>();
        var title = root.transform.Find("Content/Text group/Title").GetComponent<TMPro.TMP_Text>();
        title.text = notification.Title;
        view._text.text = notification.Description;
        view._layout = root.GetComponent<LayoutElement>();
        view._canvasGroup = root.GetComponent<CanvasGroup>();
        view._container = (RectTransform)root.transform.Find("Content");
        view._background = root.transform.Find("Content/bg").GetComponent<Image>();
        view._animator = root.GetComponent<Animator>();
        view._animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        var fallback = view._icon.sprite;
        notifier.SetupNotificationView(view);
        view._text.ForceMeshUpdate();
        view._text.GetComponent<LayoutElement>().preferredWidth = Mathf.Min(395, view._text.GetPreferredValues().x);
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)root.transform);
        view.Init(notification);
        view._icon.sprite = fallback;
        if (MissionNotificationIcons.Choices.ContainsKey(notification.MissionIcon))
        {
            var icon = (EQuestIconType)Enum.Parse(typeof(EQuestIconType), notification.MissionIcon.Substring("quest:".Length));
            view._icon.sprite = EFTHardSettings.Instance.StaticIcons.QuestIconTypeSprites[icon];
            view._icon.gameObject.SetActive(true);
            view._icon.enabled = true;
            view._icon.color = Color.white;
            view._icon.preserveAspect = true;
        }
        var sound = root.GetComponent<AudioSource>();
        StoryAudio.Configure(sound);
        sound.Play();
        view.LoadArtwork(notification.Artwork);
        return view;
    }

    private async void LoadArtwork(string id)
    {
        if (id.Length == 0)
        {
            return;
        }
        try
        {
            var texture = await SeasonImageLoader.LoadAsync(SeasonImageLoader.PathFor("hub-images", id), _imageCancellation.Token);
            if (!this || _imageCancellation.IsCancellationRequested)
            {
                Destroy(texture);
                return;
            }
            _artwork = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f));
            _icon.sprite = _artwork;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            Plugin.LogInfo("Story chapter artwork unavailable: " + exception.Message);
        }
    }

    private void OnDestroy()
    {
        _imageCancellation.Cancel();
        _imageCancellation.Dispose();
        if (_artwork)
        {
            Destroy(_artwork!.texture);
            Destroy(_artwork);
        }
    }
}
