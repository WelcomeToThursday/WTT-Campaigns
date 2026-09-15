using System.Collections.Generic;

namespace WTT.Campaigns.UI.Controls;

public static class EditorToolkitIcons
{
    public static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>
    {
        ["Undo"] = "undo-rounded",
        ["Redo"] = "redo-rounded",
        ["Move"] = "open-with-rounded",
        ["Rotate"] = "rotate-right-rounded",
        ["Scale"] = "expand-content-rounded",
        ["Snap"] = "grid-on-rounded",
        ["EditorWalk"] = "directions-walk-rounded",
        ["EditorReset"] = "restart-alt-rounded",
        ["Layouts"] = "map-outline-rounded",
        ["Routes"] = "directions-walk-rounded",
        ["Zones"] = "deployed-code-outline-rounded",
        ["Bindings"] = "bolt-rounded",
        ["Captures"] = "photo-camera-outline-rounded",
        ["Scene"] = "forest-outline-rounded",
        ["AI"] = "psychology-rounded",
        ["CloseEditor"] = "close-rounded",
        ["HelpToggle"] = "help-outline-rounded",
        ["WindowsToggle"] = "view-sidebar-outline-rounded",
        ["ContextToggle"] = "tune-rounded",
        ["LibraryCollapse"] = "remove-rounded",
        ["InspectorCollapse"] = "remove-rounded",
        ["LibraryPopout"] = "open-in-new-rounded",
        ["InspectorPopout"] = "open-in-new-rounded",
        ["AddBox"] = "deployed-code-outline-rounded",
        ["AddSphere"] = "circle-outline-rounded",
        ["Capture"] = "add-a-photo-outline-rounded",
        ["Pick"] = "touch-app-outline-rounded",
        ["MapNew"] = "add-location-alt-outline-rounded",
        ["MapStart"] = "flag-outline-rounded",
        ["MapCheckpoint"] = "location-on-outline-rounded",
        ["MapExit"] = "logout-rounded",
        ["MapBarrier"] = "block-rounded",
        ["MapMoveObject"] = "open-with-rounded",
        ["MapCopyObject"] = "content-copy-rounded",
        ["MapHideObject"] = "visibility-off-outline-rounded",
        ["MapDoor"] = "door-front-outline-rounded",
    };
}
