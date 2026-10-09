using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HideoutUncensored.Patches;
using SPT.Reflection.Patching;
using UnityEngine;

namespace HideoutUncensored
{
    /// <summary>
    /// Hideout Uncensored for SPT 4.1.x (EFT 0.16.9.40743). Client only, hideout only: nothing here runs in a raid.
    /// The hideout player is a normal LocalPlayer that the game deliberately muzzles (dummy input translators outside
    /// the shooting range, hard-blocked commands, range-only HUD). Each option below lifts one of those blocks.
    /// Every option is in F12 and is read live; turning one off gives back the vanilla behaviour.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency("com.SPT.core", "4.1.0")]
    [BepInDependency("com.moxopixel.hideoutshootout", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInProcess("EscapeFromTarkov.exe")]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.trappuss.hideoutuncensored";
        public const string PluginName = "trappuss-HideoutUncensored"; // Forge rule: "Username-ModName"
        public const string PluginVersion = "1.2.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> ShowMessages;
        internal static ConfigEntry<bool> DebugLog;

        internal static ConfigEntry<bool> ArmedAnywhere;
        internal static ConfigEntry<KeyboardShortcut> DrawKey;
        internal static ConfigEntry<bool> DrawOnWeaponKeys;
        internal static ConfigEntry<bool> AutoDraw;
        internal static ConfigEntry<bool> NoAimRestriction;
        internal static ConfigEntry<bool> SafeTrigger;

        internal static ConfigEntry<bool> Prone;
        internal static ConfigEntry<bool> DropBackpack;
        internal static ConfigEntry<bool> Grenades;
        internal static ConfigEntry<bool> Vaulting;

        internal static ConfigEntry<bool> InventoryWhileArmed;
        internal static ConfigEntry<bool> InteractWhileArmed;
        internal static ConfigEntry<bool> MagSelectorFallback;
        internal static ConfigEntry<bool> HealthPanel;
        internal static ConfigEntry<bool> FlashlightWhileArmed;
        internal static ConfigEntry<KeyboardShortcut> FlashlightKey;

        internal static ConfigEntry<bool> NoDamage;

        internal static ConfigEntry<bool> RememberDrawn;
        internal static ConfigEntry<bool> QuickSlots;
        internal static ConfigEntry<bool> InfoIcons;
        internal static ConfigEntry<bool> DiscardToMail;
        internal static ConfigEntry<bool> Skills;
        internal static ConfigEntry<bool> RocketLaunchers;

        /// <summary>Master switch and the weapons option: everything that keeps the player in "range mode" outside the range.</summary>
        internal static bool Armed => Enabled.Value && ArmedAnywhere.Value;

        private float _nextErrorLog;

        private void Awake()
        {
            Log = Logger;
            BindConfig();

            Enable(new OwnerInitPatch());
            Enable(new OwnerTranslateCommandPatch());
            Enable(new OwnerInventoryInputPatch());
            Enable(new OwnerExitInputPatch());
            Enable(new OwnerEnterRangePatch());
            Enable(new OwnerExitRangePatch());
            Enable(new OwnerPatrolPatch());
            Enable(new OwnerFlashlightPatch());
            Enable(new AreaActionsPatch());
            Enable(new WorkoutStartPatch());
            Enable(new HealthPanelPatch());
            Enable(new GrenadeHandsPatch());
            Enable(new GrenadeKeyPatch());
            Enable(new VaultingPatch());
            Enable(new NoDamagePatch());
            Enable(new DrawHandoverPatch());
            Enable(new RangeHudPatch());
            Enable(new SkillActionPatch());
            Enable(new ShotSkillPatch());
            Enable(new DiscardPatch());
            Enable(new RainCondensatorPatch());
            Enable(new EscapeDuringTransitionPatch());
            Enable(new SelectorMarkPatch());
            Enable(new RocketTriggerPatch());
            Enable(new RocketFireGuardPatch());

            Log.LogInfo($"{PluginName} {PluginVersion} loaded");
        }

        /// <summary>One failed patch (renamed target after a game update) must not take the other options down with it.</summary>
        private static void Enable(ModulePatch patch)
        {
            try
            {
                patch.Enable();
            }
            catch (Exception e)
            {
                Log.LogError($"{patch.GetType().Name} could not be applied, its option will do nothing: {e.Message}");
            }
        }

        private void Update()
        {
            try
            {
                HideoutSession.Tick();
            }
            catch (Exception e)
            {
                if (Time.unscaledTime >= _nextErrorLog)
                {
                    _nextErrorLog = Time.unscaledTime + 10f;
                    Log.LogError($"Tick failed: {e}");
                }
            }
        }

        private void BindConfig()
        {
            const string general = "1. General";
            const string weapons = "2. Weapons";
            const string movement = "3. Movement and actions";
            const string ui = "4. Inventory, interactions, HUD";
            const string safety = "5. Safety";
            const string progression = "6. Progression";

            Enabled = Option(general, "Enabled", true, 100,
                "Master switch. Off = the vanilla hideout (weapons only inside the shooting range).");
            ShowMessages = Option(general, "Show messages", true, 90,
                "Show a short notification when weapons are drawn or holstered by this mod.");
            DebugLog = Option(general, "Debug log", false, 10,
                "Write what the mod decides (draw, holster, magazine selector checks) to BepInEx/LogOutput.log.", true);

            ArmedAnywhere = Option(weapons, "Weapons anywhere in the hideout", true, 100,
                "Draw, aim, fire, reload, switch and inspect weapons anywhere in first person, without the shooting range prompt. " +
                "The game's own shooting range mode is used: you hold a copy of your gear, so ammo and durability are never spent.");
            DrawKey = Config.Bind(weapons, "Draw / holster key", new KeyboardShortcut(KeyCode.J),
                new ConfigDescription("Draws or holsters your weapons in the hideout. Escape also holsters, as it does in the shooting range.",
                    null, new ConfigurationManagerAttributes { Order = 90 }));
            DrawOnWeaponKeys = Option(weapons, "Weapon keys draw", true, 80,
                "Pressing a weapon slot key (primary, secondary, sidearm, melee) while holstered draws that weapon.");
            AutoDraw = Option(weapons, "Draw automatically", false, 70,
                "Weapons come out by themselves whenever you are in first person in the hideout.");
            RememberDrawn = Option(weapons, "Remember drawn weapons", false, 65,
                "If your weapons were out when you left the hideout, they come out again the next time you walk in.");
            QuickSlots = Option(weapons, "Quick slots", true, 55,
                "The game empties quick slots 4-0 while weapons are drawn. This keeps the weapons and (with Grenades on) grenades you bound to them. " +
                "Meds, food and other consumables stay off on purpose: using a copy would ask the server to consume an item you do not have.");
            RocketLaunchers = Option(weapons, "Rocket launchers", true, 58,
                "The game ignores the trigger of rocket launchers (RShG-2 and the like) when the location is the hideout. This lets them fire.");
            NoAimRestriction = Option(weapons, "No aiming restriction", true, 60,
                "The game lowers your weapon and blocks the trigger when you look more than 50 degrees away from the range. This removes that.");
            SafeTrigger = Option(weapons, "No firing while the cursor is free", true, 50,
                "Ignore fire, throw and melee input while a hideout panel has the mouse cursor, so clicking a button does not fire your weapon.");

            Prone = Option(movement, "Prone", true, 100,
                "The prone key is hard-blocked in the hideout. This lets it through.");
            DropBackpack = Option(movement, "Drop backpack", true, 90,
                "The drop-backpack key is hard-blocked in the hideout. Works while weapons are drawn; it drops the copy, not your real backpack.");
            Grenades = Option(movement, "Grenades", true, 80,
                "The grenade key and throwing are disabled in the hideout (a grenade in hand can only be inspected). This enables both while weapons are drawn. " +
                "Thrown grenades are copies and come back the next time you draw.");
            Vaulting = Option(movement, "Vaulting", true, 70,
                "The hideout player is created without the vaulting/climbing component. This creates it. Applies the next time the hideout loads.");

            InventoryWhileArmed = Option(ui, "Inventory while armed", true, 100,
                "The inventory key is ignored while weapons are drawn. With this on it holsters, opens the inventory, and draws again when you close it " +
                "(with whatever you changed).");
            InteractWhileArmed = Option(ui, "Hideout interactions while armed", true, 90,
                "Area prompts (switch to area, transfer items, workout) are hidden while weapons are drawn. This keeps them. " +
                "The workout holsters your weapons first; gear screens holster them as they open.");
            MagSelectorFallback = Option(ui, "Magazine selector (hold reload + scroll)", true, 80,
                "The hold-reload selector is part of the HUD and should work on its own once weapons are drawn. This is a backup that drives it directly if the HUD did not take the input.");
            InfoIcons = Option(ui, "Area info icons key while armed", true, 75,
                "The key that shows/hides the hideout area icons is ignored while weapons are drawn. This lets it through.");
            DiscardToMail = Option(ui, "Discard sends items back by message", true, 72,
                "Discarding an item from the inventory while you are in the hideout does not destroy it: it is removed and sent back to you " +
                "in a message, like an insurance return. Needs the Hideout Uncensored server part; without it nothing is discarded.");
            HealthPanel = Option(ui, "Health panel", true, 70,
                "The health HUD panel is never allowed in the hideout. This allows it (your HUD visibility settings still apply).");
            FlashlightWhileArmed = Option(ui, "Hideout flashlight while armed", true, 60,
                "The hideout's own flashlight is forced off while weapons are drawn (its key becomes aim). This keeps it available on the key below.");
            FlashlightKey = Config.Bind(ui, "Hideout flashlight key", new KeyboardShortcut(KeyCode.None),
                new ConfigDescription("Toggles the hideout flashlight while weapons are drawn. Unbound by default.",
                    null, new ConfigurationManagerAttributes { Order = 50 }));

            Skills = Option(progression, "Skill and weapon mastery gain (experimental)", false, 100,
                "The hideout player's skill and mastery hooks are empty. This runs the normal ones, so shooting and moving in the hideout " +
                "can level you up. Weapon mastery in this game only comes from hitting a player or bot, so paper targets give none; it needs a live target " +
                "(HideoutShootout's scav). Off by default: it is an XP farm, and the progress only reaches the server with your next raid.");

            NoDamage = Option(safety, "No damage in the hideout", true, 100,
                "Ignore hits on your character in the hideout (grenades, ricochets): no flinch, no hit reaction. The hideout health controller already takes no damage; this is the belt to that pair of braces.");
        }

        private ConfigEntry<bool> Option(string section, string key, bool value, int order, string description, bool advanced = false)
        {
            return Config.Bind(section, key, value,
                new ConfigDescription(description, null, new ConfigurationManagerAttributes { Order = order, IsAdvanced = advanced }));
        }

        internal static void Debug(string message)
        {
            if (DebugLog.Value)
            {
                Log.LogInfo(message);
            }
        }
    }
}
