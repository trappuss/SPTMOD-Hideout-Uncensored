using EFT;
using EFT.InputSystem;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;

namespace HideoutUncensored.Patches
{
    /// <summary>
    /// The hold-reload magazine/ammo selector belongs to the HUD quick access panel
    /// (InventoryScreenQuickAccessPanel.TryShowAmmoSelector, then AmmoSelector takes Next/Previous/Reload).
    /// Both are input nodes above the player owner, so a command only arrives here when neither of them took it.
    /// In that case drive them directly.
    /// </summary>
    internal static class MagSelector
    {
        private static AccessTools.FieldRef<GamePlayerOwner, IBattleUIScreenController> _screenController;
        private static bool _lookupFailed;
        private static float _nextWhyLog;

        /// <returns>true when the selector consumed <paramref name="command"/>.</returns>
        internal static bool Handle(HideoutPlayerOwner owner, ECommand command)
        {
            InventoryScreenQuickAccessPanel panel = QuickPanel(owner);
            if (panel == null)
            {
                return false;
            }

            AmmoSelector selector = panel.ammoSelector;
            if (selector != null && selector.IsShown)
            {
                return selector.TranslateCommand(command) != InputNode.ETranslateResult.Ignore;
            }

            if (command == ECommand.ReloadWeapon)
            {
                return false;
            }

            bool shown = panel.TryShowAmmoSelector();
            if (!shown && Plugin.DebugLog.Value && UnityEngine.Time.unscaledTime >= _nextWhyLog)
            {
                _nextWhyLog = UnityEngine.Time.unscaledTime + 2f;
                LogWhy(owner, panel);
            }

            return shown;
        }

        private static InventoryScreenQuickAccessPanel QuickPanel(HideoutPlayerOwner owner)
        {
            if (_lookupFailed)
            {
                return null;
            }

            if (_screenController == null)
            {
                try
                {
                    _screenController = AccessTools.FieldRefAccess<GamePlayerOwner, IBattleUIScreenController>("BattleUIScreenController");
                }
                catch (System.Exception e)
                {
                    _lookupFailed = true;
                    Plugin.Log.LogError($"Magazine selector fallback is off, GamePlayerOwner.BattleUIScreenController was not found: {e.Message}");
                    return null;
                }
            }

            var screen = (_screenController(owner) as EftBattleUIScreen.EftBattleUIScreenController)?.Screen;
            return screen != null ? screen.QuickAccessPanel : null;
        }

        private static void LogWhy(HideoutPlayerOwner owner, InventoryScreenQuickAccessPanel panel)
        {
            bool canSwitch = owner.CanSwitchMagazine();
            Weapon weapon = owner.Player.HandsController?.Item as Weapon;
            bool slotFound = weapon != null && panel.TryFindSlotWithItem(weapon, out _);
            bool ammoFound = weapon != null && AmmoSelector.TryFindAmmoForWeapon(owner.Player.InventoryController, weapon, out _);
            Plugin.Log.LogInfo($"Magazine selector not shown: canSwitchMagazine={canSwitch} weapon={weapon?.ShortName} weaponSlotFound={slotFound} ammoFound={ammoFound}");
        }
    }
}
