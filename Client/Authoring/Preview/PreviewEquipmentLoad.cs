using System.Collections;
using System.Diagnostics;
using Comfort.Common;
using Cysharp.Threading.Tasks;
using Diz.DependencyManager;
using Diz.Jobs;
using Diz.Resources;
using EFT;
using UnityEngine;

namespace WTT.Campaigns.Client.Authoring.Preview;

// The watchdog uses the host's coroutine loop, so a missing UniTask runner cannot also disable the deadline.
internal sealed class PreviewEquipmentLoad : IDisposable
{
    private readonly JobScheduler _scheduler;
    private readonly bool _restorePriority;
    private readonly List<IEasyBundle> _bundles = new();
    private readonly Stopwatch _elapsed = new();
    private Coroutine? _watchdog;
    private CancellationTokenRegistration _registration;

    internal PreviewEquipmentLoad(ObjectsFactory factory, IEnumerable<ResourceKey> resources)
    {
        var pending = new Stack<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var resource in resources)
            pending.Push(resource.path);
        while (pending.Count > 0)
        {
            var key = pending.Pop();
            if (!seen.Add(key))
                continue;
            if (!factory.EasyAssets.System.Nodes.TryGetValue(key, out var node))
                throw new InvalidOperationException("Playtest equipment dependency is not installed: " + key);
            _bundles.Add(node.Data);
            foreach (var dependency in node.Data.DependencyKeys)
                pending.Push(dependency);
        }
        _scheduler = Singleton<JobScheduler>.Instance;
        if (!_scheduler || !_scheduler.isActiveAndEnabled)
            throw new InvalidOperationException("The native equipment job scheduler is not active.");
        _restorePriority = !_scheduler.IsForceModeEnabled;
        if (_restorePriority)
            _scheduler.SetForceMode(true);
        Plugin.LogInfo("Playtest loading: native priority enabled; UniTask loop=" + PlayerLoopHelper.IsInjectedUniTaskPlayerLoop());
    }

    internal Task Deadline(CancellationToken token)
    {
        var completion = new TaskCompletionSource<bool>();
        _registration = token.Register(() => completion.TrySetCanceled(token));
        _elapsed.Restart();
        _watchdog = Plugin.Instance.StartCoroutine(Watch(completion, token));
        return completion.Task;
    }

    private IEnumerator Watch(TaskCompletionSource<bool> completion, CancellationToken token)
    {
        // StartCoroutine runs immediately; let the native request start before judging bundle states from a previous attempt.
        yield return null;
        var nextReport = 10d;
        while (!token.IsCancellationRequested)
        {
            if (_elapsed.Elapsed.TotalSeconds >= 45)
            {
                completion.TrySetResult(true);
                yield break;
            }
            foreach (var bundle in _bundles)
            {
                if (bundle.LoadState.Value == ELoadState.Failed || bundle is EasyBundle { _loadingJob.IsFaulted: true })
                {
                    var detail = (bundle as EasyBundle)?._loadingJob?.Exception?.GetBaseException().Message;
                    completion.TrySetException(new InvalidOperationException("Equipment bundle failed: " + bundle.Key + ". " + detail));
                    yield break;
                }
            }
            if (_elapsed.Elapsed.TotalSeconds >= nextReport)
            {
                Plugin.LogInfo("Playtest loading pending: " + DescribePending());
                nextReport += 10;
            }
            yield return null;
        }
    }

    internal string DescribePending()
    {
        var details = new List<string>();
        foreach (var bundle in _bundles)
        {
            if (bundle.LoadState.Value == ELoadState.Loaded)
                continue;
            details.Add(bundle.Key + " [" + bundle.LoadState.Value + ", " + (bundle.Progress * 100).ToString("0") + "%]");
            if (details.Count == 6)
                break;
        }
        return (details.Count == 0 ? "bundle loading complete; waiting for native pools" : string.Join("; ", details))
            + "; queued jobs="
            + JobScheduler.QueueLength;
    }

    public void Dispose()
    {
        _registration.Dispose();
        if (_watchdog != null && Plugin.Instance)
            Plugin.Instance.StopCoroutine(_watchdog);
        if (_restorePriority && _scheduler)
            _scheduler.SetForceMode(false);
    }
}
