using System;
using System.Collections;
using System.Runtime.CompilerServices;
using EFT.Achievements;
using EFT.HealthSystem;
using EFT.InputSystem;
using EFT.InventoryLogic;
using EFT.Prestige;
using EFT.Quests;
using EFT.UI;
using EFT.UI.Insurance;
using EFT.UI.Screens;
using JetBrains.Annotations;
using JsonType;
using UnityEngine;

namespace EFT;

public class EftGamePlayerOwner : CommonPlayerOwner<IEftSession>
{
    [CompilerGenerated]
    public class CG_ShowInventoryScreenLoot
    {
        public EftGamePlayerOwner eftGamePlayerOwner_0;

        public Action callback;

        public void method_0()
        {
            eftGamePlayerOwner_0.Player.SetInventoryOpened(opened: false);
            callback();
        }
    }

    [CompilerGenerated]
    public class CG_ShowInventoryScreen
    {
        public InventoryScreen.RaidInventoryScreenController inventoryScreenController;

        public IHealthController healthController;

        public EftGamePlayerOwner eftGamePlayerOwner_0;

        public Action exitAction;

        public void method_0(EDamageType obj)
        {
            inventoryScreenController?.CloseScreen();
        }

        public void method_1()
        {
            healthController.DiedEvent -= delegate
            {
                inventoryScreenController?.CloseScreen();
            };
            eftGamePlayerOwner_0.Player.UpdateInteractionCast();
            exitAction();
        }
    }

    public static TPlayerOwner Create<TPlayerOwner>(
        Player player,
        IInputTree inputTree,
        InsuranceCompany insurance,
        IEftSession session,
        GameUI gameUI,
        GameDateTime gameDateTime,
        [CanBeNull] LocationSettings.Location location
    )
        where TPlayerOwner : EftGamePlayerOwner
    {
        TPlayerOwner val = GamePlayerOwner.smethod_2<TPlayerOwner>(player, inputTree, insurance, session, gameUI, gameDateTime, location);
        val.Session = session;
        return val;
    }

    public static EftGamePlayerOwner Create(
        Player player,
        IInputTree inputTree,
        InsuranceCompany insurance,
        IEftSession session,
        GameUI gameUI,
        GameDateTime gameDateTime,
        [CanBeNull] LocationSettings.Location location
    )
    {
        EftGamePlayerOwner eftGamePlayerOwner = GamePlayerOwner.smethod_2<EftGamePlayerOwner>(
            player,
            inputTree,
            insurance,
            session,
            gameUI,
            gameDateTime,
            location
        );
        eftGamePlayerOwner.Session = session;
        return eftGamePlayerOwner;
    }

    public override void Init()
    {
        base.Player.HealthController.DiedEvent += delegate
        {
            ReleaseTactical();
        };
        base.Player.HandsChangingEvent += base.ReleaseTactical;
        EftScreenManager.Instance.OnScreenChanged += delegate(EEftScreenType screenType)
        {
            if (screenType != EEftScreenType.BattleUI)
            {
                ReleaseTactical();
            }
        };
        OnDestroyCompositeDisposable.AddDisposable(
            delegate
            {
                base.Player.HealthController.DiedEvent -= delegate
                {
                    ReleaseTactical();
                };
            }
        );
        OnDestroyCompositeDisposable.AddDisposable(
            delegate
            {
                EftScreenManager.Instance.OnScreenChanged -= delegate(EEftScreenType screenType)
                {
                    if (screenType != EEftScreenType.BattleUI)
                    {
                        ReleaseTactical();
                    }
                };
            }
        );
        OnDestroyCompositeDisposable.AddDisposable(
            delegate
            {
                base.Player.HandsChangingEvent -= base.ReleaseTactical;
            }
        );
    }

    public override ETranslateResult TranslateCommand(ECommand command)
    {
        if (base.TranslateCommand(command) == ETranslateResult.BlockAll)
        {
            return ETranslateResult.BlockAll;
        }
        if (TranslateInventoryScreenInput(command))
        {
            return ETranslateResult.BlockAll;
        }
        if (TranslateExitScreenInput(command))
        {
            return ETranslateResult.BlockAll;
        }
        return ETranslateResult.Ignore;
    }

    public override bool TranslateInventoryScreenInput(ECommand command)
    {
        if (!EftScreenManager.Instance.CheckCurrentScreen(EEftScreenType.BattleUI))
        {
            return false;
        }
        if (!base.TranslateInventoryScreenInput(command))
        {
            return false;
        }
        ShowInventoryScreen(
            delegate
            {
                base.Player.SetInventoryOpened(opened: false);
            },
            base.Player.HealthController,
            base.Player.InventoryController,
            base.Player.QuestController,
            base.Player.AchievementsController,
            base.Player.PrestigeController,
            null,
            EInventoryTab.Unchanged
        );
        return true;
    }

    public override bool TranslateExitScreenInput(ECommand command)
    {
        if (!base.TranslateExitScreenInput(command))
        {
            return false;
        }
        MenuScreen.RaidMainMenuScreenController raidMainMenuScreenController = new MenuScreen.RaidMainMenuScreenController();
        raidMainMenuScreenController.ShowScreen(EScreenState.Queued);
        raidMainMenuScreenController.OnLeave += base.OnLeaveHandler;
        return true;
    }

    public override void CloseInventoryIfOpen()
    {
        EftScreenManager.Instance.ToggleScreen(EEftScreenType.Inventory);
    }

    public override IEnumerator CloseBattleUi()
    {
        yield return null;
        yield return null;
        EftScreenManager.Instance.ToggleScreen(EEftScreenType.BattleUI);
    }

    public override void ShowInventoryScreenLoot(CompoundItem loot, Action callback, bool isFakeContainer = false)
    {
        base.Player.SetInventoryOpened(opened: true);
        ShowInventoryScreen(
            delegate
            {
                base.Player.SetInventoryOpened(opened: false);
                callback();
            },
            base.Player.HealthController,
            base.Player.InventoryController,
            base.Player.QuestController,
            base.Player.AchievementsController,
            base.Player.PrestigeController,
            loot,
            EInventoryTab.Gear,
            isFakeContainer
        );
    }

    public virtual void ShowInventoryScreen(
        Action exitAction,
        IHealthController healthController,
        InventoryController controller,
        QuestController questController,
        AchievementsController achievementsController,
        PrestigeController prestigeController,
        [CanBeNull] CompoundItem lootItem,
        EInventoryTab tab,
        bool showAsGridContent = false
    )
    {
        if (!EftScreenManager.Instance.CheckCurrentScreen(EEftScreenType.BattleUI))
        {
            Debug.Log("<colo=red>Settings or menu screen is active. I can't search.</color>");
            exitAction();
            return;
        }
        EItemViewType viewType = (
            (base.Player.BtrState == EPlayerBtrState.Inside) ? EItemViewType.InventoryWithoutDiscard : EItemViewType.Inventory
        );
        InventoryScreen.RaidInventoryScreenController inventoryScreenController = new InventoryScreen.RaidInventoryScreenController(
            Session,
            base.Player.Profile,
            healthController,
            controller,
            questController,
            achievementsController,
            prestigeController,
            lootItem,
            tab,
            showAsGridContent,
            viewType
        );
        inventoryScreenController.OnClose += delegate
        {
            healthController.DiedEvent -= delegate
            {
                inventoryScreenController?.CloseScreen();
            };
            base.Player.UpdateInteractionCast();
            exitAction();
        };
        healthController.DiedEvent += delegate
        {
            inventoryScreenController?.CloseScreen();
        };
        inventoryScreenController.ShowScreen(EScreenState.Queued);
    }

    public override void ShowBattleUIScreen()
    {
        BattleUIScreenController.OnClose += delegate
        {
            _timerPanel.Hide();
            MonoBehaviourSingleton<PreloaderUI>.Instance.RaidInfoVisibility = false;
        };
        BattleUIScreenController.OnShow += delegate
        {
            _timerPanel.Reveal();
            MonoBehaviourSingleton<PreloaderUI>.Instance.RaidInfoVisibility = true;
        };
        MonoBehaviourSingleton<PreloaderUI>.Instance.ResetTimersForShowRttAndLoss();
        BattleUIScreenController.ShowScreen(EScreenState.Root);
    }

    public override void InitBattleUIScreen()
    {
        BattleUIScreenController = new EftBattleUIScreen.RaidBattleUIScreenController(this, _gameDateTime, _location, _insurance);
    }

    public override void MumbleStatusChangedHandler(bool active)
    {
        base.MumbleStatusChangedHandler(active);
        if (active)
        {
            ReleaseTactical();
        }
    }

    [CompilerGenerated]
    public void CG_Init()
    {
        base.Player.HealthController.DiedEvent -= delegate
        {
            ReleaseTactical();
        };
    }

    [CompilerGenerated]
    public void CG_Init1()
    {
        EftScreenManager.Instance.OnScreenChanged -= delegate(EEftScreenType screenType)
        {
            if (screenType != EEftScreenType.BattleUI)
            {
                ReleaseTactical();
            }
        };
    }

    [CompilerGenerated]
    public void CG_Init2()
    {
        base.Player.HandsChangingEvent -= base.ReleaseTactical;
    }

    [CompilerGenerated]
    public void CG_Init3(EEftScreenType screenType)
    {
        if (screenType != EEftScreenType.BattleUI)
        {
            ReleaseTactical();
        }
    }

    [CompilerGenerated]
    public void CG_Init4(EDamageType damage)
    {
        ReleaseTactical();
    }

    [CompilerGenerated]
    public void CG_TranslateInventoryScreenInput()
    {
        base.Player.SetInventoryOpened(opened: false);
    }

    [CompilerGenerated]
    public void CG_ShowBattleUIScreen()
    {
        _timerPanel.Hide();
        MonoBehaviourSingleton<PreloaderUI>.Instance.RaidInfoVisibility = false;
    }

    [CompilerGenerated]
    public void CG_ShowBattleUIScreen1()
    {
        _timerPanel.Reveal();
        MonoBehaviourSingleton<PreloaderUI>.Instance.RaidInfoVisibility = true;
    }
}
