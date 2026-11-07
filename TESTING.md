# Testing

## Fixed test route (Ground Zero) — run on every build

Agreed 6 Nov 2026 (consolidation step 4). Every build is judged on the same stops, so a fix in one place can't quietly break another. Coordinates are from the F10 marks (x, y, z as in `marks.csv`); "?" = not found yet: press F10 there on the next route raid and they get filled in.

**Setup.** Ground Zero, **AI off** (so only your own shots), rain if possible (any intensity; note which). Settings: the defaults (F12, no changes) unless the build says what to compare. Start the raid, wait ~30 s for the ambience to load.

**At each stop, in order:** walk in at normal speed, stand still ~5 s, **press F10** (so mark N = stop N), listen, then fire 3 single shots and one short burst. Then walk to the next stop. If you skip a stop, just tell me which.

**Report:** for each stop, "as expected" or what was different, in your own words. I check the logs against the same table.

| # | Stop | Where | What you should hear | Log check |
|---|---|---|---|---|
| 1 | Open street | in front of the Nakatani tower entrance, ~10 m out (≈ 22, 23, 329) | Full outdoor rain and wind all round. No roof rain, no room tone. Shots: outdoor recording, far echoes off buildings | outdoor value ≈ 1, indoor layers 0 |
| 2 | Tower entrance, walking in | the doors (12.0, 23, 329.5), walk in slowly | The outside fades over about two steps; nothing cuts in and out; no jump at the door line | value falls smoothly through the doorway |
| 3 | Exit hallway by the windows | Nakatani extract hallway (7.5, 24.4, 330.3) | Rain from the window side, muffled by the glass, louder where windows are broken. No roof rain (4 floors above). No doubling | glass/open directions toward the windows; roof rain 0 |
| 4 | Deep in Nakatani | (−6.0, 24.4, 330.0) | Outside quiet and distant, from the openings' direction. **No doubled or flamming drops** | low value; voices (v2: no copies) |
| 5 | Nakatani stairwell, going down | (−16.1, 16.8, 337.2) | No roof rain. Shots: loud, hard, reverberant concrete stairwell | hard room high; floors above ≥ 2 |
| 6 | Garage under the road cover | (42.4, 22.7, 265.4) | **Loud, reverberant rain from the front entrance**, muffled from the sides. Dull rain on the concrete slab above. Shots: hard concrete room | v2: value well above 0.5, arriving from the entrance |
| 7 | Capital lobby by the shot-out windows | (109, 280), windows 12–14 m away | Outside from the windows' direction, not from inside the building. No rain "inside" | open directions toward the windows |
| 8 | Capital small interior room | deep inside (112.8, 25.0, 264.9) | Very quiet outside. Shots: strongly indoor | low value; gunshot indoor weight high |
| 9 | Small glass office | the small glass offices outside ? | Weather muffled through glass, louder at a broken pane. Shots: only partly indoor | glass directions |
| 10 | Covered bench shelter | metal frame and wood planks, covered on 3 sides ? | Almost outdoor. Shots: mostly outdoor with some close reflections | value high; gunshot indoor weight low |
| 11 | Hole in a roof | a building with part of its roof missing ? F10 under the hole, then 5 m away under intact roof | Rain from above under the hole; quieter and dull under the intact roof | v2: sky found above at the hole |
| 12 | Your start building | the long glass windows inside the concrete building you spawn in ? | Weather through the glass wall, muffled, from the windows' side | glass directions |

**Testing probe v2 (0.6.0):** F12 → `9 Ambience from probe v2` and/or `9a Gunshots from probe v2` on (one at a time is fine). Run the route as usual. At any stop, F11 shows the probe's rays: green reached the sky, cyan after bouncing, yellow through glass, red trapped. If something sounds wrong, say what the rays showed ("red everywhere in the garage" or "green through the wall"). To compare, flip 9 or 9a off and on at a stop. From 0.6.4 the fainter rays are the guided ones (aimed at openings already found); deep inside they should bunch toward the windows and doors. Flip `9f Guided rays` off and on at a deep stop: with it on, the level should stay steadier. Leave `9e` at 1 (it is now measured). From 0.6.5, deep inside with no opening nearby the rain should be wide and quiet, leaning towards the side the openings are on, and should not jump from side to side. From 0.6.6, `9g Ray quality` (default Medium) can be raised to High or Ultra at a deep stop: the level should get steadier; check the COST line afterwards. From 0.6.7: deep inside, the rain should lean towards the building's openings without coming through the walls; if you want it more pointed, raise `9h Direction strength` (try 1.5–2) and say which value sounds right. Roof rain: full under a roof, partly under a mezzanine, and on the floor below a roof it should come faintly down the stairwell. The overlay's probe line shows `roof rain` (0–1).

**Testing gun sounds (0.6.3):** at stop 1, with a semi-only gun (pistol, SKS, bolt-action, pump shotgun), fire 5 single shots with `0 Gun sounds` = SPT, then switch it to SEB in F12 and fire 5 more. The first SEB shot may still be SPT while its sounds load. With SEB, each shot should differ slightly; with SPT they're identical. Then an AKM or M4: it should stay SPT (`0a` off). Check `gunsounds.csv` and the summary's GUN SOUNDS line. If a gun stays SPT, the `reason` column says why.

**Between stops, listen for:** sounds cutting in and out, clicks, rain coming from overhead or from inside a building, anything doubled.

**Dry raid now and then:** the same route without rain checks wind and the day/night background.

---

## History: earlier test instructions


### 1. Listen to the headset bench (5 min, no game needed)

Open `bench_out/`. Play `scenario_0_raw.wav` first, then each preset. The clip has rain, footsteps alternating left/right every 0.5 s, a double-tap at 3.0 s, and a quieter far shot at 6.0 s.

For each preset, note:
- Are the footsteps **louder and clearer** than raw (electronic presets) or **muffled** (plugs, passive muffs)?
- The **shot**: is there a sharp click before it clamps? Too much, too little?
- **Pumping**: how long the footsteps stay buried after the shot. The bench measured about 0.75–0.9 s for the electronic presets. Too long or too short?
- **Hiss**: too loud on `ElectronicBudget`? Inaudible on `ElectronicPremium`?
- **Tone**: Budget is deliberately narrow and tinny, Premium wide. Does that read right?

Tell me in plain words ("Premium pumping too long, Budget click too loud"). Those are just numbers in `src/RealSoundSimulation.Dsp/HeadsetPreset.cs`. The measured values are in `bench_out/report.md`.

### 2. One raid with the diagnostics (10–15 min)

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

### Baseline note (6 Nov 2026)

For the first diagnostics raid the old OldAudioMod mod was moved out of the plugins folder to `G:\SPT\BepInEx\disabled-plugins\OldAudioMod`, so the logs show the game's own audio. To restore it, move that folder back into `G:\SPT\BepInEx\plugins\`. Its config (`BepInEx\config\ffloridamann.oldaudio.cfg`) was left in place.

### Raid 2 (with AI)

The first raid had no AI, so gunfire, the 250 m test and the obstruction stages logged nothing. The plugin was also updated to log doorway transitions (`transitions.csv`, `ambience.csv`). **Restart the game first** so the new version loads.

1. Turn AI back on. Customs or Woods is best.
2. Walk in and out of a few buildings, slowly and at normal speed, including at least one with two doors.
3. Let bots fight at range while you stand in the open, and get into one fight yourself.
4. Extract or die, then say "done".

### Trying the fixes (0.2.0)

All four fixes are in F12 → Real Sound Simulation → "2. Fixes", and all start **off**, so the game sounds the same until you switch one on. They can be switched live in raid.

| Switch | Listen for |
|---|---|
| 1 Gun mixer hysteresis | A bot firing bursts behind cover 30–60 m away: shots shouldn't jump between muffled and clear |
| 2 Obstruction score → IgnoreFailedPaths (or ParallelPaths) | Gunfire behind thin cover sounding less muffled than it should |
| 3 Smooth doorways | Walking in and out of a building: no pop; the mix should change over about two steps |
| 4 Off-map sounds as outdoor | SPTBattleAmbience's distant fights: clearer when you're outside |

Best test: turn on one at a time and flip it off and on in the same spot. The logs record which fixes were on.
