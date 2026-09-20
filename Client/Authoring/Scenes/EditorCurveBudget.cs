namespace WTT.Campaigns.Client.Authoring.Scenes;

// The editor may inspect more samples than a moving bot, but yields after two
// milliseconds or 64 samples. All inspector legs share this frame allowance.
internal sealed class EditorCurveBudget
{
    private int _frame = -1;
    private int _used;
    private double _started;

    internal bool TryTake(int frame, double now)
    {
        if (_frame != frame)
        {
            _frame = frame;
            _used = 0;
            _started = now;
        }
        if (_used >= 64 || now - _started >= .002)
            return false;
        _used++;
        return true;
    }
}
