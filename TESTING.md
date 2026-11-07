# Morning checklist (about 20 minutes)

## 1. Listen to the headset bench (5 min, no game needed)

Open `bench_out/`. Play `scenario_0_raw.wav` first, then each preset. The clip has rain, footsteps alternating left/right every 0.5 s, a double-tap at 3.0 s, and a quieter far shot at 6.0 s.

For each preset, note:
- Are the footsteps **louder and clearer** than raw (electronic presets) or **muffled** (plugs, passive muffs)?
- The **shot**: is there a sharp click before it clamps? Too much, too little?
- **Pumping**: how long the footsteps stay buried after the shot. The bench measured about 0.75–0.9 s for the electronic presets. Too long or too short?
- **Hiss**: too loud on `ElectronicBudget`? Inaudible on `ElectronicPremium`?
- **Tone**: Budget is deliberately narrow and tinny, Premium wide. Does that read right?

Tell me in plain words ("Premium pumping too long, Budget click too loud"). Those are just numbers in `src/RealSoundSimulation.Dsp/HeadsetPreset.cs`. The measured values are in `bench_out/report.md`.

## 2. One raid with the diagnostics (10–15 min)

The plugin is already installed at `G:\SPT\BepInEx\plugins\FFloridaMann-RealSoundSimulation`. It **changes no sound**; it only watches and logs.

1. Start SPT as usual.
2. Check `G:\SPT\BepInEx\RealSoundSimulationLogs\selfcheck.txt`. It should say "All targets present."
3. Pick **Customs** or **Woods** (open ground for the 250 m test), plus a building with stairs if you can.
4. Press **F9** in raid for the overlay. The first column is **P** when a sound is coming through rooms and portals, **O** when it's blocked in the same room. `occl` turns red above 0.55, where gunshots switch to the muffled mixer.
5. Things worth doing:
   - Stand in the open while bots fight at long range. This tests the 250 m cliff.
   - Have a bot walk around on the floor above or below you.
   - Walk through a doorway while a fight is going on. This tests the indoor/outdoor snap.
6. Extract or die normally. The summary is written at raid end.
7. Tell me "done". I'll read `G:\SPT\BepInEx\RealSoundSimulationLogs\raid_<time>\` (summary.txt, shots.csv, containers.csv, rolloff_dump.txt, timing.csv) and `BepInEx\LogOutput.log`.

What the logs will decide:

| Question | File |
|---|---|
| Is the 250 m cliff real? | `summary.txt` "beyond 250 m with line of sight … on Occluded mixer" and `shots.csv` |
| Does the gun mixer flip-flop? | `summary.txt` mixer changes |
| How stale does occlusion get in a fight? | active processors (avg/max) |
| What are the real rolloff curves and ranges? These set the dB maths for suppressors and footsteps | `rolloff_dump.txt`, maxDistance by group |
| Are we near the voice limit? | playing sources vs realVoices |
| How much budget do we have? | `timing.csv`, PERFORMANCE section |

If anything looks off (stutter, errors in `LogOutput.log`), set `Write logs = false` in F12 → Real Sound Simulation, or delete the plugin folder.

## Baseline note (6 Nov 2026)

For the first diagnostics raid the old OldAudioMod mod was moved out of the plugins folder to `G:\SPT\BepInEx\disabled-plugins\OldAudioMod`, so the logs show the game's own audio. To restore it, move that folder back into `G:\SPT\BepInEx\plugins\`. Its config (`BepInEx\config\ffloridamann.oldaudio.cfg`) was left in place.

## Raid 2 (with AI)

The first raid had no AI, so gunfire, the 250 m test and the obstruction stages logged nothing. The plugin was also updated to log doorway transitions (`transitions.csv`, `ambience.csv`). **Restart the game first** so the new version loads.

1. Turn AI back on. Customs or Woods is best.
2. Walk in and out of a few buildings, slowly and at normal speed, including at least one with two doors.
3. Let bots fight at range while you stand in the open, and get into one fight yourself.
4. Extract or die, then say "done".

## Trying the fixes (0.2.0)

All four fixes are in F12 → Real Sound Simulation → "2. Fixes", and all start **off**, so the game sounds the same until you switch one on. They can be switched live in raid.

| Switch | Listen for |
|---|---|
| 1 Gun mixer hysteresis | A bot firing bursts behind cover 30–60 m away: shots shouldn't jump between muffled and clear |
| 2 Obstruction score → IgnoreFailedPaths (or ParallelPaths) | Gunfire behind thin cover sounding less muffled than it should |
| 3 Smooth doorways | Walking in and out of a building: no pop; the mix should change over about two steps |
| 4 Off-map sounds as outdoor | SPTBattleAmbience's distant fights: clearer when you're outside |

Best test: turn on one at a time and flip it off and on in the same spot. The logs record which fixes were on.
