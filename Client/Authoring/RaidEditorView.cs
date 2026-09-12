using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.Client.UI;
using WTT.Campaigns.UI.Controls;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring;

internal sealed class RaidEditorView : IDisposable
{
    internal readonly GameObject Root;
    private readonly Dictionary<string, Transform> _controls;
    private readonly AssetBundle _bundle;

    internal RaidEditorView()
    {
        _bundle =
            AssetBundle.LoadFromFile(Path.Combine(Plugin.Folder, "wtt_campaigns_raid_editor.bundle"))
            ?? throw new InvalidOperationException("Install the CJ-SDK raid editor UI bundle.");
        var prefab =
            _bundle.LoadAsset<GameObject>("assets/mods/wtt-campaigns.assets/raideditor/seasonalraideditor.prefab")
            ?? throw new InvalidOperationException("The raid editor prefab is missing.");
        Root = UnityEngine.Object.Instantiate(prefab);
        _controls = Root.GetComponentsInChildren<Transform>(true)
            .AsValueEnumerable()
            .GroupBy(t => t.name)
            .ToDictionary(g => g.Key, g => g.AsValueEnumerable().First());
        var ui = new UiElements(Root.GetComponentInChildren<Text>().font, sound => SeasonUi.Instance.PlayInterfaceSound(sound));
        foreach (var button in Root.GetComponentsInChildren<Button>(true))
        {
            ui.Feedback(button);
        }

        Root.AddComponent<RaidEditorWindows>().Initialize();

        Root.SetActive(false);
    }

    internal T Get<T>(string name)
        where T : Component
    {
        return _controls[name].GetComponent<T>();
    }

    internal void Text(string name, string value)
    {
        Get<Text>(name).text = value;
    }

    internal void Caption(string name, string value)
    {
        Get<Button>(name).GetComponentInChildren<Text>().text = value;
    }

    internal void Highlight(string name, bool selected)
    {
        Get<Button>(name).targetGraphic.color = selected ? new Color(.36f, .33f, .23f) : new Color(.18f, .18f, .15f);
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
        if (!field.isFocused)
        {
            field.SetTextWithoutNotify(value);
        }
    }

    internal bool Typing
    {
        get { return Root.GetComponentsInChildren<InputField>().AsValueEnumerable().Any(f => f.isFocused); }
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
        UnityEngine.Object.Destroy(Root);
        _bundle.Unload(false);
    }
}
