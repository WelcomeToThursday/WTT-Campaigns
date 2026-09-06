using System.Runtime.CompilerServices;
using EFT.InventoryLogic;
using Newtonsoft.Json;
using SeasonalPerks.Shared.Contracts;
using SPT.Common.Http;

namespace SeasonalPerks.Client.Hub;

internal static class HubDocuments
{
    private static readonly ConditionalWeakTable<object, object> Reported = new();
    private static string? _profile;
    private static List<string> _pending = new();

    private static string JournalPath
    {
        get { return Path.Combine(Plugin.Folder, "hub-documents-" + _profile + ".json"); }
    }

    private static void LoadJournal()
    {
        var profile = Plugin.App?.Session?.Profile?.Id ?? throw new InvalidOperationException("The Seasonal profile is not loaded.");
        if (profile.Length != 24 || profile.Any(c => !Uri.IsHexDigit(c)))
        {
            throw new InvalidDataException("Invalid document journal profile identifier.");
        }
        if (_profile == profile)
        {
            return;
        }
        _profile = profile;
        _pending = File.Exists(JournalPath)
            ? JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(JournalPath))!
            : new List<string>();
    }

    private static void SaveJournal()
    {
        File.WriteAllText(JournalPath + ".tmp", JsonConvert.SerializeObject(_pending));
        if (File.Exists(JournalPath))
        {
            File.Replace(JournalPath + ".tmp", JournalPath, null);
        }
        else
        {
            File.Move(JournalPath + ".tmp", JournalPath);
        }
    }

    internal static void FlushRequired()
    {
        LoadJournal();
        while (_pending.Count > 0)
        {
            var result = JsonConvert.DeserializeObject<HubResult>(RequestHandler.PostJson("/wtt-seasonal/hub/raid-document", _pending[0]));
            if (result == null || !string.IsNullOrEmpty(result.Error))
            {
                throw new InvalidOperationException(result?.Error ?? "The document operation was not acknowledged.");
            }
            if (!result.Committed && !result.Ignored)
            {
                throw new InvalidOperationException("The document operation was not acknowledged.");
            }
            _pending.RemoveAt(0);
            SaveJournal();
        }
    }

    internal static bool IsDocument(Item item)
    {
        return Plugin.Current?.DocumentTemplates.Contains(item.TemplateId) == true;
    }

    internal static void Pickup(Item item)
    {
        Send(
            new
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ItemId = item.Id,
                PickedUp = true,
            }
        );
    }

    internal static void Transfer(
        object operation,
        Item item,
        Item target,
        int count,
        bool split,
        CommandStatus status,
        IItemOwner controller
    )
    {
        if (
            status != CommandStatus.Succeed
            || controller.ID != Plugin.Player?.Profile.Id
            || !Plugin.SeasonalPlayer
            || !Plugin.InRaid
            || !IsDocument(item)
            || Reported.TryGetValue(operation, out _)
        )
        {
            return;
        }
        Reported.Add(operation, new object());
        Send(
            new
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ItemId = item.Id,
                TargetId = target.Id,
                Count = count,
                Split = split,
                PickedUp = target.CurrentAddress?.GetOwner()?.ID == Plugin.Player!.Profile.Id,
            }
        );
    }

    private static void Send(object request)
    {
        try
        {
            LoadJournal();
            var body = Newtonsoft.Json.Linq.JObject.FromObject(request);
            body["ProtocolVersion"] = 2;
            body["SeasonId"] = Plugin.Current?.SeasonId;
            body["PackRevision"] = Plugin.Current?.PackRevision;
            _pending.Add(body.ToString(Formatting.None));
            SaveJournal();
        }
        catch (Exception e)
        {
            Plugin.Error(e);
            return;
        }
        // Persist before sending, then preserve operation order. Native raid start/end also flush the journal.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                FlushRequired();
                return;
            }
            catch (Exception e)
            {
                if (attempt == 2)
                {
                    Plugin.Error(e);
                }
            }
        }
    }
}
