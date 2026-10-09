# Hideout Uncensored

Your hideout, without the muzzle. Walk around it with your weapons out, go prone, vault, throw grenades, fire a
rocket launcher, pick magazines with hold-reload, and keep using every hideout area while you do. A single-player
[SPT](https://sp-mod.com) mod.

<!-- PREVIEW VIDEO GOES HERE -->
**PREVIEW VIDEO GOES HERE**

| | |
|---|---|
| **SPT** | 4.1.x only (built against SP-Tushonka 4.1.6, EFT 0.16.9.40743). Not for 4.0.x |
| **Parts** | client plugin (BepInEx) + small server part (needed only for "discard sends items back by message") |
| **Affects** | the hideout only. Nothing runs in a raid |
| **Your gear** | never used up: you hold the game's shooting range copy of it |
| **License** | MIT |

## Why

In the hideout you are a normal player character that the game deliberately muzzles: outside the shooting range
your keys go to dummy input handlers, some commands are thrown away before anything sees them, and parts of the
HUD are switched off. This mod lifts those blocks, one option each.

## What you get

| | Default |
|---|---|
| **Weapons anywhere** in first person: draw, aim, fire, reload, switch, inspect, fire modes, tactical devices, melee. No range prompt | on |
| Draw / holster with a key (**J**), or press a weapon slot key to draw that weapon. Escape holsters, as it does in the range | on |
| No "look away from the targets and your gun drops" restriction | on |
| **Rocket launchers** fire (the game ignores their trigger in the hideout) | on |
| **Grenades**: grenade key and throwing | on |
| Weapons and grenades stay on quick slots 4-0 | on |
| **Hold reload + scroll magazine selector**, with the selected magazine clearly marked | on |
| **Prone** | on |
| **Vaulting and climbing** | on |
| **Drop backpack** | on |
| **Inventory key while armed**: holsters, opens the inventory, draws again when you close it | on |
| **Hideout interactions while armed** (switch to area, transfer items, workout) | on |
| Area info icons key while armed | on |
| **Discard is not destructive in the hideout**: the item is removed and sent back to you in a message (server part) | on |
| **Health HUD panel** | on |
| Hideout flashlight while armed (bind a key in F12) | on, key unbound |
| No hit reactions on you in the hideout | on |
| Draw automatically on entering first person | off |
| Remember drawn weapons between hideout visits | off |
| Skill and weapon mastery gain (XP farm; mastery needs a live target) | off |

Everything is in **F12 > trappuss-HideoutUncensored** and is read live. "Enabled" off gives back the vanilla hideout.

## Install

Extract the release zip into your SPT folder (the one with `EscapeFromTarkov.exe`), with the game and server closed.

- Client: `BepInEx\plugins\HideoutUncensored\HideoutUncensored.dll`
- Server: `SPT_Runtime\user\mods\HideoutUncensored\HideoutUncensoredServer.dll`

On start the server console shows `[Hideout Uncensored] server part <version> loaded`.

## Nothing of yours is used up

- Everything you hold while armed is the game's shooting range **copy** of your gear. Firing, reloading, throwing,
  rockets and the dropped backpack all act on the copy, which the game deletes when you holster and rebuilds when you
  draw. Your real inventory is never written to by this mod.
- Consumables (meds, food) are deliberately **not** usable from the copy: their use is a server request, and the
  server would be asked to consume an item it does not have. Use them from the inventory screen as in vanilla.
- Discarding in the hideout inventory sends the item (with its contents) back to you in a message, kept for a year.
  If the server part is missing or the message cannot be sent, the item is not discarded at all. Discarding from the
  main menu stash is unchanged.
- Your stash is the right-hand panel of the hideout inventory, as in vanilla.

## Works with

- **MoxoPixel-HideoutShootout** (optional). It patches some of the same methods; the two agree. Its scav is also the
  only way to gain weapon mastery in the hideout, because the game only awards mastery for hitting a player or bot.
- **Infinite Everything** (optional). All of its inventory work in the hideout happens on the copy of your gear.
  From Infinite Everything 2.5.0 its "every compatible magazine" hold-reload list also applies in the hideout.
- Modded weapons. If a weapon ever fails to holster (the game can throw while returning it to its pool), the hideout
  player is reset to empty hands after a few seconds instead of staying stuck, with a notification.

## The sweep: what the hideout disables, and what this mod does about it

Found by reading the 0.16.9.40743 client (`HideoutPlayerOwner`, `HideoutPlayer`, `HideoutPlayerInputTranslator`,
`HideoutGrenadeInputTranslator`, `HideoutBattleUIScreenController`, `InteractionContextHelper`, `FirearmController`).

| Block in the game | Where | Here |
|---|---|---|
| All hand and player input goes to dummy translators outside the range (no weapons, slots, gestures, melee) | `HideoutPlayerOwner.PlayerInputTranslator` / `HandsInputTranslator` | Lifted while weapons are drawn |
| Weapons only through the range prompt, taken away when you walk out | `ShootingRangeBehaviour`, `ExitShootingRange` | Lifted |
| Weapon lowered and trigger blocked when looking more than 50 degrees off the range axis | `DecidePatrolStatus`, `HideoutPlayer.SetPatrol` | Lifted |
| Rocket launcher trigger ignored when the location is the hideout | `FirearmController.Idling.SetTriggerPressed` | Lifted |
| A copied rocket is a plain `Ammo`, which the rocket projectile cannot use | `Ammo.Clone`, `RocketProjectile.Initialize` | Swapped for a real `Rocket` before the shot |
| Prone thrown away | `HideoutPlayerOwner.TranslateCommand` | Lifted |
| Drop backpack thrown away | same | Lifted while armed |
| Inventory key ignored in range mode | `TranslateInventoryScreenInput` | Lifted (holster, open, re-draw) |
| Area prompts hidden in range mode | `AvailableForInteractions`, `InteractionContextHelper.GetAvailableActions` | Lifted |
| Grenade key handler is an empty override | `HideoutPlayerInputTranslator.vmethod_1` | Lifted |
| A grenade in hand can only be inspected | `HideoutGrenadeInputTranslator` | Lifted |
| Health panel never allowed | `HideoutBattleUIScreenController.AllowHealthPanel` | Lifted |
| Hideout flashlight forced off in range mode | `FlashlightAvailable` | Lifted (own key) |
| No vaulting component | `HideoutPlayer.InitVaultingComponent` (empty) | Lifted (applies on the next hideout load) |
| Quick-slot bindings 4-0 are cleared for the copy of your gear | `UpdateHideoutPlayerInventory` | Lifted for weapons and grenades. Consumables **left alone** (see above) |
| "Toggle info icons" ignored in range mode | `TranslateCommand` | Lifted |
| Discard destroys the item | `ItemUiContext.ThrowItem`, server `InventoryController.DiscardItem` | Sent back by message instead (hideout only) |
| Skill and weapon mastery gain are no-ops | `HideoutPlayer.ExecuteSkill`, `ExecuteShotSkill` | Opt-in |
| Quick slots and stance panel only in range mode | `AllowQuickPanel`, `AllowStancePanel` | Follow "weapons drawn" |
| Raid timer / exits keys thrown away | `TranslateCommand` | **Left alone**: there is no raid timer or exit in the hideout |

## Status

Everything in the table at the top has been run in game on SPT 4.1.6, except skill and mastery gain against a live
target.

If something misbehaves: F12 > "Debug log" (tick "Advanced settings"), reproduce, and look at
`BepInEx\LogOutput.log` for lines from `trappuss-HideoutUncensored`. Each option can be switched off on its own.

## Build

```
dotnet build HideoutUncensored.csproj -c Release -p:TarkovDir="<SPT folder>"
dotnet build Server\HideoutUncensoredServer.csproj -c Release
```

The client plugin is staged in `dist\BepInEx\plugins\HideoutUncensored\`, the server part in
`Server\dist\SPT_Runtime\user\mods\HideoutUncensored\`.

## Credits

By trappuss. Written with Claude Code (AI-assisted): the code and this page were produced with an AI coding agent and
tested in game by the author. MIT licensed.
