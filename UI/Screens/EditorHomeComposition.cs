namespace WTT.Campaigns.UI.Screens;

// Shared geometry for the native EFT screen and the offline uGUI preview.
public static class EditorHomeComposition
{
    public sealed class Element
    {
        public string Id,
            Text,
            Kind;
        public int X,
            Y,
            Width,
            Height,
            Size;

        public Element(string kind, string id, string text, int x, int y, int width, int height, int size = 17)
        {
            Kind = kind;
            Id = id;
            Text = text;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Size = size;
        }
    }

    public static readonly Element[] Elements =
    {
        new Element("Panel", "HomeFrame", "", 100, 60, 1080, 600),
        new Element("Panel", "HomeTitleBar", "", 100, 60, 1080, 40),
        new Element("Text", "EditorTitle", "WTT / CAMPAIGNS — CAMPAIGN EDITOR · BETA", 114, 64, 790, 32, 21),
        new Element("Button", "EditorStartup", "STARTUP: NORMAL", 916, 66, 250, 28),
        new Element("Text", "DraftHeading", "CAMPAIGN DRAFT", 124, 124, 292, 28, 20),
        new Element("Button", "EditorDraft", "SELECT DRAFT", 124, 164, 292, 38),
        new Element("Text", "DraftHelp", "Choose the campaign you want to edit.", 124, 212, 292, 44),
        new Element("Button", "EditorRefresh", "REFRESH DRAFTS", 124, 270, 292, 32),
        new Element("Text", "CreatorHeading", "WEB CREATOR", 124, 364, 292, 28, 20),
        new Element("Text", "CreatorHelp", "Create drafts and edit quests, dialogue, traders and rewards.", 124, 406, 292, 60),
        new Element("Button", "EditorWeb", "OPEN CREATOR", 124, 480, 292, 34),
        new Element("Text", "MapHeading", "MAP WORKSPACE", 460, 124, 692, 28, 20),
        new Element("Text", "EditorSelection", "Select a campaign draft to begin", 460, 158, 692, 54, 23),
        new Element("Text", "LayoutHeading", "MISSION LAYOUT", 460, 230, 692, 24),
        new Element("Button", "EditorLayout", "NEW LAYOUT", 460, 262, 692, 38),
        new Element("Text", "LocationHeading", "LOCATION", 460, 326, 692, 24),
        new Element("Button", "EditorMap", "SELECT LOCATION", 460, 358, 692, 38),
        new Element("Text", "EditorMapHelp", "Choose a location for a new layout.", 460, 406, 692, 48),
        new Element("Button", "EditorMissionTest", "TEST MISSION", 460, 478, 330, 36),
        new Element("Button", "EditorCampaignTest", "TEST CAMPAIGN FLOW", 806, 478, 346, 36),
        new Element("Text", "WorkspaceHelp", "Both tests use disposable state. Your real character is preserved.", 460, 517, 692, 26, 15),
        new Element("Text", "WorkspaceMode", "EDITOR SESSION  /  Gameplay and progression disabled", 124, 546, 1028, 26, 16),
        new Element("Button", "EditorReturn", "RETURN TO GAME", 124, 608, 210, 34),
        new Element("Text", "EditorHomeStatus", "Connecting to editor…", 352, 592, 532, 58, 16),
        new Element("Button", "EditorRetry", "RETRY CONNECTION", 916, 608, 236, 34),
        new Element("Button", "EditorOpen", "OPEN MAP", 916, 608, 236, 34),
    };
}
