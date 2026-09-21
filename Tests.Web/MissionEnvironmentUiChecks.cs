using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using WTT.Campaigns.Server.Web.Components;
using WTT.Campaigns.Shared.Missions;

internal static class MissionEnvironmentUiChecks
{
    internal static async Task Run(IServiceProvider services, Action<bool, string> check)
    {
        var mission = new MissionDefinition();
        var changes = 0;
        await using var renderer = new EditorRenderer(services);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.Mount(new Host(mission, () => changes++));
            int Section() => renderer.Components<EditorSection>().Single(c => c.Component.Title == "Time of day and weather").Id;
            Task Change(string label, object value) =>
                renderer.DispatchEventAsync(
                    renderer.Event(Section(), "label", label, "onchange"),
                    null,
                    new ChangeEventArgs { Value = value }
                );
            check(mission.Environment == null, "Rendering legacy mission leaves native conditions untouched");
            await Change(" Set mission start time", true);
            check(mission.Environment!.StartMinutes == 720, "Mission time toggle initializes noon");
            await Change("Start time", "23:59");
            await Change(" Hold time fixed", true);
            await Change(" Set mission weather", true);
            await Change("Rain (%)", "65");
            await Change("Wind direction", "8");
            check(
                mission.Environment.StartMinutes == 1439
                    && mission.Environment.FreezeTime
                    && mission.Environment.Weather!.Rain == 65
                    && mission.Environment.Weather.Direction == 8,
                "Web edits update the server mission definition"
            );
            await Change("Start time", "");
            check(
                mission.Environment.StartMinutes == 1439 && renderer.Text(Section()).Contains("Enter a time"),
                "Invalid clock edit preserves the last valid time and shows feedback"
            );
            await Change(" Set mission start time", false);
            check(
                mission.Environment.StartMinutes == null && !mission.Environment.FreezeTime && mission.Environment.Weather != null,
                "Time reset preserves independently configured weather"
            );
            await Change(" Set mission weather", false);
            check(mission.Environment.Weather == null && changes == 8, "Weather reset notifies the mission save workflow");
            int TimerSection() => renderer.Components<EditorSection>().Single(c => c.Component.Title == "Mission timer").Id;
            Task ChangeTimer(string label, object value) =>
                renderer.DispatchEventAsync(
                    renderer.Event(TimerSection(), "label", label, "onchange"),
                    null,
                    new ChangeEventArgs { Value = value }
                );
            await ChangeTimer("Timer mode", "Infinite");
            check(
                mission.TimeLimitMinutes == 0 && renderer.Text(TimerSection()).Contains("∞"),
                "Creator infinite mode saves the explicit no-expiry option"
            );
            await ChangeTimer("Timer mode", "Timed");
            await ChangeTimer("Time limit (minutes)", "25");
            check(mission.TimeLimitMinutes == 25, "Creator duration saves server mission minutes");
            await ChangeTimer("Time limit (minutes)", "0");
            check(
                mission.TimeLimitMinutes == 25 && renderer.Text(TimerSection()).Contains("whole number"),
                "Invalid timed duration preserves the last valid setting"
            );
            await ChangeTimer("Timer mode", "Map");
            check(mission.TimeLimitMinutes == null && changes == 12, "Map default clears the override and timer edits notify save");
        });
    }

    private sealed class Host(MissionDefinition mission, Action changed) : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<MissionEnvironmentFields>(0);
            builder.AddAttribute(1, "Mission", mission);
            builder.AddAttribute(2, "Changed", EventCallback.Factory.Create(this, changed));
            builder.CloseComponent();
        }
    }
}
