using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using SPT.Common.Http;
using SPT.Reflection.Patching;

namespace HideoutUncensored.Patches
{
    /// <summary>
    /// UpdateHideoutPlayerInventory clears the quick slot bindings of the copied gear. Put back the ones that are safe
    /// to use on a copy: weapons and grenades. Consumables are left unbound (their use is a server request for an
    /// item the server does not have).
    /// </summary>
    internal static class QuickSlots
    {
        /// <summary>
        /// Equipment.TopPriorityGrenade (the grenade the grenade key reaches for first) belongs to the copied gear's
        /// equipment object, which outlives each copy. After a holster it points at a grenade of the previous copy,
        /// and the grenade key then throws "Trying to get owner of discarded item". Forget it unless it is carried.
        /// </summary>
        internal static void DropStaleGrenade(HideoutPlayer player)
        {
            InventoryController copy = player != null ? player.ShootingRangeInventory : null;
            if (copy == null)
            {
                return;
            }

            ThrowWeap top = copy.Inventory.Equipment.TopPriorityGrenade;
            if (top == null)
            {
                return;
            }

            var carried = new List<ThrowWeap>();
            copy.GetReachableItemsOfTypeNonAlloc(carried, x => true);
            if (!carried.Contains(top))
            {
                copy.Inventory.Equipment.TopPriorityGrenade = null;
            }
        }

        internal static void Restore(HideoutPlayer player)
        {
            DropStaleGrenade(player);
            if (!Plugin.Armed || !Plugin.QuickSlots.Value)
            {
                return;
            }

            Inventory original = player.OriginalInventory?.Inventory;
            Inventory copy = player.ShootingRangeInventory?.Inventory;
            if (original == null || copy == null)
            {
                return;
            }

            Dictionary<EBoundItem, Item> bound = copy.FastAccess.BoundItems;
            foreach (KeyValuePair<EBoundItem, Item> pair in original.FastAccess.BoundItems)
            {
                Item item = pair.Value;
                if (item == null || bound.ContainsKey(pair.Key))
                {
                    continue;
                }

                if (!(item is Weapon) && !(item is ThrowWeap && Plugin.Grenades.Value))
                {
                    continue;
                }

                Item twin = FindCopy(original, copy, item);
                if (twin != null)
                {
                    bound[pair.Key] = twin;
                }
            }
        }

        /// <summary>The copied gear has new ids, so the twin is found by position in the same equipment slot.</summary>
        private static Item FindCopy(Inventory original, Inventory copy, Item item)
        {
            foreach (Slot slot in original.Equipment.GetAllSlots())
            {
                Item root = slot.ContainedItem;
                if (root == null)
                {
                    continue;
                }

                List<Item> items = root.GetAllItems().ToList();
                int index = items.IndexOf(item);
                if (index < 0)
                {
                    continue;
                }

                Item copyRoot = copy.Equipment.GetAllSlots().FirstOrDefault(x => x.ID == slot.ID)?.ContainedItem;
                if (copyRoot == null)
                {
                    return null;
                }

                List<Item> copies = copyRoot.GetAllItems().ToList();
                return copies.Count == items.Count && copies[index].TemplateId == item.TemplateId ? copies[index] : null;
            }

            return null;
        }
    }

    /// <summary>
    /// The last step of a hideout draw is Player.SetItemInHands(default weapon). By then the gear is copied, so this
    /// is where the quick slots go back and where a slot key picks the weapon.
    /// </summary>
    internal class DrawHandoverPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.DeclaredMethod(typeof(Player), nameof(Player.SetItemInHands));
        }

        [PatchPrefix]
        private static void Prefix(Player __instance, ref Item item)
        {
            if (!(__instance is HideoutPlayer player) || !player.IsUpdateHideoutPlayerInventoryInProgress || !Plugin.Armed)
            {
                return;
            }

            try
            {
                QuickSlots.Restore(player);
                HideoutSession.RedirectDraw(player, ref item);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Draw handover failed: {e}");
            }
        }
    }

    /// <summary>The HUD rebuilds its quick slot bar here; covers a draw with no weapon to hand over.</summary>
    internal class RangeHudPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EftBattleUIScreen.HideoutBattleUIScreenController),
                nameof(EftBattleUIScreen.HideoutBattleUIScreenController.OnShootingRangeStatus));
        }

        [PatchPrefix]
        private static void Prefix(EftBattleUIScreen.HideoutBattleUIScreenController __instance, bool status)
        {
            if (!status)
            {
                return;
            }

            try
            {
                HideoutPlayer player = __instance._hideoutPlayerOwner != null ? __instance._hideoutPlayerOwner.HideoutPlayer : null;
                if (player != null)
                {
                    QuickSlots.Restore(player);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Quick slots could not be restored: {e}");
            }
        }
    }

    /// <summary>HideoutPlayer.ExecuteSkill(Action) is an empty override; the base one runs the action.</summary>
    internal class SkillActionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.DeclaredMethod(typeof(HideoutPlayer), nameof(HideoutPlayer.ExecuteSkill), new[] { typeof(Action) });
        }

        [PatchPostfix]
        private static void Postfix(Action action)
        {
            if (!Plugin.Enabled.Value || !Plugin.Skills.Value || action == null)
            {
                return;
            }

            try
            {
                action();
            }
            catch (Exception e)
            {
                Plugin.Debug($"Skill action failed: {e.Message}");
            }
        }
    }

    /// <summary>HideoutPlayer.ExecuteShotSkill is an empty override; the base one feeds weapon mastery.</summary>
    internal class ShotSkillPatch : ModulePatch
    {
        private static bool _baseReady;

        protected override MethodBase GetTargetMethod()
        {
            try
            {
                Harmony.ReversePatch(
                    AccessTools.DeclaredMethod(typeof(Player), nameof(Player.ExecuteShotSkill)),
                    new HarmonyMethod(typeof(ShotSkillPatch), nameof(BaseShotSkill)));
                _baseReady = true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Weapon mastery stays disabled, Player.ExecuteShotSkill could not be copied: {e.Message}");
            }

            return AccessTools.DeclaredMethod(typeof(HideoutPlayer), nameof(HideoutPlayer.ExecuteShotSkill));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void BaseShotSkill(Player instance, Item weapon)
        {
            throw new NotImplementedException("reverse patch stub");
        }

        [PatchPostfix]
        private static void Postfix(HideoutPlayer __instance, Item weapon)
        {
            if (!_baseReady || !Plugin.Enabled.Value || !Plugin.Skills.Value || weapon == null)
            {
                return;
            }

            try
            {
                BaseShotSkill(__instance, weapon);
                Plugin.Debug($"Shot counted for mastery: {weapon.ShortName}");
            }
            catch (Exception e)
            {
                Plugin.Debug($"Shot skill failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Out of a raid, "Discard" destroys the item (ItemUiContext.ThrowItem: confirm, then a Remove operation).
    /// In the hideout the server part is first told which item is coming; its Remove handler then mails the item back
    /// before deleting it. If the server part does not answer, nothing is discarded.
    /// </summary>
    internal class DiscardPatch : ModulePatch
    {
        private const string Route = "/hideoutuncensored/discard";

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ItemUiContext), nameof(ItemUiContext.ThrowItem));
        }

        [PatchPrefix]
        private static bool Prefix(ItemUiContext __instance, Item item, ref Task __result, ItemController ____itemController)
        {
            if (!Plugin.Enabled.Value || !Plugin.DiscardToMail.Value || item == null || ____itemController == null || !InHideout(item))
            {
                return true;
            }

            __result = Discard(__instance, ____itemController, item);
            return false;
        }

        /// <summary>The player is walking around the hideout and the item is in their real inventory.</summary>
        private static bool InHideout(Item item)
        {
            HideoutPlayerOwner owner = HideoutSession.Owner;
            if (owner == null || !owner.FirstPersonMode)
            {
                return false;
            }

            HideoutPlayer player = owner.HideoutPlayer;
            return player != null && ReferenceEquals(item.Owner, player.OriginalInventory);
        }

        private static async Task Discard(ItemUiContext context, ItemController controller, Item item)
        {
            try
            {
                if (!controller.CanThrow(item))
                {
                    return;
                }

                string name = controller.Examined(item) ? item.ShortName.Localized() : "Unknown item".Localized();
                if (!await context.ShowMessageWindow(out _, $"Leave {name} in the hideout? It will be sent back to you in a message."))
                {
                    return;
                }

                if (!Announce(item))
                {
                    NotificationManager.DisplayWarningNotification("Hideout Uncensored server part did not answer. Nothing was discarded.");
                    return;
                }

                controller.TryThrowItem(item, null, true);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Discard failed: {e}");
            }
        }

        private static bool Announce(Item item)
        {
            try
            {
                string response = RequestHandler.PostJson(Route, "{\"item\":\"" + item.Id + "\"}");
                return response != null && response.Replace(" ", string.Empty).Contains("\"ok\":true");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"{Route} failed: {e.Message}");
                return false;
            }
        }
    }
}
