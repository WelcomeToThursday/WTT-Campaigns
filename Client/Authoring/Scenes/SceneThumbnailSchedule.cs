namespace WTT.Campaigns.Client.Authoring.Scenes;

// A new instance belongs to each map generation; old jobs only release their own slots.
internal sealed class SceneThumbnailSchedule
{
    private readonly HashSet<string> _wanted = new();
    internal int Props { get; private set; }
    internal int Icons { get; private set; }
    internal int Active => Props + Icons;

    internal void Want(IEnumerable<string> keys)
    {
        _wanted.Clear();
        foreach (var key in keys)
            _wanted.Add(key);
    }

    internal bool Wanted(string key) => _wanted.Contains(key);

    internal bool CanStart(bool native) => native ? Icons < 4 : Props < 1;

    internal int Next<T>(IReadOnlyList<T> jobs, string selected, Func<T, string> key, Func<T, bool> native)
    {
        var first = -1;
        for (var i = 0; i < jobs.Count; i++)
        {
            if (!CanStart(native(jobs[i])))
                continue;
            if (key(jobs[i]) == selected)
                return i;
            if (first < 0)
                first = i;
        }
        return first;
    }

    internal bool Start(bool native)
    {
        if (!CanStart(native))
            return false;
        if (native)
            Icons++;
        else
            Props++;
        return true;
    }

    internal void Finish(bool native)
    {
        if (native)
            Icons--;
        else
            Props--;
    }
}
