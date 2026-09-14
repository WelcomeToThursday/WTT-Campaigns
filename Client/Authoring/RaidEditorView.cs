using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.Client.UI;
using WTT.Campaigns.UI.Controls;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring;

internal sealed class RaidEditorView : IDisposable
{
    internal readonly GameObject Root;
    internal readonly RaidEditorWindows Windows;
    private readonly Dictionary<string, Transform> _controls;
    private readonly InputField[] _inputs;
    private readonly AssetBundle _bundle;
    private static AssetBundle? _sharedBundle;
    private static int _bundleUsers;
    private bool _disposed;
    internal bool Valid => !_disposed && Root;
    internal Shader PreviewShader => _bundle.LoadAsset<Shader>("assets/mods/wtt-campaigns.assets/raideditor/campaignscenepreview.shader")
        ?? throw new InvalidOperationException("Install the matching scene preview shader bundle.");

    internal RaidEditorView()
    {
        var borrowed = _sharedBundle;
        _bundle = _sharedBundle
            ? _sharedBundle!
            : AssetBundle.LoadFromFile(Path.Combine(Plugin.Folder, "wtt_campaigns_raid_editor.bundle"))
                ?? throw new InvalidOperationException("Install the CJ-SDK raid editor UI bundle.");
        try
        {
            var prefab =
                _bundle.LoadAsset<GameObject>("assets/mods/wtt-campaigns.assets/raideditor/seasonalraideditor.prefab")
                ?? throw new InvalidOperationException("The raid editor prefab is missing.");
            Root = UnityEngine.Object.Instantiate(prefab);
            WTT.Campaigns.UI.Screens.RaidEditorLayout.Prepare(Root);
            _controls = Root.GetComponentsInChildren<Transform>(true)
                .AsValueEnumerable()
                .GroupBy(t => t.name)
                .ToDictionary(g => g.Key, g => g.AsValueEnumerable().First());
            var ui = new UiElements(Root.GetComponentInChildren<Text>().font, sound => SeasonUi.Instance.PlayInterfaceSound(sound));
            foreach (var button in Root.GetComponentsInChildren<Button>(true))
            {
                ui.Feedback(button);
            }

            Windows = Root.AddComponent<RaidEditorWindows>();
            Windows.Initialize();
            EditorLayoutPreferences.Attach(Windows);
            _inputs = Root.GetComponentsInChildren<InputField>(true);

            Root.SetActive(false);
        }
        catch
        {
            if (Root)
            {
                Root!.SetActive(false);
                UnityEngine.Object.Destroy(Root);
            }
            // A failed constructor never registered a shared-bundle user.
            if (!borrowed) _bundle.Unload(true);
            throw;
        }
        _sharedBundle = _bundle;
        _bundleUsers++;
    }

    internal T Get<T>(string name)
        where T : Component
    {
        return _controls[name].GetComponent<T>();
    }

    internal void Visible(string name, bool visible)
    {
        var go = _controls[name].gameObject;
        if (go.activeSelf != visible) go.SetActive(visible);
    }

    internal void Text(string name, string value)
    {
        var label = Get<Text>(name);
        if (label.text != value) label.text = value;
    }

    internal void Caption(string name, string value)
    {
        var label = Get<Button>(name).GetComponentInChildren<Text>(true);
        if (label.text != value) label.text = value;
    }

    internal void Highlight(string name, bool selected)
    {
        var graphic = Get<Button>(name).targetGraphic;
        var color = selected ? new Color(.36f, .33f, .23f) : new Color(.18f, .18f, .15f);
        if (graphic.color != color) graphic.color = color;
    }

    internal void Button(string name, Action action)
    {
        Get<Button>(name).onClick.AddListener(() => action());
    }

    internal void Input(string name, Action<string> action)
    {
        Get<InputField>(name).onEndEdit.AddListener(value => action(value));
    }

    internal void Value(string name, string value)
    {
        var field = Get<InputField>(name);
        if (!field.isFocused && field.text != value)
        {
            field.SetTextWithoutNotify(value);
        }
    }

    internal bool Typing
    {
        get
        {
            if (!Valid) return false;
            if (Windows.Interacting) return true;
            foreach (var input in _inputs)
                if (input && input.gameObject.activeInHierarchy && input.isFocused) return true;
            return false;
        }
    }

    internal void Conflict(RaidEditorSession session)
    {
        var conflict = session.Conflict;
        _controls["ConflictShield"].gameObject.SetActive(conflict != null);
        Root.GetComponent<RaidEditorWindows>().KeepModalOnTop();
        if (conflict == null)
        {
            return;
        }

        Text("ConflictPath", conflict.Conflicts.AsValueEnumerable().Select(c => c.Path).JoinToString("\n"));
        Value("LocalConflict", conflict.Conflicts.AsValueEnumerable().Select(c => c.Local).JoinToString("\n\n"));
        Value("RemoteConflict", conflict.Conflicts.AsValueEnumerable().Select(c => c.Remote).JoinToString("\n\n"));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (Root)
        {
            EditorLayoutPreferences.Save(Windows);
            UnityEngine.Object.Destroy(Root);
        }
        if (--_bundleUsers == 0 && _bundle)
        {
            _bundle.Unload(false);
            _sharedBundle = null;
        }
    }
}
