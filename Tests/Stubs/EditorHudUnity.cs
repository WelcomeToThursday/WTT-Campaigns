// Managed test doubles only for the source-linked EditorHud. Real native contract
// checks load Unity assemblies in ClientAssemblyContext and never resolve these types.
namespace UnityEngine;

internal class Object
{
    internal bool Destroyed;
    public static implicit operator bool(Object? value) => value != null && !value.Destroyed
        && (value is not Component component || component.gameObject);
    public static void Destroy(Object value) => value.Destroyed = true;
}

internal class Component : Object
{
    public GameObject gameObject = null!;
    public void GetComponentsInChildren<T>(bool includeInactive, List<T> results) where T : Component
    {
        results.Clear();
        void Visit(GameObject node)
        {
            results.AddRange(node.Components.OfType<T>().Where(c => c));
            foreach (var child in node.Children) Visit(child);
        }
        Visit(gameObject);
    }
}

internal sealed class GameObject : Object
{
    internal readonly List<Component> Components = new();
    internal readonly List<GameObject> Children = new();
    internal readonly Component Transform;
    internal bool RejectAdd;
    internal int AddAttempts;
    public bool activeSelf = true;
    public void SetActive(bool active) => activeSelf = active;
    internal GameObject() => Transform = new Component { gameObject = this };
    public T GetComponent<T>() where T : Component => Components.OfType<T>().FirstOrDefault(c => c)!;
    public T AddComponent<T>() where T : Component, new()
    {
        AddAttempts++;
        if (RejectAdd || GetComponent<T>()) return null!;
        var component = new T { gameObject = this };
        Components.Add(component);
        return component;
    }
}

internal sealed class CanvasGroup : Component
{
    public float alpha = 1;
    public bool interactable = true, blocksRaycasts = true, ignoreParentGroups;
}

internal sealed class Terrain : Component
{
    public bool drawHeightmap = true, drawTreesAndFoliage = true;
}
