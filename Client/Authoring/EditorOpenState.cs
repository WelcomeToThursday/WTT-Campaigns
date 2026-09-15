namespace WTT.Campaigns.Client.Authoring;

// A failed automatic open requires a deliberate retry, not another attempt every frame.
internal sealed class EditorOpenState
{
    private bool _failed;

    internal bool TryBegin(bool requested)
    {
        if (requested)
            _failed = false;
        return !_failed;
    }

    internal void Fail() => _failed = true;

    internal void Reset() => _failed = false;
}
