using Newtonsoft.Json;
using WTT.Campaigns.Server.Web.Authoring;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Tests;

internal static class StoryV2Checks
{
    internal static void Run(Action<bool, string> check)
    {
        var definition = new StoryDefinition();
        var state = new StoryProgress();
        var facts = new StoryFacts { TraderId = "previous", Scene = "lobby" };
        var entry = new StoryEntryPoint
        {
            TraderId = "current",
            Scene = "lobby",
            Condition = new() { Type = "CurrentTrader", Target = "current" },
        };
        check(StoryProjection.EntryAvailable(entry, definition, state, facts), "Entry eligibility uses the visited trader");
        check(facts.TraderId == "previous", "Entry projection does not change unrelated trader context");
        facts.TraderId = "";
        check(StoryProjection.EntryAvailable(entry, definition, state, facts), "First trader visit does not need a prior conversation");
        facts.Scene = "other";
        check(!StoryProjection.EntryAvailable(entry, definition, state, facts), "Exact entry scene restriction applies");
        entry.Scene = "";
        check(StoryProjection.EntryAvailable(entry, definition, state, facts), "Empty scene preserves unrestricted entries");
        facts.InRaid = true;
        check(!StoryProjection.EntryAvailable(entry, definition, state, facts), "Lobby entries cannot enter from raid context");
        entry.Kind = "ViaRadio";
        check(StoryProjection.EntryAvailable(entry, definition, state, facts), "Radio entries use the same trader-context projection");

        var line = new StoryDialogLine { Side = "Npc" };
        check(StoryPlaybackRules.WaitForContinue(line, true, false), "Intermediate text line waits for Continue");
        check(StoryPlaybackRules.WaitForContinue(line, false, true), "Closing text remains until acknowledged");
        check(!StoryPlaybackRules.WaitForContinue(line, false, false), "Final open line exposes replies without an extra Continue");
        line.Playback.Sound = "voice";
        check(!StoryPlaybackRules.WaitForContinue(line, true, false), "Voiced lines retain playback pacing");
        line.Playback.Sound = "";
        line.Side = "Player";
        check(!StoryPlaybackRules.WaitForContinue(line, true, true), "Player reply is not acknowledged twice");

        const string target = "123456789012345678901234";
        line.Actions.Add(new() { Type = StoryActionType.CompleteItem, Target = target });
        definition.Dialogs.Add(new() { Lines = [line] });
        check(StoryProjection.Variables(definition, state)[target] == 0, "Uncompleted standalone item flag initializes native state");
        state.CompletedItems.Add(target);
        check(
            StoryProjection.Variables(definition, state)[target] == 1,
            "Standalone CompleteItem is projected without a collectible binding"
        );
        definition.Dialogs.Clear();
        check(!StoryProjection.Variables(definition, state).ContainsKey(target), "Removed story target leaves the owned projection");

        var raid = new StoryRaid { Id = "raid" };
        var observed = new StoryRaidObservation
        {
            CharacterId = "character",
            RaidId = raid.Id,
            Sequence = 1,
            Level = 5,
        };
        var conditions = new HashSet<string> { target };
        observed.Counters[target] = 0;
        StoryObservationRules.Validate(observed, "character", raid, conditions);
        facts = new()
        {
            Items = new() { [target] = 8 },
            CompletedConditions = [target],
            ConditionCounters = new() { [target] = 8 },
        };
        StoryObservationRules.Apply(facts, observed);
        check(
            facts.Items.Count == 0 && facts.ConditionCounters[target] == 0 && !facts.CompletedConditions.Contains(target),
            "Raid projection excludes stash items and resets reversible local progress"
        );
        observed.Items.Add(
            new()
            {
                Id = "223456789012345678901234",
                Template = target,
                StackCount = 2,
            }
        );
        observed.CompletedConditions.Add(target);
        observed.Counters[target] = 2;
        StoryObservationRules.Apply(facts, observed);
        check(facts.Items[target] == 2 && facts.CompletedConditions.Contains(target), "Pickup and objective completion update raid facts");
        observed.Items.Clear();
        observed.CompletedConditions.Clear();
        observed.Counters[target] = 0;
        StoryObservationRules.Apply(facts, observed);
        check(
            !facts.Items.ContainsKey(target) && !facts.CompletedConditions.Contains(target),
            "Dropping items and local resets remove stale gates"
        );
        void Reject(Action action, string name)
        {
            var rejected = false;
            try
            {
                action();
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }
            check(rejected, name);
        }
        Reject(() => StoryObservationRules.Validate(observed, "other", raid, conditions), "Another character's raid facts are rejected");
        raid.Finished = true;
        Reject(() => StoryObservationRules.Validate(observed, "character", raid, conditions), "Finished raid facts are rejected");
        raid.Finished = false;
        observed.Counters[target] = double.NaN;
        Reject(() => StoryObservationRules.Validate(observed, "character", raid, conditions), "Nonfinite observed counters are rejected");
        observed.Counters[target] = -1;
        Reject(() => StoryObservationRules.Validate(observed, "character", raid, conditions), "Negative observed counters are rejected");
        observed.Counters.Clear();
        observed.Counters["unknown"] = 1;
        Reject(() => StoryObservationRules.Validate(observed, "character", raid, conditions), "Unknown observed objectives are rejected");

        var resource = new StoryMedia { Kind = "TraderScene" };
        check(!JsonConvert.SerializeObject(resource).Contains("TraderId"), "Unset room assignment preserves legacy media serialization");
        resource.TraderId = target;
        check(
            JsonConvert.DeserializeObject<StoryMedia>(JsonConvert.SerializeObject(resource))!.TraderId == target,
            "Custom room trader assignment survives packs"
        );
        check(StoryAuthoring.Visible(resource, "TraderId"), "Custom room editor exposes trader assignment");
        resource.Kind = "Image";
        check(!StoryAuthoring.Visible(resource, "TraderId"), "Images do not expose room assignment");
        check(
            StoryAuthoring.ReferenceKind(new StoryRaidBinding(), "MediaId") == "eventmedia",
            "Ordinary raid events select playable media"
        );
        check(new StoryRequest().Version == 2 && new StoryResponse().Version == 2, "Story client protocol defaults to version two");
        var preparation = new WTT.Campaigns.Server.Story.StoryPreparation { Identity = "operation", ProfileHash = "profile" };
        var draw = preparation.Draw(0, 100);
        check(
            Enumerable.Range(0, 10).All(_ => preparation.Draw(0, 100) == draw),
            "Preparation retries and commit reuse the same random outcome"
        );
        Reject(() => preparation.Draw(0, 101), "A changed prepared random group is rejected");
    }
}
