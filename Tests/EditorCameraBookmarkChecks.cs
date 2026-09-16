using Newtonsoft.Json;
using WTT.Campaigns.Client.Authoring;

namespace WTT.Campaigns.Tests;

internal static class EditorCameraBookmarkChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var bookmarks = new EditorCameraBookmarks();
        var first = new EditorCameraBookmarks.Pose { Position = new[] { 100.5f, 12f, -98f }, Rotation = new[] { 0f, 1f, 0f, 0f } };
        var second = new EditorCameraBookmarks.Pose { Position = new[] { -20f, 50f, 400f }, Rotation = new[] { 0f, 0f, 0f, 1f } };
        check(!bookmarks.TryGet("draft", "Interchange", out _), "First visit has no saved camera");
        check(bookmarks.Save("draft", "Interchange", first), "Save departing editor position and angle");
        bookmarks.Save("draft", "Woods", second);
        bookmarks.Save("other-draft", "Interchange", second);
        var restored = JsonConvert.DeserializeObject<EditorCameraBookmarks>(JsonConvert.SerializeObject(bookmarks))!;
        check(restored.TryGet("draft", "interchange", out var pose) && pose.Position.SequenceEqual(first.Position) && pose.Rotation.SequenceEqual(first.Rotation), "New session restores exact position and angle after persistence");
        check(restored.TryGet("draft", "Woods", out pose) && pose.Position.SequenceEqual(second.Position), "Maps keep separate bookmarks");
        check(restored.TryGet("other-draft", "Interchange", out pose) && pose.Position.SequenceEqual(second.Position), "Drafts keep separate bookmarks");
        restored.Save("draft", "Interchange", second);
        check(restored.TryGet("draft", "Interchange", out pose) && pose.Position.SequenceEqual(second.Position), "Most recent departure replaces the previous bookmark");
        check(!restored.Save("", "Interchange", first), "Unconnected sessions cannot save anonymous bookmarks");
        check(!restored.Save("draft", "", first), "Camera bookmarks require a map");
        check(!restored.Save("draft", "Interchange", new() { Position = new[] { float.NaN, 0f, 0f }, Rotation = first.Rotation }), "Invalid camera position is not persisted");
        check(!restored.Save("draft", "Interchange", new() { Position = first.Position, Rotation = new float[4] }), "Invalid camera orientation is not persisted");
        check(restored.TryGet("draft", "Interchange", out pose) && pose.Position.SequenceEqual(second.Position), "Invalid save preserves the last valid camera");
        var broken = JsonConvert.DeserializeObject<EditorCameraBookmarks>("{\"Poses\":null}")!;
        check(!broken.TryGet("draft", "Interchange", out _), "Malformed saved collection falls back to player camera");
        check(broken.Save("draft", "Interchange", first), "Valid departure repairs malformed bookmark collection");
        restored.Poses.Values.First().Position = null!;
        check(!restored.TryGet("draft", "Interchange", out _), "Malformed saved pose is rejected before Unity receives it");
    }
}
