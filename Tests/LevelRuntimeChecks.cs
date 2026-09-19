using Mono.Cecil;

namespace WTT.Campaigns.Tests;

internal static class LevelRuntimeChecks
{
    internal static void Run(string clientPath)
    {
        using var client = AssemblyDefinition.ReadAssembly(clientPath);
        var types = client.MainModule.GetTypes().ToArray();
        TypeDefinition Type(string name) => types.Single(t => t.Name == name);
        static MethodReference[] Calls(MethodDefinition method) =>
            method.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().ToArray();
        static void Need(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
        var extracts = Type("LevelExtractRuntime");
        var apply = Calls(extracts.Methods.Single(m => m.Name == "Apply"));
        var dispose = Calls(extracts.Methods.Single(m => m.Name == "Dispose"));
        foreach (var method in new[] { "StartExtraction", "CancelExtraction", "LoadSettings", "set_ExfiltrationPoints" })
            Need(apply.Any(m => m.Name == method), "Level extracts must register native " + method);
        Need(
            !extracts.Methods.Where(m => m.HasBody).SelectMany(Calls).Any(m => m.Name == "Stop"),
            "Level extracts must never force a survived result"
        );
        Need(
            !extracts.Methods.Where(m => m.HasBody).SelectMany(Calls).Any(m => m.Name == "SetTime"),
            "Level extracts must not rebuild or duplicate existing native timers"
        );
        foreach (var method in new[] { "CancelExtraction", "remove_OnStatusChanged", "set_ExfiltrationPoints", "Disable", "Destroy" })
            Need(dispose.Any(m => m.Name == method), "Level extract cleanup must perform " + method);
        var zones = Calls(Type("LevelZoneRuntime").Methods.Single(m => m.Name == "Dispose"));
        Need(
            zones.Any(m => m.DeclaringType.Name == "HazardRuntime" && m.Name == "Clear")
                && zones.Any(m => m.DeclaringType.Name == "NativeZoneBridge" && m.Name == "Clear")
                && zones.Any(m => m.Name == "SetActive")
                && zones.Any(m => m.Name == "Destroy"),
            "Level zones clean hazards, interactions and world objects"
        );
        var edit = Calls(Type("RaidEditorSession").Methods.Single(m => m.Name == "Edit"));
        Need(
            edit.Any(m => m.DeclaringType.Name == "EditorContentRules" && m.Name == "ValidateEdit"),
            "Editor mutations enforce shared capabilities"
        );
        Console.WriteLine(
            "Level runtime contracts: native countdown registration, no forced raid result, cleanup and editor mutation gate passed."
        );
    }
}
