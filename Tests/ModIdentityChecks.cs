using Mono.Cecil;
using Mono.Cecil.Cil;

namespace WTT.Campaigns.Tests;

internal static class ModIdentityChecks
{
    public static void Run(string clientPath, string serverPath)
    {
        foreach (
            var path in new[]
            {
                clientPath,
                serverPath,
                Path.Combine(Path.GetDirectoryName(clientPath)!, "WTT-Campaigns.UI.dll"),
                Path.Combine(Path.GetDirectoryName(clientPath)!, "WTT-Campaigns.Shared.dll"),
                Path.Combine(Path.GetDirectoryName(serverPath)!, "WTT-Campaigns.Shared.dll"),
            }
        )
        {
            using var assembly = AssemblyDefinition.ReadAssembly(path);
            Require(assembly.Name.Name == Path.GetFileNameWithoutExtension(path), "Assembly identity matches its deployed filename");
            Require(
                !assembly.MainModule.AssemblyReferences.Any(reference => reference.Name.StartsWith("WTT-Seasonal")),
                "No legacy assembly references"
            );
            Require(!assembly.MainModule.GetTypes().Any(type => type.Namespace.StartsWith("SeasonalPerks")), "No legacy mod namespaces");
            var strings = assembly
                .MainModule.GetTypes()
                .SelectMany(type => type.Methods)
                .Where(method => method.HasBody)
                .SelectMany(method => method.Body.Instructions)
                .Where(instruction => instruction.OpCode == OpCodes.Ldstr)
                .Select(instruction => (string)instruction.Operand)
                .ToArray();
            Require(
                !strings.Any(value => value.StartsWith("/wtt-seasonal") || value == "com.cj.seasonalperks"),
                "No legacy routes or plugin GUID"
            );
            if (path == clientPath)
            {
                var plugin = assembly.MainModule.GetType("WTT.Campaigns.Client.Plugin");
                var metadata = plugin.CustomAttributes.Single(attribute => attribute.AttributeType.FullName == "BepInEx.BepInPlugin");
                Require((string)metadata.ConstructorArguments[0].Value == "com.wtt.campaigns", "Client GUID");
                Require((string)metadata.ConstructorArguments[1].Value == "WTT-Campaigns", "Client display name");
                Require(
                    plugin.CustomAttributes.Any(a =>
                        a.AttributeType.FullName == "BepInEx.BepInDependency"
                        && (string)a.ConstructorArguments[0].Value == "com.wtt.commonlib"
                        && (string)a.ConstructorArguments[1].Value == "3.0.6"
                    ),
                    "Client requires CommonLib 3.0.6"
                );
                Require(
                    assembly.MainModule.AssemblyReferences.Any(r => r.Name == "WTT-ClientCommonLib"),
                    "Client links the CommonLib salvage runtime"
                );
            }
            if (assembly.Name.Name == "WTT-Campaigns.UI")
            {
                Require(!strings.Contains("Season_1_logo_video_1380x460.webm"), "UI cannot play the old season logo video");
                Require(
                    !assembly.MainModule.Resources.Any(resource => resource.Name.EndsWith("season-1-logo.png")),
                    "Old season lettering is not embedded in UI"
                );
                Require(
                    strings.Contains("PvE Campaign") && strings.Contains("CAMPAIGN REWARDS") && strings.Contains("Choose a campaign"),
                    "Campaign labels are present in compiled UI"
                );
                var label = assembly
                    .MainModule.GetType("WTT.Campaigns.UI.Controls.UiElements")
                    .Methods.Single(method => method.Name == "Label");
                Require(
                    label.Body.Instructions.Any(instruction =>
                        instruction.Operand is MethodReference call
                        && call.DeclaringType.FullName == "WTT.Campaigns.Shared.Presentation.CampaignText"
                        && call.Name == "Display"
                    ),
                    "Dynamic campaign names use presentation terminology"
                );
            }
            if (path == serverPath)
            {
                Require(strings.Contains("com.wtt.commonlib") && strings.Contains(">=3.0.6"), "Server requires CommonLib 3.0.6");
                Require(strings.Contains("4.1.x") && !strings.Contains("~4.1." + "3"), "Server supports the complete SPT 4.1.x line");
                var documentation = assembly.MainModule.GetType("WTT.Campaigns.Server.Web.Pages.Documentation");
                Require(
                    documentation.CustomAttributes.Any(attribute =>
                        attribute.AttributeType.FullName == "Microsoft.AspNetCore.Components.RouteAttribute"
                        && attribute.ConstructorArguments.Any(argument => (string)argument.Value == "/wtt-campaigns/docs")
                    ),
                    "Server exposes embedded wiki documentation"
                );
                Require(
                    assembly.MainModule.AssemblyReferences.Any(reference => reference.Name == "Markdig"),
                    "Server links the Markdown documentation renderer"
                );
                Require(
                    assembly.MainModule.Resources.Count(resource => resource.Name.StartsWith("WTT.Campaigns.Wiki.")) >= 30,
                    "Server embeds the complete wiki page set"
                );
                var metadata = assembly.MainModule.GetType("WTT.Campaigns.Server.Metadata");
                var constructor = metadata.Methods.Single(method => method.IsConstructor && !method.HasParameters && !method.IsStatic);
                foreach (
                    var entry in new Dictionary<string, string>
                    {
                        ["ModGuid"] = "com.wtt.campaigns",
                        ["Name"] = "WTT-Campaigns",
                        ["HomePage"] = "/wtt-campaigns/creator",
                        ["WWWRootUrl"] = "wtt-campaigns-creator-assets",
                    }
                )
                {
                    var store = constructor.Body.Instructions.Single(instruction =>
                        instruction.OpCode == OpCodes.Stfld
                        && ((FieldReference)instruction.Operand).Name == "<" + entry.Key + ">k__BackingField"
                    );
                    Require(store.Previous.OpCode == OpCodes.Ldstr && (string)store.Previous.Operand == entry.Value, "Server " + entry.Key);
                }
            }
        }
        Console.WriteLine("WTT-Campaigns assembly identities, namespaces, routes and client/server GUIDs verified offline.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
