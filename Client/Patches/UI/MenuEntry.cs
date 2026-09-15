using System.Reflection;
using EFT.UI;
using SPT.Reflection.Patching;
using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.Client.Hub;
using WTT.Campaigns.Client.Missions;
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
        Authoring.CampaignTestMode.AttachMenu(__instance);
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
        }
        else
        {
            var source = __instance._playerButton;
            var parent = source.transform.parent;
            var inMenuList = parent.GetComponent<VerticalLayoutGroup>() != null;
            existing = UnityEngine.Object.Instantiate(source, inMenuList ? parent : __instance.transform, false);
            existing.name = "CampaignsEntry";
            existing.OnClick.RemoveAllListeners();
            existing.OnClick.AddListener(() => SeasonUi.Instance.Open());
            existing.SetRawText("CHARACTERS", inMenuList ? (int)source._headerLabel.fontSize : 24);
            existing.SetIcon(null);
            existing.Interactable = true;
            if (inMenuList)
            {
                existing.transform.SetSiblingIndex(source.transform.GetSiblingIndex() + 1);
            }
            else
            {
                var rect = (RectTransform)existing.transform;
                rect.anchorMin = rect.anchorMax = Vector2.one;
                rect.pivot = Vector2.one;
                rect.anchoredPosition = new Vector2(-45, -110);
                rect.sizeDelta = new Vector2(280, 46);
            }
        }
        existing!.gameObject.SetActive(!Plugin.InRaid && !Authoring.CampaignTestMode.Restricted);
        AttachMissions(__instance, existing);
    }

    private static void AttachMissions(MenuScreen screen, DefaultUIButton campaigns)
    {
        var existing = screen
            .GetComponentsInChildren<DefaultUIButton>(true)
            .AsValueEnumerable()
            .FirstOrDefault(button => button.name == "MissionsEntry");
        if (existing != null)
        {
            existing.gameObject.SetActive(MissionUi.Available);
            return;
        }

        var source = screen._playerButton;
        var parent = source.transform.parent;
        var inMenuList = parent.GetComponent<VerticalLayoutGroup>() != null;
        existing = UnityEngine.Object.Instantiate(source, inMenuList ? parent : screen.transform, false);
        existing.name = "MissionsEntry";
        existing.OnClick.RemoveAllListeners();
        existing.OnClick.AddListener(() => MissionUi.Instance.Open());
        existing.SetRawText("MISSIONS", inMenuList ? (int)source._headerLabel.fontSize : 24);
        existing.SetIcon(null);
        existing.Interactable = true;
        if (inMenuList)
        {
            existing.transform.SetSiblingIndex(campaigns.transform.GetSiblingIndex() + 1);
        }
        else
        {
            var rect = (RectTransform)existing.transform;
            rect.anchorMin = rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-45, -165);
            rect.sizeDelta = new Vector2(280, 46);
        }
        existing.gameObject.SetActive(MissionUi.Available);
    }

    private static void AttachEditor(MenuScreen screen)
    {
        var existing = screen
            .GetComponentsInChildren<DefaultUIButton>(true)
            .AsValueEnumerable()
            .FirstOrDefault(b => b.name == "CampaignEditorEntry");
        if (existing)
        {
            existing!.gameObject.SetActive(!Authoring.CampaignTestMode.Restricted);
            return;
        }
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
            rect.anchoredPosition = new Vector2(-45, -220);
            rect.sizeDelta = new Vector2(280, 46);
        }
        button.gameObject.SetActive(!Authoring.CampaignTestMode.Restricted);
    }
}
