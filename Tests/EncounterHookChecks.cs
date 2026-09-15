using Mono.Cecil;
using Mono.Cecil.Cil;
using WTT.Campaigns.Client.Encounters;

namespace WTT.Campaigns.Tests;

/// <summary>
/// Verifies the native activation surface used by the editor encounter runtime.
///
/// These checks intentionally read the installed assemblies with Cecil.  Loading EFT or
/// invoking any of its types would make the check dependent on a live Unity process and would
/// miss the most useful failure mode: a dumped method was renamed or an override was left
/// outside the admission boundary.
/// </summary>
internal static class EncounterHookChecks
{
    private const string BigBrainGuid = "xyz.drakia.bigbrain";
    private const string SainGuid = "me.sol.sain";

    internal static void Run(string gameRoot, string clientPath)
    {
        gameRoot = Path.GetFullPath(gameRoot);
        clientPath = Path.GetFullPath(clientPath);
        var nativePath = Path.Combine(gameRoot, "BepInEx", "DumpedAssemblies", "EscapeFromTarkov", "Assembly-CSharp.dll");
        Require(File.Exists(nativePath), "The installed Assembly-CSharp dump is required for encounter hook checks.");
        Require(File.Exists(clientPath), "The compiled client assembly is required for encounter hook checks.");

        var bigBrainPath = PluginPath(gameRoot, "DrakiaXYZ-BigBrain.dll");
        var sainPath = PluginPath(gameRoot, "SAIN.dll");
        using var native = AssemblyDefinition.ReadAssembly(nativePath);
        using var client = AssemblyDefinition.ReadAssembly(clientPath);
        using var bigBrain = AssemblyDefinition.ReadAssembly(bigBrainPath);
        using var sain = AssemblyDefinition.ReadAssembly(sainPath);

        CheckPlugin(bigBrain, BigBrainGuid, "1.5.0", "BigBrain");
        CheckPlugin(sain, SainGuid, "4.5.1", "SAIN");
        CheckNativeSurface(native);
        CheckAssetPreparation(native, client);
        CheckActivationReadiness(native, client);
        CheckOptionalModSurface(bigBrain, sain);

        var gate = RequireType(client, "WTT.Campaigns.Client.Encounters.EncounterSpawnAdmissionGate");
        var install = RequireMethod(gate, "Install");
        CheckAdmissionCoverage(native, gate, install);
        CheckAdmissionPolicy();
        CheckCoordinatorCoverage(client);
        CheckPatrolCoverage(client);
        CheckPatrolFacing(native, client);
        CheckPatrolPathDispatch(native, client);
        CheckHealthInterceptionCoverage(client);
        CheckSceneNavigation(gameRoot, client);
        CheckNativeSpawnPreflight(native, client);
        CheckOwnedCores(native, client);
        CheckCoverAndHold(sain, native, client);
        CheckPlayerPoolCleanup(native, client);

        Console.WriteLine(
            "Encounter hooks: installed native activation, preactivation, world registration, SAIN and BigBrain surfaces verified offline."
        );
    }

    private static void CheckPlayerPoolCleanup(AssemblyDefinition native, AssemblyDefinition client)
    {
        var cleanup = RequireMethod(RequireType(client, "WTT.Campaigns.Client.Encounters.EncounterPlayerCleanup"), "Dispose");
        foreach (var call in new[] { "get_IsInPool", "GetComponent", "Dispose", "DestroyLoot", "ReturnToPool" })
            Require(Calls(cleanup, call), "Encounter cleanup preserves native pool ownership: " + call);
        Require(!Calls(cleanup, "Destroy") && !Calls(cleanup, "DestroyImmediate"), "Cleanup must never destroy a reusable player root");
        var coordinator = RequireType(client, "WTT.Campaigns.Client.Encounters.EncounterNative");
        var owned = RequireMethod(coordinator, "DisposeOwnedNativeRegistration");
        Require(!Calls(owned, "Destroy"), "Owned and late bots cannot schedule destruction before corpse pooling");
        var gate = RequireType(client, "WTT.Campaigns.Client.Encounters.EncounterSpawnAdmissionGate");
        var denied = RequireMethod(gate, "DisposePlayer");
        Require(!Calls(denied, "Destroy"), "Denied activation cleanup cannot poison the player pool");
        foreach (var method in new[] { owned, denied })
            Require(
                method.Body.Instructions.Any(i =>
                    i.Operand is MethodReference m && m.DeclaringType.Name == "EncounterPlayerCleanup" && m.Name == "Dispose"
                ),
                "All encounter player cleanup uses native pooling"
            );
        RequireMethod(
            RequireType(native, "EFT.AssetsManager.AssetPoolObject"),
            "ReturnToPool",
            "System.Void",
            "UnityEngine.GameObject",
            "System.Boolean"
        );
        var corpse = RequireType(native, "EFT.Interactive.Corpse");
        Require(Calls(RequireMethod(corpse, "Kill"), "Kill"), "Native corpse cleanup delegates to loot cleanup");
        Require(
            Calls(RequireMethod(RequireType(native, "EFT.Interactive.LootItem"), "Kill"), "ReturnToPool"),
            "Native corpse roots are returned to the asset pool"
        );
        Console.WriteLine("Encounter replay: owned, late and denied players preserve native corpse and player-pool ownership.");
    }

    private static void CheckCoverAndHold(AssemblyDefinition sain, AssemblyDefinition native, AssemblyDefinition client)
    {
        var finder = RequireType(sain, "SAIN.Components.CoverFinder.CoverFinderComponent");
        var data = RequireType(sain, "SAIN.SAINComponent.SubComponents.CoverFinder.SainBotCoverData");
        var analyzer = RequireType(sain, "SAIN.SAINComponent.SubComponents.CoverFinder.CoverAnalyzer");
        Require(
            finder.Fields.Any(f => f.Name == "CoverData" && f.FieldType.FullName == data.FullName),
            "SAIN finder retains its candidate cache"
        );
        Require(
            finder.Properties.Any(p => p.Name == "CoverAnalyzer" && p.PropertyType.FullName == analyzer.FullName),
            "SAIN finder exposes the expected analyzer contract"
        );
        Require(
            data.Fields.Any(f =>
                f.Name == "validCollidersHashSet" && f.FieldType.FullName == "System.Collections.Generic.HashSet`1<UnityEngine.Collider>"
            ),
            "SAIN candidate deduplication cache matches"
        );
        RequireMethod(data, "HandleLists", "System.Void", "UnityEngine.Vector3");
        RequireMethod(data, "OverlapBoxAndFilter", "System.Int32", data.FullName + "/BotColliderQueryParams");
        foreach (var name in new[] { "CheckCreateNewCoverPoint", "RecheckCoverPoint" })
            RequireMethod(analyzer, name);
        var cover = RequireType(client, "WTT.Campaigns.Client.Encounters.EncounterCoverRuntime");
        Require(Calls(RequireMethod(cover, "Refresh"), "VisitCoverColliders"), "Authored cover enters SAIN's normal candidate loop");
        foreach (var name in new[] { "Created", "Rechecked", "Creating", "Rechecking" })
            Require(
                Calls(RequireMethod(cover, name), name.EndsWith("ing") ? "ContainsKey" : "TryGetValue"),
                "Cover hook is scoped to owned analyzers: " + name
            );
        var validate = RequireMethod(cover, "Validate");
        Require(
            Calls(validate, "HasCompletePath") && Calls(validate, "HasStandingClearance"),
            "Accepted cover must remain reachable with standing space"
        );
        var hold = RequireType(client, "WTT.Campaigns.Client.Encounters.EncounterHoldRuntime");
        Require(Calls(RequireMethod(hold, "Active"), "Eligible"), "Hold behavior yields through the combat/recovery eligibility gate");
        Require(Calls(RequireMethod(hold, "Move"), "TryPatrolPath"), "Return to spawn uses validated live navigation");
        var mover = RequireType(native, "BotMover");
        var movePlayer = RequireMethod(mover, "MovePlayer");
        Require(
            Calls(movePlayer, "Move") && Calls(movePlayer, "get_Speed"),
            "Native movement still uses player speed at the dispatch hook"
        );
        var patrol = RequireType(client, "WTT.Campaigns.Client.Encounters.EncounterPatrolRuntime");
        var pace = RequireMethod(patrol, "ApplyOwnedPace");
        Require(
            Calls(pace, "GetActiveLayer") && Calls(pace, "SquadEligible") && Calls(pace, "ChangeSpeed"),
            "Pace is enforced only while the campaign owns eligible movement"
        );
        Console.WriteLine(
            "Cover and hold: SAIN cache contracts, scoped candidate validation, hold boundaries and native walking dispatch passed offline."
        );
    }

    private static void CheckOwnedCores(AssemblyDefinition native, AssemblyDefinition client)
    {
        EncounterCoreChecks.Run();
        var lease = RequireType(client, "WTT.Campaigns.Client.Encounters.EncounterCorePoints");
        foreach (var method in lease.Methods.Where(m => m.HasBody))
        foreach (var reference in method.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>())
            if (reference.DeclaringType.FullName is "AICorePoint" or "AICorePointHolder" or "AICoversData")
                RequireMethod(
                    RequireType(native, reference.DeclaringType.FullName),
                    reference.Name,
                    reference.ReturnType.FullName,
                    reference.Parameters.Select(p => p.ParameterType.FullName).ToArray()
                );
        var prepare = RequireMethod(lease, "Prepare");
        Require(
            Calls(prepare, "get_HasIdentity") && Calls(prepare, "get_PublishedLayoutConfirmed"),
            "Local cores require authenticated runtime scope"
        );
        Require(Calls(prepare, "HasStandingClearance") && Calls(prepare, "IsOnNavMesh"), "A local core cannot legalize an invalid spawn");
        Require(
            Calls(prepare, "ClearConnections") && Calls(prepare, "SetIds") && Calls(prepare, "AddCorePoint"),
            "Local cores initialize native graph identity before use"
        );
        var dispose = RequireMethod(lease, "Dispose");
        Require(
            Calls(dispose, "Remove") && Calls(dispose, "Destroy") && Calls(dispose, "SetActive"),
            "Local core cleanup removes registry ownership and disables scene objects"
        );
        var coordinator = RequireType(client, "WTT.Campaigns.Client.Encounters.EncounterNative");
        var reset = RequireMethod(coordinator, "Reset");
        var instructions = reset.Body.Instructions;
        var removeBots = instructions.First(i => i.Operand is MethodReference m && m.Name == "DisposeOwnedBot");
        var removeCores = instructions.First(i =>
            i.Operand is MethodReference m && m.DeclaringType.FullName == lease.FullName && m.Name == "Dispose"
        );
        Require(
            instructions.IndexOf(removeBots) < instructions.IndexOf(removeCores),
            "Bot teardown precedes removal of their core identities"
        );
        Console.WriteLine("Mission cores: island planning, unique identities, native registration and cleanup contracts passed offline.");
    }

    private static void CheckNativeSpawnPreflight(AssemblyDefinition native, AssemblyDefinition client)
    {
        var holder = RequireType(native, "AICorePointHolder");
        RequireMethod(holder, "GetAllTestObjects", "System.Collections.Generic.List`1<AICorePoint>", "System.Boolean");
        RequireMethod(holder, "GetAnyPointToConnect", "AICorePoint", "UnityEngine.Vector3");
        var coordinator = RequireType(client, "WTT.Campaigns.Client.Encounters.EncounterNative");
        var begin = coordinator.NestedTypes.Single(t => t.Name.StartsWith("<BeginAsync>")).Methods.Single(m => m.Name == "MoveNext");
        Require(Calls(begin, "ValidateNativeSpawns"), "Native connectivity must fail during preparation before waves start");
        var resolve = RequireMethod(coordinator, "TryResolveNativeSpawn");
        var refresh = resolve.Body.Instructions.Single(i => i.Operand is MethodReference m && m.Name == "GetAllTestObjects");
        Require(refresh.Previous.OpCode == OpCodes.Ldc_I4_0, "Native spawn lookup must refresh the current map holder");
        Require(Calls(resolve, "GetAnyPointToConnect"), "Native two-way connectivity must remain mandatory");
        Require(
            Calls(RequireMethod(coordinator, "ValidateNativeSpawns"), "get_SpawnPointIds"),
            "Preflight must check assigned spawns only"
        );
        Console.WriteLine("Native spawn preflight: current-map holder and strict connectivity contracts verified offline.");
    }

    private static void CheckSceneNavigation(string gameRoot, AssemblyDefinition client)
    {
        using var ai = AssemblyDefinition.ReadAssembly(
            Path.Combine(gameRoot, "EscapeFromTarkov_Data", "Managed", "UnityEngine.AIModule.dll")
        );
        var obstacle = RequireType(ai, "UnityEngine.AI.NavMeshObstacle");
        var navigation = RequireType(client, "WTT.Campaigns.Client.Authoring.SceneNavigation");
        var constructor = RequireMethod(navigation, ".ctor");
        var references = constructor
            .Body.Instructions.Select(i => i.Operand)
            .OfType<MethodReference>()
            .Where(m => m.DeclaringType.FullName == obstacle.FullName)
            .ToArray();
        Require(references.Length > 0, "Authored scenery must configure native navigation obstacles");
        foreach (var reference in references)
            RequireMethod(
                obstacle,
                reference.Name,
                reference.ReturnType.FullName,
                reference.Parameters.Select(p => p.ParameterType.FullName).ToArray()
            );
        var follower = RequireType(client, "WTT.Campaigns.Client.Authoring.SceneNavigationFollower");
        Require(Calls(RequireMethod(follower, "Sync"), "get_activeInHierarchy"), "Hidden scenery must stop carving");
        Require(Calls(RequireMethod(navigation, "Dispose"), "SetActive"), "Navigation cuts must disable before deferred destruction");
        Console.WriteLine("Scene navigation: carving APIs resolve against installed Unity; hide and disposal contracts passed offline.");
    }

    private static void CheckActivationReadiness(AssemblyDefinition native, AssemblyDefinition client)
    {
        var coordinator = RequireType(client, "WTT.Campaigns.Client.Encounters.EncounterNative");
        var callback = RequireMethod(coordinator, "OnNativeCreated");
        Require(
            !Calls(callback, "AddMember") && !Calls(callback, "MakeStrategyDecision"),
            "The preactive creation callback must not expose uninitialized members to squad tactics"
        );
        var spawn = coordinator.NestedTypes.Single(t => t.Name.StartsWith("<SpawnAsync>")).Methods.Single(m => m.Name == "MoveNext");
        Require(Calls(spawn, "AwaitNativeActivation"), "A registered bot must finish activation before the wave receives it");
        var ready = coordinator
            .NestedTypes.Single(t => t.Name.StartsWith("<AwaitNativeActivation>"))
            .Methods.Single(m => m.Name == "MoveNext");
        foreach (
            var method in new[]
            {
                "get_BotState",
                "get_SubTactic",
                "IsCurrent",
                "ThrowIfCancellationRequested",
                "Delay",
                "get_ElapsedMilliseconds",
            }
        )
            Require(Calls(ready, method), "Native activation readiness must check " + method);
        Require(
            Calls(ready, "Contains") && Calls(ready, "AddMember") && Calls(ready, "MakeStrategyDecision"),
            "Ready bots join their squad without duplicate membership before choosing tactics"
        );
        var nativeActivation = RequireMethod(RequireType(native, "EFT.BotOwner"), "method_10");
        Require(
            nativeActivation.Body.Instructions.Any(i =>
                i.Operand is MethodReference m && m.DeclaringType.FullName == "BotTacticData" && m.Name == "Activate"
            ),
            "Native activation remains the owner of tactic initialization"
        );
        Require(
            !CallsAny(coordinator, "method_10", m => m.DeclaringType.FullName == "EFT.BotOwner"),
            "Preview spawning must not force or duplicate native AI activation"
        );
    }

    private static void CheckAssetPreparation(AssemblyDefinition native, AssemblyDefinition client)
    {
        RequireMethod(
            RequireType(native, "EFT.Profile"),
            "GetAllPrefabPaths",
            "System.Collections.Generic.IEnumerable`1<EFT.ResourceKey>",
            "System.Boolean"
        );
        var coordinator = RequireType(client, "WTT.Campaigns.Client.Encounters.EncounterNative");
        var spawn = coordinator.NestedTypes.Single(t => t.Name.StartsWith("<SpawnAsync>")).Methods.Single(m => m.Name == "MoveNext");
        var calls = spawn.Body.Instructions.Where(i => i.Operand is MethodReference).ToArray();
        var profileData = calls.Single(i =>
            ((MethodReference)i.Operand).Name == ".ctor" && ((MethodReference)i.Operand).DeclaringType.FullName == "GetProfileDataParams"
        );
        var spawnId = profileData.Previous?.Previous;
        Require(
            spawnId?.OpCode == OpCodes.Stfld
                && spawnId.Operand is FieldReference field
                && field.DeclaringType.FullName == "BotSpawnParams"
                && field.Name == "Id_spawn"
                && spawnId.Previous?.OpCode == OpCodes.Ldstr
                && spawnId.Previous.Previous?.OpCode == OpCodes.Dup
                && spawnId.Previous.Previous.Previous?.Operand is MethodReference spawnParams
                && spawnParams.Name == ".ctor"
                && spawnParams.DeclaringType.FullName == "BotSpawnParams",
            "Generated bots must pass a fresh native spawn object with an initialized ID to registration"
        );
        var id = (string)spawnId!.Previous.Operand;
        Require(!id.ToLower().Contains("hunt"), "MoreBots' exact spawn-ID listener expression must safely reject automatic hunt behavior");
        var trigger = RequireType(native, "SpawnTriggerType").Fields.Single(f => f.Name == "none");
        Require(Convert.ToInt32(trigger.Constant) == 0, "Default native spawn parameters must represent an untriggered bot");
        var resources = calls.Single(i => ((MethodReference)i.Operand).Name == "GetAllPrefabPaths");
        var load = calls.Single(i => ((MethodReference)i.Operand).Name == "LoadBundlesAndCreatePools");
        var admission = calls.Single(i => ((MethodReference)i.Operand).Name == "TryOpen");
        var activation = calls.Single(i => ((MethodReference)i.Operand).Name == "ActivateBot");
        Require(
            resources.Offset < load.Offset && load.Offset < admission.Offset && admission.Offset < activation.Offset,
            "Every generated bot must prepare its native appearance and equipment before spawn admission"
        );
        Require(
            calls.Any(i => i.Offset > load.Offset && i.Offset < admission.Offset && ((MethodReference)i.Operand).Name == "GetResult"),
            "Native resource loading must finish before activation"
        );
        Require(
            calls.Any(i =>
                i.Offset > load.Offset && i.Offset < admission.Offset && ((MethodReference)i.Operand).Name == "ThrowIfCancellationRequested"
            ),
            "A reset during resource loading must cancel before opening native spawn admission"
        );
    }

    private static void CheckNativeSurface(AssemblyDefinition native)
    {
        var spawner = RequireType(native, "EFT.BotSpawner");
        var botCreator = RequireType(native, "BotCreatorClient");
        var botCreatorInterface = RequireType(native, "IBotCreator");
        var creationData = RequireType(native, "BotCreationData");
        var botOwner = RequireType(native, "EFT.BotOwner");

        // These are the native wave, ambient, forced, boss/support and debug entry points.  A
        // missing overload is a compatibility failure even when another overload has survived.
        RequireMethod(spawner, "ActivateBotsByWave", "System.Void", "BossLocationSpawn");
        RequireMethod(spawner, "ActivateBotsByWave", "System.Threading.Tasks.Task", "EFT.SpawnWave");
        RequireMethod(spawner, "ActivateBotsWithoutWave", "System.Threading.Tasks.Task", "System.Int32", "IGetProfileData");
        RequireMethod(spawner, "SpawnBotBTR", "System.Threading.Tasks.Task");
        RequireMethod(
            spawner,
            "SpawnBotByTypeForce",
            "System.Threading.Tasks.Task",
            "System.Int32",
            "EFT.WildSpawnType",
            "BotDifficulty",
            "BotSpawnParams"
        );
        RequireMethod(
            spawner,
            "TryToSpawnInZoneAndDelay",
            "System.Void",
            "BotZone",
            "BotCreationData",
            "System.Boolean",
            "System.Boolean",
            "System.Collections.Generic.List`1<EFT.Game.Spawning.ISpawnPoint>",
            "System.Boolean"
        );
        RequireMethod(
            spawner,
            "SpawnBotsInZoneOnPositions",
            "System.Void",
            "System.Collections.Generic.List`1<EFT.Game.Spawning.ISpawnPoint>",
            "BotZone",
            "BotCreationData",
            "System.Action`1<EFT.BotOwner>"
        );
        RequireMethod(spawner, "TrySpawnFreeAndDelay", "System.Void", "BotCreationData", "System.Boolean");
        RequireMethod(spawner, "CheckSpawnOnFreeAfterDelay", "System.Void", "EFT.SimpleBotSpawnDelayModel");
        RequireMethod(
            spawner,
            "method_7",
            "System.Threading.Tasks.Task",
            "System.Collections.Generic.List`1<EFT.Game.Spawning.ISpawnPoint>",
            "BotZone",
            "BotCreationData",
            "System.Action`1<EFT.BotOwner>",
            "System.Threading.CancellationToken"
        );
        RequireMethod(
            spawner,
            "method_10",
            "System.Void",
            "BotZone",
            "BotCreationData",
            "System.Action`1<EFT.BotOwner>",
            "System.Threading.CancellationToken"
        );
        RequireMethod(spawner, "DebugSpawnAnyway", "System.Threading.Tasks.Task");
        RequireMethod(
            spawner,
            "SpawnAndActivateNowDebugClient",
            "System.Threading.Tasks.Task",
            "EFT.EPlayerSide",
            "BotZone",
            "DebugBotProfileChooser",
            "System.Boolean"
        );
        RequireMethod(
            spawner,
            "SpawnAndActivateNowDebugFromLocalFilesServer",
            "System.Threading.Tasks.Task",
            "EFT.EPlayerSide",
            "BotZone",
            "System.Int32",
            "DebugBotProfileChooser",
            "System.Boolean"
        );
        RequireMethod(
            spawner,
            "SpawnAndActivateNowDebugServer",
            "System.Threading.Tasks.Task",
            "EFT.EPlayerSide",
            "BotZone",
            "EFT.WildSpawnType",
            "BotDifficulty",
            "System.Boolean"
        );
        RequireMethod(spawner, "GetGroupAndSetEnemies", "BotsGroup", "EFT.BotOwner", "BotZone");
        RequireMethod(
            spawner,
            "ActivateBotCallback",
            "System.Void",
            "EFT.BotOwner",
            "BotCreationData",
            "System.Action`1<EFT.BotOwner>",
            "System.Boolean",
            "System.Diagnostics.Stopwatch"
        );
        RequireMethod(spawner, "BotDied", "System.Void", "EFT.BotOwner");

        var botsController = RequireType(native, "EFT.BotsController");
        RequireMethod(botsController, "ActivateBotsByWave", "System.Threading.Tasks.Task", "EFT.SpawnWave");
        Require(
            CallsAny(
                botsController,
                "ActivateBotsByWave",
                call =>
                    call.DeclaringType.FullName == "EFT.BotSpawner"
                    && call.Parameters.Count == 1
                    && call.Parameters[0].ParameterType.FullName == "EFT.SpawnWave"
            ),
            "Installed controller wave scheduler forwards to BotSpawner"
        );
        RequireMethod(botsController, "ActivateBotsByWave", "System.Void", "BossLocationSpawn");
        RequireMethod(botsController, "ActivateBotsWithoutWave", "System.Void", "System.Int32", "IGetProfileData");
        RequireMethod(botsController, "DebugSpawnServerAnyway", "System.Void");

        // BotCreatorClient has a concrete profile path in addition to the data path.  The
        // editor uses the latter, while the admission gate must cover both native paths.
        RequireMethod(
            botCreator,
            "CreateBot",
            "System.Threading.Tasks.Task",
            "EFT.Profile",
            "PositionNote",
            "System.Action`1<EFT.BotOwner>",
            "System.Boolean",
            "System.Threading.CancellationToken"
        );
        RequireMethod(
            botCreator,
            "ActivateBot",
            "System.Threading.Tasks.Task",
            "BotCreationData",
            "BotZone",
            "System.Boolean",
            "System.Func`3<EFT.BotOwner,BotZone,BotsGroup>",
            "System.Action`1<EFT.BotOwner>",
            "System.Threading.CancellationToken"
        );
        RequireMethod(
            botCreator,
            "GenerateProfile",
            "System.Threading.Tasks.Task`1<EFT.Profile>",
            "BotCreationData",
            "System.Threading.CancellationToken",
            "System.Boolean"
        );
        RequireMethod(
            botCreator,
            "method_3",
            "System.Void",
            "BotZone",
            "EFT.BotOwner",
            "System.Action`1<EFT.BotOwner>",
            "System.Func`3<EFT.BotOwner,BotZone,BotsGroup>"
        );
        RequireMethod(
            botCreator,
            "method_0",
            "System.Threading.Tasks.Task",
            "BotCreationData",
            "System.Boolean",
            "System.Action`1<EFT.BotOwner>",
            "System.Threading.CancellationToken",
            "System.Boolean"
        );
        RequireMethod(
            botCreator,
            "method_1",
            "System.Threading.Tasks.Task",
            "EFT.Profile",
            "PositionNote",
            "System.Boolean",
            "System.Action`1<EFT.BotOwner>",
            "System.Threading.CancellationToken"
        );
        RequireMethod(
            botOwner,
            "Create",
            "EFT.BotOwner",
            "EFT.Player",
            "UnityEngine.GameObject",
            "EFT.GameDateTime",
            "EFT.BotsController",
            "System.Boolean",
            "AICorePoint"
        );
        RequireMethod(
            botCreatorInterface,
            "ActivateBot",
            "System.Threading.Tasks.Task",
            "BotCreationData",
            "BotZone",
            "System.Boolean",
            "System.Func`3<EFT.BotOwner,BotZone,BotsGroup>",
            "System.Action`1<EFT.BotOwner>",
            "System.Threading.CancellationToken"
        );
        RequireMethod(
            botCreatorInterface,
            "GenerateProfile",
            "System.Threading.Tasks.Task`1<EFT.Profile>",
            "BotCreationData",
            "System.Threading.CancellationToken",
            "System.Boolean"
        );

        RequireMethod(creationData, "CreateWithoutProfile", "BotCreationData", "IGetProfileData");
        RequireMethod(creationData, "AddProfile", "System.Void", "EFT.Profile");
        RequireMethod(creationData, "AddPosition", "System.Void", "UnityEngine.Vector3", "System.Int32");
        RequireMethod(creationData, "StopSpawn", "System.Void");
        RequireMethod(botOwner, "PreActivate", "System.Void", "BotZone", "EFT.GameDateTime", "BotsGroup", "AICoversData", "System.Boolean");
    }

    private static void CheckOptionalModSurface(AssemblyDefinition bigBrain, AssemblyDefinition sain)
    {
        var brainManager = RequireType(bigBrain, "DrakiaXYZ.BigBrain.Brains.BrainManager");
        RequireMethod(
            brainManager,
            "AddCustomLayer",
            "System.Int32",
            "System.Type",
            "System.Collections.Generic.List`1<System.String>",
            "System.Int32",
            "System.Collections.Generic.List`1<EFT.WildSpawnType>"
        );
        RequireType(bigBrain, "DrakiaXYZ.BigBrain.Brains.CustomLayer");

        var external = RequireType(sain, "SAIN.Interop.SAINExternal");
        RequireMethod(external, "CanBotQuest", "System.Boolean", "EFT.BotOwner", "UnityEngine.Vector3", "System.Single");
    }

    private static void CheckAdmissionCoverage(AssemblyDefinition native, TypeDefinition gate, MethodDefinition install)
    {
        var spawnEntryPoints = new[]
        {
            "ActivateBotsByWave",
            "ActivateBotsWithoutWave",
            "SpawnBotBTR",
            "SpawnBotByTypeForce",
            "TryToSpawnInZoneAndDelay",
            "SpawnBotsInZoneOnPositions",
            "TrySpawnFreeAndDelay",
            "CheckSpawnOnFreeAfterDelay",
            "method_7",
            "method_10",
            "DebugSpawnAnyway",
            "SpawnAndActivateNowDebugClient",
            "SpawnAndActivateNowDebugFromLocalFilesServer",
            "SpawnAndActivateNowDebugServer",
            "ActivateBot",
            "CreateBot",
            "method_0",
            "method_1",
        };
        foreach (var name in spawnEntryPoints)
            Require(ContainsString(install, name), "Admission install names native activation path " + name);

        foreach (
            var (typeName, methodName) in new[]
            {
                ("BotCreatorClient", "method_3"),
                ("EFT.BotOwner", "PreActivate"),
                ("BotsGroup", "AddEnemy"),
                ("BotsGroup", "CheckAndAddEnemy"),
                ("EFT.BotMemory", "AddEnemy"),
            }
        )
            Require(ContainsString(install, methodName), "Admission install names " + typeName + "." + methodName);

        Require(Calls(install, "PatchSpawnMethod"), "Each native spawn entry point is sent through the admission patch helper");
        Require(Calls(install, "PatchRequiredVoid"), "Required preactivation and world hooks use fail-closed patching");
        Require(Calls(install, "PatchTarget"), "Bot targeting hooks use the admission target helper");
        Require(Calls(install, "PatchBotOwnerCreate"), "BotOwner.Create is admitted before native registration");
        var createHelper = RequireMethod(gate, "PatchBotOwnerCreate");
        Require(ContainsString(createHelper, "Create"), "BotOwner.Create admission helper names the native factory");

        foreach (
            var methodName in new[]
            {
                "NativeSpawnVoidPrefix",
                "NativeSpawnTaskPrefix",
                "BotActivationPrefix",
                "BotPreActivatePrefix",
                "BotOwnerCreatePrefix",
                "BotOwnerCreatePostfix",
                "RegisterPlayerPrefix",
                "TargetPrefix",
            }
        )
            Require(FindMethod(gate, methodName) != null, "Admission prefix exists: " + methodName);

        var taskPrefix = RequireMethod(gate, "NativeSpawnTaskPrefix");
        Require(Calls(taskPrefix, "DecideTask"), "Async admission prefix uses the tested task decision policy");
        Require(Calls(taskPrefix, "get_CompletedTask"), "Ambient async scheduler denial completes successfully");
        Require(Calls(taskPrefix, "FromException"), "Explicit async admission denial remains faulted");

        var worlds = native
            .MainModule.GetTypes()
            .Where(type => IsGameWorld(type, native.MainModule))
            .Where(type => type.Methods.Any(IsRegisterPlayer))
            .ToArray();
        Require(worlds.Length >= 3, "The installed client world hierarchy exposes all RegisterPlayer overrides");
        foreach (var world in worlds)
        {
            Require(ContainsTypeReference(install, world), "Admission install covers RegisterPlayer override " + world.FullName);
            Require(ContainsString(install, "RegisterPlayer"), "Admission install names RegisterPlayer for " + world.FullName);
        }

        var registerPrefix = RequireMethod(gate, "RegisterPlayerPrefix");
        Require(
            registerPrefix.Parameters.Count == 1 && registerPrefix.Parameters[0].ParameterType.FullName == "EFT.IPlayer",
            "RegisterPlayer admission prefix accepts the native IPlayer argument"
        );
    }

    private static void CheckCoordinatorCoverage(AssemblyDefinition client)
    {
        var native = RequireType(client, "WTT.Campaigns.Client.Encounters.EncounterNative");
        var gate = RequireType(client, "WTT.Campaigns.Client.Encounters.EncounterSpawnAdmissionGate");
        var compatibility = RequireType(client, "WTT.Campaigns.Client.Encounters.EncounterCompatibility");

        Require(
            CallsAny(
                native,
                "ActivateBot",
                method => method.Parameters.Count > 0 && method.Parameters[0].ParameterType.FullName == "BotCreationData"
            ),
            "EncounterNative uses the concrete BotCreationData activation overload"
        );
        Require(
            CallsAny(native, "ActivateBotCallback", method => method.DeclaringType.FullName == "EFT.BotSpawner"),
            "EncounterNative completes native registration through ActivateBotCallback"
        );
        Require(
            CallsAny(native, "SetBotAsEnemy", method => method.DeclaringType.FullName == "EFT.BotSpawner"),
            "EncounterNative initializes native enemy relationships through BotSpawner"
        );
        Require(
            CallsAny(native, ".ctor", method => method.DeclaringType.FullName == "BotsGroup"),
            "EncounterNative constructs the native squad group"
        );
        Require(
            CallsAny(native, "AddNoKey", method => method.DeclaringType.FullName == "BotZoneGroupsDictionary"),
            "EncounterNative registers the squad with native zone groups"
        );
        Require(
            CallsAny(native, "CreateWithoutProfile", method => method.DeclaringType.FullName == "BotCreationData"),
            "EncounterNative creates a profile-bearing BotCreationData record"
        );
        Require(
            CallsAny(native, "AddProfile", method => method.DeclaringType.FullName == "BotCreationData"),
            "EncounterNative attaches the generated profile to BotCreationData"
        );
        Require(
            CallsAny(native, "AddPosition", method => method.DeclaringType.FullName == "BotCreationData"),
            "EncounterNative passes the authored position to BotCreationData"
        );
        Require(
            CallsAny(native, "StopSpawn", method => method.DeclaringType.FullName == "BotCreationData"),
            "EncounterNative stops native spawn data during cleanup"
        );
        Require(
            CallsAny(native, "BotDied", method => method.DeclaringType.FullName == "EFT.BotSpawner"),
            "EncounterNative removes preview bots through the native BotSpawner lifecycle"
        );
        Require(
            CallsAny(native, "Dispose", method => method.DeclaringType.FullName is "EFT.BotOwner" or "EFT.Player"),
            "EncounterNative disposes native bot and player objects"
        );
        Require(
            CallsAny(native, "FindBotControllerEditorOnly", method => method.DeclaringType.FullName == "EFT.BotsController"),
            "EncounterNative waits for the editor bot controller"
        );
        Require(
            CallsAny(native, "get_BotSpawner", method => method.DeclaringType.FullName == "EFT.BotsController"),
            "EncounterNative waits for the native bot spawner"
        );
        Require(
            CallsAny(native, "get_StartProfilesLoaded", method => method.DeclaringType.FullName == "IBotCreator"),
            "EncounterNative waits for generated profile readiness"
        );

        var install = RequireMethod(native, "Install");
        Require(Calls(install, "Ensure"), "EncounterNative fail-closes on optional-mod compatibility");
        Require(Calls(install, "Install"), "EncounterNative installs admission coverage before preview activation");

        var ensure = RequireMethod(compatibility, "Ensure");
        Require(ContainsString(ensure, "CanBotQuest"), "Compatibility checks the SAIN quest eligibility signal");
        Require(ContainsString(ensure, "AddCustomLayer"), "Compatibility checks the BigBrain custom-layer registration API");
        Require(ContainsTypeReference(ensure, "SAIN.Interop.SAINExternal"), "Compatibility binds the installed SAIN API");
        Require(ContainsTypeReference(ensure, "DrakiaXYZ.BigBrain.Brains.BrainManager"), "Compatibility binds the installed BigBrain API");

        var gateInstall = RequireMethod(gate, "Install");
        Require(ContainsString(gateInstall, "com.wtt.campaigns.encounter-admission"), "Admission uses its dedicated Harmony identity");
    }

    private static void CheckAdmissionPolicy()
    {
        Require(
            EncounterSpawnAdmissionPolicy.IsAmbientScheduler("EFT.BotSpawner")
                && EncounterSpawnAdmissionPolicy.IsAmbientScheduler("EFT.BotsController")
                && !EncounterSpawnAdmissionPolicy.IsAmbientScheduler("BotCreatorClient"),
            "Only native bot scheduler types are ambient admission sources"
        );
        Require(
            EncounterSpawnAdmissionPolicy.DecideTask(false, false, false, "EFT.BotSpawner") == EncounterSpawnTaskDecision.PassThrough,
            "Normal raid async spawn calls pass through"
        );
        Require(
            EncounterSpawnAdmissionPolicy.DecideTask(true, true, true, "BotCreatorClient") == EncounterSpawnTaskDecision.PassThrough,
            "A current reservation passes through the async prefix"
        );
        Require(
            EncounterSpawnAdmissionPolicy.DecideTask(true, false, false, "EFT.BotSpawner") == EncounterSpawnTaskDecision.CompleteNoOp
                && EncounterSpawnAdmissionPolicy.DecideTask(true, false, false, "EFT.BotsController")
                    == EncounterSpawnTaskDecision.CompleteNoOp,
            "Unscoped editor scheduler requests complete as no-ops"
        );
        Require(
            EncounterSpawnAdmissionPolicy.DecideTask(true, false, true, "EFT.BotSpawner") == EncounterSpawnTaskDecision.Fault
                && EncounterSpawnAdmissionPolicy.DecideTask(true, false, true, "EFT.BotsController") == EncounterSpawnTaskDecision.Fault
                && EncounterSpawnAdmissionPolicy.DecideTask(true, false, false, "BotCreatorClient") == EncounterSpawnTaskDecision.Fault,
            "Explicit stale or mismatched reservations remain faulted"
        );
    }

    private static void CheckPatrolCoverage(AssemblyDefinition client)
    {
        var runtime = RequireType(client, "WTT.Campaigns.Client.Encounters.EncounterPatrolRuntime");
        var layer = RequireType(client, "WTT.Campaigns.Client.Encounters.CampaignPatrolLayer");
        var logic = RequireType(client, "WTT.Campaigns.Client.Encounters.CampaignPatrolLogic");
        Require(
            layer.BaseType?.FullName == "DrakiaXYZ.BigBrain.Brains.CustomLayer",
            "Campaign patrol layer derives from BigBrain CustomLayer"
        );
        Require(
            logic.BaseType?.FullName == "DrakiaXYZ.BigBrain.Brains.CustomLogic",
            "Campaign patrol logic derives from BigBrain CustomLogic"
        );
        foreach (var methodName in new[] { "IsActive", "GetNextAction", "IsCurrentActionEnding", "Stop" })
            Require(FindMethod(layer, methodName) != null, "Campaign patrol layer implements " + methodName);
        Require(FindMethod(logic, "Update") != null, "Campaign patrol logic implements Update");
        Require(
            CallsAny(runtime, "CanBotQuest", method => method.DeclaringType.FullName == "SAIN.Interop.SAINExternal"),
            "Patrol eligibility checks SAIN before movement"
        );
        Require(
            CallsAny(runtime, "GetActiveLayer", method => method.DeclaringType.FullName == "DrakiaXYZ.BigBrain.Brains.BrainManager"),
            "Patrol eligibility checks the active BigBrain layer"
        );
        Require(
            CallsAny(runtime, "AddCustomLayer", method => method.DeclaringType.FullName == "DrakiaXYZ.BigBrain.Brains.BrainManager"),
            "Patrol registers above idle wandering through BigBrain"
        );
    }

    private static void CheckPatrolFacing(AssemblyDefinition native, AssemblyDefinition client)
    {
        var steering = RequireType(native, "BotSteering");
        var moving = RequireMethod(steering, "LookToMovingDirection", "System.Void");
        Require(Calls(moving, "LookToMovingDirection"), "Native movement-facing overload uses configured rotation speed");
        Require(Calls(RequireMethod(steering, "Steering"), "get_DirCurPoint"), "Native movement steering follows the current path segment");

        var runtime = RequireType(client, "WTT.Campaigns.Client.Encounters.EncounterPatrolRuntime");
        var face = RequireMethod(runtime, "FaceMovement");
        Require(Calls(face, "LookToMovingDirection"), "Authored patrol selects native movement-facing steering");
        foreach (var guard in new[] { "GetActiveLayer", "SquadEligible", "Owns" })
            Require(Calls(face, guard), "Patrol facing respects navigation ownership and combat handoff: " + guard);
        Require(!Calls(face, "get_RealDestPoint"), "An intermediate corner must not disable patrol facing");
        var ownership = RequireType(client, "WTT.Campaigns.Client.Encounters.EncounterMovementPath");
        var owns = RequireMethod(ownership, "Owns");
        Require(
            Calls(owns, "get_CurPath") && owns.Body.Instructions.Any(i => i.OpCode == OpCodes.Ceq),
            "Movement ownership compares the exact native path reference"
        );
        Require(Calls(RequireMethod(ownership, "Keep"), "RemainingPathClear"), "Retained paths must be checked against current scenery");
        Require(Calls(RequireMethod(ownership, "Keep"), "KeepPath"), "Retained paths must allow stalled movement to retry");
        Require(
            CallsAny(runtime, "Keep", m => m.DeclaringType.Name == "EncounterMovementPath"),
            "Patrol refresh preserves corner progress"
        );
        var hold = RequireType(client, "WTT.Campaigns.Client.Encounters.EncounterHoldRuntime");
        Require(Calls(RequireMethod(hold, "Move"), "Keep"), "Hold returns preserve corner progress too");
        var navigation = RequireType(client, "WTT.Campaigns.Client.Encounters.EncounterNavigation");
        Require(Calls(RequireMethod(navigation, "RemainingPathClear"), "Raycast"), "Retained corners respect new NavMesh cuts");
        Require(Calls(RequireMethod(navigation, "RemainingPathClear"), "ClearSegments"), "Retained corners respect solid scenery");
        RequireMethod(RequireType(native, "AbstractBotPath"), "get_CurIndex", "System.Int32");
        RequireMethod(RequireType(native, "AbstractBotPath"), "GetPoint", "UnityEngine.Vector3", "System.Int32");
        Require(
            face.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "OwnsNavigation"),
            "Released patrol navigation cannot retain control of facing"
        );
        var move = RequireMethod(runtime, "Move");
        var calls = move.Body.Instructions.Where(i => i.Operand is MethodReference).ToArray();
        var facingCalls = calls.Where(i => ((MethodReference)i.Operand).Name == "FaceMovement").ToArray();
        var dispatch = calls.Single(i => ((MethodReference)i.Operand).Name == "Apply");
        Require(
            facingCalls.Length == 2 && facingCalls[0].Offset < dispatch.Offset && facingCalls[1].Offset > dispatch.Offset,
            "Patrol refreshes facing on both throttled ticks and freshly dispatched navigation"
        );
    }

    private static void CheckPatrolPathDispatch(AssemblyDefinition native, AssemblyDefinition client)
    {
        RequireMethod(RequireType(native, "BotMover"), "GoToByWay", "System.Void", "UnityEngine.Vector3[]", "System.Single");
        var runtime = RequireType(client, "WTT.Campaigns.Client.Encounters.EncounterPatrolRuntime");
        Require(
            CallsAny(runtime, "GoToByWay", m => m.DeclaringType.Name == "BotMover"),
            "Patrol submits the validated live mesh corners to native movement"
        );
        Require(
            !CallsAny(runtime, "GoToPoint", m => m.DeclaringType.Name == "BotMover"),
            "Patrol must not reenter baked cover graph routing or its teleport recovery"
        );
        var navigation = RequireType(client, "WTT.Campaigns.Client.Encounters.EncounterNavigation");
        var path = RequireMethod(navigation, "TryPatrolPath");
        foreach (var call in new[] { "CalculatePath", "get_status", "get_corners", "ClearSegments" })
            Require(Calls(path, call), "Live patrol paths validate " + call);
        Require(
            Calls(RequireMethod(runtime, "Describe"), "GetActiveLogic"),
            "Patrol diagnostics include the native action that owns movement"
        );
        Console.WriteLine(
            "Patrol dispatch: live NavMesh corners, solid clearance, native movement API and action diagnostics verified offline."
        );
    }

    private static void CheckHealthInterceptionCoverage(AssemblyDefinition client)
    {
        var restrictions = RequireType(client, "WTT.Campaigns.Client.Authoring.EditorRestrictions");
        var enable = RequireMethod(restrictions, "Enable", "System.Void");
        var healthHook = RequireMethod(restrictions, "PreviewHealthChanged", "System.Void", "EFT.HealthSystem.ActiveHealthController");
        Require(healthHook.IsPrivate && healthHook.IsStatic, "PreviewHealthChanged remains a private static postfix callback");

        // The callback must inspect both lethal vital parts.  Checking only the first call would
        // allow a head-only or chest-only health path to skip defeat handling after an effect.
        var vitalCalls = CountCalls(
            healthHook,
            "GetBodyPartHealth",
            call =>
                call.DeclaringType.FullName == "EFT.HealthSystem.ActiveHealthController"
                || call.DeclaringType.FullName.StartsWith("EFT.HealthSystem.BaseHealthController`1", StringComparison.Ordinal)
        );
        Require(vitalCalls >= 2, "PreviewHealthChanged checks both Head and Chest with GetBodyPartHealth");
        Require(
            CallsAny(healthHook, "AiDefeated", call => call.DeclaringType.FullName == "WTT.Campaigns.Client.Authoring.RaidEditor"),
            "PreviewHealthChanged reports lethal damage through RaidEditor.AiDefeated"
        );

        // Enable must actually install this method as a Harmony postfix.  The handler name is
        // emitted by nameof(PreviewHealthChanged), while Patch proves the registration call was
        // retained by the compiled assembly.
        Require(ContainsString(enable, "PreviewHealthChanged"), "EditorRestrictions.Enable names the health postfix");
        Require(Calls(enable, "Patch"), "EditorRestrictions.Enable installs the health postfix through Harmony");

        var killHook = RequireMethod(restrictions, "PreviewKill", "System.Boolean", "EFT.HealthSystem.ActiveHealthController");
        Require(
            CallsAny(killHook, "AiDefeated", call => call.DeclaringType.FullName == "WTT.Campaigns.Client.Authoring.RaidEditor"),
            "PreviewKill reports direct lethal transitions through RaidEditor.AiDefeated"
        );
    }

    private static void CheckPlugin(AssemblyDefinition assembly, string id, string minimum, string name)
    {
        var metadata = assembly
            .MainModule.GetTypes()
            .SelectMany(type => type.CustomAttributes)
            .FirstOrDefault(attribute => attribute.AttributeType.Name == "BepInPlugin");
        Require(metadata != null && metadata.ConstructorArguments.Count >= 3, name + " exposes BepInPlugin metadata");
        Require((string)metadata!.ConstructorArguments[0].Value == id, name + " plugin id is " + id);
        var versionText = (string)metadata.ConstructorArguments[2].Value;
        Require(
            Version.TryParse(versionText, out var installed) && installed!.CompareTo(Version.Parse(minimum)) >= 0,
            name + " version meets " + minimum
        );
    }

    private static string PluginPath(string gameRoot, string fileName)
    {
        var pluginRoot = Path.Combine(gameRoot, "BepInEx", "plugins");
        var direct = Path.Combine(pluginRoot, fileName);
        if (File.Exists(direct))
            return direct;

        var matches = Directory.Exists(pluginRoot)
            ? Directory.GetFiles(pluginRoot, fileName, SearchOption.AllDirectories)
            : Array.Empty<string>();
        Require(matches.Length == 1, "Installed optional plugin is missing or ambiguous: " + fileName);
        return matches[0];
    }

    private static bool IsGameWorld(TypeDefinition type, ModuleDefinition module)
    {
        var current = type;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (current != null && seen.Add(current.FullName))
        {
            if (current.FullName == "EFT.GameWorld")
                return true;
            var baseName = current.BaseType?.FullName;
            if (string.IsNullOrWhiteSpace(baseName) || !module.GetTypes().Any(candidate => candidate.FullName == baseName))
                return false;
            current = module.GetTypes().First(candidate => candidate.FullName == baseName);
        }

        return false;
    }

    private static bool IsRegisterPlayer(MethodDefinition method) =>
        method.Name == "RegisterPlayer" && method.Parameters.Count == 1 && method.Parameters[0].ParameterType.FullName == "EFT.IPlayer";

    private static TypeDefinition RequireType(AssemblyDefinition assembly, string fullName)
    {
        var type =
            assembly.MainModule.GetTypes().FirstOrDefault(candidate => candidate.FullName == fullName)
            ?? assembly.MainModule.GetTypes().FirstOrDefault(candidate => candidate.Name == fullName);
        Require(type != null, "Missing required type: " + fullName);
        return type!;
    }

    private static MethodDefinition RequireMethod(TypeDefinition type, string name, string? returnType = null, params string[] parameters)
    {
        var method = FindMethod(type, name, returnType, parameters);
        Require(method != null, "Missing required method: " + type.FullName + "." + name + "(" + string.Join(", ", parameters) + ")");
        return method!;
    }

    private static MethodDefinition? FindMethod(TypeDefinition type, string name, string? returnType = null, params string[] parameters) =>
        type.Methods.FirstOrDefault(method =>
            method.Name == name
            && (returnType == null || method.ReturnType.FullName == returnType)
            && (
                (returnType == null && parameters.Length == 0)
                || method.Parameters.Select(parameter => parameter.ParameterType.FullName).SequenceEqual(parameters)
            )
        );

    private static IEnumerable<MethodDefinition> AllMethods(TypeDefinition type)
    {
        foreach (var method in type.Methods)
            yield return method;
        foreach (var nested in type.NestedTypes)
        foreach (var method in AllMethods(nested))
            yield return method;
    }

    private static bool ContainsString(MethodDefinition method, string value) =>
        method.HasBody
        && method.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Ldstr && (string)instruction.Operand == value);

    private static bool Calls(MethodDefinition method, string name) =>
        method.HasBody && method.Body.Instructions.Any(instruction => instruction.Operand is MethodReference call && call.Name == name);

    private static int CountCalls(MethodDefinition method, string name, Func<MethodReference, bool>? predicate = null) =>
        !method.HasBody
            ? 0
            : method.Body.Instructions.Count(instruction =>
                instruction.Operand is MethodReference call && call.Name == name && (predicate == null || predicate(call))
            );

    private static bool CallsAny(TypeDefinition type, string name, Func<MethodReference, bool> predicate) =>
        AllMethods(type)
            .Any(method =>
                method.HasBody
                && method.Body.Instructions.Any(instruction =>
                    instruction.Operand is MethodReference call && call.Name == name && predicate(call)
                )
            );

    private static bool CallsAny(MethodDefinition method, string name, Func<MethodReference, bool> predicate) =>
        method.HasBody
        && method.Body.Instructions.Any(instruction => instruction.Operand is MethodReference call && call.Name == name && predicate(call));

    private static bool ContainsTypeReference(MethodDefinition method, TypeDefinition target) =>
        method.HasBody
        && method.Body.Instructions.Any(instruction => instruction.Operand is TypeReference reference && TypeMatches(reference, target));

    private static bool ContainsTypeReference(MethodDefinition method, string fullName) =>
        method.HasBody
        && method.Body.Instructions.Any(instruction =>
            instruction.Operand is TypeReference reference && (reference.FullName == fullName || reference.Name == fullName)
        );

    private static bool TypeMatches(TypeReference reference, TypeDefinition target) =>
        reference.FullName == target.FullName || reference.Name == target.Name;

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("Encounter hook check failed: " + message);
    }
}
