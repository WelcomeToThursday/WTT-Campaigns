using SeasonalPerks.Shared.Contracts;
using SeasonalPerks.UI.Screens;
using ZLinq;

namespace SeasonalPerks.Client.UI;

public sealed partial class SeasonUi
{
    private void ChooseSeason(string seasonId)
    {
        ChooseSeason(seasonId, "");
    }

    private async void ChooseSeason(string seasonId, string characterId)
    {
        if (Plugin.Busy || Plugin.InRaid || _screen == null)
        {
            return;
        }

        Plugin.Busy = true;
        _screen.SetBusy(true, "Loading season modifiers...");
        try
        {
            var preview = await Plugin.Request("snapshot", new Mutation { SeasonId = seasonId, CharacterId = characterId });
            if (_destroyed)
            {
                return;
            }

            _screen.SetBusy(false);
            _screen.BeginCreation(Presentation(preview), characterId);
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
            _screen.SetBusy(false);
            _screen.SetMessage(exception.Message, true);
        }
        finally
        {
            Plugin.Busy = false;
        }
    }

    private async void ManageCharacter(string characterId, bool wipe)
    {
        if (Plugin.Busy || Plugin.InRaid || _screen == null)
        {
            return;
        }

        Plugin.Busy = true;
        _screen.SetBusy(true, wipe ? "Resetting character..." : "Deleting character...");
        try
        {
            await Plugin.FlushPendingOperations();
            // Leave the character's session before removing its profile. This also
            // keeps recovery requests authenticated after a lost response.
            if (Plugin.Current?.EffectiveProfileId == characterId)
            {
                await Plugin.Reload(await Plugin.Request("switch", new Mutation { Mode = "normal" }));
            }
            await Plugin.Request(
                wipe ? "wipe" : "delete",
                new Mutation { CharacterId = characterId, OperationId = Guid.NewGuid().ToString("N") }
            );
            var snapshot = await Plugin.Request("snapshot");
            Plugin.Accept(snapshot);
            _screen.SetBusy(false);
            _screen.SetState(Presentation(snapshot), ScreenPage.Characters);
            if (wipe)
            {
                var character = snapshot.Characters.AsValueEnumerable().Single(c => c.Id == characterId);
                var preview = await Plugin.Request("snapshot", new Mutation { SeasonId = character.SeasonId, CharacterId = characterId });
                _screen.BeginCreation(Presentation(preview), characterId);
                _screen.SetMessage("Progress reset. Earned achievements kept. Choose your new character.");
            }
            else
            {
                _screen.SetMessage("Character deleted.");
            }
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
            _screen.SetBusy(false);
            try
            {
                var snapshot = await Plugin.Request("snapshot");
                Plugin.Accept(snapshot);
                if (snapshot.EffectiveProfileId != Plugin.App?.Session?.Profile?.Id)
                {
                    _startup = true;
                    _screen.StartupSelection = true;
                }
                _screen.SetState(Presentation(snapshot), ScreenPage.Characters);
            }
            catch (Exception recovery)
            {
                Plugin.Error(recovery);
            }
            _screen.SetMessage(exception.Message, true);
        }
        finally
        {
            Plugin.Busy = false;
        }
    }
}
