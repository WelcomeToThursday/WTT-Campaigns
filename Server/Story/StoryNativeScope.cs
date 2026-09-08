using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Utils.Cloners;

namespace SeasonalPerks.Server.Story;

// Native helpers must see the staged full profile, including mail and reward state.
// AsyncLocal keeps autosaves and unrelated requests outside this transaction.
internal sealed class StoryNativeScope : IDisposable
{
    private static readonly AsyncLocal<StoryNativeScope?> Slot = new();
    internal static StoryNativeScope? Current
    {
        get { return Slot.Value; }
    }
    internal MongoId Id { get; }
    internal SptProfile Profile { get; }
    internal ItemEventRouterResponse Output { get; }
    internal List<Func<Task>> Notifications { get; } = new();

    internal StoryNativeScope(MongoId id, SptProfile profile, ICloner cloner)
    {
        if (Slot.Value != null)
        {
            throw new InvalidOperationException("Nested story transactions are not supported.");
        }
        Id = id;
        Profile = profile;
        var pmc = profile.CharacterData!.PmcData!;
        Output = new ItemEventRouterResponse
        {
            ProfileChanges = new()
            {
                [id] = new ProfileChange
                {
                    Id = id,
                    Experience = pmc.Info!.Experience,
                    Quests = [],
                    RagFairOffers = [],
                    WeaponBuilds = [],
                    EquipmentBuilds = [],
                    Items = new ItemChanges
                    {
                        NewItems = [],
                        ChangedItems = [],
                        DeletedItems = [],
                    },
                    Production = [],
                    Improvements = [],
                    Skills = new Skills
                    {
                        Common = [],
                        Mastering = [],
                        Points = 0,
                    },
                    Health = cloner.Clone(pmc.Health)!,
                    TraderRelations = [],
                    QuestsStatus = [],
                },
            },
            Warnings = [],
        };
        Slot.Value = this;
    }

    public void Dispose()
    {
        if (ReferenceEquals(Slot.Value, this))
        {
            Slot.Value = null;
        }
    }
}
