# Changelog

## 1.2.0 (2026-10-09)

First public release. Same code as 1.1.3, which was confirmed in game (rocket launcher flies, explodes and re-arms;
grenades; everything listed under 1.1.x).

- Vaulting is on by default and no longer marked experimental. Its option is now called "Vaulting" (a value saved
  under the old name "Vaulting (experimental)" is not carried over).
- README rewritten for release; server metadata has the project URL.

## 1.1.3 (2026-10-08)

- Fix: a rocket fired in the hideout hung in the air with no effects, and the launcher needed a re-holster.
  `RocketProjectile.Initialize` casts the round to `Rocket`, but the hideout's copied gear is made with
  `Item.CloneItem`, and `Ammo.Clone` always builds a plain `Ammo`. The round in the tube is now swapped for a real
  `Rocket` item just before the shot. (This is very likely why the game blocks launchers in the hideout.)
- Skills opt-in, clarified after testing: weapon mastery in this game is only awarded when a shot damages a player
  or bot (`Player.ApplyDamageInfo` calls the shooter's `ExecuteShotSkill`). Shooting targets gives none, in a raid
  or here. The option can only matter with a live target in the hideout.
- Confirmed in game on 1.1.2: grenade key, grenade on a quick slot, grenade after a re-draw.

## 1.1.2 (2026-10-08)

- Fix: the grenade key did nothing ("Trying to get owner of discarded item" in the log). The game remembers a
  preferred grenade on the copied gear's equipment object, which survives a holster while the grenade it points at
  does not. It is now forgotten unless you are carrying it.
- "Remember drawn weapons" now defaults to off (Escape holsters on the way out anyway; starting holstered is the preferred behaviour). An existing config keeps its saved value.
- With "Debug log" on, each shot counted for weapon mastery is logged, so the opt-in skill gain can be checked.
- New: rocket launchers fire in the hideout. `FirearmController.Idling.SetTriggerPressed` returns early when the location is "hideout" and the weapon is a rocket launcher (found via a report from the Infinite Everything session: RShG-2 would not fire). If building the shot throws there, the fire operation is finished cleanly instead of sticking. Not yet run in game.
- Confirmed in game: vaulting, discard-to-message, the 1.1.1 stuck-hands fix path did not trigger again.

## 1.1.1 (2026-10-08)

Fixes the "hands floating, J only shows the notification, nothing works until the game is restarted" state.

What happened (from `Logs\...errors.log`): Escape started a holster, a second Escape left first person during the
animation, and the game then fast-forwarded the hands operation. Returning the pistol to its pool threw a
NullReferenceException in `RainCondensatorHelper.SetEnabled` (one of the weapon's rain condensator components had
already been destroyed; this was after several borrowed-magazine reloads on a modded pistol). The exception escaped
`HideoutPlayer.ReleaseShootingRangeInventory`, which never cleared its "in progress" flag, so every later draw and
holster waited forever. The hideout object survives leaving and re-entering, so that did not help.

- In the hideout, destroyed rain condensators are skipped instead of throwing.
- Escape is ignored while a draw or holster is running, so the animation is not fast-forwarded halfway.
- If a holster is still "running" after 6 s (a draw: 45 s), the hideout player is reset: empty hands, copied gear
  removed, flags cleared, using the game's own forced reset. A notification says so. No restart needed.
- Hold-reload selector: the selected magazine cell is also enlarged in the hideout. The log shows the picks were
  being applied (15 and 24 round magazines were loaded), so the selection moved but its colour highlight was not
  visible.

Not fixed here: whatever destroyed the condensator belongs to the weapon/magazine side (Infinite Everything's borrowed
magazines or WeaponBase), and the same exception could occur in a raid, where this mod does nothing.

## 1.1.0 (2026-10-08)

1.0.0 was confirmed working in game. Everything below is new and **not yet run in game**.

- Slot keys draw that weapon directly (no more default weapon first).
- Quick slots 4-0 keep the weapons and grenades you bound to them while armed. Consumables stay unbound on purpose.
- Drawn weapons are remembered between hideout visits (option).
- The area info icons key works while armed.
- Discard in the hideout inventory is no longer destructive: the item (with its contents) is removed and mailed back
  in a system message, kept for a year. New **server part** for this. Without it, or if the mail fails, nothing is
  discarded. Discarding from the main menu stash is unchanged.
- Opt-in: skill and weapon mastery gain in the hideout.
- Infinite Everything (separate project): its full hold-R list now also applies in the hideout. Checked that all of
  its inventory work in the hideout happens on the copy of your gear.
- Verified, no change needed: the hideout inventory already shows the whole stash on the right.

### First things to try

1. Holstered, press the sidearm key: the sidearm comes out, not the rifle.
2. Bind a grenade to 4-0 in the inventory, draw, look at the quick slot bar, press the key.
3. Discard a cheap item from the hideout inventory: the confirm text mentions a message, the item arrives in
   messages. Server console shows `item(s) left in the hideout were mailed back`.
4. Hold R + scroll with Infinite Everything's Infinite ammo on: every compatible magazine is listed.
5. Still untried from 1.0.0: grenades, vaulting (opt-in).

## 1.0.0 (2026-10-08)

First version. Written and audited from the 0.16.9.40743 client code; **not yet run in game**.

### Audit (static, second reader with no stake in the first design)

Confirmed against the game code:
- Draw (`EnterShootingRange`, then `SetPatrol(false)` on the next frame) and holster (`SetShootingRangeStatus(false)`)
  match what the game does itself; no deadlock found, with or without MoxoPixel-HideoutShootout.
- Escape and the reconciler cannot double-holster (`SetShootingRangeStatus` is idempotent).
- Every patch target, injected parameter name and `__result` type matches (`tools\verify-targets.ps1` checks the same
  against the installed DLL).
- The real inventory (`HideoutPlayer.OriginalInventory`) is never touched: weapons, grenades and the dropped
  backpack are all the game's shooting range copy.
- Nothing runs in a raid except one type check in the hit-reaction patch.

Fixed after the audit:
- The draw and flashlight keys used `KeyboardShortcut.IsDown()`, which needs every other key up, so they would not
  have fired while walking. They now only need the key itself (plus the shortcut's own modifiers).
- With "No aiming restriction" off, a draw outside the range's 50 degree cone waited forever. A draw started by the
  mod is now always handed over.
- A draw/holster that never reports back is fast-forwarded after 15 s (the game's own remedy outside first person).
- Weapon-key draw no longer gives up after 8 s on a slow first bundle load (30 s).

Known and accepted:
- Drawing with a slot key other than the default weapon draws the default weapon first, then switches.
- "No hit reactions" is cosmetic: the hideout health controller already ignores damage.
- The magazine selector backup should never be needed (the HUD panel has no hideout gate); it is kept because
  whether that panel receives input in the hideout HUD prefab could not be read from code.

### First things to try in game

1. Hideout, first person, press **J**: weapon comes out anywhere. Turn around, fire, reload.
2. Hold reload + scroll: magazine selector. If it does not open, turn on "Debug log" and send the
   `Magazine selector not shown: ...` line.
3. Prone key.
4. Inventory key while armed: holster, inventory, re-draw on close.
5. Look at an area while armed: prompts are there; start a workout.
6. Grenade key, throw.
7. Escape: holster. Escape again: back to the overview.
