using System.Reflection;
using EFT.UI;
using SPT.Reflection.Patching;
using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.Client.Hub;
using WTT.Campaigns.Client.UI;
using ZLinq;

namespace WTT.Campaigns.Client.Patches.UI;

internal sealed class MenuEntry(Type screenType) : ModulePatch("WTT.Campaigns.MenuEntry." + screenType.Name)
{
    protected override MethodBase GetTargetMethod()
    {
        return screenType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .AsValueEnumerable()
            .Single(method =>
                method.Name == "Show" && method.GetParameters().AsValueEnumerable().FirstOrDefault()?.ParameterType == typeof(EFT.Profile)
            );
    }

    [PatchPostfix]
    private static void Postfix(MenuScreen __instance)
    {
        if (Authoring.EditorMode.Active && !Authoring.EditorMode.Returning)
        {
            Authoring.EditorMode.Instance.MenuReady(__instance);
            return;
        }
        AttachEditor(__instance);
        SeasonUi.Instance.ShowStartupSelection();
        try
        {
            SeasonHubUi.Instance.AttachMenu(__instance);
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
        }
        var existing = __instance
            .GetComponentsInChildren<DefaultUIButton>(true)
            .AsValueEnumerable()
            .FirstOrDefault(button => button.name == "CampaignsEntry");
        if (existing != null)
        {
            existing.gameObject.SetActive(!Plugin.InRaid);
            return;
        }
        var source = __instance._playerButton;
        var parent = source.transform.parent;
        var inMenuList = parent.GetComponent<VerticalLayoutGroup>() != null;
        var entry = UnityEngine.Object.Instantiate(source, inMenuList ? parent : __instance.transform, false);
        entry.name = "CampaignsEntry";
        entry.OnClick.RemoveAllListeners();
        entry.OnClick.AddListener(() => SeasonUi.Instance.Open());
        entry.SetRawText("CHARACTERS", inMenuList ? (int)source._headerLabel.fontSize : 24);
        entry.SetIcon(null);
        entry.Interactable = true;
        if (inMenuList)
        {
            entry.transform.SetSiblingIndex(source.transform.GetSiblingIndex() + 1);
        }
        else
        {
            var rect = (RectTransform)entry.transform;
            rect.anchorMin = rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-45, -110);
            rect.sizeDelta = new Vector2(280, 46);
        }
        entry.gameObject.SetActive(!Plugin.InRaid);
    }

    private static void AttachEditor(MenuScreen screen)
    {
        if (screen.GetComponentsInChildren<DefaultUIButton>(true).AsValueEnumerable().Any(b => b.name == "CampaignEditorEntry"))
            return;
        var button = UnityEngine.Object.Instantiate(screen._playerButton, screen._playerButton.transform.parent, false);
        button.name = "CampaignEditorEntry";
        button.OnClick.RemoveAllListeners();
        button.OnClick.AddListener(() => Authoring.EditorMode.Instance.Enter());
        button.SetRawText("CAMPAIGN EDITOR", 24);
        button.SetIcon(null);
        button.Interactable = !Plugin.InRaid;
        if (button.transform.parent.GetComponent<VerticalLayoutGroup>() == null)
        {
            var rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-45, -170);
            rect.sizeDelta = new Vector2(280, 46);
        }
    }
}
