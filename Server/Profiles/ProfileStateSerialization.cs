using System.Text.Json;
using Newtonsoft.Json;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace WTT.Campaigns.Server.Profiles;

/// <summary>SPT owns the untyped extension-data interface; seasonal callers always read concrete state contracts.</summary>
internal static class ProfileStateSerialization
{
    public static T? Read<T>(PmcData pmc, string key)
    {
        if (!pmc.ExtensionData.TryGetValue(key, out var raw))
        {
            return default;
        }

        var text = raw switch
        {
            string saved => saved,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            null => null,
            _ => throw new InvalidDataException("Invalid campaign profile extension: " + key),
        };
        return string.IsNullOrWhiteSpace(text) ? default : JsonConvert.DeserializeObject<T>(text);
    }
}
