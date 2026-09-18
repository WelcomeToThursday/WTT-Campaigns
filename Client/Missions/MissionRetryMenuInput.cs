using UnityEngine;
using UnityEngine.EventSystems;

namespace WTT.Campaigns.Client.Missions;

/// <summary>Owns pointer input only while the frozen mission's retry dialog is visible.</summary>
internal sealed class MissionRetryMenuInput : MonoBehaviour
{
    private readonly List<EventSystem> _suspended = new();
    private GameObject? _input;
    private CursorLockMode _previousLock;
    private bool _previousVisible;

    private void Awake()
    {
        _previousLock = Cursor.lockState;
        _previousVisible = Cursor.visible;
        foreach (var system in FindObjectsOfType<EventSystem>())
        {
            if (!system.enabled)
                continue;
            _suspended.Add(system);
            system.enabled = false;
        }
        _input = new GameObject("Mission retry input", typeof(EventSystem), typeof(StandaloneInputModule));
        LateUpdate();
    }

    private void LateUpdate()
    {
        if (!_input)
            return;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    internal void Release()
    {
        if (!_input)
            return;
        _input.SetActive(false);
        Destroy(_input);
        _input = null;
        foreach (var system in _suspended)
            if (system)
                system.enabled = true;
        _suspended.Clear();
        Cursor.lockState = _previousLock;
        Cursor.visible = _previousVisible;
    }

    private void OnDestroy() => Release();
}
