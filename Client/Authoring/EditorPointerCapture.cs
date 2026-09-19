using System.Runtime.InteropServices;
using UnityEngine;

namespace WTT.Campaigns.Client.Authoring;

internal sealed class EditorPointerCapture
{
    private readonly EditorPointerRestore _restore = new();

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X,
            Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    internal void Remember()
    {
        // This runs from the native input prefix BEFORE EFT centers the pointer.
        if (!_restore.Pending && Application.isFocused && GetCursorPos(out var point))
            _restore.Remember(GetForegroundWindow(), point.X, point.Y);
    }

    internal void Release(bool restore = true)
    {
        if (_restore.Release(GetForegroundWindow(), restore && Application.isFocused, out var x, out var y))
            SetCursorPos(x, y);
    }
}
