using EFT.UI;
using UnityEngine;

namespace WTT.Campaigns.Client.Story;

internal sealed class StoryQuestListHost : MonoBehaviour
{
    private QuestsListView? _list;
    private int _generation;

    internal static bool Allows(string questId) =>
        !StoryClient.Available
        || StoryQuestVisibility.Allows(StoryClient.Current, Plugin.Current!.EffectiveProfileId, Plugin.Current.SeasonId, questId);

    internal async void Open(QuestsListView list)
    {
        Clear();
        if (!StoryClient.Available)
            return;
        _list = list;
        StoryClient.Changed += Refresh;
        var generation = _generation;
        try
        {
            await StoryClient.Load();
            if (this && generation == _generation)
                Refresh();
        }
        catch (Exception exception)
        {
            Plugin.LogInfo("Trader story quest membership could not load: " + exception.Message);
        }
    }

    private void Refresh()
    {
        if (_list && _list!.isActiveAndEnabled)
            _list.UpdateVisibility();
    }

    private void Clear()
    {
        ++_generation;
        StoryClient.Changed -= Refresh;
        _list = null;
    }

    private void OnDisable() => Clear();
}
