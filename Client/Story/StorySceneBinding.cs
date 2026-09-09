using EFT;
using EFT.Ballistics;
using SeasonalPerks.Shared.Story;
using UnityEngine;

namespace SeasonalPerks.Client.Story;

public sealed class StorySceneBinding : MonoBehaviour
{
    private StoryRaidBinding? _binding;
    private BallisticCollider? _ballistic;
    private float _nextTrigger;
    private readonly HashSet<Collider> _overlaps = new();
    private bool _reported;

    internal void Initialize(StoryRaidBinding binding)
    {
        _binding = binding;
        if (binding.Kind == "Shoot")
        {
            _ballistic = GetComponent<BallisticCollider>();
            if (_ballistic != null)
            {
                _ballistic.OnHitAction += Hit;
            }
            else
            {
                Plugin.LogInfo("Story shoot binding has no ballistic collider: " + binding.ObjectPath);
            }
        }
    }

    private bool Eligible()
    {
        var snapshot = StoryClient.Current;
        return _binding != null
            && !Plugin.Busy
            && !StoryPresentationDispatcher.Active
            && !StoryVisitRuntime.Instance.InputBlocked
            && Plugin.SeasonalPlayer
            && Plugin.Player?.HealthController?.IsAlive == true
            && snapshot?.State?.Raid is { Finished: false }
            && (
                !_binding.Once || !snapshot.State.CompletedBindings.Contains(_binding.Id) && !snapshot.State.Raid.Seen.Contains(_binding.Id)
            )
            && StoryRules.Evaluate(_binding.Condition, snapshot.Definition!, snapshot.State, snapshot.Facts!);
    }

    internal void ZoneEnter(Collider other)
    {
        OnTriggerEnter(other);
    }

    internal void ZoneExit(Collider other)
    {
        OnTriggerExit(other);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.GetComponentInParent<Player>() != Plugin.Player)
        {
            return;
        }

        _overlaps.Add(other);
        if (!_reported && Time.realtimeSinceStartup >= _nextTrigger && _binding?.Kind is "Trigger" or "Cinematic" && Eligible())
        {
            _reported = true;
            _nextTrigger = Time.realtimeSinceStartup + .5f;
            StoryRaidRuntime.Instance.Report(_binding, _binding.Kind == "Cinematic" ? "begin" : null);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        _overlaps.Remove(other);
        if (_overlaps.Count == 0)
        {
            _reported = false;
        }
    }

    internal void RetryTrigger()
    {
        _reported = false;
        _nextTrigger = Time.realtimeSinceStartup + 5;
    }

    private void OnTriggerStay(Collider other)
    {
        if (Time.realtimeSinceStartup >= _nextTrigger)
        {
            OnTriggerEnter(other);
        }
    }

    internal bool CanInteract()
    {
        return _binding?.Kind == "Interact" && Eligible();
    }

    internal void Interact()
    {
        if (CanInteract())
        {
            StoryRaidRuntime.Instance.Report(_binding!);
        }
    }

    private void Hit(DamageInfo damage)
    {
        if (Eligible() && damage.Player?.iPlayer?.ProfileId == Plugin.Player?.Profile.Id)
        {
            StoryRaidRuntime.Instance.Report(_binding!);
        }
    }

    private void OnDestroy()
    {
        if (_ballistic != null)
        {
            _ballistic.OnHitAction -= Hit;
        }
    }
}
