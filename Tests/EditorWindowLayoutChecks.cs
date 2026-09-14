using Newtonsoft.Json;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.Tests;

internal static class EditorWindowLayoutChecks
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (var (width, height) in new[] { (1280, 720), (1280, 1024), (1920, 1080), (2560, 1440), (3440, 1440) })
        foreach (
            var source in new[]
            {
                new EditorWindowPlacement
                {
                    Id = "Library",
                    Width = 440,
                    Height = 620,
                    X = -.35f,
                    Y = .1f,
                    Visible = true,
                },
                new EditorWindowPlacement
                {
                    Id = "Inspector",
                    Width = 99999,
                    Height = 99999,
                    X = 99,
                    Y = -99,
                },
                new EditorWindowPlacement
                {
                    Id = "Controls",
                    Width = -1,
                    Height = -1,
                    X = float.NaN,
                    Y = float.PositiveInfinity,
                },
            }
        )
        {
            var fitted = EditorWindowPlacement.Fit(source, width, height, 360, 320);
            var left = fitted.X * width - fitted.Width / 2;
            var top = fitted.Y * height + fitted.Height / 2;
            check(left >= -width / 2f + 7.9f && left + fitted.Width <= width / 2f - 7.9f, "Restored window fits display horizontally");
            check(
                top <= height / 2f - 91.9f && top - fitted.Height >= -height / 2f + 39.9f,
                "Restored window keeps its title below the toolbar and its base on screen"
            );
            check(fitted.Width >= 360 && fitted.Height >= 320, "Window retains readable minimum dimensions");
            check(fitted.Visible == source.Visible && fitted.Id == source.Id, "Clamping preserves identity and visibility");
            var again = EditorWindowPlacement.Fit(fitted, width, height, 360, 320);
            check(
                Math.Abs(again.X - fitted.X) < .0001 && Math.Abs(again.Y - fitted.Y) < .0001,
                "Layout restore is stable across repeated display checks"
            );
        }
        var layout = new EditorWindowLayout
        {
            Windows = new[]
            {
                new EditorWindowPlacement
                {
                    Id = "Library",
                    Width = 520,
                    Height = 480,
                    Visible = true,
                    X = -.2f,
                },
            },
        };
        var json = JsonConvert.SerializeObject(layout);
        var restored = JsonConvert.DeserializeObject<EditorWindowLayout>(json)!;
        check(
            restored.Version == 1 && restored.Windows[0].Width == 520 && restored.Windows[0].Visible,
            "Local layout survives serialization between launches"
        );
        var onSmallerDisplay = EditorWindowPlacement.Fit(restored.Windows[0], 1280, 720, 400, 390);
        check(onSmallerDisplay.Width == 520 && onSmallerDisplay.Height == 480, "Display changes preserve user dimensions when they fit");
    }
}
