using Comfort.Common;
using EFT;
using EFT.Hideout;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using SeasonalPerks.Shared.Story;
using UnityEngine;

namespace SeasonalPerks.Client.Story;

internal static class StoryLinks
{
    internal static async void Open(StoryNoteLink link)
    {
        if (!StoryClient.Available)
        {
            return;
        }
        try
        {
            if (link.Kind == "Item")
            {
                var item = Singleton<ItemFactory>.Instance.CreateItem(MongoID.Generate().ToString(), link.Target, null);
                ItemUiContext.Instance.Inspect(new DefaultItemContext(item, EItemViewType.Handbook), null);
            }
            else if (Plugin.InRaid)
            {
                throw new InvalidOperationException("Return from the raid to open trading or crafting.");
            }
            else if (link.Kind == "Offer")
            {
                await Offer(link);
            }
            else if (link.Kind == "Craft")
            {
                await Craft(link);
            }
            else
            {
                throw new InvalidOperationException("This journal link has no registered navigation adapter: " + link.Kind);
            }
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
            ItemUiContext.Instance.Tooltip.Show(exception.Message);
        }
    }

    private static async Task Offer(StoryNoteLink link)
    {
        var trader =
            Plugin.App!.Session.Traders.SingleOrDefault(t => t.Id == link.TraderId)
            ?? throw new InvalidOperationException("This trader is unavailable.");
        var taskbar = PreloaderUI.Instance.MenuTaskBar;
        var open =
            AccessTools.Field(typeof(MenuTaskBar), "OnTaskBarButtonPressed").GetValue(taskbar) as Action<EMenuType, bool>
            ?? throw new InvalidOperationException("Trading navigation is not ready.");
        var character = Plugin.Current!.EffectiveProfileId;
        open(EMenuType.Trade, true);
        var trading = await WaitFor(() => UnityEngine.Object.FindObjectOfType<TradingScreen>(), character);
        trading._merchantsList.ShowTrader(trader);
        var screen = await WaitFor(
            () => UnityEngine.Object.FindObjectsOfType<TraderScreensGroup>().FirstOrDefault(s => s.Trader?.Id == trader.Id),
            character
        );
        screen.SetMode(TraderScreensGroup.ETraderMode.Trade);
        await trader.RefreshAssortment(true, false);
        if (!StoryClient.Available || character != Plugin.Current!.EffectiveProfileId || !screen || !screen.isActiveAndEnabled)
        {
            return;
        }
        var item =
            trader
                .CurrentAssortment.GetTraderCollections.GetAllItemsFromCollections()
                .FirstOrDefault(i => i.Id == link.Target || i.TemplateId == link.Target)
            ?? throw new InvalidOperationException("This offer is no longer available from the trader.");
        trader.CurrentAssortment.SelectItem(item);
    }

    private static async Task Craft(StoryNoteLink link)
    {
        var character = Plugin.Current!.EffectiveProfileId;
        var recipes = await Plugin.App!.Session.GetProductionRecipes();
        var recipe =
            recipes.ProductionSchemes.SingleOrDefault(r => r._id == link.Target)
            ?? throw new InvalidOperationException("This crafting recipe is unavailable.");
        if (!StoryClient.Available || character != Plugin.Current!.EffectiveProfileId)
        {
            return;
        }
        var taskbar = PreloaderUI.Instance.MenuTaskBar;
        var open =
            AccessTools.Field(typeof(MenuTaskBar), "_onHideoutCraftsButtonClick").GetValue(taskbar) as Action<EAreaType>
            ?? throw new InvalidOperationException("Hideout navigation is not ready.");
        open((EAreaType)recipe.areaType);
        var panel = await WaitFor(
            () =>
                UnityEngine
                    .Object.FindObjectsOfType<ProductionPanel>()
                    .FirstOrDefault(p => p.GetSortedSchemes(false).Any(r => r._id == recipe._id)),
            character,
            60
        );
        panel.Search(recipe.endProduct.LocalizedName());
    }

    private static async Task<T> WaitFor<T>(Func<T?> find, string character, float seconds = 10)
        where T : Behaviour
    {
        var deadline = Time.realtimeSinceStartup + seconds;
        while (
            Time.realtimeSinceStartup < deadline
            && StoryClient.Available
            && character == Plugin.Current!.EffectiveProfileId
            && !Plugin.InRaid
        )
        {
            var target = find();
            if (target && target!.isActiveAndEnabled)
            {
                return target;
            }
            await Task.Delay(50);
        }
        throw new InvalidOperationException("The requested screen could not be opened for this character.");
    }
}
