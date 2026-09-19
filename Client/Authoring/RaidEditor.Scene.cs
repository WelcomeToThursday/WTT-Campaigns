using System.Diagnostics;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using WTT.Campaigns.Client.Authoring.Scenes;

namespace WTT.Campaigns.Client.Authoring;

public sealed partial class RaidEditor
{
    private void RefreshLoadedScenes()
    {
        if (_session == null)
            return;
        var scenes = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (scene.isLoaded)
                scenes.Add(scene.name);
        }
        // EFT pools raid geometry in the persistent scene, which SceneManager's
        // ordinary scene enumeration omits. Include it only from a live raid object.
        if (_player && _player!.gameObject.scene.IsValid() && _player.gameObject.scene.isLoaded)
            scenes.Add(_player.gameObject.scene.name);
        if (gameObject.scene.IsValid() && gameObject.scene.isLoaded)
            scenes.Add(gameObject.scene.name);
        _session.Scenes = new List<string>(scenes);
    }

    private readonly SceneObjectIndex<Transform> _sceneIndex = new(ScenePath, t => t);
    private IEnumerator<Transform>? _sceneWalk;
    private readonly Stopwatch _sceneClock = new();
    private bool _sceneStarted;
    private string SceneIndexStatus =>
        !_sceneIndex.Complete ? "Indexing scenery… Picking remains available."
        : _sceneIndex.Limited ? "Scene index limit reached. Scene bindings require a complete index; narrow map edits remain available."
        : "";

    private static string? ScenePath(Transform target)
    {
        var parts = new List<string>(16);
        var length = 0;
        for (var current = target; current; current = current.parent)
        {
            var name = current.name;
            length += name.Length + 1;
            if (parts.Count >= 128 || length > SceneObjectIndex<Transform>.MaxPathLength - 256)
                return null;
            parts.Add(name);
        }
        var scene = target.gameObject.scene.name;
        if (length + scene.Length + 2 > SceneObjectIndex<Transform>.MaxPathLength)
            return null;
        var path = new StringBuilder(length + scene.Length + 2).Append(scene).Append(":/");
        for (var i = parts.Count - 1; i >= 0; i--)
        {
            if (i < parts.Count - 1)
                path.Append('/');
            path.Append(parts[i]);
        }
        return path.ToString();
    }

    private IEnumerable<Transform> WalkScene()
    {
        // EFT can pool native containers outside the ordinary map scenes. Index
        // them first so the scenery limit cannot hide the map's loot containers.
        var containers = new HashSet<Transform>();
        foreach (var container in Resources.FindObjectsOfTypeAll<EFT.Interactive.LootableContainer>())
        {
            if (
                !container
                || !container.gameObject.scene.IsValid()
                || !container.gameObject.scene.isLoaded
                || string.IsNullOrEmpty(container.Id)
            )
                continue;
            var excluded = false;
            for (var parent = container.transform; parent; parent = parent.parent)
                if (ExcludedSceneBranch(parent.gameObject))
                {
                    excluded = true;
                    break;
                }
            if (excluded)
                continue;
            containers.Add(container.transform);
            yield return container.transform;
        }

        var scenes = new HashSet<Scene>();
        for (var i = 0; i < SceneManager.sceneCount; i++)
            scenes.Add(SceneManager.GetSceneAt(i));
        // Keep discovery consistent with the scenes exposed by RefreshLoadedScenes.
        if (_player)
            scenes.Add(_player!.gameObject.scene);
        scenes.Add(gameObject.scene);
        var roots = new List<GameObject>();
        foreach (var scene in scenes)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                continue;
            roots.Clear();
            scene.GetRootGameObjects(roots);
            foreach (var root in roots)
            {
                if (!root)
                    continue;
                var stack = new Stack<(Transform Node, int Next)>();
                stack.Push((root.transform, -1));
                while (stack.Count > 0)
                {
                    var frame = stack.Pop();
                    if (!frame.Node)
                        continue;
                    if (frame.Next == -1)
                    {
                        var go = frame.Node.gameObject;
                        if (ExcludedSceneBranch(go))
                            continue;
                        if (!containers.Contains(frame.Node))
                            yield return frame.Node;
                        frame.Next = 0;
                    }
                    if (frame.Next >= frame.Node.childCount)
                        continue;
                    var child = frame.Node.GetChild(frame.Next++);
                    stack.Push(frame);
                    if (stack.Count >= 128)
                        throw new InvalidOperationException("Scene hierarchy exceeds the editor depth limit.");
                    stack.Push((child, -1));
                }
            }
        }
    }

    private static bool ExcludedSceneBranch(GameObject go) =>
        go.GetComponent<Canvas>()
        || go.GetComponent<EFT.Player>()
        || go.name.StartsWith("CampaignEditor", StringComparison.Ordinal)
        || go.name.StartsWith("SeasonalRaidEditor", StringComparison.Ordinal);

    private void IndexScene()
    {
        if (_sceneStarted)
            return;
        _sceneStarted = true;
        _sceneWalk = WalkScene().GetEnumerator();
    }

    private void AdvanceSceneIndex()
    {
        using var diagnostic = EditorDiagnostics.Measure(EditorDiagnostics.Area.Index);
        if (_sceneWalk == null)
            return;
        _sceneClock.Restart();
        try
        {
            for (var count = 0; count < 128 && _sceneClock.Elapsed.TotalMilliseconds < 2; count++)
            {
                if (!_sceneWalk.MoveNext())
                {
                    _sceneIndex.Complete = true;
                    FinishSceneIndex();
                    break;
                }
                var node = _sceneWalk.Current;
                if (node)
                {
                    var renderer = node.GetComponent<Renderer>();
                    if (renderer && renderer is MeshRenderer or SkinnedMeshRenderer)
                        _sceneRenderers.Add(renderer);
                    Catalog.DiscoverSceneNode(node);
                }
                if (node && !_sceneIndex.Add(node, node.name))
                {
                    _sceneIndex.Limit();
                    FinishSceneIndex();
                    break;
                }
            }
        }
        catch (Exception e)
        {
            _sceneIndex.Limit();
            FinishSceneIndex();
            _notice = "Scene indexing stopped: " + e.Message;
            Plugin.Error(e);
        }
    }

    private void FinishSceneIndex()
    {
        if (_sceneWalk != null && _sceneIndex.Complete)
        {
            Plugin.LogInfo(
                $"Raid editor scene index: {_sceneIndex.Count} objects, {_sceneIndex.RetainedCharacters} cached characters, limited={_sceneIndex.Limited}."
            );
            if (_sceneIndex.Limited)
                _notice = SceneIndexStatus;
        }
        _sceneWalk?.Dispose();
        _sceneWalk = null;
    }

    private void ClearSceneIndex()
    {
        FinishSceneIndex();
        Catalog.Reset();
        _aiController?.Reset();
        _mapController?.Reset();
        _sceneIndex.Clear();
        _sceneStarted = false;
        _picked = null;
    }

    private string PickedPath => _picked ? _sceneIndex.Path(_picked!) ?? "Scene path exceeds the editor limit." : "";
}
