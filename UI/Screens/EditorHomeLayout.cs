using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.UI.Screens;

public static partial class RaidEditorLayout
{
    private static void BuildEditorHome(UiElements ui, Transform root)
    {
        var home = UiElements.Rect("EditorHome", root, 1280, 720);
        foreach (var item in EditorHomeComposition.Elements)
        {
            var rect = UiElements.Rect(item.Id, home, item.Width, item.Height);
            Place(rect, item.X, item.Y, item.Width, item.Height);
            if (item.Kind == "Panel")
            {
                UiElements.Fill(rect, item.Id.EndsWith("TitleBar") ? EditorTarkovTheme.Container : EditorTarkovTheme.Surface, true);
                EditorTarkovTheme.Frame(rect);
            }
            else if (item.Kind == "Button")
            {
                var button = ui.Button(rect, item.Text, item.Width, 0, 0, () => { }, item.Height);
                button.name = item.Id;
                rect.name = item.Id + "Slot";
                button.GetComponentInChildren<Text>().fontSize = 18;
            }
            else
            {
                var label = ui.Label(rect, item.Id, item.Text, item.Size, item.Width, item.Height);
                rect.name = item.Id + "Slot";
                UiElements.Stretch(label.rectTransform);
            }
        }
        home.gameObject.SetActive(false);
    }

    private static void BuildMapInspector(UiElements ui, Transform parent)
    {
        var map = Stack(parent, "MapInspector");
        Field(ui, map, "MapName", "Name");
        Vectors(ui, map, "MapPosition", "POSITION · metres");
        Vectors(ui, map, "MapRotation", "ROTATION · degrees");
        Vectors(ui, map, "MapSize", "SIZE / SCALE");
        ActionRow(ui, map, "MapShapeGroup", ("MapShape", "Shape: box"));
        ActionRow(ui, map, "MapPlacementGroup", ("MapRebind", "Rebind to picked"), ("MapAtPlayer", "At player"));
        ActionRow(ui, map, "MapOrderGroup", ("MapEarlier", "Earlier checkpoint"), ("MapLater", "Later checkpoint"));
        ActionRow(ui, map, "MapWalkGroup", ("MapWalkStart", "Walk from marker: off"));
        ActionRow(ui, map, "MapRecordActions", ("MapCopy", "Duplicate"), ("MapDelete", "Delete"));
        var details = Row(map, "MapDetailsGroup", 170);
        ui.Label(
            details,
            "MapDetails",
            "Pick scenery in Scene, then return to Layouts.\nAll map edits belong to the selected layout.",
            15,
            318,
            170
        ).alignment = TextAnchor.UpperLeft;
        map.gameObject.SetActive(false);
    }
}
