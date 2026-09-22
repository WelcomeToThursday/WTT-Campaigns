using Newtonsoft.Json;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Shared.Seasons;

namespace WTT.Campaigns.Server.Editor;

// One atomic document owns both the profile and the content it was saved against.
// This directory is deliberately outside native/seasonal profile discovery.
public sealed class CampaignTestRecord
{
    public int Version { get; set; } = 1;
    public string Owner { get; set; } = "";
    public string DraftId { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public long SourceRevision { get; set; }
    public SeasonDefinition Definition { get; set; } = new();
    public Dictionary<string, string> Identities { get; set; } = new();
    public string ProfileJson { get; set; } = "";
    public string LastAction { get; set; } = "";
    public string LastOperation { get; set; } = "";
    public HashSet<string> RetiredProfiles { get; set; } = new();
}

public sealed class CampaignTestStorage(string directory)
{
    private readonly object _gate = new();

    private string FilePath(string owner, string draft)
    {
        if (!SeasonValidator.IsId(owner) || !SeasonValidator.IsId(draft))
            throw new InvalidDataException("Invalid test owner or draft.");
        return Path.Combine(directory, owner, draft + ".json");
    }

    public CampaignTestRecord? Read(string owner, string draft)
    {
        lock (_gate)
        {
            var path = FilePath(owner, draft);
            if (!File.Exists(path))
                return null;
            var record =
                JsonConvert.DeserializeObject<CampaignTestRecord>(File.ReadAllText(path))
                ?? throw new InvalidDataException("The campaign test save is unreadable.");
            if (
                record.Version != 1
                || record.Owner != owner
                || record.DraftId != draft
                || !SeasonValidator.IsId(record.ProfileId)
                || !SeasonValidator.IsId(record.Definition.Id)
            )
                throw new InvalidDataException("The campaign test save has an incompatible identity.");
            return record;
        }
    }

    public void Save(CampaignTestRecord record)
    {
        lock (_gate)
        {
            var path = FilePath(record.Owner, record.DraftId);
            var previous = Read(record.Owner, record.DraftId);
            if (previous?.RetiredProfiles.Contains(record.ProfileId) == true)
                throw new InvalidOperationException("This test character has been retired.");
            SeasonRepository.Atomic(path, JsonConvert.SerializeObject(record));
        }
    }
}
