using System;
using System.Linq;
using System.Threading.Tasks;
using EFT;
using EFT.CameraControl;
using EFT.Hideout;
using EFT.Communications;
using EFT.InputSystem;
using EFT.InventoryLogic;
using EFT.UI.Screens;
using BepInEx.Configuration;
using UnityEngine;

namespace HideoutUncensored
{
    /// <summary>
    /// Keeps the hideout player in the state the user asked for. "Armed" is the game's own shooting range mode
    /// (HideoutPlayerOwner.InShootingRange): real input translators, a cloned copy of the equipment, weapon in hands.
    /// The game only enters it from the range prompt and leaves it when you walk out; here <see cref="Want"/> decides,
    /// and <see cref="Tick"/> moves the game towards it whenever the game is able to (first person, no gear screen
    /// open, no workout, no draw/holster already running).
    /// </summary>
    internal static class HideoutSession
    {
        private const float SettleSeconds = 0.5f;
        private const float AfterActionSeconds = 2f;
        private const float PendingSlotSeconds = 30f;
        private const float HolsterStuckSeconds = 6f;
        private const float DrawStuckSeconds = 45f;
        private const float TransitionTimeout = 15f;

        internal static HideoutPlayerOwner Owner { get; private set; }

        /// <summary>The user wants weapons out. Survives screens that force a holster (inventory, workout, overview).</summary>
        internal static bool Want { get; private set; }

        private static bool _pendingInventory;
        private static Action _afterHolster;
        private static ECommand _pendingSlot = ECommand.None;
        private static float _pendingSlotUntil;
        private static float _noDrawUntil;
        private static bool _wasArmable;
        private static bool _holstering;
        private static float _holsterStarted;
        private static bool _drawPending;
        private static float _busySince;
        private static bool _busyHandled;

        /// <summary>A stuck draw/holster is being cleared; the usual guards stand aside.</summary>
        internal static bool Recovering { get; private set; }

        internal static void Attach(HideoutPlayerOwner owner)
        {
            Owner = owner;
            bool want = Want && Plugin.RememberDrawn.Value;
            Reset();
            Want = want;
            Plugin.Debug("Hideout player owner attached");
        }

        private static void Reset()
        {
            Want = false;
            _pendingInventory = false;
            _afterHolster = null;
            _pendingSlot = ECommand.None;
            _noDrawUntil = 0f;
            _wasArmable = false;
            _holstering = false;
            _drawPending = false;
            _busySince = 0f;
            _busyHandled = false;
        }

        internal static void Tick()
        {
            HideoutPlayerOwner owner = Owner;
            if (owner == null)
            {
                if (!ReferenceEquals(owner, null))
                {
                    // destroyed with the hideout
                    Owner = null;
                    Reset();
                }

                return;
            }

            HideoutPlayer player = owner.HideoutPlayer;
            if (player == null)
            {
                return;
            }

            if (!Plugin.Armed)
            {
                // vanilla rules: whatever state the range is in is the game's business
                _pendingInventory = false;
                _afterHolster = null;
                _pendingSlot = ECommand.None;
                _wasArmable = false;
                return;
            }

            float now = Time.unscaledTime;
            bool armable = Armable(owner, player);
            if (armable && !_wasArmable)
            {
                _noDrawUntil = Mathf.Max(_noDrawUntil, now + SettleSeconds);
                if (Plugin.AutoDraw.Value)
                {
                    Want = true;
                }
            }

            _wasArmable = armable;

            if (armable)
            {
                ReadKeys(owner);
            }
            else
            {
                _pendingInventory = false;
                _pendingSlot = ECommand.None;
            }

            if (_holstering)
            {
                if (now - _holsterStarted > TransitionTimeout)
                {
                    Plugin.Log.LogWarning("Holstering did not finish in time, carrying on");
                    _holstering = false;
                }

                return;
            }

            if (Recovering)
            {
                return;
            }

            if (player.IsUpdateHideoutPlayerInventoryInProgress)
            {
                WatchBusy(owner, player, now);
                return;
            }

            _busySince = 0f;
            _busyHandled = false;

            bool shouldBeArmed = Want && armable && !_pendingInventory && _afterHolster == null;
            if (owner.InShootingRange)
            {
                if (!shouldBeArmed)
                {
                    Holster(owner);
                    return;
                }

                // UpdateHideoutPlayerInventory waits for BlockFirearms to clear before it hands over the weapon. In the
                // vanilla range that happens once you face the targets (ShootingRangeBehaviour ticks DecidePatrolStatus).
                // A draw started here must not depend on where the player is looking, so it is always let through.
                if (Plugin.NoAimRestriction.Value || _drawPending)
                {
                    if (player.IsInPatrol || player.MovementContext.BlockFirearms)
                    {
                        player.SetPatrol(false);
                    }

                    _drawPending = false;
                }
                else
                {
                    owner.DecidePatrolStatus();
                }

                FlushPendingSlot(player, now);
                return;
            }

            if (_afterHolster != null)
            {
                Action action = _afterHolster;
                _afterHolster = null;
                _noDrawUntil = now + AfterActionSeconds;
                action();
                return;
            }

            if (_pendingInventory)
            {
                _pendingInventory = false;
                _noDrawUntil = now + SettleSeconds;
                owner.TranslateInventoryScreenInput(ECommand.ToggleInventory);
                return;
            }

            if (shouldBeArmed && now >= _noDrawUntil)
            {
                Plugin.Debug("Drawing weapons");
                owner.EnterShootingRange();
                _drawPending = owner.InShootingRange;
            }
        }

        /// <summary>First person, alive, not in a workout, and no screen on top that edits the real inventory.</summary>
        internal static bool Armable(HideoutPlayerOwner owner, HideoutPlayer player)
        {
            if (!owner.FirstPersonMode || player.PointOfView != EPointOfView.FirstPerson || player.CustomAnimationsAreProcessing)
            {
                return false;
            }

            if (player.HandsController == null || player.HealthController == null || !player.HealthController.IsAlive)
            {
                return false;
            }

            EftScreenManager screens = EftScreenManager.Instance;
            return screens != null && (screens.CheckCurrentScreen(EEftScreenType.BattleUI) || screens.CheckCurrentScreen(EEftScreenType.Hideout));
        }

        private static void ReadKeys(HideoutPlayerOwner owner)
        {
            if (Pressed(Plugin.DrawKey.Value))
            {
                Want = !Want;
                _pendingSlot = ECommand.None;
                Notify(Want ? "Weapons drawn" : "Weapons holstered");
            }

            if (Plugin.FlashlightWhileArmed.Value && owner.InShootingRange && Pressed(Plugin.FlashlightKey.Value))
            {
                owner.FlashLightState = !owner.FlashLightState;
                RefreshFlashlight(owner);
            }
        }

        /// <summary>
        /// KeyboardShortcut.IsDown() needs every other key to be up, so it never fires while moving. This only asks for
        /// the main key and the shortcut's own modifiers.
        /// </summary>
        private static bool Pressed(KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey == KeyCode.None || !Input.GetKeyDown(shortcut.MainKey))
            {
                return false;
            }

            foreach (KeyCode modifier in shortcut.Modifiers)
            {
                if (!Input.GetKey(modifier))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The game's draw and holster are async methods that set IsUpdateHideoutPlayerInventoryInProgress and clear it
        /// at the end. An exception in between (a weapon that fails to go back to its pool) leaves the flag set for
        /// as long as the hideout is loaded: no draw, no holster, hands frozen. A holster has no loading in it, so one
        /// that is still running after a few seconds is dead; a draw loads bundles and gets much longer.
        /// </summary>
        private static void WatchBusy(HideoutPlayerOwner owner, HideoutPlayer player, float now)
        {
            if (_busySince <= 0f)
            {
                _busySince = now;
                return;
            }

            float limit = owner.InShootingRange ? DrawStuckSeconds : HolsterStuckSeconds;
            if (_busyHandled || now - _busySince < limit)
            {
                return;
            }

            _busyHandled = true;
            Plugin.Log.LogWarning($"{(owner.InShootingRange ? "Draw" : "Holster")} did not finish after {limit:0} s, resetting the hideout player's hands");
            _ = RecoverAsync(owner, player);
        }

        /// <summary>
        /// Back to the state the hideout starts in: empty hands, no copied gear, not in the range. The last step is the
        /// game's own forced reset (what HideoutPlayerOwner.Init runs).
        /// </summary>
        private static async Task RecoverAsync(HideoutPlayerOwner owner, HideoutPlayer player)
        {
            Recovering = true;
            try
            {
                Want = false;
                _pendingInventory = false;
                _afterHolster = null;
                _pendingSlot = ECommand.None;

                try
                {
                    player.FastForwardCurrentOperations();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Fast-forward failed: {e.Message}");
                }

                if (!(player.HandsController is Player.EmptyHandsController))
                {
                    try
                    {
                        player.DestroyController();
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogWarning($"Old hands controller could not be destroyed cleanly: {e.Message}");
                    }

                    // the same two lines HideoutPlayer.Create uses to give a new player its hands
                    Player.EmptyHandsController hands = Player.EmptyHandsController.CreateController<Player.EmptyHandsController>(player);
                    HarmonyLib.AccessTools.FieldRefAccess<Player, Player.AbstractHandsController>("_handsController")(player) = hands;
                    hands.Spawn(1f, () => { });
                }

                // operations of the dead controller that would keep the copied gear "busy"
                InventoryController copy = player.ShootingRangeInventory;
                if (copy != null)
                {
                    foreach (ItemEventArgs active in copy.ActiveEvents.ToArray())
                    {
                        copy.RemoveActiveEvent(active);
                    }
                }

                player.ProcessStatus = default;
                player.IsUpdateHideoutPlayerInventoryInProgress = false;
                await owner.SetShootingRangeStatus(false, true);
                RefreshFlashlight(owner);
                Notify("Hideout hands were stuck and have been reset");
                Plugin.Log.LogInfo("Hideout player reset");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Reset failed: {e}");
            }
            finally
            {
                Recovering = false;
                _holstering = false;
                _busySince = 0f;
                _busyHandled = false;
                _noDrawUntil = Time.unscaledTime + AfterActionSeconds;
            }
        }

        /// <summary>The range was entered (prompt or this mod): from here on the user is armed until they holster.</summary>
        internal static void OnRangeEntered()
        {
            Want = true;
        }

        /// <summary>Escape while armed holsters, exactly like leaving the vanilla range with Escape.</summary>
        internal static void OnEscape()
        {
            Want = false;
            _pendingSlot = ECommand.None;
            _pendingInventory = false;
        }

        internal static void OnWeaponKey(HideoutPlayerOwner owner, ECommand command)
        {
            HideoutPlayer player = owner.HideoutPlayer;
            if (!Plugin.DrawOnWeaponKeys.Value || player == null || !Armable(owner, player))
            {
                return;
            }

            Want = true;
            _pendingSlot = command;
            _pendingSlotUntil = Time.unscaledTime + PendingSlotSeconds;
        }

        /// <summary>
        /// The game is about to put the default weapon in the player's hands for a draw. If a slot key started the
        /// draw, hand over that weapon instead.
        /// </summary>
        internal static void RedirectDraw(HideoutPlayer player, ref Item item)
        {
            if (_pendingSlot == ECommand.None || !TryGetSlot(_pendingSlot, out EquipmentSlot slot))
            {
                return;
            }

            Item wanted = player.Equipment.GetSlot(slot).ContainedItem;
            if (wanted != null)
            {
                item = wanted;
                _pendingSlot = ECommand.None;
            }
        }

        internal static void RequestInventory()
        {
            _pendingInventory = true;
        }

        /// <summary>Holster, then run <paramref name="action"/> (something the game only supports unarmed).</summary>
        internal static void AfterHolster(Action action)
        {
            _afterHolster = action;
        }

        private static void Holster(HideoutPlayerOwner owner)
        {
            Plugin.Debug("Holstering weapons");
            _holstering = true;
            _holsterStarted = Time.unscaledTime;
            _ = HolsterAsync(owner);
        }

        /// <summary>
        /// ExitShootingRange without going through it: that method is what the range trigger calls when you walk out,
        /// so it is the one this mod (and HideoutShootout) blocks.
        /// </summary>
        private static async Task HolsterAsync(HideoutPlayerOwner owner)
        {
            try
            {
                await owner.SetShootingRangeStatus(false);
                if (owner != null)
                {
                    RefreshFlashlight(owner);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Holstering failed: {e}");
            }
            finally
            {
                _holstering = false;
            }
        }

        /// <summary>The slot key that drew the weapons: switch to that weapon once the copy of the gear is in place.</summary>
        private static void FlushPendingSlot(HideoutPlayer player, float now)
        {
            if (_pendingSlot == ECommand.None)
            {
                return;
            }

            if (now > _pendingSlotUntil || !TryGetSlot(_pendingSlot, out EquipmentSlot slot))
            {
                _pendingSlot = ECommand.None;
                return;
            }

            Item item = player.Equipment.GetSlot(slot).ContainedItem;
            if (item == null)
            {
                // empty slot, or the gear has not been copied yet: the timeout sorts out which
                return;
            }

            _pendingSlot = ECommand.None;
            if (!ReferenceEquals(player.HandsController?.Item, item))
            {
                player.SetSlotItem(slot, result => { });
            }
        }

        private static bool TryGetSlot(ECommand command, out EquipmentSlot slot)
        {
            switch (command)
            {
                case ECommand.SelectFirstPrimaryWeapon:
                    slot = EquipmentSlot.FirstPrimaryWeapon;
                    return true;
                case ECommand.SelectSecondPrimaryWeapon:
                    slot = EquipmentSlot.SecondPrimaryWeapon;
                    return true;
                case ECommand.SelectSecondaryWeapon:
                case ECommand.QuickSelectSecondaryWeapon:
                    slot = EquipmentSlot.Holster;
                    return true;
                case ECommand.SelectKnife:
                    slot = EquipmentSlot.Scabbard;
                    return true;
                default:
                    slot = EquipmentSlot.FirstPrimaryWeapon;
                    return false;
            }
        }

        internal static void RefreshFlashlight(HideoutPlayerOwner owner)
        {
            if (!CameraManager.Exist)
            {
                return;
            }

            HideoutCameraFlashlight flashlight = CameraManager.Instance.Flashlight;
            if (flashlight != null)
            {
                flashlight.SetState(owner.FlashlightAvailable());
            }
        }

        private static void Notify(string message)
        {
            if (!Plugin.ShowMessages.Value)
            {
                return;
            }

            try
            {
                NotificationManager.DisplayMessageNotification(message);
            }
            catch (Exception e)
            {
                Plugin.Debug($"Notification failed: {e.Message}");
            }
        }
    }
}
