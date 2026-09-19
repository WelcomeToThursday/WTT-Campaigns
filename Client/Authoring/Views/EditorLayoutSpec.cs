namespace WTT.Campaigns.Client.Authoring.Views;

// Platform-independent control inventory: shared by runtime construction and offline coverage.
internal static class EditorLayoutSpec
{
    internal sealed class Node
    {
        internal readonly string Kind,
            Id,
            Text;
        internal readonly Node[] Children;

        internal Node(string kind, string id, string text, params Node[] children)
        {
            Kind = kind;
            Id = id;
            Text = text;
            Children = children;
        }
    }

    private static Node B(string id, string text) => new("button", id, text);

    private static Node Check(string id, string text) => new("toggle", id, text);

    private static Node T(string id, string text) => new("text", id, text);

    private static Node I(string id, string text) => new("input", id, text);

    private static Node G(string id, params Node[] children) => new("group", id, "", children);

    private static Node R(string id, params Node[] children) => new("row", id, "", children);

    private static Node F(string id, string text) => G(id + "Group", I(id, text));

    private static Node V(string id, string text) =>
        G(id + "Group", T(id + "Caption", text), R(id + "Axes", I(id + "X", "X"), I(id + "Y", "Y"), I(id + "Z", "Z")));

    private static Node C(string id, string text) => G(id + "Group", new Node("choice", id, text));

    private static Node A(string id, string button, string caption) => R(id, B(button, caption));

    internal static readonly Node[] Sections =
    {
        R(
            "WorkspaceTitleBar",
            T("WorkspaceTitle", "EDITOR · BETA"),
            T("Connection", "Connecting…"),
            B("ContextToggle", "Session"),
            B("WindowsToggle", "Windows"),
            B("HelpToggle", "Help"),
            B("CloseEditor", "Close")
        ),
        R(
            "TransformToolbar",
            B("Undo", "Undo"),
            B("Redo", "Redo"),
            B("Move", "Move"),
            B("Rotate", "Rotate"),
            B("Scale", "Scale"),
            B("Snap", "Snap"),
            T("CameraSpeedLabel", "Fly m/s"),
            B("CameraSlower", "-"),
            I("CameraSpeed", ""),
            B("CameraFaster", "+"),
            R(
                "EditorMapToolbar",
                B("EditorWalk", "Walkthrough"),
                B("AiObserve", "Observe"),
                B("AiPlaytest", "Playtest"),
                B("TestCheckpoints", "Test checkpoints"),
                new Node("choice", "AiPlaytestGear", "Playtest kit"),
                B("EditorReset", "Reset preview")
            )
        ),
        R(
            "CategoryRail",
            B("Layouts", "Layouts"),
            B("Routes", "Routes"),
            B("Zones", "Zones"),
            B("Hazards", "Hazards"),
            B("Bindings", "Events"),
            B("Captures", "Captures"),
            B("Scene", "Scene"),
            B("LootTool", "Loot"),
            B("AI", "AI")
        ),
        G(
            "Library",
            I("Search", "Search"),
            R("SceneTabs", B("SceneCatalog", "Catalog"), B("SceneExisting", "In scene"), B("SceneChanges", "Changes")),
            R("SceneFilters", new Node("choice", "SceneSource", "Source"), new Node("choice", "SceneFilter", "Filter")),
            R("CatalogViews", B("CatalogGrid", "Grid"), B("CatalogList", "List")),
            new Node("browser", "LibraryScroll", ""),
            T("LibraryCount", "No records"),
            R("Paging", B("Previous", "Previous"), B("Next", "Next")),
            R(
                "CreationTools",
                B("AddBox", "+ Box"),
                B("AddMinefield", "+ Minefield"),
                B("AddClaymore", "+ Claymore"),
                B("AddSniper", "+ Sniper zone"),
                B("AddBarbedWire", "+ Barbed wire"),
                B("AddSphere", "+ Sphere"),
                B("Capture", "Capture"),
                B("Pick", "Pick scenery"),
                B("MapNew", "+ Layout"),
                B("MapStart", "Set start"),
                B("MapCheckpoint", "+ Checkpoint"),
                B("MapExit", "Set exit"),
                B("MapBarrier", "+ Barrier"),
                B("MapMoveObject", "Move prop"),
                B("MapCopyObject", "Copy prop"),
                B("MapHideObject", "Hide prop"),
                B("MapDoor", "Door state"),
                new Node("choice", "ZoneCreateScope", "New zone scope")
            ),
            G(
                "AiTools",
                G(
                    "AiCreateSection",
                    T("AiCreateHeading", "BUILD ENCOUNTERS"),
                    R("AiCreateRow", B("AiEncounter", "+ Encounter"), B("AiWave", "+ Wave"), B("AiRoster", "+ Roster")),
                    R("AiPlacementRow", B("AiSpawn", "+ Spawn"), B("AiPatrol", "+ Patrol"), B("AiWaypoint", "+ Waypoint"))
                ),
                G("AiNavigationSection", T("AiNavigationHeading", "NAVIGATION"), B("AiNavigation", "Inspect navigation: off")),
                G(
                    "AiPreviewSection",
                    T("AiPreviewHeading", "TEST ENCOUNTERS"),
                    R("AiRunRow", B("AiSimulate", "Trigger selected"), B("AiReset", "Reset preview"))
                )
            )
        ),
        G(
            "LootConfiguration",
            new Node(
                "scroll",
                "ContainerScroll",
                "",
                T("ContainerSelection", "Select a lootable container"),
                T("ContainerEmpty", "Click a placed container in the scene to configure its loot."),
                G(
                    "ContainerSettingsGroup",
                    G(
                        "ContainerLootSection",
                        T("ContainerHeading", "CONTENTS"),
                        C("ContainerMode", "Contents"),
                        C("ContainerPool", "Loot pool"),
                        F("ContainerChance", "Spawn chance (%)")
                    ),
                    G(
                        "ContainerFixedSection",
                        T("ContainerFixedHeading", "FIXED CONTENTS"),
                        F("ContainerSearch", "Find an item"),
                        C("ContainerItem", "Search results"),
                        F("ContainerQuantity", "Quantity"),
                        A("ContainerItemActions", "ContainerAdd", "Add item"),
                        C("ContainerContents", "Contents"),
                        A("ContainerRemoveGroup", "ContainerRemove", "Remove selected item")
                    ),
                    G(
                        "ContainerAccessSection",
                        T("ContainerAccessHeading", "ACCESS"),
                        G("ContainerLockGroup", Check("ContainerLock", "Locked")),
                        T("ContainerKey", "No key selected"),
                        F("ContainerKeySearch", "Find a key"),
                        C("ContainerKeyItem", "Keys"),
                        A("ContainerKeyActions", "ContainerUseKey", "Use selected key")
                    )
                )
            )
        ),
        G(
            "Inspector",
            new Node(
                "scroll",
                "PropertyScroll",
                "",
                G(
                    "SceneInspector",
                    G("ScenePreviewGroup", new Node("image", "ScenePreview", ""), T("ScenePreviewStatus", "Loading preview…")),
                    G("SceneHeadingGroup", T("SceneHeading", "Select an object")),
                    A("ScenePreviewRetryGroup", "ScenePreviewRetry", "Retry preview"),
                    R("SceneFocusGroup", B("SceneFrame", "Frame (F)"), B("SceneAnchor", "Anchor: Center")),
                    R("ScenePlaceGroup", B("ScenePlace", "Place"), Check("SceneRepeat", "Repeat placement")),
                    R(
                        "SceneEditGroup",
                        B("SceneMove", "Move (W)"),
                        B("SceneRotate", "Rotate (E)"),
                        B("SceneScale", "Scale (R)"),
                        B("SceneRemove", "Remove")
                    ),
                    R("SceneRestoreGroup", B("SceneRestore", "Restore original"), B("SceneRebind", "Rebind to picked")),
                    G("SceneInfoGroup", T("SceneInfo", ""))
                ),
                G(
                    "RecordInspector",
                    F("Name", "Name"),
                    G("IdentityGroup", T("Identity", "")),
                    A("EventKindGroup", "EventKind", "Event kind"),
                    V("Position", "POSITION · metres"),
                    V("Rotation", "ROTATION · degrees"),
                    V("Size", "BOX DIMENSIONS · metres"),
                    F("Radius", "SPHERE RADIUS · metres"),
                    R("PlacementGroup", B("AtAim", "At aim point")),
                    G("ZoneUsesGroup", T("ZoneUsesCaption", "ZONE TYPE"), new Node("choice", "ZoneUses", "Select zone types")),
                    C("ZoneScope", "ZONE SCOPE"),
                    G("HazardInfoGroup", T("HazardInfo", "")),
                    G(
                        "SniperSoundGroup",
                        G("SniperPlaySoundGroup", Check("SniperPlaySound", "Play shot sound")),
                        G("SniperSuppressedGroup", Check("SniperSuppressed", "Suppressed shots"))
                    ),
                    R("SceneActionsGroup", B("Parent", "Select parent"), B("UseObject", "Use scene target")),
                    R("RecordActionsGroup", B("Duplicate", "Duplicate"), B("Delete", "Delete")),
                    G("DetailsGroup", T("Details", "")),
                    A("RecordDetailsToggleGroup", "RecordDetailsToggle", "Details +"),
                    G(
                        "AiTriggerSection",
                        T("AiTriggerHeading", "ACTIVATION"),
                        A("AiTriggerGroup", "AiTrigger", "Trigger"),
                        F("AiTriggerEventId", "Event id"),
                        F("AiTriggerZoneId", "Trigger zone")
                    ),
                    G(
                        "AiWaveSection",
                        T("AiWaveHeading", "WAVE TIMING"),
                        F("AiWaveDelaySeconds", "Delay (seconds)"),
                        G("AiWaveWaitPreviousGroup", Check("AiWaveWaitPrevious", "Wait for previous wave defeat"))
                    ),
                    G(
                        "AiRosterSection",
                        T("AiRosterHeading", "BOT ROSTER"),
                        C("AiRosterRole", "Role"),
                        C("AiRosterDifficulty", "Difficulty"),
                        F("AiRosterCount", "Bot count"),
                        F("AiRosterSquadId", "Squad name")
                    ),
                    G(
                        "AiAssignmentSection",
                        T("AiAssignmentHeading", "SPAWN & PATROL ASSIGNMENTS"),
                        G("AiRosterSpawnNextGroup", new Node("choice", "AiRosterSpawnNext", "Choose spawn points")),
                        T("AiAssignedSpawns", "No spawns assigned"),
                        F("AiRosterSpawnPoints", "Spawn point ids"),
                        G("AiRosterPatrolNextGroup", new Node("choice", "AiRosterPatrolNext", "Choose patrol")),
                        F("AiRosterPatrolRoute", "Patrol route id")
                    ),
                    G(
                        "AiPatrolSection",
                        T("AiPatrolHeading", "PATROL MOVEMENT"),
                        C("AiPace", "Pace"),
                        C("AiCompletion", "Route end"),
                        F("AiWaypointWaitSeconds", "Wait (seconds)")
                    )
                ),
                G(
                    "MapInspector",
                    G("RouteGuideGroup", T("RouteGuide", "")),
                    F("MapName", "Name"),
                    G(
                        "DoorInspectorGroup",
                        C("DoorStartState", "Starting state"),
                        F("DoorKeyId", "Key ID"),
                        F("DoorKeySearch", "Find key by name"),
                        C("DoorKeyResults", "Matching keys"),
                        B("DoorUseKey", "Use selected key"),
                        B("DoorOriginalKey", "Use original key"),
                        C("DoorBreach", "Breaching"),
                        C("DoorOperatable", "Interaction"),
                        T("DoorHelp", "")
                    ),
                    G("MapNormalRaidGroup", Check("MapNormalRaid", "Apply in normal raids")),
                    G(
                        "MapLayerHelpGroup",
                        T(
                            "MapLayerHelp",
                            "After publication: regular characters combine enabled layers from all published campaigns; campaign characters use their own campaign. Scenery, doors, barriers, loot, layout zones, hazards and additional extracts apply. Native PMC spawning and AI remain unchanged."
                        )
                    ),
                    V("MapPosition", "POSITION · metres"),
                    V("MapRotation", "ROTATION · degrees"),
                    V("MapSize", "SIZE / SCALE"),
                    A("MapShapeGroup", "MapShape", "Shape: box"),
                    R("MapPlacementGroup", B("MapRebind", "Rebind to picked"), B("MapAtPlayer", "At player")),
                    R("MapOrderGroup", B("MapEarlier", "Earlier checkpoint"), B("MapLater", "Later checkpoint")),
                    G("MapWalkGroup", Check("MapWalkStart", "Start walkthrough at marker")),
                    R("MapRecordActions", B("MapCopy", "Duplicate"), B("MapDelete", "Delete")),
                    G("MapDetailsGroup", T("MapDetails", "")),
                    A("MapDetailsToggleGroup", "MapDetailsToggle", "Details +"),
                    A("RouteFrameGroup", "RouteFrame", "Frame waypoint")
                )
            )
        ),
        G(
            "EnvironmentMenu",
            new Node(
                "scroll",
                "EnvironmentScroll",
                "",
                T("EnvironmentTitle", "TIME OF DAY"),
                I("EnvironmentHour", "HH:mm"),
                B("EnvironmentApply", "Set time"),
                R(
                    "EnvironmentPresets",
                    B("EnvironmentDawn", "Dawn"),
                    B("EnvironmentNoon", "Noon"),
                    B("EnvironmentDusk", "Dusk"),
                    B("EnvironmentNight", "Night")
                ),
                R(
                    "EnvironmentStep",
                    B("EnvironmentEarlier", "−1 hour"),
                    B("EnvironmentLater", "+1 hour"),
                    B("EnvironmentReset", "Use raid time")
                ),
                T("EnvironmentStatus", ""),
                T("WeatherHeading", "WEATHER"),
                R(
                    "WeatherPresets",
                    B("WeatherPresetClear", "Clear"),
                    B("WeatherPresetCloudy", "Cloudy"),
                    B("WeatherPresetRain", "Rain"),
                    B("WeatherPresetStorm", "Storm")
                ),
                I("WeatherClouds", "Clouds %"),
                I("WeatherRain", "Rain %"),
                I("WeatherFog", "Fog %"),
                I("WeatherWind", "Wind %"),
                I("WeatherThunder", "Thunder %"),
                B("WeatherDirection", "Wind: raid"),
                R("WeatherActions", B("WeatherApply", "Apply weather"), B("WeatherReset", "Use raid weather")),
                T("WeatherStatus", "")
            )
        ),
        G(
            "Controls",
            new Node(
                "scroll",
                "ControlsScroll",
                "",
                T(
                    "Help",
                    "RMB + WASD: fly · Q / E: elevation\nFly m/s: speed · Shift: 4× · Ctrl: precision\nDrag handles · Alt: bypass snap · Ctrl+Z/Y: undo/redo\nDrag titles or dock tabs to float, split or group tools. Drag dividers to resize docks.\nThe left rail opens tools. Drag a floating corner to resize. Reset layout restores defaults.\nEscape dismisses menus, releases a field, cancels a tool, then closes."
                )
            )
        ),
        G(
            "WindowsMenu",
            B("LibraryToggle", "Browser"),
            B("InspectorToggle", "Properties"),
            B("LootWindowToggle", "Loot configuration"),
            B("EnvironmentWindowToggle", "Environment"),
            B("HelpWindowToggle", "Editor controls"),
            T("UiSizeLabel", "UI size: 85%"),
            R("UiSizeActions", B("UiSizeSmaller", "Smaller"), B("UiSizeLarger", "Larger")),
            B("UiSizeReset", "Reset UI size"),
            B("ResetLayout", "Reset layout")
        ),
        G("ContextMenu", B("EditorUnload", "Unload map / return home"), B("EnvironmentToggle", "Environment / time and weather")),
        R("CaptureTask", T("CaptureRequest", ""), B("Complete", "Complete capture"), B("Cancel", "Cancel")),
        R(
            "StatusBar",
            T("Status", ""),
            T("PreviewStatus", ""),
            B("NoticeToggle", "Notice..."),
            G("NoticePanel", new Node("scroll", "NoticeScroll", "", T("NoticeText", "")), B("NoticeDismiss", "Dismiss notice")),
            T("Request", "")
        ),
        T("EditorWalkStatus", "Esc to return to editing"),
        G(
            "ConflictShield",
            G(
                "Conflict",
                T("ConflictHeading", "Resolve draft conflict"),
                T("ConflictPath", ""),
                I("LocalConflict", "Local version"),
                I("RemoteConflict", "Remote version"),
                new Node("scroll", "ConflictRows", ""),
                T("ConflictExplanation", ""),
                R("ConflictActions", B("KeepLocal", "Keep all local"), B("KeepRemote", "Keep all remote"))
            )
        ),
    };
}
