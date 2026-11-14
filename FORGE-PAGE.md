# Real Sound Simulation

**A simulated audio overhaul for SPT.** The game plays a recording at a volume. This mod traces how each sound would really travel through the map (through which doorway, off which walls, through what material) and plays what would actually reach you.

> **Beta (0.9).** Tested over 130 raids, but by one player on one PC, and not yet in Fika. Version 1.0 comes after your feedback, so please report anything that sounds wrong.

## What you'll hear

- **No more doorway pop.** Outdoor sound fades in and out as the space around you really opens and closes, through the windows and doors it can get through.
- **Your gun in the room you're in.** Echoes come off the real surfaces around you, by material: a concrete stairwell rings, a furnished room soaks it up, an open field barely answers.
- **Fights you can place.** A fight in the next room comes through the doorway it would really use, duller the longer its route. Through a wall, it depends on the wall: a solid one leaves only a thump, or nothing.
- **Grenades you feel.** The blast comes from the opening it travels through, a deep thump reaches you through the wall first, and a small closed room hits far harder than a big hall. Fragments whizz past along their real path.
- **Concussion by the blast that reaches you.** Not straight-line distance: the pressure that gets to you through doorways and walls decides whether you're concussed, only deafened, or fine. Your hearing cuts without chopping the blast off, headsets protect your ears, and a close call sets your heart racing.
- **Rain from the real roof.** You hear how many floors are above you and what the roof is made of.
- **Optional:** your ears ring from gunfire without a headset, by calibre, muzzle device and room. Off by default.

## How it works

Thousands of times a second it traces rays through the map's real geometry. Every material is matched to published acoustic measurements: how much sound it absorbs at each pitch, how much it reflects, and how much gets through it. From that it works out which paths reach you, how long they take at the speed of sound, which way they arrive from, and what each wall, corner and doorway takes off.

## Why I made it

I couldn't stand the indoor and outdoor audio snapping over the moment I crossed a doorway, or hearing a fight and not knowing where it really was. I shoot regularly, so this is built from what real gunfire and blasts sound like, backed by many hours of research into how sound and blast waves travel.

## Performance

About **2% FPS** (122 instead of 125 on a Ryzen 7 9800X3D, Factory with 8 bots) and about 2% of one CPU core. In a raid checked frame by frame, the mod caused no hitches during play. On a slower PC, set **Sound quality** (F12, 02 GENERAL) to Medium or Low, or turn off **Other guns - echoes** (01 FEATURES).

## Install

1. You need SPT 4.1.
2. Optional, for the concussion part: [Bring Back Concussion](https://forge.sp-tarkov.com/mod/2550/bring-back-concussion).
3. If you use [Sound Engine Backport](https://sp-mod.com/mod/3314/sound-engine-backport) or [True Acoustics](LINK-HERE), remove them first. This mod already includes Sound Engine Backport's routes between rooms.
4. Extract the zip into your SPT folder (the one with `EscapeFromTarkov.exe`).
5. Start the game. Settings are in F12 → **Real Sound Simulation**. Everything is on and tuned by default.

**To uninstall,** delete `BepInEx/plugins/FFloridaMann-RealSoundSimulation` and the files starting with `com.ffloridamann.realsoundsimulation` in `BepInEx/config`.

## Good to know

- **It changes how sound travels, not the recordings.** Modded weapons get the same treatment, and a gun that sounds bad in vanilla still does.
- **Prefer something lighter?** Sound Engine Backport by red_the_fox with True Acoustics by Kaan sound great. Use them instead of this mod, not alongside it.
- **Grenades:** my next mod, Real Grenade Simulation (coming soon), gives each grenade its real number of fragments for this mod to make you hear.
- **Known issues:**
  - rain still has odd spots in some buildings;
  - some semi-auto guns (the ADAR 2-15 most clearly) have a slight static at the end of their tails after quick single shots;
  - the G36 doesn't blend its indoor and outdoor recordings;
  - with Bring Back Concussion, crossing a doorway as your hearing comes back can leave the indoor ambience at the old level for a while.

## Roadmap

**Not in 0.9.0 or 0.9.1: footsteps and everyday sounds.** Footsteps, gear, looting, healing and eating play as recorded, and the switch **General - footsteps and handling like EFT 1.0** (01 FEATURES, on by default) picks how: on uses Sound Engine Backport's backported EFT 1.0 footstep settings (distances, stereo spread and 3D), off is vanilla SPT. The simulated version, heard through doorways, muffled by walls and floors and placed above or below you, is built but held back until it sounds right. It comes after 1.0.

- **0.9.0 (out now):** the first public beta.
- **0.9.1:** fixes only. The debug overlay (F9, clashed with Fika) and ray view (F11) are gone from the public build, and the bug report key (F10) only works while bug report logs are on.
- **1.0:** better raid start loading, final testing, polishing, bug fixes from your beta reports, and fixes for any Fika issues you report.
- **After 1.0, in order:** weather, a headsets revamp (each headset protects and sounds as it's really built), **footsteps and everyday sounds**, bots feeling blasts too, the crack of passing bullets, and room reverb for other guns (a shot in a big hall next door brings the hall's reverb).

[Full roadmap](https://github.com/FFloridaMann/RealSoundSimulation/blob/main/ROADMAP.md)

## Found a bug?

1. Turn on **Bug report - logs** (F12 → 02 GENERAL) and play one raid.
2. Press **F10** wherever something sounds wrong.
3. After the raid, wait a few seconds on the results screen, then send me the newest zip from the **RealSoundSimulation Logs** folder in your SPT folder, and say roughly when and where it happened.
4. Turn the logs off again; they cost a little FPS.

The zip holds the mod's logs, your mod list and your BepInEx log. It never leaves your PC unless you send it.

I'm a solo developer with a full-time job, so please be patient. Every report gets read.

## Credits

- [Bring Back Concussion](https://forge.sp-tarkov.com/mod/2550/bring-back-concussion) by harmony: the concussion effect; the soft hearing cut is adapted from it.
- [Sound Engine Backport](https://sp-mod.com/mod/3314/sound-engine-backport) by red_the_fox: the routes sound takes between rooms (included) and the footstep and handling settings.

Both used with permission. Thank you both.

**Development:** 90 hours, over 100 versions and 130 test raids. For a smoother raid, try my [Bot Loading Freeze Fix](LINK-HERE), which removes the freeze about two minutes into every raid.

If you enjoy it, you can [buy me a coffee](LINK-HERE). Entirely optional; everything stays free.
