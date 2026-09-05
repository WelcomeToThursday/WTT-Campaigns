using System.Reflection;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;
using UnityEngine.UI;

namespace SeasonalPerks.Client.Patches.UI;

internal sealed class MenuEntry(Type screenType)
    : ModulePatch("SeasonalPerks.MenuEntry." + screenType.Name)
{
    protected override MethodBase GetTargetMethod()
    {
        return screenType
            .GetMethods(
                BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly
            )
            .Single(method =>
                method.Name == "Show"
                && method.GetParameters().FirstOrDefault()?.ParameterType == typeof(EFT.Profile)
            );
    }

    [PatchPostfix]
    private static void Postfix(MenuScreen __instance)
    {
        SeasonUi.Instance.ShowStartupSelection();
        var existing = __instance
            .GetComponentsInChildren<DefaultUIButton>(true)
            .FirstOrDefault(button => button.name == "SeasonalPerksEntry");
        if (existing)
        {
            existing.gameObject.SetActive(!Plugin.InRaid);
            return;
        }
        var source = __instance._playerButton;
        var parent = source.transform.parent;
        var inMenuList = parent.GetComponent<VerticalLayoutGroup>() != null;
        var entry = UnityEngine.Object.Instantiate(
            source,
            inMenuList ? parent : __instance.transform,
            false
        );
        entry.name = "SeasonalPerksEntry";
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
}
