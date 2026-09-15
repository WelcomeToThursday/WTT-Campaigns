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
            T("WorkspaceTitle", "CAMPAIGN EDITOR · BETA"),
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
            R("EditorMapToolbar", B("EditorWalk", "Walkthrough"), B("EditorReset", "Reset preview"))
        ),
        R(
            "CategoryRail",
            B("Layouts", "Layouts"),
            B("Routes", "Routes"),
            B("Zones", "Zones"),
            B("Bindings", "Events"),
            B("Captures", "Captures"),
            B("Scene", "Scene"),
            B("AI", "AI")
        ),
        G(
            "Library",
            I("Search", "Search"),
            R("SceneTabs", B("SceneCatalog", "Catalog"), B("SceneExisting", "In scene"), B("SceneChanges", "Changes")),
            R("SceneFilters", new Node("choice", "SceneSource", "Source"), B("SceneProps", "Props"), B("SceneContainers", "Containers"), B("SceneLoot", "Loot"), B("ScenePresets", "Presets")),
            R("CatalogViews", B("CatalogGrid", "Grid"), B("CatalogList", "List")),
            new Node("browser", "LibraryScroll", ""),
            T("LibraryCount", "No records"),
            R("Paging", B("Previous", "Previous"), B("Next", "Next")),
            R(
                "CreationTools",
                B("AddBox", "+ Box"),
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
                    R("AiPreviewRow", B("AiObserve", "Observe"), B("AiPlaytest", "Playtest")),
                    R("AiRunRow", B("AiSimulate", "Trigger selected"), B("AiReset", "Reset preview"))
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
                    A("ScenePlaceGroup", "ScenePlace", "Place"),
                    R("SceneEditGroup", B("SceneMove", "Move (W)"), B("SceneRotate", "Rotate (E)"), B("SceneScale", "Scale (R)"), B("SceneRemove", "Remove")),
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
                    R("PlacementGroup", B("AtFeet", "At player"), B("AtAim", "At aim point")),
                    R("ZoneUsesGroup", B("InZone", "In zone"), B("VisitPlace", "Visit"), B("LeaveItemAtLocation", "Place item")),
                    C("ZoneScope", "ZONE SCOPE"),
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
                        A("AiWaveWaitPreviousGroup", "AiWaveWaitPrevious", "Wave wait mode")
                    ),
                    G(
                        "AiRosterSection",
                        T("AiRosterHeading", "BOT ROSTER"),
                        A("AiRosterRoleGroup", "AiRosterRole", "Roster role"),
                        A("AiRosterDifficultyGroup", "AiRosterDifficulty", "Roster difficulty"),
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
                        A("AiPaceGroup", "AiPace", "Patrol pace"),
                        A("AiCompletionGroup", "AiCompletion", "Patrol completion"),
                        F("AiWaypointWaitSeconds", "Wait (seconds)")
                    )
                ),
                G(
                    "MapInspector",
                    G("RouteGuideGroup", T("RouteGuide", "")),
                    F("MapName", "Name"),
                    V("MapPosition", "POSITION · metres"),
                    V("MapRotation", "ROTATION · degrees"),
                    V("MapSize", "SIZE / SCALE"),
                    A("MapShapeGroup", "MapShape", "Shape: box"),
                    R("MapPlacementGroup", B("MapRebind", "Rebind to picked"), B("MapAtPlayer", "At player")),
                    R("MapOrderGroup", B("MapEarlier", "Earlier checkpoint"), B("MapLater", "Later checkpoint")),
                    A("MapWalkGroup", "MapWalkStart", "Walk from marker: off"),
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
            B("EnvironmentWindowToggle", "Environment"),
            B("HelpWindowToggle", "Editor controls"),
            T("UiSizeLabel", "UI size: 85%"),
            R("UiSizeActions", B("UiSizeSmaller", "Smaller"), B("UiSizeLarger", "Larger")),
            B("UiSizeReset", "Reset UI size"),
            B("ResetLayout", "Reset layout")
        ),
        G("ContextMenu", B("EditorUnload", "Unload map / return home"), B("EnvironmentToggle", "Environment / time and weather")),
        R("CaptureTask", T("CaptureRequest", ""), B("Complete", "Complete capture"), B("Cancel", "Cancel")),
        R("StatusBar", T("Status", ""), T("Request", "")),
        T("EditorWalkStatus", "Esc to return to editing"),
        G(
            "ConflictShield",
            G(
                "Conflict",
                T("ConflictHeading", "Resolve draft conflict"),
                T("ConflictPath", ""),
                I("LocalConflict", "Local version"),
                I("RemoteConflict", "Remote version"),
                R("ConflictActions", B("KeepLocal", "Keep local"), B("KeepRemote", "Keep remote"))
            )
        ),
    };
}
