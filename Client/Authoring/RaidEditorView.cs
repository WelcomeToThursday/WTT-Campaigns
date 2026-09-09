using SeasonalPerks.Client.UI;
using SeasonalPerks.UI.Controls;
using UnityEngine;
using UnityEngine.UI;

namespace SeasonalPerks.Client.Authoring;

internal sealed class RaidEditorView : IDisposable
{
    internal readonly GameObject Root;
    private readonly Dictionary<string, Transform> _controls;
    private readonly AssetBundle _bundle;
    internal RaidEditorView()
    {
        _bundle = AssetBundle.LoadFromFile(Path.Combine(Plugin.Folder, "seasonal_raid_editor.bundle")) ?? throw new InvalidOperationException("Install the CJ-SDK raid editor UI bundle.");
        var prefab = _bundle.LoadAsset<GameObject>("assets/mods/seasonalperks.assets/raideditor/seasonalraideditor.prefab") ?? throw new InvalidOperationException("The raid editor prefab is missing.");
        Root = UnityEngine.Object.Instantiate(prefab);
        _controls = Root.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
        var ui = new UiElements(Root.GetComponentInChildren<Text>().font, sound => SeasonUi.Instance.PlayInterfaceSound(sound));
        foreach (var button in Root.GetComponentsInChildren<Button>(true))
        {
            ui.Feedback(button);
        }

        Root.SetActive(false);
    }
    internal T Get<T>(string name) where T : Component
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

    internal void Button(string name, Action action)
    {
        Get<Button>(name).onClick.AddListener(() => action());
    }

    internal void Input(string name, Action<string> action)
    {
        Get<InputField>(name).onEndEdit.AddListener(value => action(value));
    }

    internal void Value(string name, string value) { var field = Get<InputField>(name); if (!field.isFocused)
        {
            field.SetTextWithoutNotify(value);
        }
    }
    internal bool Typing
    {
        get
        {
            return Root.GetComponentsInChildren<InputField>().Any(f => f.isFocused);
        }
    }

    internal void Conflict(RaidEditorSession session)
    {
        var conflict = session.Conflict;
        _controls["Conflict"].gameObject.SetActive(conflict != null);
        if (conflict == null)
        {
            return;
        }

        Text("ConflictPath", string.Join("\n", conflict.Conflicts.Select(c => c.Path)));
        Value("LocalConflict", string.Join("\n\n", conflict.Conflicts.Select(c => c.Local)));
        Value("RemoteConflict", string.Join("\n\n", conflict.Conflicts.Select(c => c.Remote)));
    }
    public void Dispose() { UnityEngine.Object.Destroy(Root); _bundle.Unload(false); }
}
