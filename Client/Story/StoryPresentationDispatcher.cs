using Cysharp.Threading.Tasks;
using EFT.AnimationSequencePlayer;
using EFT.UI;
using WTT.Campaigns.Shared.Story;
using ZLinq;

namespace WTT.Campaigns.Client.Story;

internal static class StoryPresentationDispatcher
{
    private static readonly SemaphoreSlim Gate = new(1);
    internal static bool Active => Gate.CurrentCount == 0;

    internal static async UniTask Dispatch(StoryResponse response, SequenceReader? reader = null)
    {
        var inRaid = Plugin.InRaid;
        var raid = response.State?.Raid?.Id;
        await Gate.WaitAsync();
        try
        {
            for (var step = 0; step < 64; step++)
            {
                if (
                    !StoryClient.Available
                    || Plugin.Current!.EffectiveProfileId != response.CharacterId
                    || Plugin.InRaid != inRaid
                    || inRaid && (Plugin.Player?.HealthController?.IsAlive != true || StoryClient.Current?.State?.Raid?.Id != raid)
                )
                {
                    return;
                }

                if (response.EventMediaId.Length > 0)
                {
                    if (await StoryCinematicRuntime.Instance.Play(response.EventMediaId) == "interrupt")
                    {
                        return;
                    }
                }
                if (response.CinematicBindingId.Length > 0)
                {
                    var action = response.Presentation.AsValueEnumerable().Single(a => a.Type == StoryActionType.StartCinematic);
                    var result = await StoryCinematicRuntime.Instance.Play(action.Target);
                    var binding = response.Definition!.RaidBindings.AsValueEnumerable().Single(b => b.Id == response.CinematicBindingId);
                    var scene = binding.ObjectPath.Split(new[] { ":/" }, StringSplitOptions.None)[0];
                    if (
                        !StoryClient.Available
                        || Plugin.Current!.EffectiveProfileId != response.CharacterId
                        || Plugin.InRaid != inRaid
                        || inRaid && StoryClient.Current?.State?.Raid?.Id != raid
                    )
                    {
                        return;
                    }

                    response = await StoryClient.Mutate("raid", binding.Id, result, scene: scene);
                    if (result == "interrupt")
                    {
                        return;
                    }

                    continue;
                }
                var visit = StoryVisitRuntime.Instance;
                if (response.Lines.Count > 0 || response.State?.Conversation is { Closed: false })
                {
                    if (!await visit.PresentResponse(response, reader))
                    {
                        return;
                    }
                }

                foreach (var action in response.Presentation)
                {
                    if (
                        !StoryClient.Available
                        || Plugin.Current!.EffectiveProfileId != response.CharacterId
                        || Plugin.InRaid != inRaid
                        || inRaid && Plugin.Player?.HealthController?.IsAlive != true
                    )
                    {
                        return;
                    }

                    if (action.Type == StoryActionType.StartCinematic)
                    {
                        await visit.CloseForPresentation();
                        if (await StoryCinematicRuntime.Instance.Play(action.Target) == "interrupt")
                        {
                            return;
                        }
                    }
                    else if (
                        action.Type
                        is StoryActionType.TradingScreenAction
                            or StoryActionType.QuestsScreenAction
                            or StoryActionType.SelectSubService
                    )
                    {
                        await visit.Navigate(
                            action.Type == StoryActionType.TradingScreenAction ? TraderScreensGroup.ETraderMode.Trade
                            : action.Type == StoryActionType.QuestsScreenAction ? TraderScreensGroup.ETraderMode.Tasks
                            : TraderScreensGroup.ETraderMode.Services
                        );
                    }
                }
                visit.FinishPresentation(response);
                return;
            }
            throw new InvalidOperationException("The cinematic chain exceeded the story playback limit.");
        }
        finally
        {
            Gate.Release();
        }
    }
}
