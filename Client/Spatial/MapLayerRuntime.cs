using Comfort.Common;
using EFT;
using Newtonsoft.Json;
using SPT.Common.Http;
using UnityEngine;
using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.Client.Missions;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Spatial;

/// <summary>Owns one ordinary raid's scenery; never enters the mission runtime.</summary>
internal sealed class MapLayerRuntime : MonoBehaviour
{
    private sealed class Run : IDisposable
    {
        internal readonly CancellationTokenSource Lifetime = new();
        internal readonly MapSceneAdapter Scene = new();
        internal readonly MissionLoot Loot = new();
        internal GameWorld World = null!;
        internal Player Player = null!;
        internal string Character = "",
            Season = "",
            Location = "";
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Lifetime.Cancel();
            try
            {
                Loot.Dispose();
            }
            finally
            {
                try
                {
                    Scene.Dispose();
                }
                finally
                {
                    Lifetime.Dispose();
                }
            }
        }
    }

    private Run? _run;
    private GameWorld? _excludedWorld;

    private static string Character => Plugin.App?.Session?.Profile?.Id ?? "";
    private static string Season => Plugin.SeasonalPlayer ? MissionClient.SeasonId : "";
    private bool Eligible =>
        Plugin.InRaid
        && !ReferenceEquals(_excludedWorld, Singleton<GameWorld>.Instance)
        && !string.IsNullOrEmpty(Character)
        && Plugin.Player?.Profile.Id == Character
        && !Authoring.EditorMode.Active
        && !MissionRaidRuntime.HasPending
        && !MissionRaidRuntime.Starting
        && !MissionRaidRuntime.Active;

    private void Update()
    {
        // A mission/editor world stays excluded even after its runtime begins shutting down.
        if (
            Singleton<GameWorld>.Instantiated
            && (Authoring.EditorMode.Active || MissionRaidRuntime.HasPending || MissionRaidRuntime.Starting || MissionRaidRuntime.Active)
        )
            _excludedWorld = Singleton<GameWorld>.Instance;
        if (_run != null && !Current(_run))
            Clear();
        if (_run != null || !Eligible)
            return;
        var run = new Run
        {
            World = Singleton<GameWorld>.Instance,
            Player = Plugin.Player!,
            Character = Character,
            Season = Season,
            Location = ZoneRuntime.Location,
        };
        _run = run;
        _ = Apply(run, run.Lifetime.Token);
    }

    private bool Current(Run run) =>
        ReferenceEquals(_run, run)
        && Eligible
        && Singleton<GameWorld>.Instantiated
        && ReferenceEquals(run.World, Singleton<GameWorld>.Instance)
        && ReferenceEquals(run.Player, Plugin.Player)
        && run.Character == Character
        && run.Season == Season
        && run.Location == ZoneRuntime.Location;

    private void RequireCurrent(Run run, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Current(run))
            throw new OperationCanceledException("The map layer raid ended.");
    }

    private async Task Apply(Run run, CancellationToken token)
    {
        try
        {
            var json = await RequestHandler.PostJsonAsync(
                "/wtt-campaigns/map-layers",
                JsonConvert.SerializeObject(
                    new MapLayerRequest
                    {
                        CharacterId = run.Character,
                        SeasonId = run.Season,
                        Location = run.Location,
                    }
                )
            );
            RequireCurrent(run, token);
            var response =
                JsonConvert.DeserializeObject<MapLayerResponse>(json)
                ?? throw new InvalidOperationException("Map layers did not receive a server response.");
            if (response.Error != null)
                throw new InvalidOperationException(response.Error);
            if (
                response.CharacterId != run.Character
                || response.SeasonId != run.Season
                || response.Location != run.Location
                || string.IsNullOrWhiteSpace(response.RaidId)
                || response.Layout != null && response.Layout.Location != run.Location
            )
                throw new InvalidOperationException("Map layers belong to another raid or map.");
            if (response.Layout == null)
                return;
            await run.Scene.ApplyAsync(response.Layout, requirePlayerRoute: false, token: token, runtime: true);
            RequireCurrent(run, token);
            await run.Loot.ApplyAsync(response.Layout, response.RaidId, token, response.ContainerLoot);
            RequireCurrent(run, token);
            Plugin.LogInfo("Applied normal raid map layers on " + run.Location + ".");
        }
        catch (OperationCanceledException)
        {
            Dispose(run);
        }
        catch (Exception exception)
        {
            Dispose(run);
            Plugin.Error(
                new InvalidOperationException("Normal raid map layers could not be applied; changes were rolled back.", exception)
            );
            if (Current(run))
                EFT.UI.PreloaderUI.Instance.ShowErrorScreen("Map layers", "Map layers could not be applied. " + exception.Message);
        }
        // Retain this world's attempted run after failure: do not retry and duplicate loot each frame.
    }

    private static void Dispose(Run run)
    {
        try
        {
            run.Dispose();
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
        }
    }

    private void Clear()
    {
        var run = _run;
        _run = null;
        if (run != null)
            Dispose(run);
    }

    private void OnDestroy() => Clear();
}
