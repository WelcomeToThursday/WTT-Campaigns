using EFT.UI;
using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.Client.Story;

internal sealed class StoryTaskTabs
{
    public RectTransform Root { get; }
    private readonly List<AnimatedToggle> _toggles = new();

    public StoryTaskTabs(UIAnimatedToggleSpawner native, Action<int> select)
    {
        var original = (RectTransform)native.transform;
        Root = UiElements.Rect("Story task categories", original.parent, 0, original.rect.height);
        Root.SetSiblingIndex(original.GetSiblingIndex());
        Root.gameObject.SetActive(false);
        var layout = Root.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        var group = Root.gameObject.AddComponent<ToggleGroup>();
        group.allowSwitchOff = false;
        foreach (var caption in new[] { "STORY", "SIDE", "OPERATIONAL" })
        {
            var index = _toggles.Count;
            // Use the native prefab, not the spawned instance with TasksScreen listeners.
            var toggle = UnityEngine.Object.Instantiate(native._object, Root, false);
            toggle.name = caption;
            var spawnable = toggle.GetComponent<UISpawnableToggle>();
            spawnable.Init(group);
            spawnable.InitSpawnableButton(caption, native._headerFontSize, null, null);
            toggle.SetIsOnWithoutNotify(index == 0);
            toggle.onValueChanged.AddListener(on =>
            {
                if (on)
                    select(index);
            });
            _toggles.Add(toggle);
        }
    }

    public void Select(int index)
    {
        for (var i = 0; i < _toggles.Count; i++)
            _toggles[i].ToggleSilent(i == index);
    }
}
