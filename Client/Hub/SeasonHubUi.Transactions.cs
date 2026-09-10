using Newtonsoft.Json;
using SeasonalPerks.Shared.Contracts;
using SeasonalPerks.UI.Models;
using SPT.Common.Http;
using ZLinq;

namespace SeasonalPerks.Client.Hub;

public sealed partial class SeasonHubUi
{
    private bool _transacting;
    private string? _pendingBody;
    private string? _pendingAction;
    private string? _pendingProfile;
    private bool _needsReload;
    private readonly Dictionary<string, (string Body, string Action, bool Reload)> _pendingByProfile = new();

    private void RestorePending()
    {
        var profile = Plugin.Current?.EffectiveProfileId;
        if (_pendingBody != null && _pendingProfile != null)
        {
            _pendingByProfile[_pendingProfile] = (_pendingBody, _pendingAction!, _needsReload);
        }
        _pendingProfile = profile;
        _pendingBody = null;
        _needsReload = false;
        if (profile != null && _pendingByProfile.TryGetValue(profile, out var pending))
        {
            _pendingBody = pending.Body;
            _pendingAction = pending.Action;
            _needsReload = pending.Reload;
        }
        else if (profile != null && File.Exists(PendingPath(profile)))
        {
            var saved =
                JsonConvert.DeserializeObject<PendingHubOperation>(File.ReadAllText(PendingPath(profile)))
                ?? throw new InvalidDataException("Invalid pending hub operation.");
            _pendingBody = saved.Body;
            _pendingAction = saved.Action;
            if (_pendingAction is not ("claim" or "exchange"))
            {
                throw new InvalidDataException("Invalid pending seasonal operation.");
            }
        }
    }

    private static string PendingPath(string profile)
    {
        if (profile.Length != 24 || profile.AsValueEnumerable().Any(c => !Uri.IsHexDigit(c)))
        {
            throw new InvalidDataException("Invalid pending-operation profile identifier.");
        }
        return Path.Combine(Plugin.Folder, "hub-pending-" + profile + ".json");
    }

    private void PersistPending()
    {
        var path = PendingPath(_pendingProfile!);
        File.WriteAllText(
            path + ".tmp",
            JsonConvert.SerializeObject(new PendingHubOperation { Body = _pendingBody!, Action = _pendingAction! })
        );
        if (File.Exists(path))
        {
            File.Replace(path + ".tmp", path, null);
        }
        else
        {
            File.Move(path + ".tmp", path);
        }
    }

    private void ClearPending()
    {
        File.Delete(PendingPath(_pendingProfile!));
        _pendingByProfile.Remove(_pendingProfile!);
        _pendingBody = null;
    }

    private async void Transact(HubAction action)
    {
        if (_transacting || !Available || _pendingBody != null)
        {
            return;
        }
        try
        {
            _transacting = true;
            Plugin.Busy = true;
            _screen!.ShowMessage("Saving inventory...", false);
            await Plugin.FlushPendingOperations();
            var body = new HubMutation
            {
                ProtocolVersion = 2,
                SeasonId = Plugin.Current!.SeasonId,
                PackRevision = Plugin.Current.PackRevision,
                OperationId = Guid.NewGuid().ToString("N"),
                ExpectedRevision = action.ExpectedRevision,
                RewardId = action.RewardId,
                UseClassified = action.UseClassified,
                DocumentId = action.DocumentId,
                Crate = action.Crate,
                Sources = new(action.Sources),
            };
            _pendingBody = JsonConvert.SerializeObject(body);
            _pendingAction = action.Action;
            _pendingProfile = Plugin.Current!.EffectiveProfileId;
            PersistPending();
        }
        catch (Exception e)
        {
            _pendingBody = null;
            Plugin.Error(e);
            if (IsOpen)
            {
                _screen!.ShowMessage("The inventory could not be saved. Try again.", true);
            }
        }
        finally
        {
            _transacting = false;
            Plugin.Busy = false;
        }
        if (_pendingBody != null)
        {
            await SendPending();
        }
    }

    private async Task SendPending()
    {
        if (_transacting || _pendingBody == null || _pendingProfile != Plugin.Current?.EffectiveProfileId || Plugin.InRaid)
        {
            return;
        }
        _transacting = true;
        Plugin.Busy = true;
        try
        {
            if (IsOpen)
            {
                _screen!.ShowMessage("Applying seasonal transaction...", false);
            }
            var raw = await RequestHandler.PostJsonAsync("/wtt-seasonal/hub/" + _pendingAction, _pendingBody);
            var result = JsonConvert.DeserializeObject<HubResult>(raw) ?? throw new InvalidDataException("Invalid hub response.");
            var error = result.Error;
            if (error.Length > 0)
            {
                ClearPending();
                Plugin.Busy = false;
                await LoadState();
                if (IsOpen)
                {
                    _screen!.ShowResult(error);
                }
                return;
            }
            if (result.Committed != true)
            {
                throw new InvalidDataException("The server did not confirm the transaction.");
            }
            _needsReload = true;
            await Plugin.Reload(await Plugin.Request("snapshot"));
            _needsReload = false;
            ClearPending();
            Plugin.Busy = false;
            if (IsOpen)
            {
                await LoadState();
                _screen!.ShowResult(result.Message);
            }
        }
        catch (Exception e)
        {
            Plugin.Error(e);
            if (IsOpen)
            {
                _screen!.ShowMessage(
                    _needsReload
                        ? "Reward saved. Retry to reload your inventory."
                        : "Result not confirmed. Retry safely using the same operation.",
                    true
                );
            }
        }
        finally
        {
            _transacting = false;
            Plugin.Busy = false;
        }
    }
}
