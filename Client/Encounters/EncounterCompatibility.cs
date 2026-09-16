using System.Reflection;
using BepInEx.Bootstrap;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Encounters;

/// <summary>
/// Compatibility gate for the native encounter preview.  The plugin itself remains loadable
/// without either optional mod; only AI previews require the verified integration surface.
/// </summary>
internal static class EncounterCompatibility
{
    internal const string BigBrainGuid = "xyz.drakia.bigbrain";
    internal const string SainGuid = "me.sol.sain";
    internal const string BigBrainVersion = "1.5.0";
    internal const string SainVersion = "4.5.1";

    private static readonly object Gate = new();
    private static bool _checked;
    private static Type? _runtimeType;
    private static string _error = "";

    internal static string Error
    {
        get
        {
            lock (Gate)
            {
                return _error;
            }
        }
    }

    internal static bool Ensure(out string error)
    {
        lock (Gate)
        {
            if (_checked)
            {
                error = _error;
                return _error.Length == 0;
            }

            try
            {
                RequirePlugin(BigBrainGuid, BigBrainVersion, "BigBrain");
                RequirePlugin(SainGuid, SainVersion, "SAIN");
                RequireNativeMethod(
                    Chainloader.PluginInfos[SainGuid].Instance.GetType().Assembly.GetType("SAIN.Interop.SAINExternal", true),
                    "CanBotQuest"
                );
                RequireNativeMethod(
                    Chainloader
                        .PluginInfos[BigBrainGuid]
                        .Instance.GetType()
                        .Assembly.GetType("DrakiaXYZ.BigBrain.Brains.BrainManager", true),
                    "AddCustomLayer"
                );
                // Load only after both plugins passed the gate. No core field, signature or base type references this assembly.
                var integration = Assembly.LoadFrom(Path.Combine(Plugin.Folder, "WTT-Campaigns.AI.dll"));
                integration.GetTypes();
                _runtimeType = integration.GetType("WTT.Campaigns.Client.Encounters.EncounterAiRuntime", true);
                _error = "";
            }
            catch (Exception exception)
            {
                _error = "AI preview compatibility is unavailable: " + exception.Message;
            }

            _checked = true;
            error = _error;
            return _error.Length == 0;
        }
    }

    internal static IEncounterAiRuntime Create(MapLayout layout)
    {
        if (!Ensure(out var error))
            throw new InvalidOperationException(error);
        return (IEncounterAiRuntime)
            Activator.CreateInstance(
                _runtimeType!,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new object[] { layout },
                null
            );
    }

    private static void RequirePlugin(string guid, string minimum, string name)
    {
        if (!Chainloader.PluginInfos.TryGetValue(guid, out var info) || info == null)
            throw new InvalidOperationException(name + " " + minimum + " or later is required for AI preview.");

        if (
            !TryVersion(info.Metadata.Version?.ToString(), out var installed)
            || !TryVersion(minimum, out var required)
            || installed < required
        )
        {
            throw new InvalidOperationException(
                name + " " + minimum + " or later is required for AI preview (installed " + info.Metadata.Version + ")."
            );
        }
    }

    private static bool TryVersion(string? text, out System.Version version)
    {
        version = new System.Version(0, 0);
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = text.Trim();
        var dash = normalized.IndexOf('-');
        if (dash >= 0)
            normalized = normalized[..dash];
        var plus = normalized.IndexOf('+');
        if (plus >= 0)
            normalized = normalized[..plus];
        if (!System.Version.TryParse(normalized, out var parsed))
            return false;

        version = parsed;
        return true;
    }

    private static void RequireNativeMethod(Type type, string name)
    {
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
        {
            if (method.Name == name)
                return;
        }

        throw new MissingMethodException(type.FullName, name);
    }
}
