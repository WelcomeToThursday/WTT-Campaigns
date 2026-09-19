namespace WTT.Campaigns.Client.Authoring;

// Platform-independent capture lifetime; native cursor access is isolated below it.
internal sealed class EditorPointerRestore
{
    private nint _window;
    private int _x,
        _y;
    internal bool Pending { get; private set; }

    internal void Remember(nint window, int x, int y)
    {
        if (Pending || window == 0)
            return;
        _window = window;
        _x = x;
        _y = y;
        Pending = true;
    }

    internal bool Release(nint window, bool focused, out int x, out int y)
    {
        x = _x;
        y = _y;
        var restore = Pending && focused && window == _window;
        Pending = false;
        return restore;
    }
}
