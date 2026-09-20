using System.Reflection;
using Mono.Cecil;
using Newtonsoft.Json;
using WTT.Campaigns.Client.Authoring.Navigation;

namespace WTT.Campaigns.Tests;

internal static class NavigationAssemblyChecks
{
    internal static void Run(string gameRoot, string clientPath)
    {
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.Combine(gameRoot, "EscapeFromTarkov_Data", "Managed"));
        resolver.AddSearchDirectory(Path.GetDirectoryName(clientPath)!);
        using var client = AssemblyDefinition.ReadAssembly(clientPath, new ReaderParameters { AssemblyResolver = resolver });
        var types = client.MainModule.GetTypes().ToArray();
        var navigation = types
            .Where(t => t.FullName.StartsWith("WTT.Campaigns.Client.Authoring.Navigation.", StringComparison.Ordinal))
            .ToArray();
        var calls = navigation
            .SelectMany(t => t.Methods)
            .Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions)
            .Select(i => i.Operand)
            .OfType<MethodReference>()
            .ToArray();
        Require(
            calls.All(m => m.Name is not "RemoveAllNavMeshData" and not "RestoreNavMesh"),
            "Experiments never clear all native navigation or use the deprecated no-op restore"
        );
        Require(
            calls.Any(m => m.Name == "UpdateNavMeshDataAsync")
                && calls.Any(m => m.Name == "Cancel" && m.DeclaringType.Name == "NavMeshBuilder"),
            "Candidate baking uses the cancellable native async builder"
        );
        Require(
            calls.All(m => m.DeclaringType.FullName != "System.Threading.Tasks.Task" || m.Name is not "Delay" and not "Yield"),
            "Unity navigation waits remain on the player loop"
        );
        Require(
            calls.Any(m => m.DeclaringType.Name == "AsyncGPUReadback" && m.Name == "Request"),
            "Mesh recovery uses asynchronous installed GPU readback"
        );
        Require(
            calls.All(m =>
                m.Name is not "WaitForCompletion" and not "WaitAllRequests"
                && !(m.DeclaringType.Name == "GraphicsBuffer" && m.Name == "GetData")
                && m.Name is not "set_vertexBufferTarget" and not "set_indexBufferTarget"
                && !(m.DeclaringType.Name == "MeshCollider" && m.Name == "set_sharedMesh")
            ),
            "Recovery never blocks on GPU reads, recreates native buffers or replaces collision meshes"
        );
        foreach (
            var call in calls.Where(m =>
                m.DeclaringType.Namespace.StartsWith("UnityEngine", StringComparison.Ordinal)
                || m.DeclaringType.Namespace.StartsWith("Unity.AI", StringComparison.Ordinal)
            )
        )
            Require(call.Resolve() != null, "Installed Unity navigation API resolves: " + call.FullName);

        var editor = types.Single(t => t.FullName == "WTT.Campaigns.Client.Authoring.RaidEditor");
        bool Calls(MethodDefinition method, string name) =>
            method.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == name);
        var navigationFeedback = editor.Methods.Single(m => m.Name == "LogNavigationFeedback");
        Require(
            Calls(navigationFeedback, "LogInfo")
                && Calls(navigationFeedback, "LogWarning")
                && !Calls(navigationFeedback, "PublishEditorFeedback"),
            "Navigation publishes once through the file logger and console listener, preserving warning severity"
        );
        Require(
            !types.Any(t =>
                t.Name
                    is "NavigationExperiment"
                        or "NavigationWorkflow"
                        or "NavigationReplacement"
                        or "NativeNavigationSnapshot"
                        or "WaypointsNavigation"
            ),
            "Full-map expansion, native replacement and Waypoints takeover implementations are removed"
        );
        Require(
            Calls(editor.Methods.Single(m => m.Name == "Close"), "StopNavigationPainting"),
            "Editor close releases owned paint resources"
        );
        Require(Calls(editor.Methods.Single(m => m.Name == "Update"), "Tick"), "Editor lifecycle checks stale navigation work");
        var follower = types.Single(t => t.Name == "SceneNavigationFollower");
        Require(
            Calls(follower.Methods.Single(m => m.Name == "Sync"), "PaintSupportCap"),
            "Explicit physical support faces retain their top surface while solid bodies remain carved"
        );

        // Exercise the actual compiled geometry helpers against installed Unity value types,
        // without invoking the engine or starting a client.
        var context = new ClientAssemblyContext(gameRoot, clientPath);
        var loaded = context.LoadFromAssemblyPath(Path.GetFullPath(clientPath));
        CheckPaintSupportLifetime(loaded);
        CheckSurfaceFiltering(loaded);
        CheckRecipeSettings(loaded);
        CheckOmittedMeshTransforms(loaded);
        var survey = navigation.Single(t => t.Name == "NavigationSurvey");
        var omittedMesh = survey.Methods.Single(m => m.Name == "AddOmittedMesh");
        Require(
            Calls(omittedMesh, "get_sharedMesh")
                && Calls(omittedMesh, "Find")
                && Calls(omittedMesh, "UnresolvedReason")
                && Calls(omittedMesh, "set_sourceObject")
                && Calls(omittedMesh, "set_component")
                && Calls(omittedMesh, "get_localToWorldMatrix"),
            "Omitted triangle colliders retain their collision mesh, recovery outcome, physical owner and world transform"
        );
        Require(
            omittedMesh.Body.Instructions.Any(i =>
                i.Operand is MethodReference m && m.Name == "AddIssue" && i.Previous.OpCode.Code != Mono.Cecil.Cil.Code.Ldnull
            ),
            "Omitted unreadable meshes are submitted to recovery instead of becoming unrecoverable generic issues"
        );
        var paint = navigation.Single(t => t.Name == "NavigationPaintPreview");
        Require(
            Calls(paint.Methods.Single(m => m.Name == "RequireObservation"), "get_Owned")
                && Calls(paint.Methods.Single(m => m.Name == "get_Owned"), "get_owner"),
            "Observe checks the live owned addition registration"
        );
        Require(
            Calls(paint.Methods.Single(m => m.Name == "Dispose"), "Cancel")
                && Calls(paint.Methods.Single(m => m.Name == "Dispose"), "RemoveOwned"),
            "Editor teardown cancels in-flight work and removes only owned resources"
        );
        var buildMethods = navigation
            .Where(t => t.FullName.Contains("NavigationPaintPreview"))
            .SelectMany(t => t.Methods)
            .Where(m => m.HasBody)
            .ToArray();
        var registration = buildMethods.Single(m => Calls(m, "AddNavMeshData"));
        var registrationCode = registration.Body.Instructions.ToList();
        var registerAt = registrationCode.FindIndex(i => i.Operand is MethodReference m && m.Name == "AddNavMeshData");
        var snapshotBefore = registrationCode.FindLastIndex(
            registerAt,
            i => i.Operand is MethodReference m && m.Name == "CalculateTriangulation"
        );
        var snapshotAfter = registrationCode.FindIndex(
            registerAt,
            i => i.Operand is MethodReference m && m.Name == "CalculateTriangulation"
        );
        Require(
            snapshotBefore >= 0
                && snapshotAfter > registerAt
                && !registrationCode
                    .Skip(snapshotBefore)
                    .Take(snapshotAfter - snapshotBefore)
                    .Any(i => i.Operand is MethodReference m && m.Name is "NextFrame" or "AwaitUnsafeOnCompleted" or "AwaitOnCompleted"),
            "Native and preview snapshots bracket registration without a player-loop yield that could include unrelated carving"
        );
        Require(
            Calls(paint.Methods.Single(m => m.Name == "ValidateFootprint"), "Outside")
                && Calls(paint.Methods.Single(m => m.Name == "ValidateFootprint"), "SaveFootprintFailure"),
            "Runtime uses the tested exact footprint checker and preserves failed geometry for offline replay"
        );
        Require(
            buildMethods.Any(m => Calls(m, "ValidateFootprint")) && buildMethods.Any(m => Calls(m, "ValidateConnection")),
            "Manual build validates its painted footprint and explicit physical connections before success"
        );
        Require(
            !calls.Any(m => m.DeclaringType.Name == "NavMeshSurface" && m.Name is "RemoveData" or "AddData"),
            "Paint tools never replace native surface registrations"
        );
        var preview = types
            .Single(t => t.Name.StartsWith("<BeginAiPreview>d__", StringComparison.Ordinal))
            .Methods.Single(m => m.Name == "MoveNext");
        var previewCalls = preview.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().Select(m => m.Name).ToArray();
        Require(
            Array.IndexOf(previewCalls, "RequireObservation") >= 0
                && Array.IndexOf(previewCalls, "RequireObservation") < Array.IndexOf(previewCalls, "BeginAsync")
                && previewCalls.Count(n => n == "RequireObservation") >= 3,
            "Observe verifies manual preview before preparing bots and again across async preparation before mission start"
        );
        Require(
            Calls(editor.Methods.Single(m => m.Name == "UpdateAiPreview"), "RequireObservation"),
            "Running observation revalidates manual preview before ticking bots"
        );
        var renderer = navigation.Single(t => t.Name == "NavigationSurfaceRenderer");
        var record = renderer.Methods.Single(m => m.Name == "RecordOverlay");
        Require(
            Calls(renderer.Methods.Single(m => m.Name == "Capture"), "FindPass")
                && record.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "_projectionPass"),
            "Runtime resolves the bundled ground projection pass by name instead of using stale numeric pass indices"
        );
        Require(
            Calls(renderer.Methods.Single(m => m.Name == "Capture"), "CaptureReceivers")
                && Calls(renderer.Methods.Single(m => m.Name == "CaptureReceivers"), "GetInterpolatedHeight")
                && Calls(renderer.Methods.Single(m => m.Name == "SetReceiver"), "get_heightmapTexture")
                && Calls(renderer.Methods.Single(m => m.Name == "SetReceiver"), "get_heightmapPixelError")
                && Calls(record, "SetReceiver")
                && !Calls(record, "GetInterpolatedHeight"),
            "Terrain receiver ownership is captured once and native height textures gate each draw without per-frame physics sampling"
        );
        Require(
            Calls(renderer.Methods.Single(m => m.Name == "Capture"), "GetSettingsByID")
                && Calls(renderer.Methods.Single(m => m.Name == "Capture"), "get_agentClimb")
                && Calls(renderer.Methods.Single(m => m.Name == "Capture"), "get_voxelSize")
                && Calls(renderer.Methods.Single(m => m.Name == "Capture"), "get_agentSlope"),
            "Mesh floor projection derives voxel lift, stair reach and receiver slope from native navigation settings"
        );
        var viewport = types.Single(t => t.Name == "EditorViewport");
        Require(
            !navigation.Any(t => t.Name is "NavigationSurfaceDetail" or "NavigationSurfaceSnapshot")
                && Calls(viewport.Methods.Single(m => m.Name == "Attach"), "set_depthTextureMode")
                && Calls(viewport.Methods.Single(m => m.Name == "OnDisable"), "set_depthTextureMode")
                && !Calls(viewport.Methods.Single(m => m.Name == "OnPreRender"), "get_projectionMatrix"),
            "Ground projection uses native scene depth and render-time rays without the failed height-refinement workaround or copied camera matrices"
        );
        Require(
            Calls(viewport.Methods.Single(m => m.Name == "OnPreRender"), "RecordOverlay")
                && Calls(record, "DrawMesh")
                && !Calls(record, "CalculateTriangulation")
                && !Calls(viewport.Methods.Single(m => m.Name == "OnRenderImage"), "get_projectionMatrix")
                && !Calls(viewport.Methods.Single(m => m.Name == "OnRenderImage"), "get_worldToCameraMatrix"),
            "Navigation layer uses native render-time matrices, never a post-render camera reconstruction"
        );
        Require(
            !Calls(viewport.Methods.Single(m => m.Name == "OnPreRender"), "SetRenderTarget")
                && !Calls(viewport.Methods.Single(m => m.Name == "OnPreRender"), "SetViewport")
                && !viewport.Fields.Any(f => f.Name == "_navigationTexture"),
            "Ground coverage shares the native camera colour target through image effects and viewport presentation"
        );
        Require(
            Calls(renderer.Methods.Single(m => m.Name == "Clear"), "Remove")
                && Calls(viewport.Methods.Single(m => m.Name == "OnDisable"), "RemoveCommandBuffer")
                && Calls(viewport.Methods.Single(m => m.Name == "OnDisable"), "Release")
                && record.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "_compositeFrame")
                && record.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "_compositeCamera"),
            "Navigation layer cleanup removes owned commands and scopes cached draws to the current camera/frame"
        );
        var healthCalls = navigation
            .Where(t => t.Name == "NavigationHealthScan" || t.FullName.Contains("NavigationHealthScan/"))
            .SelectMany(t => t.Methods)
            .Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions)
            .Select(i => i.Operand)
            .OfType<MethodReference>()
            .ToArray();
        Require(
            healthCalls.Any(m => m.Name == "CalculatePath")
                && healthCalls.Any(m => m.Name == "RaycastNonAlloc")
                && healthCalls.Any(m => m.Name == "OverlapCapsuleNonAlloc")
                && healthCalls.Any(m => m.Name == "ThrowIfCancellationRequested"),
            "Health diagnostics use cancellable native path and bounded physical queries"
        );
        Require(
            healthCalls.All(m =>
                m.DeclaringType.Name != "NavMeshBuilder"
                && (
                    m.DeclaringType.Name != "NavMesh"
                    || m.Name is "GetSettingsByID" or "CalculateTriangulation" or "SamplePosition" or "CalculatePath"
                )
            ) && !healthCalls.Any(m => m.Name is "AddComponent" or "set_carving" or "set_enabled"),
            "Health scans never bake, register, carve, expand or repair navigation"
        );
        Require(
            Calls(editor.Methods.Single(m => m.Name == "StopNavigationPainting"), "ClearNavigationHealth")
                && Calls(editor.Methods.Single(m => m.Name == "ClearNavigationHealth"), "Cancel")
                && Calls(editor.Methods.Single(m => m.Name == "ClearNavigationHealth"), "Dispose"),
            "Editor cleanup cancels diagnostics and releases their overlay"
        );
        Require(
            Calls(renderer.Methods.Single(m => m.Name == "Draw"), "DrawMesh")
                && !Calls(renderer.Methods.Single(m => m.Name == "Draw"), "CalculateTriangulation")
                && Calls(renderer.Methods.Single(m => m.Name == "Dispose"), "Destroy"),
            "Surface draws the cached mesh and releases owned rendering resources"
        );
        var recoveryType = loaded.GetType("WTT.Campaigns.Client.Authoring.Navigation.NavigationMeshRecovery", true)!;
        CheckRecoveryDrain(recoveryType);
        using (var stream = loaded.GetManifestResourceStream("WTT.Campaigns.Navigation.native-collision.json")!)
        using (var reader = new StreamReader(stream))
        {
            var catalog = JsonConvert.DeserializeObject<NavigationCollisionCatalog>(reader.ReadToEnd())!;
            Require(
                catalog.VerifyFiles(Path.Combine(gameRoot, "EscapeFromTarkov_Data")) == "",
                "Actual compiled terrain catalogue matches the installed native scene, data and bindings"
            );
            Require(
                catalog.Terrains.Length == 4 && catalog.Terrains.All(t => t.EnableTreeColliders == false),
                "Pilot terrain catalogue records the four serialized collision-disabled owners"
            );
        }
        var evidence = navigation
            .Where(t => t.FullName.Contains("NavigationTerrainEvidence", StringComparison.Ordinal))
            .SelectMany(t => t.Methods)
            .Where(m => m.HasBody)
            .ToArray();
        Require(
            evidence.Any(m => Calls(m, "add_sceneLoaded"))
                && evidence.Any(m => Calls(m, "add_sceneUnloaded"))
                && evidence.Any(m => Calls(m, "VerifyFiles")),
            "Terrain evidence is captured at scene load, released on unload and file-verified"
        );
        Require(
            evidence
                .SelectMany(m => m.Body.Instructions)
                .Select(i => i.Operand)
                .OfType<MethodReference>()
                .All(m => m.Name is not "Instantiate" and not "set_terrainData" and not "set_enabled"),
            "Terrain evidence never changes native collision or copies terrain components"
        );
        foreach (var full in new[] { false, true })
        {
            var recovery = Activator.CreateInstance(
                recoveryType,
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new object[] { full },
                null
            )!;
            Require(
                (int)recoveryType.GetProperty("MeshLimit", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(recovery)!
                    == (full ? 4096 : 128),
                "Actual compiled recovery enforces scope-specific mesh limit"
            );
            Require(
                (long)recoveryType.GetProperty("ByteLimit", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(recovery)!
                    == (full ? 256L : 64L) * 1024 * 1024,
                "Actual compiled recovery enforces scope-specific memory reservation"
            );
        }
        var treeTypes = navigation.Where(t => t.FullName.Contains("NavigationTerrainTrees", StringComparison.Ordinal)).ToArray();
        var treeCalls = treeTypes
            .SelectMany(t => t.Methods)
            .Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions)
            .Select(i => i.Operand)
            .OfType<MethodReference>()
            .ToArray();
        Require(
            treeCalls.All(m => m.Name is not "Instantiate" and not "set_treeInstances" and not "set_terrainData" and not "set_enabled"),
            "Tree coverage audit never changes native trees, prefabs or physics"
        );
        Require(
            treeCalls.Any(m => m.Name == "get_treeInstances") && treeCalls.Any(m => m.Name == "Matches"),
            "Tree audit checks actual instances against matching baked geometry"
        );
        var treeAudit = treeTypes
            .SelectMany(t => t.Methods)
            .Single(m => m.HasBody && m.Body.Instructions.Any(i => i.Operand is MethodReference call && call.Name == "OutsideProbe"));
        var auditCode = treeAudit.Body.Instructions.ToList();
        var scopeCheck = auditCode.FindIndex(i => i.Operand is MethodReference call && call.Name == "OutsideProbe");
        var disabledError = auditCode.FindIndex(i =>
            i.Operand is string text && text.StartsWith("Disabled prototype collider", StringComparison.Ordinal)
        );
        var transformedError = auditCode.FindIndex(i =>
            i.Operand is string text && text.StartsWith("Scaled/rotated prototype root", StringComparison.Ordinal)
        );
        Require(
            scopeCheck >= 0 && disabledError > scopeCheck && transformedError > scopeCheck,
            "Actual tree audit scopes remote geometry before reporting unsupported disabled or transformed prototypes"
        );
        Console.WriteLine(
            "Manual navigation: installed APIs, authored footprint validation, owned cleanup and removal of automatic replacement verified offline; live brush and bot acceptance remain manual."
        );
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void CheckOmittedMeshTransforms(Assembly client)
    {
        var validate = client
            .GetType("WTT.Campaigns.Client.Authoring.Navigation.NavigationSurvey", true)!
            .GetMethod("SupportedMeshTransform", BindingFlags.Static | BindingFlags.NonPublic)!;
        var matrixType = validate.GetParameters()[0].ParameterType;
        bool Accept(params (string field, float value)[] changes)
        {
            var matrix = Activator.CreateInstance(matrixType)!;
            foreach (var field in new[] { "m00", "m11", "m22", "m33" })
                matrixType.GetField(field)!.SetValue(matrix, 1f);
            foreach (var (field, value) in changes)
                matrixType.GetField(field)!.SetValue(matrix, value);
            return (bool)validate.Invoke(null, new[] { matrix })!;
        }
        Require(Accept(), "Omitted static mesh accepts its original identity transform");
        Require(Accept(("m00", 2), ("m11", 3), ("m22", 4), ("m03", 100)), "Omitted mesh preserves translated non-uniform scale");
        Require(Accept(("m00", 0), ("m02", 1), ("m20", -1), ("m22", 0)), "Omitted mesh preserves source orientation");
        Require(
            !Accept(("m00", -1)) && !Accept(("m00", 0)) && !Accept(("m01", .25f)),
            "Omitted mesh rejects mirrored, collapsed and sheared collision transforms"
        );
        Require(
            !Accept(("m03", float.NaN)) && !Accept(("m22", float.PositiveInfinity)),
            "Omitted mesh rejects non-finite position and scale"
        );
    }

    private static void CheckPaintSupportLifetime(Assembly client)
    {
        var type = client.GetType("WTT.Campaigns.Client.Authoring.Scenes.SceneNavigation", true)!;
        var set = type.GetMethod("SetPaintSupportCaps", BindingFlags.Static | BindingFlags.NonPublic)!;
        var field = type.GetField("_paintSupportCaps", BindingFlags.Static | BindingFlags.NonPublic)!;
        var first = new object();
        var second = new object();
        var oldCaps = Activator.CreateInstance(field.FieldType)!;
        var newCaps = Activator.CreateInstance(field.FieldType)!;
        set.Invoke(null, new[] { first, oldCaps });
        set.Invoke(null, new[] { second, newCaps });
        set.Invoke(null, new object?[] { first, null });
        Require(
            ReferenceEquals(field.GetValue(null), newCaps),
            "Late cleanup from an old preview cannot clear the new preview's support caps"
        );
        set.Invoke(null, new object?[] { second, null });
        Require(field.GetValue(null) == null, "The current preview releases its own support caps");
    }

    private static void CheckRecoveryDrain(Type recovery)
    {
        Require(
            (int)recovery.GetField("BatchSize", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()! == 4,
            "Recovery has a fixed four-mesh concurrency bound"
        );
        var drain = recovery.GetMethod("Drain", BindingFlags.Static | BindingFlags.NonPublic)!;
        var task = drain.ReturnType;
        var sourceType = task.Assembly.GetType("Cysharp.Threading.Tasks.UniTaskCompletionSource", true)!;
        var fromException = task.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == "FromException" && !m.IsGenericMethod && m.GetParameters().Length == 1);
        foreach (
            var failure in new Exception[]
            {
                new InvalidOperationException("first readback failed"),
                new OperationCanceledException(new CancellationToken(true)),
            }
        )
        {
            var source = Activator.CreateInstance(sourceType)!;
            var batch = Array.CreateInstance(task, 2);
            batch.SetValue(fromException.Invoke(null, new object[] { failure }), 0);
            batch.SetValue(sourceType.GetProperty("Task")!.GetValue(source), 1);
            var result = drain.Invoke(null, new object[] { batch })!;
            var awaiter = task.GetMethod("GetAwaiter")!.Invoke(result, null)!;
            var awaiterType = awaiter.GetType();
            Require(
                !(bool)awaiterType.GetProperty("IsCompleted")!.GetValue(awaiter)!,
                "Actual recovery drain retains sibling buffers after an earlier failure or cancellation"
            );
            sourceType.GetMethod("TrySetResult", Type.EmptyTypes)!.Invoke(source, null);
            Require(
                (bool)awaiterType.GetProperty("IsCompleted")!.GetValue(awaiter)!,
                "Recovery drain completes after the last in-flight readback"
            );
            var propagated = false;
            try
            {
                awaiterType.GetMethod("GetResult")!.Invoke(awaiter, null);
            }
            catch (TargetInvocationException error)
            {
                // UniTask represents cancellation by token and may recreate its exception.
                propagated = failure is OperationCanceledException cancelled
                    ? error.InnerException is OperationCanceledException actual && actual.CancellationToken == cancelled.CancellationToken
                    : ReferenceEquals(error.InnerException, failure);
            }
            Require(propagated, "Recovery drain preserves the original failure after all siblings finish");
        }
    }

    private static void CheckSurfaceFiltering(Assembly client)
    {
        var filter = client
            .GetType("WTT.Campaigns.Client.Authoring.Navigation.NavigationSurfaceGeometry", true)!
            .GetMethod("Filter", BindingFlags.Static | BindingFlags.NonPublic)!;
        var vector = filter.GetParameters()[0].ParameterType.GetElementType()!;
        // A lower floor, an upper floor, and a tall ramp crossing the band.
        var heights = new[] { 0f, 0f, 0f, 8f, 8f, 8f, -4f, 12f, 12f };
        var vertices = Array.CreateInstance(vector, heights.Length);
        for (var i = 0; i < heights.Length; i++)
            vertices.SetValue(Activator.CreateInstance(vector, (float)i, heights[i], 0f), i);
        var indices = Enumerable.Range(0, heights.Length).ToArray();
        int[] Select(float? floor) => (int[])filter.Invoke(null, new object?[] { vertices, indices, floor })!;
        Require(ReferenceEquals(Select(null), indices), "Whole-map surface keeps all triangles without an arbitrary edge cap");
        Require(Select(0).SequenceEqual(new[] { 0, 1, 2, 6, 7, 8 }), "Floor filter hides upper floor and retains intersecting ramp");
        Require(Select(8).SequenceEqual(new[] { 3, 4, 5, 6, 7, 8 }), "Floor filter changes level without dropping a crossing ramp");
        Require(Select(40).Length == 0, "Empty height band produces no surface");
        var large = Enumerable.Repeat(new[] { 0, 1, 2 }, 22000).SelectMany(x => x).ToArray();
        Require(
            ((int[])filter.Invoke(null, new object?[] { vertices, large, 0f })!).Length == 66000,
            "Surface filtering retains more than 16-bit index-count geometry"
        );
    }

    private static void CheckRecipeSettings(Assembly client)
    {
        var adapter = client.GetType("WTT.Campaigns.Client.Authoring.Navigation.NavigationSettingsAdapter", true)!;
        var apply = adapter.GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic)!;
        var settingsType = apply.GetParameters()[0].ParameterType;
        var recipeType = apply.GetParameters()[1].ParameterType;
        var native = Activator.CreateInstance(settingsType)!;
        void Set(string name, object value) => settingsType.GetProperty(name)!.SetValue(native, value);
        object Get(object value, string name) => settingsType.GetProperty(name)!.GetValue(value)!;
        Set("agentRadius", .7f);
        Set("agentHeight", 2.1f);
        Set("agentSlope", 50f);
        Set("agentClimb", .3f);
        Set("minRegionArea", 19f);
        Set("tileSize", 256);
        Set("voxelSize", .2f);
        var inherited = apply.Invoke(null, new object?[] { native, null })!;
        Require(
            (float)Get(inherited, "agentRadius") == .7f && (float)Get(inherited, "minRegionArea") == 19f,
            "Actual settings adapter preserves native settings for legacy/no-recipe layouts"
        );
        var recipe = JsonConvert.DeserializeObject(
            "{\"Version\":1,\"Settings\":{\"Radius\":0.45,\"Height\":1.9,\"Slope\":40,\"Step\":0.25,\"VoxelSize\":0.1,\"TileSize\":128}}",
            recipeType
        )!;
        var customized = apply.Invoke(null, new[] { native, recipe })!;
        Require(
            (float)Get(customized, "agentRadius") == .45f
                && (float)Get(customized, "agentHeight") == 1.9f
                && (float)Get(customized, "agentSlope") == 40f
                && (float)Get(customized, "agentClimb") == .25f
                && (float)Get(customized, "voxelSize") == .1f
                && (int)Get(customized, "tileSize") == 128
                && (bool)Get(customized, "overrideVoxelSize")
                && (bool)Get(customized, "overrideTileSize"),
            "Actual compiled adapter applies all saved build settings and enables resolution overrides"
        );
        Require(
            Equals(Get(customized, "agentTypeID"), Get(native, "agentTypeID")) && (float)Get(customized, "minRegionArea") == 19f,
            "Recipe overrides preserve native agent identity and unrelated settings"
        );
        var unsupported = JsonConvert.DeserializeObject("{\"Version\":3}", recipeType)!;
        var rejected = false;
        try
        {
            apply.Invoke(null, new[] { native, unsupported });
        }
        catch (TargetInvocationException error)
        {
            rejected = error.InnerException is InvalidOperationException;
        }
        Require(rejected, "Compiled runtime rejects unsupported recipe versions before source collection");
    }
}
