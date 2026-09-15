using System.Reflection;
using EFT;
using EFT.HealthSystem;
using EFT.InputSystem;
using HarmonyLib;
using UnityEngine;

namespace WTT.Campaigns.Client.Missions;

/// <summary>
/// Holds the campaign player in the native raid while the server-bound mission
/// descriptor and authored world are being prepared.  The guard is deliberately
/// short-lived and only applies to the exact player that entered the mission run.
/// </summary>
internal static class MissionStartupGuard
{
    private static readonly object Gate = new();
    private static Harmony? _harmony;
    private static bool _installed;
    private static string _error = "";
    private static Player? _player;
    private static Vector3 _position;
    private static Vector2 _rotation;
    private static bool _active;

    internal static bool Active
    {
        get
        {
            lock (Gate)
            {
                return _active;
            }
        }
    }

    internal static bool IsBlocking(Player? player)
    {
        lock (Gate)
        {
            return _active && player != null && ReferenceEquals(_player, player);
        }
    }

    internal static void Install()
    {
        lock (Gate)
        {
            if (_installed)
                return;

            try
            {
                _harmony = new Harmony("com.wtt.campaigns.mission-startup");
                var input = AccessTools.Method(typeof(InputNode), nameof(InputNode.TranslateInput));
                if (input == null)
                    throw new MissingMethodException(typeof(InputNode).FullName, nameof(InputNode.TranslateInput));
                _harmony.Patch(input, prefix: new HarmonyMethod(typeof(MissionStartupGuard), nameof(InputPrefix)));

                PatchHealth(nameof(ActiveHealthController.ApplyDamage), nameof(DamagePrefix));
                PatchHealth(nameof(ActiveHealthController.ChangeHealth), nameof(HealthPrefix));
                PatchHealth(nameof(ActiveHealthController.ChangeEnergy), nameof(HealthPrefix));
                PatchHealth(nameof(ActiveHealthController.ChangeHydration), nameof(HealthPrefix));
                PatchHealth(nameof(ActiveHealthController.Kill), nameof(HealthPrefix));
            }
            catch (Exception exception)
            {
                // A missing native seam must fail closed when a mission tries to
                // start. Ordinary raids remain untouched until the guard is active.
                _error = exception.Message;
                Plugin.Error(new InvalidOperationException("Mission startup protection is unavailable.", exception));
            }
            finally
            {
                _installed = true;
            }
        }
    }

    internal static void Begin(Player player)
    {
        if (player == null)
            throw new InvalidOperationException("The mission player is unavailable.");

        Install();
        if (_error.Length > 0)
            throw new InvalidOperationException("Mission startup protection is unavailable: " + _error);
        lock (Gate)
        {
            _player = player;
            _position = player.Transform.position;
            _rotation = player.Rotation;
            _active = true;
        }
    }

    internal static void Hold(Player player)
    {
        Vector3 position;
        Vector2 rotation;
        lock (Gate)
        {
            if (!_active || !ReferenceEquals(_player, player))
                return;
            position = _position;
            rotation = _rotation;
        }

        try
        {
            player.Teleport(position);
            player.Rotation = rotation;
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
        }
    }

    internal static void End(Player? player = null)
    {
        lock (Gate)
        {
            if (player != null && _player != null && !ReferenceEquals(_player, player))
                return;
            _active = false;
            _player = null;
            _position = default;
            _rotation = default;
        }
    }

    private static void PatchHealth(string name, string prefixName)
    {
        var methods = typeof(ActiveHealthController).GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
        );
        var found = false;
        foreach (var method in methods)
        {
            if (!string.Equals(method.Name, name, StringComparison.Ordinal))
                continue;
            found = true;
            _harmony!.Patch(method, prefix: new HarmonyMethod(typeof(MissionStartupGuard), prefixName));
        }

        if (!found)
            throw new MissingMethodException(typeof(ActiveHealthController).FullName, name);
    }

    private static bool InputPrefix(InputNode __instance, List<ECommand> commands, ref float[]? axes, ref ECursorResult shouldLockCursor)
    {
        if (!Active)
            return true;

        commands.Clear();
        shouldLockCursor = ECursorResult.ShowCursor;
        if (axes != null)
            Array.Clear(axes, 0, axes.Length);
        return false;
    }

    private static bool DamagePrefix(ActiveHealthController __instance, ref float __result)
    {
        if (!IsBlocking(__instance?.Player))
            return true;
        __result = 0;
        return false;
    }

    private static bool HealthPrefix(ActiveHealthController __instance)
    {
        return !IsBlocking(__instance?.Player);
    }
}
