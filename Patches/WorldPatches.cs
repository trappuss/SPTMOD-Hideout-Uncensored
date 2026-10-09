using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using EFT;
using EFT.Hideout;
using EFT.InputSystem;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;
using QteHandleData = UI.Hideout.QteHandleData;

namespace HideoutUncensored.Patches
{
    /// <summary>
    /// The area prompt list is built from owner.AvailableForInteractions and owner.InShootingRange, both false-ish
    /// while armed. Build it as if unarmed. The actions that cannot run armed holster first (HideoutSession).
    /// </summary>
    internal class AreaActionsPatch : ModulePatch
    {
        private const string EnterRangeAction = "Shoot with the range";

        private static bool _flipped;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(InteractionContextHelper), nameof(InteractionContextHelper.GetAvailableActions),
                new[] { typeof(HideoutPlayerOwner), typeof(HideoutArea) });
        }

        [PatchPrefix]
        private static void Prefix(HideoutPlayerOwner owner)
        {
            _flipped = false;
            if (!Plugin.Armed || !Plugin.InteractWhileArmed.Value || owner == null || !owner.InShootingRange)
            {
                return;
            }

            HideoutPlayer player = owner.HideoutPlayer;
            if (player == null || player.IsUpdateHideoutPlayerInventoryInProgress)
            {
                return;
            }

            owner.InShootingRange = false;
            _flipped = true;
        }

        [PatchFinalizer]
        private static void Finalizer(HideoutPlayerOwner owner, AvailableInteractionState __result)
        {
            if (!_flipped)
            {
                return;
            }

            _flipped = false;
            owner.InShootingRange = true;
            // already armed: the range prompt would do nothing
            __result?.Actions?.RemoveAll(action => action.Name == EnterRangeAction);
        }
    }

    /// <summary>The workout swaps the body animator and camera; it has to start unarmed.</summary>
    internal class WorkoutStartPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(WorkoutBehaviour), nameof(WorkoutBehaviour.StartQte));
        }

        [PatchPrefix]
        private static bool Prefix(WorkoutBehaviour __instance, HideoutPlayerOwner owner, QteHandleData qteData)
        {
            if (!Plugin.Armed || owner == null || !owner.InShootingRange)
            {
                return true;
            }

            HideoutSession.AfterHolster(() => __instance.StartQte(owner, qteData));
            return false;
        }
    }

    /// <summary>HideoutBattleUIScreenController.AllowHealthPanel is a constant false.</summary>
    internal class HealthPanelPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.PropertyGetter(typeof(EftBattleUIScreen.HideoutBattleUIScreenController),
                nameof(EftBattleUIScreen.HideoutBattleUIScreenController.AllowHealthPanel));
        }

        [PatchPostfix]
        private static void Postfix(ref bool __result)
        {
            if (Plugin.Enabled.Value && Plugin.HealthPanel.Value)
            {
                __result = true;
            }
        }
    }

    /// <summary>A grenade in hand gets HideoutGrenadeInputTranslator, which only knows "inspect". Use the real one.</summary>
    internal class GrenadeHandsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(HideoutGrenadeInputTranslator), nameof(HideoutGrenadeInputTranslator.TranslateCommand));
        }

        [PatchPrefix]
        private static bool Prefix(HideoutGrenadeInputTranslator __instance, ECommand command, ref InputNode.ETranslateResult __result)
        {
            if (!Plugin.Enabled.Value || !Plugin.Grenades.Value || __instance._controller == null)
            {
                return true;
            }

            __result = new GrenadeInputTranslator(__instance._controller).TranslateCommand(command);
            return false;
        }
    }

    /// <summary>
    /// HideoutPlayerInputTranslator overrides the grenade key handler (vmethod_1) with an empty body. Run the base one.
    /// </summary>
    internal class GrenadeKeyPatch : ModulePatch
    {
        private static bool _baseReady;

        protected override MethodBase GetTargetMethod()
        {
            try
            {
                Harmony.ReversePatch(
                    AccessTools.DeclaredMethod(typeof(PlayerInputTranslator), nameof(PlayerInputTranslator.vmethod_1)),
                    new HarmonyMethod(typeof(GrenadeKeyPatch), nameof(BaseGrenadeKey)));
                _baseReady = true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Grenade key stays disabled, PlayerInputTranslator.vmethod_1 could not be copied: {e.Message}");
            }

            return AccessTools.DeclaredMethod(typeof(HideoutPlayerInputTranslator), nameof(HideoutPlayerInputTranslator.vmethod_1));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void BaseGrenadeKey(PlayerInputTranslator instance)
        {
            throw new NotImplementedException("reverse patch stub");
        }

        [PatchPostfix]
        private static void Postfix(HideoutPlayerInputTranslator __instance)
        {
            if (!_baseReady || !Plugin.Enabled.Value || !Plugin.Grenades.Value)
            {
                return;
            }

            try
            {
                QuickSlots.DropStaleGrenade(__instance._player as HideoutPlayer);
                BaseGrenadeKey(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Grenade key failed: {e}");
            }
        }
    }

    /// <summary>HideoutPlayer.InitVaultingComponent is an empty override, so the hideout player cannot vault or climb.</summary>
    internal class VaultingPatch : ModulePatch
    {
        private static bool _baseReady;

        protected override MethodBase GetTargetMethod()
        {
            try
            {
                Harmony.ReversePatch(
                    AccessTools.DeclaredMethod(typeof(Player), nameof(Player.InitVaultingComponent)),
                    new HarmonyMethod(typeof(VaultingPatch), nameof(BaseInitVaulting)));
                _baseReady = true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Vaulting stays disabled, Player.InitVaultingComponent could not be copied: {e.Message}");
            }

            return AccessTools.DeclaredMethod(typeof(HideoutPlayer), nameof(HideoutPlayer.InitVaultingComponent));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void BaseInitVaulting(Player instance, bool aiControlled)
        {
            throw new NotImplementedException("reverse patch stub");
        }

        [PatchPostfix]
        private static void Postfix(HideoutPlayer __instance, bool aiControlled)
        {
            if (!_baseReady || !Plugin.Enabled.Value || !Plugin.Vaulting.Value)
            {
                return;
            }

            try
            {
                BaseInitVaulting(__instance, aiControlled);
                Plugin.Log.LogInfo("Vaulting component created for the hideout player");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Vaulting could not be created: {e}");
            }
        }
    }

    /// <summary>The hideout player carries the profile's real health controller.</summary>
    internal class NoDamagePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.DeclaredMethod(typeof(Player), nameof(Player.ApplyDamageInfo));
        }

        [PatchPrefix]
        private static bool Prefix(Player __instance)
        {
            return !(__instance is HideoutPlayer) || !Plugin.Enabled.Value || !Plugin.NoDamage.Value;
        }
    }
}
