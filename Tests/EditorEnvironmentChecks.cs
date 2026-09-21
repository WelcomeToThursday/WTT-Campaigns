using Mono.Cecil;
using WTT.Campaigns.Client.Authoring.Rendering;

namespace WTT.Campaigns.Tests;

internal static class EditorEnvironmentChecks
{
    internal static void Values(Action<bool, string> check)
    {
        foreach (var text in new[] { "24:00", "23:60", "-1:00", "1:2", "NaN", "12", "", "1.12:00" })
            check(!EnvironmentValues.TryHour(text, out _), "Invalid preview clock input is rejected: " + text);
        foreach (var (text, hour) in new[] { ("00:00", 0f), ("6:30", 6.5f), (" 23:59 ", 23 + 59 / 60f) })
            check(
                EnvironmentValues.TryHour(text, out var value) && Math.Abs(value - hour) < .0001f,
                "Preview clock preserves minutes: " + text
            );
        foreach (var text in new[] { "NaN", "Infinity", "-Infinity", "101", "-1", "", "1,5" })
            check(!EnvironmentValues.TryPercent(text, out _), "Invalid weather intensity is rejected: " + text);
        foreach (var text in new[] { "0", "100", " 12.5 " })
            check(EnvironmentValues.TryPercent(text, out _), "Weather intensity accepts its boundaries and fractions: " + text);
    }

    internal static void Native(AssemblyDefinition native, AssemblyDefinition client)
    {
        var types = native.MainModule.GetTypes().ToDictionary(t => t.FullName);
        foreach (
            var (type, name, argument) in new[]
            {
                ("Koenigz.PerfectCulling.PerfectCullingCamera", "Update", ""),
                ("Koenigz.PerfectCulling.PerfectCullingCamera", "CamPreCull", "UnityEngine.Camera"),
                ("Koenigz.PerfectCulling.EFT.PerfectCullingCrossSceneSampler", "Update", ""),
                ("CullingManager", "ScheduleCullingJobs", ""),
                ("CullingManager", "CameraRender", "UnityEngine.Camera"),
                ("OcclusionCullingSwitcher", "UpdateCameraSettings", ""),
            }
        )
        {
            var method = types[type].Methods.Single(m => m.Name == name);
            Require(
                method.HasBody
                    && !method.IsStatic
                    && method.ReturnType.FullName == "System.Void"
                    && method
                        .Parameters.Select(p => p.ParameterType.FullName)
                        .SequenceEqual(argument.Length == 0 ? Array.Empty<string>() : new[] { argument }),
                "Native camera hook changed: " + type + "." + name
            );
        }
        var perfect = types["Koenigz.PerfectCulling.PerfectCullingCamera"];
        Require(perfect.Properties.Single(p => p.Name == "ObservePosition").SetMethod.IsPublic, "Native observer must remain writable.");
        Require(
            Calls(perfect.Methods.Single(m => m.Name == "Update"), "set_ObservePosition"),
            "Native Update resets the observer before the editor correction."
        );
        var sampler = types["Koenigz.PerfectCulling.EFT.PerfectCullingCrossSceneSampler"];
        Require(
            Calls(sampler.Methods.Single(m => m.Name == "method_4"), "get_ObservePosition"),
            "Adaptive map culling reads the corrected observer."
        );
        var curve = types["EFT.Weather.WeatherController"].Properties.Single(p => p.Name == "WeatherCurve");
        Require(
            curve.GetMethod.IsPublic && curve.PropertyType.FullName == "EFT.Weather.IWeatherCurve",
            "Weather overlay uses the native curve contract."
        );
        Require(
            types["TOD_Time"].Fields.Any(f => f.Name == "LockCurrentTime" && f.IsPublic && f.FieldType.FullName == "System.Boolean"),
            "Sky preview can hold time without changing the raid clock."
        );
        var environment = client.MainModule.GetType("WTT.Campaigns.Client.Authoring.Rendering.EditorEnvironment");
        Require(
            Calls(types["DisablerTerrainCullingObject"].Methods.Single(m => m.Name == "SetComponentsEnabled"), "set_drawHeightmap")
                && Calls(types["DisablerCullingObjectBase"].Methods.Single(m => m.Name == "ManualUpdate"), "get_HasEntered"),
            "Installed terrain visibility is controlled by player triggers separately from camera culling."
        );
        Require(
            Calls(types["TerrainLod"].Methods.Single(m => m.Name == "set_TerrainIsVisible"), "SetActive")
                && types["TerrainLod"].Fields.Single(f => f.Name == "_terrainLod").IsPublic,
            "Terrain visibility must switch its native proxy as well as heightmap drawing."
        );
        Require(
            Calls(environment.Methods.Single(m => m.Name == "Sync"), "set_useOcclusionCulling")
                && Calls(environment.Methods.Single(m => m.Name == "Sync"), "Show"),
            "Every editor sampling pass must restore camera occlusion and terrain visibility."
        );
        Require(
            Calls(environment.Methods.Single(m => m.Name == "RestoreTime"), "CalculateTaxonomyDate"),
            "Reset returns to current native raid time."
        );
        Require(
            !environment
                .Methods.Where(m => m.HasBody)
                .SelectMany(m => m.Body.Instructions)
                .Any(i => i.Operand is MethodReference m && m.DeclaringType.Name == "GameDateTime" && m.Name == "Reset"),
            "Preview must not reset the gameplay clock."
        );
        Require(
            !environment
                .Methods.Where(m => m.HasBody)
                .SelectMany(m => m.Body.Instructions)
                .Any(i =>
                    i.OpCode.Code == Mono.Cecil.Cil.Code.Stfld
                    && i.Operand is FieldReference f
                    && f.DeclaringType.Name == "WeatherController"
                ),
            "Weather preview must not mutate native backend/debug state."
        );
        var editor = client.MainModule.GetType("WTT.Campaigns.Client.Authoring.Editor.RaidEditor");
        Require(Calls(editor.Methods.Single(m => m.Name == "Close"), "Dispose"), "Editor close releases its preview.");
        Console.WriteLine("Editor environment: native culling, sky clock, weather overlay and restoration contracts passed offline.");
    }

    private static bool Calls(MethodDefinition method, string name) =>
        method.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == name);

    private static void Require(bool value, string message)
    {
        if (!value)
            throw new InvalidOperationException(message);
    }
}
