using Mono.Cecil;
using WTT.Campaigns.Client.Authoring.Scenes;

namespace WTT.Campaigns.Tests;

internal static class ScenePreviewChecks
{
    internal static void Client(ModuleDefinition client, Action<bool, string> check)
    {
        var preview = client.GetType("WTT.Campaigns.Client.Authoring.Scenes.ScenePreviewModel");
        var calls = preview
            .Methods.Concat(preview.NestedTypes.SelectMany(t => t.Methods))
            .Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions)
            .Select(i => i.Operand)
            .OfType<MethodReference>()
            .ToArray();
        check(
            !calls.Any(m => m.Name == "Instantiate" || m.Name is "Supported" or "Restriction"),
            "Visual previews do not clone native behaviours or require gameplay placement support"
        );
        var added = calls
            .OfType<GenericInstanceMethod>()
            .Where(m => m.Name == "AddComponent")
            .SelectMany(m => m.GenericArguments)
            .Select(t => t.FullName)
            .ToArray();
        check(
            added.Length == 2 && added.All(t => t is "UnityEngine.MeshFilter" or "UnityEngine.MeshRenderer"),
            "Preview copies can only add mesh geometry, never colliders, scripts or native interactions"
        );
        foreach (var property in new[] { "rotation", "lossyScale", "localPosition", "localRotation", "localScale" })
            check(calls.Any(m => m.Name == "get_" + property), "Thumbnail geometry preserves source " + property);
        check(calls.Any(m => m.Name == "GetLODs"), "Thumbnail mesh selection accounts for native LOD groups");
        var render = client
            .GetType("WTT.Campaigns.Client.Authoring.Controllers.EditorCatalogController")
            .Methods.Single(m => m.Name == "RenderPropThumbnail");
        var renderCalls = render.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().ToArray();
        check(
            !renderCalls.Any(m => m.Name is "SetPositionAndRotation" or "set_rotation" or "set_localRotation" or "CopyForPlacement"),
            "Thumbnail rendering never discards the preserved orientation or uses the placement copy path"
        );
        check(
            renderCalls.Any(m => m.DeclaringType.Name == "ScenePreviewModel" && m.Name == "Copy"),
            "Current-map thumbnails use the geometry-only preview path"
        );
        IEnumerable<MethodDefinition> Methods(TypeDefinition type) => type.Methods.Concat(type.NestedTypes.SelectMany(Methods));
        MethodReference[] Calls(TypeDefinition type) =>
            Methods(type)
                .Where(m => m.HasBody)
                .SelectMany(m => m.Body.Instructions)
                .Select(i => i.Operand)
                .OfType<MethodReference>()
                .ToArray();
        var controllerCalls = Calls(client.GetType("WTT.Campaigns.Client.Authoring.Controllers.EditorCatalogController"));
        var catalogController = client.GetType("WTT.Campaigns.Client.Authoring.Controllers.EditorCatalogController");
        var sceneRowsCalls = catalogController
            .Methods.Single(m => m.Name == "SceneRows")
            .Body.Instructions.Select(i => i.Operand)
            .OfType<MethodReference>()
            .ToArray();
        check(
            !sceneRowsCalls.Any(m => m.DeclaringType.Name == "SceneAssetCatalog" && m.Name is "Start" or "Rescan"),
            "Opening the catalog never starts native bundle inspection"
        );
        var worldSelectionCalls = catalogController
            .Methods.Single(m => m.Name == "SelectSceneTarget")
            .Body.Instructions.Select(i => i.Operand)
            .OfType<MethodReference>()
            .ToArray();
        check(
            !worldSelectionCalls.Any(m => m.Name is "set__sceneTab" or "set_SceneTab"),
            "Picking world props, placed assets and doors preserves the browser tab"
        );
        var editor = client.GetType("WTT.Campaigns.Client.Authoring.Editor.RaidEditor");
        check(
            !editor
                .Methods.Single(m => m.Name == "EnterSceneSelection")
                .Body.Instructions.Select(i => i.Operand)
                .OfType<MethodReference>()
                .Any(m => m.Name == "set_SceneTab"),
            "Entering Scene from a world pick preserves its prior browser tab"
        );
        check(
            editor
                .Methods.Single(m => m.Name == "TrySelectionBounds")
                .Body.Instructions.Select(i => i.Operand)
                .OfType<MethodReference>()
                .Any(m => m.Name == "SelectionFrame"),
            "Selection bounds use the model geometry frame without altering the authored transform"
        );
        check(
            controllerCalls.Any(m => m.DeclaringType.Name == "SceneCatalogPage" && m.Name == "Merge"),
            "The shipped browser seeks into sorted catalogs and creates only the visible page"
        );
        var browser = client
            .GetType("WTT.Campaigns.Client.Authoring.Editor.RaidEditor")
            .Methods.Single(m => m.Name == "RefreshToolBrowser");
        check(
            browser.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().Count(m => m.Name == "get_PagedCatalog") >= 3,
            "Paged scene catalogs refresh on page changes and bypass full-catalog sorting and local row-count clamping"
        );
        check(
            controllerCalls.Any(m => m.DeclaringType.Name == "ItemIcon" && m.Name == "get_Changed")
                && controllerCalls.Any(m => m.DeclaringType.FullName == "Diz.Binding.BindableEvent" && m.Name == "Subscribe"),
            "Concurrent native icons snapshot at generation notification rather than after shared textures are reused"
        );
        var renderer = client.GetType("WTT.Campaigns.Client.Authoring.Scenes.SceneThumbnailRenderer");
        var rendererCalls = Calls(renderer);
        check(
            !rendererCalls.Any(m => m.Name == "ReadPixels" || m.DeclaringType.Name == "Texture2D"),
            "Thumbnail rendering and native snapshots never perform CPU texture readbacks"
        );
        check(
            rendererCalls.Any(m => m.Name == "Blit") && rendererCalls.Any(m => m.Name == "Release"),
            "GPU snapshots have explicit render-texture release paths"
        );
        var frameRender = renderer.Methods.Single(m => m.Name == "Render");
        check(
            !frameRender.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().Any(m => m.Name == "AddComponent"),
            "Rendering a thumbnail reuses its session camera"
        );
        var assets = client.GetType("WTT.Campaigns.Client.Authoring.Scenes.SceneAssetCatalog");
        var assetCalls = Calls(assets);
        check(
            assetCalls.Any(m => m.Name == "LoadAssetAsync")
                && !assetCalls.Any(m => m.Name is "LoadAsset" or "LoadScene" or "LoadSceneAsync"),
            "Asset discovery uses asynchronous requests and never loads an unloaded map scene"
        );
        check(
            !Methods(assets)
                .Where(m => m.HasBody)
                .SelectMany(m => m.Body.Instructions)
                .Any(i => i.Operand is string text && text == "maps/"),
            "Ordinary map-folder bundles are eligible for both discovery and placement"
        );
        check(
            assetCalls.Any(m => m.Name == "get_isStreamedSceneAssetBundle") && assetCalls.Any(m => m.Name == "Restriction"),
            "Bundle contents and placement restrictions gate independently placeable assets"
        );
        check(!assets.Methods.Any(m => m.Name is "Scan" or "Start" or "Rescan"), "Runtime bundle catalog scanning has been removed");
        var retainingMethods = Methods(assets)
            .Where(m => m.HasBody && m.Body.Instructions.Any(i => i.Operand is MethodReference call && call.Name == "Retain"))
            .ToArray();
        check(retainingMethods.Length == 1, "Only explicitly requested saved assets retain native dependencies");
        foreach (var method in retainingMethods)
        {
            var methodCalls = method.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().ToArray();
            check(
                Array.FindIndex(methodCalls, m => m.Name == "InspectArchives") >= 0
                    && Array.FindIndex(methodCalls, m => m.Name == "InspectArchives")
                        < Array.FindIndex(methodCalls, m => m.Name == "Retain"),
                "Scene archive preflight happens before native loading in " + method.DeclaringType.Name
            );
            check(methodCalls.Any(m => m.Name == "WaitForBundle"), "Native load faults are observed in " + method.DeclaringType.Name);
        }
        var advance = client.GetType("WTT.Campaigns.Client.Authoring.Editor.RaidEditor").Methods.Single(m => m.Name == "AdvanceSceneIndex");
        var levelLibrary = client.GetType("WTT.Campaigns.Client.Authoring.Scenes.NativeLevelPropLibrary");
        var levelCalls = Calls(levelLibrary);
        check(
            Methods(levelLibrary)
                .Where(m => m.HasBody)
                .SelectMany(m => m.Body.Instructions)
                .Any(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Stfld && i.Operand is FieldReference f && f.Name == "SelectionGeometry"),
            "Generated level prop loading retains its geometry root for oriented selection outlines"
        );
        check(
            levelCalls.Any(m => m.Name == "LoadFromFileAsync")
                && levelCalls.Any(m => m.Name == "LoadAssetAsync")
                && !levelCalls.Any(m => m.Name is "LoadScene" or "LoadSceneAsync" or "ReadPixels"),
            "Level props load generated assets asynchronously without activating unloaded maps or reading thumbnails through the CPU"
        );
        check(
            levelCalls.Any(m => m.Name == "ComputeHash") && levelCalls.Any(m => m.Name == "Unload"),
            "Generated resources are hash checked and released after their reference owners finish"
        );
        check(
            levelCalls.Any(m => m.Name == "Restriction")
                && levelCalls.Any(m => m.DeclaringType.Name == "ScenePreviewModel" && m.Name == "Copy"),
            "Level prop placement retains native restrictions and geometry-only previews"
        );
        check(
            assetCalls.Any(m => m.DeclaringType.Name == "NativeLevelPropLibrary" && m.Name == "Load"),
            "Saved level prop targets dispatch through the same library as catalog previews"
        );
        check(
            controllerCalls.Any(m => m.DeclaringType.Name == "NativeLevelPropLibrary" && m.Name == "Search"),
            "All-game scene browsing includes the indexed level files"
        );
        check(
            advance.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().Any(m => m.Name == "Observe"),
            "Live scene traversal uses the tested discovery path that continues beyond binding limits"
        );
    }

    internal static void Run(Action<bool, string> check)
    {
        SceneBundleArchiveChecks.Run(check);
        var schedule = new SceneThumbnailSchedule();
        schedule.Want(new[] { "selected", "visible" });
        var work = new[] { (Key: "slow icon", Native: true), (Key: "selected", Native: false), (Key: "prop", Native: false) };
        check(schedule.Next(work, "selected", j => j.Key, j => j.Native) == 1, "Selected thumbnail starts ahead of queued page work");
        for (var i = 0; i < 4; i++)
            check(schedule.Start(true), "Up to four native icon waits can coexist");
        check(
            schedule.Next(work, "slow icon", j => j.Key, j => j.Native) == 1,
            "Saturated native icon slots do not block a runnable prop behind them"
        );
        check(
            !schedule.Start(true) && schedule.Start(false),
            "Slow native icons cannot starve prop rendering or exceed the native slot bound"
        );
        check(!schedule.Start(false), "Only one prop load or render can be active");
        schedule.Finish(true);
        check(schedule.Start(true), "Completed or cancelled native work releases its slot");
        schedule.Want(new[] { "new-page" });
        check(!schedule.Wanted("selected") && schedule.Wanted("new-page"), "Page changes reject obsolete in-flight results");
        var nextMap = new SceneThumbnailSchedule();
        schedule.Finish(false);
        check(nextMap.Active == 0 && nextMap.Start(false), "Late work releases only its original map's scheduling slots");

        var evicted = new List<string>();
        var lru = new ScenePreviewCache<string>(3, evicted.Add);
        foreach (var key in new[] { "a", "b", "c" })
            lru.Complete(lru.Generation, key, key, "");
        lru.Get("a");
        lru.Complete(lru.Generation, "d", "d", "");
        check(evicted.SequenceEqual(new[] { "b" }), "Recently viewed previews survive eviction ahead of older unused previews");
        lru.Protect(new[] { "a", "c" });
        lru.Complete(lru.Generation, "e", "e", "");
        check(lru.Get("a") != null && lru.Get("c") != null && evicted.Last() == "d", "Visible-page protection applies to every tile");
        lru.Protect(new[] { "a", "c", "e" });
        check(
            !lru.Complete(lru.Generation, "f", "f", "") && evicted.Last() == "f",
            "A fully protected cache releases rejected results once"
        );
        lru.Clear();
        check(
            evicted.Count == 6 && evicted.Distinct().Count() == 6,
            "Eviction, rejected results and teardown each release ownership exactly once"
        );

        check(
            !PreviewMaterialPolicy.UsesOpacity("Opaque", "Custom/Bumped Specular", false, false),
            "Opaque prop surface masks must not cut holes in thumbnails"
        );
        check(!PreviewMaterialPolicy.UsesOpacity("", "Custom/Container", false, false), "Untagged solid props default to opaque previews");
        check(
            PreviewMaterialPolicy.UsesOpacity("TransparentCutout", "Custom/Fence", false, false),
            "Cutout fence textures retain their holes"
        );
        check(
            PreviewMaterialPolicy.UsesOpacity("", "Custom/Leaves", false, false),
            "Leaf shaders retain opacity even without a render tag"
        );
        check(PreviewMaterialPolicy.UsesOpacity("Opaque", "Standard", true, false), "Alpha-test keyword overrides an opaque render tag");
        check(PreviewMaterialPolicy.UsesOpacity("", "Standard", false, true), "Blended materials retain texture opacity");
        var released = new List<string>();
        var cache = new ScenePreviewCache<string>(2, released.Add);
        var generation = cache.Generation;
        check(cache.Request("crate") && !cache.Request("crate"), "Preview requests coalesce while loading");
        cache.Fail(generation, "crate", "Unavailable");
        check(!cache.Request("crate") && cache.Error("crate") == "Unavailable", "Failed preview remains explicit without a retry loop");
        cache.Retry("crate");
        check(cache.Request("crate"), "Unavailable preview can be retried");
        cache.Complete(generation, "crate", "crate texture", "crate");
        cache.Complete(generation, "loot", "native icon lease", "crate");
        cache.Complete(generation, "barrel", "barrel texture", "crate");
        check(
            cache.Get("crate") != null && cache.Get("barrel") != null && cache.Get("loot") == null,
            "Cache eviction retains the selected preview"
        );
        check(released.SequenceEqual(new[] { "native icon lease" }), "Eviction releases exactly one result");
        cache.Request("pending");
        cache.Clear();
        check(
            !cache.Complete(generation, "pending", "late texture", "") && cache.Get("pending") == null,
            "Late preview cannot attach to the next map"
        );
        cache.Fail(generation, "pending", "stale error");
        check(cache.Error("pending") == "" && cache.Request("pending"), "Stale failure cannot poison the next map");
        cache.Abandon("pending");
        check(cache.Request("pending"), "Pruned page request can be queued again");
        check(released.Count == 4 && released.Distinct().Count() == 4, "Clear and stale completion release all owned results once");

        var pages = new ScenePreviewCache<string>(64, _ => { });
        for (var i = 0; i < 64; i++)
            pages.Complete(pages.Generation, "old" + i, "image" + i, "old0");
        for (var page = 0; page < 10; page++)
        {
            for (var row = 0; row < 10; row++)
            {
                var key = page + ":" + row;
                pages.Complete(pages.Generation, key, "image" + key, "old0");
                pages.Fail(pages.Generation, "error" + key, "Unavailable");
            }
            check(pages.Get("old0") != null, "Paging preserves the selected thumbnail");
            for (var row = 0; row < 10; row++)
            {
                var key = page + ":" + row;
                check(pages.Get(key) != null && !pages.Request(key), "Full cache retains every newly rendered page row without retrying");
                check(
                    pages.Error("error" + key) == "Unavailable" && !pages.Request("error" + key),
                    "Full error cache retains the current page failures without retrying"
                );
            }
        }
    }
}
