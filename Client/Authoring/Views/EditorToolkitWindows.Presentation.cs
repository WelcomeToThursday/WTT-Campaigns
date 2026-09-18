namespace WTT.Campaigns.Client.Authoring.Views;

internal sealed partial class EditorToolkitWindows
{
    public void Present(
        string mode,
        string kind,
        bool hasSelection,
        bool capture,
        bool picked,
        bool mapReady,
        bool bindZone,
        bool sceneWorkspace = false
    )
    {
        var routes = mode == "Routes" && mapReady;
        var layouts = (mode == "Layouts" || mode == "Maps") && mapReady;
        var maps = layouts || routes;
        var ai = mode == "AI";
        Visible("RouteGuideGroup", routes);
        Visible("RouteFrameGroup", routes && hasSelection && kind != "Layout");
        Visible("MapWalkGroup", routes);
        Visible("MapNormalRaidGroup", layouts && hasSelection && kind == "Layout");
        Visible("MapLayerHelpGroup", layouts && hasSelection && kind == "Layout");

        var zone = (mode is "Zones" or "Hazards") && hasSelection;

        var aiPoint = ai && (kind == "spawn" || kind == "waypoint" || kind == "trigger");
        var point = hasSelection && kind != "Scene" && (zone || mode == "Captures" || aiPoint);

        if (!sceneWorkspace)
        {
            Visible("MapInspector", maps);
            Visible("RecordInspector", !maps);
        }

        Visible("DoorInspectorGroup", false);
        Visible("EditorMapToolbar", mapReady);
        Visible("EditorUnload", mapReady);

        Visible("NameGroup", hasSelection && mode != "Scene" && kind != "Scene");

        _identityAvailable = hasSelection;

        Visible("EventKindGroup", mode == "Bindings" && hasSelection && kind != "Scene");

        Visible("PositionGroup", point);
        Visible("RotationGroup", point);

        Visible("SizeGroup", zone && kind == "Box" || ai && kind == "trigger");
        Visible("RadiusGroup", zone && kind == "Sphere" || ai && kind == "trigger");

        Visible("PlacementGroup", point && !ai);
        Visible("ZoneUsesGroup", zone && mode != "Hazards");
        Visible("HazardInfoGroup", zone && mode == "Hazards");
        Visible("ZoneScopeGroup", zone && mapReady);

        Visible("SceneActionsGroup", picked || bindZone);

        Visible("RecordActionsGroup", hasSelection && mode != "Scene" && kind != "Scene");

        _recordAvailable = hasSelection || picked;

        if (!sceneWorkspace)
        {
            Visible("MapPositionGroup", hasSelection && kind != "Layout" && kind != "Door");

            Visible("MapRotationGroup", hasSelection && kind != "Layout" && kind != "Door");

            Visible("MapSizeGroup", kind == "Volume" || kind == "Checkpoint" || kind == "Copy");

            Visible("MapShapeGroup", kind == "Volume" || kind == "Checkpoint");

            Visible("MapPlacementGroup", hasSelection && kind != "Layout");

            Visible("MapRebind", kind == "Copy" || kind == "Move" || kind == "Hide" || kind == "Door");

            Visible("MapAtPlayer", kind != "Door");

            Visible("MapOrderGroup", kind == "Checkpoint");

            Visible("MapRecordActions", hasSelection && (!routes || kind != "Layout"));
        }

        Visible("AddBox", mode == "Zones" || mode == "Bindings");
        Visible("AddSphere", mode == "Zones" || mode == "Bindings");

        Visible("Capture", mode == "Captures");
        Visible("Pick", mode == "Scene" || mode == "Captures" || mode == "Bindings");

        foreach (
            var name in new[]
            {
                "MapNew",
                "MapStart",
                "MapCheckpoint",
                "MapExit",
                "MapBarrier",
                "MapMoveObject",
                "MapCopyObject",
                "MapHideObject",
                "MapDoor",
                "ZoneCreateScope",
            }
        )
            Visible(
                name,
                name == "MapNew" ? layouts && !routes
                    : name == "MapStart" || name == "MapCheckpoint" || name == "MapExit" ? routes
                    : name == "ZoneCreateScope" ? mode is "Zones" or "Hazards"
                    : sceneWorkspace
            );

        Visible("CaptureTask", capture);

        if (_capture != capture)
        {
            _capture = capture;
            FitPanels();
        }
        FitContents();
        UpdateDetails();
    }

    public void PresentScene(
        bool enabled,
        string tab,
        string kind,
        bool selected,
        bool hasPoint,
        bool canEdit,
        bool picked,
        bool hasSavedPoint = true
    )
    {
        var catalog = tab == "Catalog";
        var removed = kind == "Hide";
        Visible("SceneTabs", enabled);
        Visible("SceneFilters", enabled && catalog);
        Visible("SceneInspector", enabled);
        if (enabled)
            Visible("MapWalkGroup", false);
        FitContents();
        if (!enabled)
            return;
        Visible("RecordInspector", false);
        Visible("MapInspector", !catalog && hasPoint);
        Visible("MapRecordActions", false);
        Visible("MapPositionGroup", !removed);
        Visible("MapRotationGroup", !removed);
        Visible("MapSizeGroup", kind == "Copy" || kind == "Move" || kind == "Barrier" || kind == "Volume");
        Visible("MapPlacementGroup", !removed);
        Visible("MapAtPlayer", !removed);
        Visible("MapShapeGroup", kind == "Barrier" || kind == "Volume");
        Visible("MapOrderGroup", false);
        Visible("MapRebind", false);
        Visible("ScenePreviewGroup", catalog && selected);
        Visible("ScenePlaceGroup", catalog);
        Visible("SceneEditGroup", !catalog && selected && !removed);
        Visible("SceneRestoreGroup", !catalog && hasSavedPoint && (kind == "Move" || kind == "Hide" || kind == "Copy"));
        Visible("SceneRestore", kind != "Copy");
        Visible("SceneFocusGroup", !catalog && selected && !removed);
    }

    private void UpdateDetails()
    {
        Visible("IdentityGroup", _identityAvailable && _recordDetails);
        Visible("DetailsGroup", _recordAvailable && _recordDetails);
        Visible("MapDetailsGroup", _mapDetails);
        _view.Get<EditorButton>("RecordDetailsToggle").text = _recordDetails ? "Details −" : "Details +";
        _view.Get<EditorButton>("MapDetailsToggle").text = _mapDetails ? "Details −" : "Details +";
    }
}
