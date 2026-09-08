using EFT;
using EFT.InputSystem;
using SeasonalPerks.Shared.Story;
using UnityEngine;

namespace SeasonalPerks.Client.Story;

public sealed class StoryRaidRuntime : MonoBehaviour
{
    internal static StoryRaidRuntime Instance = null!;
    private Player? _player;
    private bool _loading;
    private readonly List<StorySceneBinding> _bindings = new();
    private readonly HashSet<string> _pending = new();
    private readonly Dictionary<string, (Player Player, string Template)> _earlyPickups = new();
    private StoryInteractionPrompt? _prompt;
    private bool _interactionCaptured;

    private void Awake()
    {
        Instance = this;
    }

    private void Update()
    {
        var player = StoryClient.Available && Plugin.SeasonalPlayer ? Plugin.Player : null;
        var focused = player ? FocusedInteraction() : null;
        if (focused)
        {
            _prompt ??= new StoryInteractionPrompt();
        }
        _prompt?.Show(focused);
        if (player == _player || _loading)
        {
            return;
        }
        Clear();
        _player = player;
        foreach (var id in _earlyPickups.Where(p => p.Value.Player != player).Select(p => p.Key).ToArray())
        {
            _earlyPickups.Remove(id);
        }
        if (player != null)
        {
            Bind(player);
        }
    }

    private StorySceneBinding? FocusedInteraction()
    {
        if (Plugin.Busy || Cursor.visible || !Plugin.SeasonalPlayer || Plugin.Player?.HealthController?.IsAlive != true)
        {
            return null;
        }
        var camera = Camera.main;
        if (!camera || !Physics.Raycast(camera!.transform.position, camera.transform.forward, out var hit, 2.5f))
        {
            return null;
        }
        return _bindings.FirstOrDefault(b =>
            b && b.CanInteract() && (hit.transform == b.transform || hit.transform.IsChildOf(b.transform))
        );
    }

    internal void ConsumeInteraction(List<ECommand> commands)
    {
        if (_interactionCaptured && commands.Remove(ECommand.EndInteracting))
        {
            _interactionCaptured = false;
        }
        if (!commands.Contains(ECommand.BeginInteracting))
        {
            return;
        }
        var focused = FocusedInteraction();
        if (!focused)
        {
            return;
        }
        commands.Remove(ECommand.BeginInteracting);
        _interactionCaptured = !commands.Remove(ECommand.EndInteracting);
        focused!.Interact();
    }

    internal void ClearCapturedInteraction()
    {
        _interactionCaptured = false;
    }

    private async void Bind(Player player)
    {
        _loading = true;
        try
        {
            var snapshot = await StoryClient.Load();
            if (!player || player != Plugin.Player || snapshot.State?.Raid is not { Finished: false } raid)
            {
                return;
            }
            var transforms = Resources
                .FindObjectsOfTypeAll<Transform>()
                .Where(t => t.gameObject.scene.IsValid())
                .GroupBy(ObjectPath)
                .ToDictionary(g => g.Key, g => g.ToArray());
            foreach (var binding in snapshot.Definition!.RaidBindings.Where(b => b.Location == raid.Location && b.Kind != "Collectible"))
            {
                if (!transforms.TryGetValue(binding.ObjectPath, out var matches) || matches.Length != 1)
                {
                    Plugin.LogInfo("Story interaction requires one exact scene object: " + binding.ObjectPath);
                    continue;
                }
                var component = matches[0].gameObject.AddComponent<StorySceneBinding>();
                component.Initialize(binding);
                _bindings.Add(component);
            }
            foreach (var pickup in _earlyPickups.Where(p => p.Value.Player == player).ToArray())
            {
                _earlyPickups.Remove(pickup.Key);
                CollectReady(pickup.Key, pickup.Value.Template);
            }
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
        }
        finally
        {
            _loading = false;
        }
    }

    internal static string ObjectPath(Transform transform)
    {
        var names = new Stack<string>();
        for (var current = transform; current != null; current = current.parent)
        {
            names.Push(current.name);
        }
        return transform.gameObject.scene.name + ":/" + string.Join("/", names);
    }

    internal async void Report(StoryRaidBinding binding, string? kind = null, string itemId = "")
    {
        var pendingKey = binding.Id + ":" + itemId;
        if (!StoryClient.Available || !Plugin.SeasonalPlayer || !_pending.Add(pendingKey))
        {
            return;
        }
        try
        {
            var response = await StoryClient.Mutate("raid", binding.Id, kind ?? binding.Kind, itemId: itemId);
            if (binding.EntryPointId.Length > 0 && response.State?.Conversation is { Closed: false })
            {
                var owner = _bindings.FirstOrDefault(b => b && ObjectPath(b.transform) == binding.ObjectPath);
                var reader = owner ? owner!.GetComponentInParent<EFT.AnimationSequencePlayer.SequenceReader>() : null;
                StoryVisitRuntime.Instance.ShowConversation(response, reader);
            }
            foreach (var action in response.Presentation.Where(a => a.Type == StoryActionType.StartCinematic))
            {
                StoryCinematicRuntime.Instance.Play(action.Target, binding.Id);
            }
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
        }
        finally
        {
            _pending.Remove(pendingKey);
        }
    }

    internal void Collect(string itemId, string templateId)
    {
        if (!StoryClient.Available || !Plugin.SeasonalPlayer || !Plugin.Player)
        {
            return;
        }
        if (_loading || _player != Plugin.Player || StoryClient.Current?.State?.Raid is not { Finished: false })
        {
            _earlyPickups[itemId] = (Plugin.Player!, templateId);
            return;
        }
        CollectReady(itemId, templateId);
    }

    private void CollectReady(string itemId, string templateId)
    {
        if (StoryClient.Current?.State?.Raid is not { Finished: false } raid)
        {
            return;
        }
        foreach (
            var binding in StoryClient.Current.Definition!.RaidBindings.Where(b =>
                b.Location == raid.Location && b.Kind == "Collectible" && b.ItemId == templateId
            )
        )
        {
            if (raid.SpawnedItems.ContainsKey(itemId) && !raid.PickedItems.Contains(itemId))
            {
                Report(binding, itemId: itemId);
            }
        }
    }

    private void Clear()
    {
        foreach (var binding in _bindings.Where(b => b))
        {
            Destroy(binding);
        }
        _bindings.Clear();
        _prompt?.Dispose();
        _prompt = null;
        _interactionCaptured = false;
        _player = null;
    }

    private void OnDestroy()
    {
        Clear();
    }
}
