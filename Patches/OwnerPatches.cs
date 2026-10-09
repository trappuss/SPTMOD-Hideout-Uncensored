using System;
using System.Reflection;
using System.Threading.Tasks;
using EFT;
using EFT.InputSystem;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace HideoutUncensored.Patches
{
    /// <summary>A new hideout player owner exists (the hideout was loaded).</summary>
    internal class OwnerInitPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(HideoutPlayerOwner), nameof(HideoutPlayerOwner.Init));
        }

        [PatchPostfix]
        private static void Postfix(HideoutPlayerOwner __instance)
        {
            HideoutSession.Attach(__instance);
        }
    }

    /// <summary>
    /// HideoutPlayerOwner.TranslateCommand drops ToggleProne and DropBackpack before any translator sees them, and
    /// everything it does pass on reaches dummy translators unless the player is in the range. This runs first.
    /// </summary>
    internal class OwnerTranslateCommandPatch : ModulePatch
    {
        private static float _nextErrorLog;
        private static bool _infoFlip;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(HideoutPlayerOwner), nameof(HideoutPlayerOwner.TranslateCommand));
        }

        /// <summary>Undo the ToggleInfo flip below, whatever the original did.</summary>
        [PatchFinalizer]
        private static void Finalizer(HideoutPlayerOwner __instance)
        {
            if (_infoFlip)
            {
                _infoFlip = false;
                __instance.InShootingRange = true;
            }
        }

        [PatchPrefix]
        private static bool Prefix(HideoutPlayerOwner __instance, ECommand command, ref InputNode.ETranslateResult __result)
        {
            try
            {
                if (!Plugin.Enabled.Value || !__instance.CanTranslateCommand(command))
                {
                    return true;
                }

                return Handle(__instance, command, ref __result);
            }
            catch (Exception e)
            {
                if (Time.unscaledTime >= _nextErrorLog)
                {
                    _nextErrorLog = Time.unscaledTime + 10f;
                    Plugin.Log.LogError($"Command {command} failed: {e}");
                }

                return true;
            }
        }

        /// <returns>false when the command was dealt with here and the game must not see it.</returns>
        private static bool Handle(HideoutPlayerOwner owner, ECommand command, ref InputNode.ETranslateResult result)
        {
            HideoutPlayer player = owner.HideoutPlayer;
            if (player == null)
            {
                return true;
            }

            bool inRange = owner.InShootingRange;
            switch (command)
            {
                case ECommand.ToggleProne:
                    if (!Plugin.Prone.Value)
                    {
                        return true;
                    }

                    player.CurrentManagedState.Cancel();
                    player.ToggleProne();
                    result = InputNode.ETranslateResult.Ignore;
                    return false;

                case ECommand.DropBackpack:
                    if (!Plugin.DropBackpack.Value || !inRange || player.IsUpdateHideoutPlayerInventoryInProgress)
                    {
                        return true;
                    }

                    player.CurrentManagedState.Cancel();
                    player.DropBackpack();
                    result = InputNode.ETranslateResult.Ignore;
                    return false;

                case ECommand.SelectFirstPrimaryWeapon:
                case ECommand.SelectSecondPrimaryWeapon:
                case ECommand.SelectSecondaryWeapon:
                case ECommand.QuickSelectSecondaryWeapon:
                case ECommand.SelectKnife:
                    if (Plugin.Armed && !inRange)
                    {
                        HideoutSession.OnWeaponKey(owner, command);
                    }

                    return true;

                case ECommand.ToggleShooting:
                case ECommand.TryHighThrow:
                case ECommand.TryLowThrow:
                case ECommand.PressThrowGrenade:
                case ECommand.QuickKnifeKick:
                    if (Plugin.Armed && inRange && Plugin.SafeTrigger.Value && Cursor.lockState != CursorLockMode.Locked)
                    {
                        result = InputNode.ETranslateResult.Ignore;
                        return false;
                    }

                    return true;

                case ECommand.NextMagazine:
                case ECommand.PreviousMagazine:
                case ECommand.ReloadWeapon:
                    if (inRange && Plugin.MagSelectorFallback.Value && MagSelector.Handle(owner, command))
                    {
                        result = InputNode.ETranslateResult.Block;
                        return false;
                    }

                    return true;

                case ECommand.ToggleInfo:
                    // the game only raises its info-icons event when not in the range; let this one command think so
                    if (Plugin.Armed && inRange && Plugin.InfoIcons.Value && !player.IsUpdateHideoutPlayerInventoryInProgress)
                    {
                        owner.InShootingRange = false;
                        _infoFlip = true;
                    }

                    return true;

                default:
                    return true;
            }
        }
    }

    /// <summary>The inventory key is ignored in the range. Holster first, then open it (HideoutSession does both).</summary>
    internal class OwnerInventoryInputPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(HideoutPlayerOwner), nameof(HideoutPlayerOwner.TranslateInventoryScreenInput));
        }

        [PatchPrefix]
        private static bool Prefix(HideoutPlayerOwner __instance, ECommand command, ref bool __result)
        {
            if (command != ECommand.ToggleInventory || !Plugin.Armed || !Plugin.InventoryWhileArmed.Value || !__instance.InShootingRange)
            {
                return true;
            }

            HideoutSession.RequestInventory();
            __result = true;
            return false;
        }
    }

    /// <summary>Escape in the range leaves the range. Armed anywhere, that means "holster", so stop wanting weapons.</summary>
    internal class OwnerExitInputPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(HideoutPlayerOwner), nameof(HideoutPlayerOwner.TranslateExitScreenInput));
        }

        [PatchPrefix]
        private static void Prefix(HideoutPlayerOwner __instance, ECommand command)
        {
            if (command == ECommand.Escape && Plugin.Armed && __instance.InShootingRange && __instance.Player != null
                && __instance.Player.PointOfView == EPointOfView.FirstPerson)
            {
                HideoutSession.OnEscape();
            }
        }
    }

    /// <summary>Entering through the vanilla prompt counts as drawing.</summary>
    internal class OwnerEnterRangePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(HideoutPlayerOwner), nameof(HideoutPlayerOwner.EnterShootingRange));
        }

        [PatchPostfix]
        private static void Postfix(HideoutPlayerOwner __instance)
        {
            if (Plugin.Armed && __instance.InShootingRange)
            {
                HideoutSession.OnRangeEntered();
            }
        }
    }

    /// <summary>
    /// Walking out of the range trigger calls ExitShootingRange. While the user wants weapons out that must not
    /// holster them. Escape clears the wish first, so it still gets through.
    /// </summary>
    internal class OwnerExitRangePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(HideoutPlayerOwner), nameof(HideoutPlayerOwner.ExitShootingRange));
        }

        [PatchPrefix]
        private static bool Prefix(HideoutPlayerOwner __instance, ref Task __result)
        {
            if (!Plugin.Armed || !HideoutSession.Want || !__instance.InShootingRange)
            {
                return true;
            }

            __result = Task.CompletedTask;
            return false;
        }
    }

    /// <summary>
    /// DecidePatrolStatus lowers the weapon and blocks the trigger when the player looks more than 50 degrees off the
    /// range axis. Armed anywhere, the weapon stays up.
    /// </summary>
    internal class OwnerPatrolPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(HideoutPlayerOwner), nameof(HideoutPlayerOwner.DecidePatrolStatus));
        }

        [PatchPrefix]
        private static bool Prefix(HideoutPlayerOwner __instance)
        {
            if (!Plugin.Armed || !Plugin.NoAimRestriction.Value || !__instance.InShootingRange)
            {
                return true;
            }

            HideoutPlayer player = __instance.HideoutPlayer;
            if (player != null && (player.IsInPatrol || player.MovementContext.BlockFirearms))
            {
                player.SetPatrol(false);
            }

            return false;
        }
    }

    /// <summary>FlashlightAvailable is hard false in the range.</summary>
    internal class OwnerFlashlightPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(HideoutPlayerOwner), nameof(HideoutPlayerOwner.FlashlightAvailable));
        }

        [PatchPostfix]
        private static void Postfix(HideoutPlayerOwner __instance, ref bool __result)
        {
            if (Plugin.Armed && Plugin.FlashlightWhileArmed.Value && __instance.InShootingRange)
            {
                __result = __instance.FlashLightState;
            }
        }
    }
}
