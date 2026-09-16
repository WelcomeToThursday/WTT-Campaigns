namespace WTT.Campaigns.Client.Authoring;

// Plain data so persisted camera poses can be validated without a Unity runtime.
internal sealed class EditorCameraBookmarks
{
    public sealed class Pose
    {
        public float[] Position = Array.Empty<float>();
        public float[] Rotation = Array.Empty<float>();

        internal bool Valid
        {
            get
            {
                if (Position?.Length != 3 || Rotation?.Length != 4)
                    return false;
                foreach (var value in Position)
                    if (float.IsNaN(value) || float.IsInfinity(value))
                        return false;
                double length = 0;
                foreach (var value in Rotation)
                {
                    if (float.IsNaN(value) || float.IsInfinity(value))
                        return false;
                    length += (double)value * value;
                }
                return length > .000001 && length < 4;
            }
        }
    }

    public Dictionary<string, Pose> Poses = new(StringComparer.Ordinal);

    private static string Key(string draft, string map) => draft.Length + ":" + draft + ":" + map.Trim().ToLowerInvariant();

    internal bool TryGet(string draft, string map, out Pose pose)
    {
        pose = null!;
        return !string.IsNullOrWhiteSpace(draft)
            && !string.IsNullOrWhiteSpace(map)
            && Poses != null
            && Poses.TryGetValue(Key(draft, map), out pose)
            && pose != null
            && pose.Valid;
    }

    internal bool Save(string draft, string map, Pose pose)
    {
        if (string.IsNullOrWhiteSpace(draft) || string.IsNullOrWhiteSpace(map) || !pose.Valid)
            return false;
        Poses ??= new(StringComparer.Ordinal);
        Poses[Key(draft, map)] = pose;
        return true;
    }
}
