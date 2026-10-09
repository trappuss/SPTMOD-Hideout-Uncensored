using System.Collections.Generic;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.InputSystem;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace HideoutUncensored.Patches
{
    /// <summary>
    /// A weapon going back to the pool switches off its rain condensators (Firearms.OnReturnToPool). If one of them
    /// was destroyed in the meantime (seen with a magazine swapped out from under a modded weapon), the game throws
    /// in the middle of holstering: the hands controller is left half destroyed and the hideout's draw/holster flag
    /// stays set for good. In the hideout, destroyed entries are skipped instead.
    /// </summary>
    internal class RainCondensatorPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(RainCondensatorHelper), nameof(RainCondensatorHelper.SetEnabled));
        }

        [PatchPrefix]
        private static bool Prefix(IEnumerable<RainCondensator> rainCondensators, bool enabled)
        {
            if (ReferenceEquals(HideoutSession.Owner, null) || !Plugin.Enabled.Value || rainCondensators == null)
            {
                return true;
            }

            foreach (RainCondensator condensator in rainCondensators)
            {
                if (condensator != null)
                {
                    condensator.enabled = enabled;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// A second Escape during the holster animation leaves first person, and the game then fast-forwards the hands
    /// operation halfway through. Swallow Escape until the draw or holster has finished.
    /// </summary>
    internal class EscapeDuringTransitionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(HideoutPlayerOwner), nameof(HideoutPlayerOwner.TranslateExitScreenInput));
        }

        [PatchPrefix]
        private static bool Prefix(HideoutPlayerOwner __instance, ECommand command, ref bool __result)
        {
            if (command != ECommand.Escape || !Plugin.Enabled.Value || HideoutSession.Recovering)
            {
                return true;
            }

            HideoutPlayer player = __instance.HideoutPlayer;
            if (player == null || !player.IsUpdateHideoutPlayerInventoryInProgress || player.PointOfView != EPointOfView.FirstPerson)
            {
                return true;
            }

            __result = true;
            return false;
        }
    }

    /// <summary>
    /// FirearmController.Idling.SetTriggerPressed returns at once when the location is "hideout" and the weapon is a
    /// rocket launcher. For that one call the location reads as something else.
    /// </summary>
    internal class RocketTriggerPatch : ModulePatch
    {
        private const string Hideout = "hideout";

        private static bool _flipped;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.DeclaredMethod(typeof(Player.FirearmController.Idling), nameof(Player.FirearmController.Idling.SetTriggerPressed));
        }

        [PatchPrefix]
        private static void Prefix(Player.FirearmController.Idling __instance, bool pressed)
        {
            _flipped = false;
            if (!pressed || ReferenceEquals(HideoutSession.Owner, null) || !Plugin.Enabled.Value || !Plugin.RocketLaunchers.Value
                || !(__instance.Weapon is EFT.InventoryLogic.RocketLauncher) || !Singleton<GameWorld>.Instantiated)
            {
                return;
            }

            GameWorld world = Singleton<GameWorld>.Instance;
            if (world.LocationId == Hideout)
            {
                world.LocationId = string.Empty;
                _flipped = true;
            }
        }

        [PatchFinalizer]
        private static void Finalizer()
        {
            if (_flipped)
            {
                _flipped = false;
                if (Singleton<GameWorld>.Instantiated)
                {
                    Singleton<GameWorld>.Instance.LocationId = Hideout;
                }
            }
        }
    }

    /// <summary>
    /// The game never fires a rocket in the hideout, so nothing guarantees the shot can be built there. If it throws,
    /// finish the fire operation instead of leaving the launcher stuck mid-shot.
    /// </summary>
    internal class RocketFireGuardPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.DeclaredMethod(typeof(Player.FirearmController.RocketLauncherFire),
                nameof(Player.FirearmController.RocketLauncherFire.OnFireEvent));
        }

        /// <summary>
        /// The copied gear is made with Item.CloneItem, and Ammo.Clone always builds a plain Ammo, even from a Rocket.
        /// RocketProjectile.Initialize casts the round to Rocket, so the copy in the tube has to be a real one.
        /// </summary>
        [PatchPrefix]
        private static void Prefix(Player.FirearmController.RocketLauncherFire __instance)
        {
            if (ReferenceEquals(HideoutSession.Owner, null) || !Plugin.Enabled.Value)
            {
                return;
            }

            try
            {
                Slot chamber = __instance.Weapon != null ? __instance.Weapon.FirstLoadedChamberSlot : null;
                if (chamber == null || !(chamber.ContainedItem is Ammo round) || round is Rocket)
                {
                    return;
                }

                if (!(Singleton<ItemFactory>.Instance.CreateItem(MongoID.Generate(), round.TemplateId, null) is Rocket rocket))
                {
                    return;
                }

                chamber.RemoveItem();
                chamber.AddWithoutRestrictions(rocket);
                Plugin.Debug("Rocket in the tube replaced by a real Rocket item");
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"Rocket could not be prepared: {e}");
            }
        }

        [PatchFinalizer]
        private static System.Exception Finalizer(Player.FirearmController.RocketLauncherFire __instance, System.Exception __exception)
        {
            if (__exception == null || ReferenceEquals(HideoutSession.Owner, null))
            {
                return __exception;
            }

            Plugin.Log.LogError($"Rocket shot failed in the hideout: {__exception}");
            try
            {
                __instance.OnFireEndEvent();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"Rocket fire operation could not be finished: {e.Message}");
            }

            return null;
        }
    }

    /// <summary>
    /// The hold-reload selector marks the selected magazine by cell colour only, which is hard to see on the hideout
    /// HUD. Make the selected cell larger as well.
    /// </summary>
    internal class SelectorMarkPatch : ModulePatch
    {
        private const float SelectedScale = 1.2f;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(AmmoSelector), nameof(AmmoSelector.SetSelectedState));
        }

        [PatchPostfix]
        private static void Postfix(int index, bool isSelected, List<GridItemView> ____magazinesViews)
        {
            if (ReferenceEquals(HideoutSession.Owner, null) || !Plugin.Enabled.Value || ____magazinesViews == null
                || index < 0 || index >= ____magazinesViews.Count)
            {
                return;
            }

            GridItemView view = ____magazinesViews[index];
            if (view != null)
            {
                view.transform.localScale = isSelected ? Vector3.one * SelectedScale : Vector3.one;
            }
        }
    }
}
