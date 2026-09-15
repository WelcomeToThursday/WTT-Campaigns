using System.Reflection;
using HarmonyLib;
using SAIN.Components;
using SAIN.Components.CoverFinder;
using SAIN.SAINComponent.SubComponents.CoverFinder;
using UnityEngine;
using WTT.Campaigns.Client.Authoring;

namespace WTT.Campaigns.Client.Encounters;

// Extends SAIN's existing candidate pipeline only for registered encounter bots.
// It never fabricates cover points or bypasses SAIN's threat/visibility checks.
internal sealed class EncounterCoverRuntime
{
    private sealed class Binding
    {
        internal BotComponent Bot = null!;
        internal CoverFinderComponent Finder = null!;
        internal SainBotCoverData Data = null!;
        internal CoverAnalyzer Analyzer = null!;
        internal HashSet<Collider> NativeSet = null!;
        internal SainBotCoverData.BotColliderQueryParams Query;
        internal bool HasQuery;
        internal float NextRefresh;
        internal int Added,
            Rejected,
            Invalidated;
        internal readonly Dictionary<Collider, (Bounds Bounds, Quaternion Rotation)> Shapes = new();
        internal readonly EncounterNavigation Navigation = new();
    }

    private static readonly Dictionary<SainBotCoverData, Binding> DataOwners = new();
    private static readonly Dictionary<CoverAnalyzer, Binding> Analyzers = new();
    private static readonly Dictionary<EFT.BotOwner, Binding> Bots = new();
    private readonly List<Binding> _owned = new();
    private static bool _installed;
    private static readonly FieldInfo DataField = AccessTools.Field(typeof(CoverFinderComponent), "CoverData");
    private static readonly PropertyInfo AnalyzerProperty = AccessTools.Property(typeof(CoverFinderComponent), "CoverAnalyzer");
    private static readonly FieldInfo SetField = AccessTools.Field(typeof(SainBotCoverData), "validCollidersHashSet");

    internal void Add(EFT.BotOwner owner)
    {
        Install();
        var bot = owner.GetComponent<BotComponent>();
        var finder = bot ? bot.Cover?.CoverFinder : null;
        if (!finder)
            throw new InvalidOperationException("SAIN cover finder is not ready for the encounter bot.");
        var data = (SainBotCoverData)DataField.GetValue(finder);
        var binding = new Binding
        {
            Bot = bot,
            Finder = finder!,
            Data = data,
            Analyzer = (CoverAnalyzer)AnalyzerProperty.GetValue(finder),
            NativeSet = (HashSet<Collider>)SetField.GetValue(data),
        };
        DataOwners.Add(data, binding);
        Analyzers.Add(binding.Analyzer, binding);
        Bots.Add(owner, binding);
        _owned.Add(binding);
    }

    private static void Install()
    {
        if (_installed)
            return;
        if (DataField == null || AnalyzerProperty == null || SetField == null)
            throw new InvalidOperationException("Installed SAIN cover cache contract is unsupported.");
        var harmony = new Harmony("com.wtt.campaigns.encounter.cover");
        harmony.Patch(
            AccessTools.Method(typeof(SainBotCoverData), nameof(SainBotCoverData.OverlapBoxAndFilter)),
            postfix: new HarmonyMethod(typeof(EncounterCoverRuntime), nameof(Query))
        );
        harmony.Patch(
            AccessTools.Method(typeof(SainBotCoverData), nameof(SainBotCoverData.HandleLists)),
            prefix: new HarmonyMethod(typeof(EncounterCoverRuntime), nameof(Refresh))
        );
        harmony.Patch(
            AccessTools.Method(typeof(CoverAnalyzer), nameof(CoverAnalyzer.CheckCreateNewCoverPoint)),
            prefix: new HarmonyMethod(typeof(EncounterCoverRuntime), nameof(Creating)),
            postfix: new HarmonyMethod(typeof(EncounterCoverRuntime), nameof(Created))
        );
        harmony.Patch(
            AccessTools.Method(typeof(CoverAnalyzer), nameof(CoverAnalyzer.RecheckCoverPoint)),
            prefix: new HarmonyMethod(typeof(EncounterCoverRuntime), nameof(Rechecking)),
            postfix: new HarmonyMethod(typeof(EncounterCoverRuntime), nameof(Rechecked))
        );
        _installed = true;
    }

    private static void Query(SainBotCoverData __instance, SainBotCoverData.BotColliderQueryParams parameters)
    {
        if (DataOwners.TryGetValue(__instance, out var binding))
        {
            binding.Query = parameters;
            binding.HasQuery = true;
        }
    }

    private static bool Solid(Collider collider) =>
        collider && collider.enabled && !collider.isTrigger && collider.gameObject.activeInHierarchy;

    private static void Refresh(SainBotCoverData __instance)
    {
        if (!DataOwners.TryGetValue(__instance, out var binding) || !binding.Bot || Time.time < binding.NextRefresh)
            return;
        binding.NextRefresh = Time.time + 1;
        var seen = new HashSet<Collider>();
        // Run at SAIN's loop boundary, before its coroutine captures list indices.
        for (var i = __instance.ValidCollidersList.Count - 1; i >= 0; i--)
        {
            var item = __instance.ValidCollidersList[i];
            var collider = item.Collider;
            if (!Solid(collider))
            {
                if (item.CoverPoint != null)
                    item.CoverPoint.CoverData.IsBad = true;
                binding.NativeSet.Remove(collider);
                __instance.ValidCollidersList.RemoveAt(i);
                binding.Invalidated++;
                continue;
            }
            seen.Add(collider);
            var shape = (collider.bounds, collider.transform.rotation);
            if (
                binding.Shapes.TryGetValue(collider, out var prior)
                && (!prior.Bounds.Equals(shape.bounds) || prior.Rotation != shape.rotation)
            )
            {
                if (item.CoverPoint != null)
                    item.CoverPoint.CoverData.IsBad = true;
                item.CoverPoint = null;
                __instance.ValidCollidersList[i] = item;
                binding.Invalidated++;
            }
            binding.Shapes[collider] = shape;
        }
        foreach (var collider in new List<Collider>(binding.Shapes.Keys))
            if (!seen.Contains(collider))
                binding.Shapes.Remove(collider);
        binding.Finder.CoverPoints.RemoveAll(point => point == null || !Solid(point.Collider) || point.CoverData.IsBad);
        if (!binding.HasQuery)
            return;
        var query = binding.Query;
        var area = new Bounds(query.origin, query.halfExtents * 2);
        var added = 0;
        SceneNavigation.VisitCoverColliders(collider =>
        {
            if (added >= 64 || binding.NativeSet.Contains(collider) || !area.Intersects(collider.bounds))
                return;
            var size = collider.bounds.size;
            if (
                size.x > query.maxColliderSize.x
                || size.y > query.maxColliderSize.y
                || size.z > query.maxColliderSize.z
                || size.y < query.minColliderSize.y
                || (size.x < query.minColliderSize.x && size.z < query.minColliderSize.z)
            )
                return;
            binding.NativeSet.Add(collider);
            __instance.ValidCollidersList.Add(new SainBotColliderData(collider));
            binding.Shapes[collider] = (collider.bounds, collider.transform.rotation);
            added++;
        });
        binding.Added += added;
    }

    private static bool Validate(Binding binding, CoverPoint point, Vector3 botPosition)
    {
        if (point == null || !Solid(point.Collider))
            return false;
        var position = new WTT.Campaigns.Shared.Spatial.SpatialVector
        {
            X = point.Position.x,
            Y = point.Position.y,
            Z = point.Position.z,
        };
        return binding.Navigation.HasStandingClearance(position, binding.Bot.BotOwner.GetPlayer)
            && binding.Navigation.HasCompletePath(botPosition, point.Position);
    }

    private static bool Creating(
        CoverAnalyzer __instance,
        Collider collider,
        ref CoverPoint coverPoint,
        ref string reason,
        ref bool __result
    )
    {
        if (!Analyzers.ContainsKey(__instance) || Solid(collider))
            return true;
        coverPoint = null!;
        reason = "Campaign cover collider is inactive";
        __result = false;
        return false;
    }

    private static bool Rechecking(CoverAnalyzer __instance, CoverPoint coverPoint, ref string reason, ref bool __result)
    {
        if (!Analyzers.ContainsKey(__instance) || (coverPoint != null && Solid(coverPoint.Collider)))
            return true;
        if (coverPoint != null)
            coverPoint.CoverData.IsBad = true;
        reason = "Campaign cover collider is inactive";
        __result = false;
        return false;
    }

    private static void Created(
        CoverAnalyzer __instance,
        Vector3 botPosition,
        ref CoverPoint coverPoint,
        ref string reason,
        ref bool __result
    )
    {
        if (__result && Analyzers.TryGetValue(__instance, out var binding) && !Validate(binding, coverPoint, botPosition))
        {
            if (coverPoint != null)
                coverPoint.CoverData.IsBad = true;
            coverPoint = null!;
            reason = "Campaign cover blocked, inactive, or unreachable";
            binding.Rejected++;
            __result = false;
        }
    }

    private static void Rechecked(
        CoverAnalyzer __instance,
        CoverPoint coverPoint,
        Vector3 botPosition,
        ref string reason,
        ref bool __result
    )
    {
        if (__result && Analyzers.TryGetValue(__instance, out var binding) && !Validate(binding, coverPoint, botPosition))
        {
            if (coverPoint != null)
                coverPoint.CoverData.IsBad = true;
            reason = "Campaign cover blocked, inactive, or unreachable";
            binding.Rejected++;
            __result = false;
        }
    }

    internal static string Describe(EFT.BotOwner owner)
    {
        if (!Bots.TryGetValue(owner, out var binding))
            return "Cover not registered";
        var chosen = binding.Bot.Cover.CoverInUse;
        return $"cover: candidates={binding.Data.ValidCollidersList.Count}, accepted={binding.Finder.CoverPoints.Count}, authoredAdded={binding.Added}, invalidated={binding.Invalidated}, rejected={binding.Rejected}, selected={(chosen == null ? "none" : chosen.Position.ToString("F2"))}";
    }

    internal void Reset()
    {
        foreach (var binding in _owned)
        {
            DataOwners.Remove(binding.Data);
            Analyzers.Remove(binding.Analyzer);
            foreach (var bot in new List<EFT.BotOwner>(Bots.Keys))
                if (ReferenceEquals(Bots[bot], binding))
                    Bots.Remove(bot);
        }
        _owned.Clear();
    }
}
