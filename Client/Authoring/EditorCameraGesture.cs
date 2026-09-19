namespace WTT.Campaigns.Client.Authoring;

internal enum EditorCameraGesture
{
    None,
    Fly,
    Orbit,
    Pan,
}

internal static class EditorCameraGesturePolicy
{
    internal static EditorCameraGesture Resolve(
        EditorCameraGesture current,
        bool allowed,
        bool inside,
        bool right,
        bool middle,
        bool left,
        bool alt
    )
    {
        if (!allowed)
            return EditorCameraGesture.None;
        var held = current switch
        {
            EditorCameraGesture.Fly => right,
            EditorCameraGesture.Pan => middle,
            EditorCameraGesture.Orbit => left && alt,
            _ => false,
        };
        if (held)
            return current;
        if (!inside)
            return EditorCameraGesture.None;
        return right ? EditorCameraGesture.Fly
            : middle ? EditorCameraGesture.Pan
            : left && alt ? EditorCameraGesture.Orbit
            : EditorCameraGesture.None;
    }
}
