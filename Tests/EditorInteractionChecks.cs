using WTT.Campaigns.Client.Authoring.Views;

namespace WTT.Campaigns.Tests;

internal static class EditorInteractionChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var edit = new EditorEditState();
        edit.Reset("12");
        foreach (var text in new[] { "garbage", "NaN", "Infinity", "1e99", "1,5" })
        {
            check(!edit.Accept("PositionX", text), "Invalid transform never commits: " + text);
            check(edit.Committed == "12" && edit.Invalid, "Invalid edit preserves last committed value for Escape");
        }
        edit.Reset(edit.Committed);
        check(edit.Committed == "12" && !edit.Invalid, "Cancellation restores the committed value and removes validation");
        check(edit.Accept("PositionX", "-1.25e2") && edit.Committed == "-1.25e2", "Finite signed scientific coordinates are allowed");
        foreach (
            var (id, bad, good) in new[]
            {
                ("ContainerChance", "101", "100"),
                ("ContainerQuantity", "0", "1"),
                ("ContainerQuantity", "1.5", "10000"),
                ("AiRosterCount", "257", "256"),
                ("AiWaveDelaySeconds", "-1", "0"),
                ("AiWaypointWaitSeconds", "3601", "3600"),
                ("WeatherRain", "-0.1", "50.5"),
                ("CameraSpeed", "0.1", "0.25"),
                ("Radius", "0", "0.001"),
                ("MapSizeY", "-1", "0.001"),
                ("SizeZ", "0", "2"),
            }
        )
        {
            check(!edit.Accept(id, bad), id + " rejects out-of-domain input");
            check(edit.Accept(id, good) && !edit.Invalid, id + " correction clears the error");
        }
        var labels = new[] { "Alpha", "Beta", "ALPHABET", "Alpha" };
        check(
            EditorInteractionPolicy.Matches(labels, " alpha ").SequenceEqual(new[] { 0, 2, 3 }),
            "Filtering preserves source indices including duplicate labels"
        );
        check(EditorInteractionPolicy.Matches(labels, "missing").Count == 0, "Unmatched queries have no selectable result");
        var large = Enumerable.Range(0, 20000).Select(i => "Option " + i).ToArray();
        check(EditorInteractionPolicy.Matches(large, "Option 19999").Single() == 19999, "Large choice lists preserve selection identity");
        foreach (var (width, height) in new[] { (1280, 720), (1920, 1080), (2560, 1440), (3440, 1440) })
        foreach (var percent in new[] { 60, 85, 130 })
        {
            var scale = EditorUiScale.Resolve(width, height, percent);
            var w = width / scale;
            var h = height / scale;
            foreach (var left in new[] { 8f, w - 70 })
            foreach (var top in new[] { 8f, h - 40 })
            foreach (var popupWidth in new[] { 250f, 500f, w * 2 })
            {
                var popup = EditorInteractionPolicy.Popup(left, top, top + 30, popupWidth, 350, w, h);
                check(
                    popup.X >= 8 && popup.Y >= 8 && popup.X + popup.Width <= w - 7.99f && popup.Y + popup.Height <= h - 7.99f,
                    $"Popup stays within {width}x{height} at {percent}% including wide choices and screen edges"
                );
                if (top > h / 2)
                    check(popup.Y + popup.Height <= top + .01f, "Bottom-edge choices open above their anchor");
            }
        }
        var saved = new Dictionary<string, bool> { ["Scene/Copy/Preview"] = true };
        check(EditorInteractionPolicy.Expanded(saved, "Scene/Copy", "Preview", false), "Saved expansion wins for the same selection type");
        check(!EditorInteractionPolicy.Expanded(saved, "Scene/Move", "Preview", false), "Other selection types retain compact defaults");
        check(EditorInteractionPolicy.Expanded(saved, "AI/wave", "Wave", true), "Missing preferences retain useful defaults");
        foreach (
            var (local, remote) in new[]
            {
                ("abc12xyz", "abc34xyz"),
                ("", "added"),
                ("removed", ""),
                ("same", "same"),
                ("<tag>\nA", "<tag>\nB"),
            }
        )
        {
            var diff = EditorInteractionPolicy.Difference(local, remote);
            check(
                diff.Before + diff.Local + diff.After == local && diff.Before + diff.Remote + diff.After == remote,
                "Conflict highlighting preserves complete values including multiline text and markup"
            );
        }
    }
}
