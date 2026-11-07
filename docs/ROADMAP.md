# Roadmap

The agreed order of work (6 Nov 2026), and why. How things work right now: [DESIGN.md](DESIGN.md). Each step ends with a test raid; the next step uses what that raid showed. Status is updated as steps finish. Details of each fix: [FIXES.md](FIXES.md).

| # | Step | Status | Why in this order |
|---|---|---|---|
| 1 | Doorway fix: keep the same doorway through a crossing; enclosure probe with logging | **Tested (raid 6).** No doorway swaps; probe tracks open/covered/room well once the ground is ignored (0.3.0) | The probe has to be checked against real spaces before anything relies on it |
| 2 | Own-gun room-reverb experiment (setting 5) | **In use:** `5` at 0 dB, `5a` +6 dB × how hard the room is (0.3.3; stairwells, raids 10–13). Meta's acoustic map is confirmed running (raid 6) | Cheap. Shows how much the game's existing Meta room acoustics can do for your own shots before building echoes ourselves |
| 3 | Own-gun recording blend driven by enclosure and aim (fix 3c, Enclosure mode) | **Done (raid 10: thresholds, stairwell and office "good").** Further tuning goes with the material research. History: **heard (raids 6–8).** 0.3.0 fixed the ground; 0.3.1 measures a room as walls × roof, scales by size and recognises windows (raid 8: "sounds pretty good"); 0.3.2 adds a roof-right-above-you test, because stepping just outside a small space still sounded indoor | Uses the probe; low risk; covers "covered on three sides" and part of "into vs out of a room" |
| 4 | Synthesised early reflections ("echoes") for your own shots, with air absorption and surface materials built in | **Done; tuning continues** (stairwell reverb via 5a). **Heard (raid 7): "much much better", "overall huge improvement".** Semi-auto and full-auto together (hooked on the gun's sound queue). Tuning continues | The main "dynamic" effect. Air absorption and materials are shared with it, so they're built together |
| 4b | **Interior/exterior mixing (phases 1–2 heard; phase 4 built 0.5.0: sound from the openings; phase 3, roof material, still to do):** how much of the outside you hear indoors, and how. Ambience (rain, wind, distant sounds) through windows, glass walls and openings; rain on the roof only under the real roof; no cutting in and out | **Phases 1, 2 and 4 built and in use** (0.4.0–0.5.8); raid 20: "a lot better at thresholds". To do: phase 3 (roof material), roof holes. 0.5.7–0.5.8 not raided yet. Current behaviour: [DESIGN.md](DESIGN.md) §3 | The doorway blend (fix 3) only fixes the *switch*. Indoors, the level still comes from the game's per-room setting, which is 0 in places with whole glass walls. Before the headset, because ear-pro amplifies ambience |
| 5 | Headset simulation into the game, then tuning | Planned | Electronic ear-pro reacts to echoes, so it has to be tuned after step 4, or it would be tuned twice |
| 6 | Portal positioning (sounds through a doorway come from the doorway), **how much gets through walls** (by material), then echoes for bot gunshots | Planned | Bot echoes need the correct source position first |
| 6b | **SEB recordings on full-auto guns** (user, 7 Nov). Setting `0 Gun sounds` can use Sound Engine Backport's EFT 1.0 recordings, but they are single shots and the game loops one Body recording while the trigger is held, so full-auto guns stay on SPT (`0a` off). User runs SEB on single fire, SPT on full auto. Patch playback so a held trigger plays one SEB single-shot take per round fired (from `FireBullet`, at the gun's fire rate, varied takes, tail on release) instead of the loop | Planned | Independent of the environment work; before step 7, which judges every gun's recordings |
| 7 | **Gun audio overhaul for every gun in the game** (user, 6 Nov). Go through every weapon's sound banks: body, tail, silenced, near/mid/far tiers, indoor/outdoor sets. Make each one sound right and consistent: loudness across guns and calibres, suppressed against unsuppressed, near/far and indoor/outdoor recordings that match, full-auto loops and tails. Needs a way to measure the recordings' real loudness; the game keeps them compressed, so measure the output while they play | Planned, long | After the environment work (steps 4b–6), so every gun is judged in the finished acoustics |
| – | **Weather and acoustics research** (user, raid 19). In rain the user hears less gunshot echo and less open-air sound. Logs: echo levels unchanged (dry −22.1 dB, medium rain −22.4, heavy −24.9, the last from more far echoes), so it is masking by the rain bed, which fix 8 now also brings indoors. Research how real rain and weather change what you hear, then decide what step 4 (echoes) and 4b should do: rain noise level by intensity and its masking; humidity and temperature effect on air absorption (high humidity absorbs *less* high frequency at normal temperatures); wet surfaces (water fills pores, so reflections get slightly stronger and soil and grass absorb less); wind (bends sound with height and gusts make it waver); drops hitting nearby surfaces (roofs, cars, puddles) as sound sources | Planned, research first | Before tuning echo levels for weather |
| – | Bot gunshots changing when *you* cross a threshold | Planned, any time | Independent |
| – | Realistic material acoustics (raid 13, user: "still some tweaking to do on how the materials behave"): research each surface type's measured behaviour (absorption and scattering by frequency, transmission loss, resonance for thin metal and glass) and replace the rough tables in fix 6 and the room measure | Planned (user idea, raid 9) | Builds on the material tags fixes 6 and 3c already read; better after the headset (step 5), so tuning happens once |
| – | Distant obstruction (nothing past ~250 m is muffled by terrain or buildings) | Planned, any time | Independent; uses different, long rays |
| – | Server settings (phase 1 of the solutions doc) | Planned, any time | Independent |

## Consolidation (6 Nov 2026, after 0.5.7)

0.5.0 → 0.5.7 went out in one afternoon, each fixing the last one's side effects. Before building more: checkpoint, clean up, one current-state design doc, a fixed test route, then one change per build.

1. **Checkpoint — done.** Git repo, branch `main`, private GitHub remote (see README). Every build that goes into a raid gets a commit.
2. **Clean-up — done (0.5.8, user approved all of A–E).** None of it changes the sound with your current F12 settings.
   - **A. Defaults = the tested setup?** The code defaults are "everything off", but every raid since raid 10 ran with your F12 values: all fixes on, `2 Obstruction score` ParallelPaths, `3d` Enclosure, `3i` Soft, `8f` HRTF on. Option: make those the defaults, so a reset or fresh install gives the tested setup (every fix stays switchable). Your own config file keeps its values either way.
   - **B. Remove `3h Ambience through glass`.** Fix 8 replaced it (0.4.2); with fix 8 on it does nothing. The setting, `DoorwayBlend.GlassLeak` and its ambience.csv column go.
   - **C. Remove dead code.** `AmbienceMix.IndoorFade()` (replaced by `IndoorLayers()` in 0.5.7, only logged) and the 0.5.0 opening "clusters" in `OutdoorExposure` (the opening sounds use the 6 fixed sectors since 0.5.2; the clusters are only logged, 3 exposure.csv columns).
   - **D. Correct setting descriptions** (no key renames, since a rename resets the value): `Outdoor exposure probe` (says "indoors only, 24 rays, changes no sound"; it now runs everywhere, 48 directions, and drives fix 8); `8b` (says "needs fix 7"; not since 0.5.6); `8` (list what it does now); `3c` (its name says "at doorways"; in Enclosure mode it acts everywhere, so say so in the description).
   - **E. Correct code comments.** The `OutdoorExposure` header still describes 0.4.0 (every 0.25 s, indoors only, 1/(1+(d/5)²) weighting, 3 strongest openings, "logged only"). Rename `PreviewValue` to what it is now (the applied value); drop unused constants.
   - **Decided, kept:** the 15 s source scan (51.5 ms every 15 s; user, 6 Nov: keep it).
3. **Current-state design doc — done:** [DESIGN.md](DESIGN.md). Roadmap table brought up to date; the raid-check sections below are marked as history.
4. **Fixed test route — done:** [TESTING.md](../TESTING.md), 12 stops on Ground Zero with what you should hear and what the logs should show.
4a. **Probe v2 — built in 0.6.0, not raided yet** (raid 21, user: "we really need better probes"). Decisions and review: [AMBIENCE.md](AMBIENCE.md) §12–13. Switches 9 (ambience) and 9a (gunshots), off by default, so the current probes stay for A/B. Raid 22: "really good"; 0.6.1 steadies it. Raid 24 (0.6.2, 7 Nov 09:02): sound from inside the building "improved"; **outdoor ambience still too loud too far into the building.**
   - **Raid 24 finding (the loudness):** at mark 1 (Capital, 139.0, 29.7, 265.5) 2 of 128 paths reach outside. Traced paths give −24 dB; the **room build-up adds the rest: −14 dB total, 88% of it from build-up** (escape rate 0.002 per bounce, absorption 0.052). Over the raid, wherever you're enclosed, build-up is a median 62% of the level. The build-up formula treats the whole connected interior as **one empty concrete room evenly fed by its openings**. Right for the garage (one room, open side), wrong three rooms and a corridor away from the door, where every doorway in between loses most of the sound (coupled rooms). Untagged low-detail shapes also count as hard (absorption 0.06; concrete 0.02), so a furnished floor reads as a bunker. Not a ray-count problem. Live check without a rebuild: `9e` 0.3 at mark 1 gives about −18 dB, 0 gives −24 dB (but jumpy, from only 2 paths).
   - **Next, proposed 7 Nov (user asked: improve the rays, then offer more rays as an option?), one build each, in order:**
     1. **Build-up from the paths that get out** (fixes the loudness): continue the bounce-by-bounce fade that the escaping paths actually show, instead of one room-wide formula, and trace further (3 → 8 bounces) so less is estimated. Needs enough escaping paths, so it ships with 2.
     2. **Guided rays + remembered openings + smoothing by confidence** (fixes the in and out). About half the rays go where outside was found last time, each weighted down so totals stay right (user: "what if we weighted the rays?"). Openings once found are kept and re-checked every sweep: the user's "fixed rays" idea, fixed onto real openings instead of fixed directions, which would leave permanent gaps between them (the 0.5.x cutting in and out). The fewer paths escape, the longer the level is averaged.
     3. **Ray quality setting** (user idea): Low / Medium / High / Ultra = rays per second (now ~2200/s, 16 ms per second of game time). Noise falls with the square root of the ray count, so Ultra (4×) halves it at ~4× the cost.
     4. **Exit points + distant sounds through openings** (step 6; user, raids 23–24): find where each escaping path leaves the building (halve the last leg, test for sky), so the rain comes from the window or door. Then SPTBattleAmbience's distant gunfire (`BetterAudio.PlayAtPoint`, 450–600 m, off the baked map) is re-aimed through those openings and set by the probe's level when you're indoors, instead of through the walls from its true direction. **Own switch** (user: some people won't want it applied to the ambient gunfire mod).
5. **Then one change per build**, from: roof holes, phase 3 (roof material), step 6, weather research, material research, the Tarkov/3D-audio review, step 5, step 7.

## History and evidence

Raid-by-raid checks and design notes as they happened, oldest at the bottom. Not current: for how things work now see [DESIGN.md](DESIGN.md).

### Next raid: check 0.4.2 (step 4b phase 2)

Turn on **"8 Outdoor sound indoors from openings"** (keep fix 3 on; fix 7 on too). Then A/B it (8 on/off) at:
1. The tower entrance: step sideways just inside; no more 6% ↔ 95% swings.
2. The glass building with broken windows: outdoor sound should come in, louder near the windows.
3. Facing away from an open side vs facing it: duller and panned toward it when it's behind you.
4. A deep interior room: should sound as before.
5. In rain: lower floors of a tall building should have quieter roof rain.

F10 anywhere it sounds wrong. Tune with 8a–8d.

### Raid 12 check of 0.4.1

1. The Nakatani tower: walk in and out and stay inside a while. The outdoor ambience should no longer cut on and off.
2. A stairwell: are gunshots now loud enough? Tune with "6e Hard room echo boost dB" (and "5a Hard room reverb boost dB").
3. Break a window and stand at it, and stand at a barred window. Then I'll check `exposure.csv` and the collider sizes in the summary.
4. If it rains: fix 7 on, walk between rooms and up stairs.

### Raid 11 check of 0.4.0 (step 4b phase 1; rain helps, no AI needed)

The exposure probe changes no sound; it measures. Press **F9** to see it on the overlay and **F10** to mark spots. Visit:
1. The glass building from raid 10 (broken windows on two sides): expect many open directions near the windows, fewer deeper in.
2. A room deep inside a building with no windows: expect almost all blocked.
3. A room with intact glass windows: expect glass directions.
4. A high floor of a tall building, then the top floor or a roof room: compare floors above and roof material.
5. Under a metal roof or shed, if there is one.

Also turn on **"7 Fix rain cutting out between rooms"** in rain and walk between rooms and up stairs. Does the rain still cut in and out? (`rain.csv` shows every time the game would have silenced it.)

### Step 6 evidence: too much gets through hard walls (raid 12, user)

Inside the concrete Nakatani stairwell, distant background gunfire (SPTBattleAmbience fake battles, about 470 m away, off the baked map) was "pretty hearable" and came from its **true direction** through the concrete. The logs (`containers.csv`, 350–372 s) show the game already treating it as **fully blocked**: propagation 1, occlusion 1, on the muffled "Occluded" gun channel. So:
1. **The game's "fully blocked" isn't blocked enough** indoors behind hard walls. It's a filter and a fixed cut, not the 45–55 dB that concrete takes off. Needs a material-based through-wall loss (solutions doc 3.1, using the same material tags as fixes 6 and 3c), applied to remote and distant sounds, with fix 4's off-map sounds included.
2. **The direction is wrong.** Blocked sound should arrive from the openings (doors, windows, the stair exit), not through the wall. That's portal positioning (solutions doc 2.1), which uses the step 4b exposure directions for off-map sounds.
3. **Both paths at once (user, 6 Nov).** A blocked sound plays as two parts, mixed:
   - **via the openings:** placed at the door, window or stair exit, with loss from the longer path;
   - **through the material:** from the true direction, with loss and dulling by material type *and thickness*. Thin wood or sheet metal lets a fair amount through, half a metre of concrete almost nothing. Thickness is measured by casting back from the far side of the wall: the solutions doc's "two colliders" check.

   **Both paths always play**, each at its own level and from its own direction, as in reality: in a concrete stairwell you hear the gunfire mainly from the stair exit plus a faint, dull version from its true direction through the wall; behind a thin wall the through-wall part is the stronger one but the doorway still adds to it. Neither path switches the other off.

Applies to bot gunshots too, so it's tested with AI on.

### Next raid: check 0.5.2 (rain)

- The two spots from raid 16 (the big building's lobby by the entrance): rain should no longer sit on top of you, move, or come from inside.
- Rain through openings should sound like an area, not a point. If it's still too localised, lower "8e Sound from openings" (for example 0.4) so more of it stays all-around.
- Also try "8f HRTF on opening sounds".
- **The start building** (a long wall of glass windows in a concrete building): press F10 there. `rays.csv` shows whether its glass reads as glass (good), blocked (the glass stops the rays like a wall: too quiet), or open (too loud).

### Raid 16 check of 0.5.1 (rain if possible)

As for 0.5.0 below. Also, in the Nakatani hall, stand facing the wall close up with your ears toward the windows (the case the user found worst). The outside should come from the side. If not, press F10 in that pose.

### Raid 15 check of 0.5.0

Fixes 3, 7 and 8 on.
1. **Openings:** stand in a room open on one side, then turn around. The outside should come from the opening. Compare `8e` at 0.7 and 0, and try `8f` HRTF on and off.
2. **The Nakatani exit hallway:** stand by the open windows and press F10. If the rain still doesn't come through, `rays.csv` will show why.
3. **Roof rain:** a single-storey building (full), a top floor (full), a lower floor of a tall building and the exit stairs (none).
4. **Outdoors:** must sound exactly as before (the emitters fade out).

### Before phase 4 (raid 14, rain)

- **Nakatani exit hallway:** the open windows were barely found (exposure 0.07). Possible causes: rays 30° apart miss windows to the side, or the area outside those windows is an indoor room in the game's map. Plan: 24 directions (15° apart), and F10 dumps every direction (hit, distance, material, where it reached outside). Phase 4's emitters are placed at the openings found, so this has to be right first.
- **Exit stairs below ground:** roof rain stayed at 30% (fixed 8b for any floors above). Plan: fade per floor: 8b to the power of floors above (0.3, 0.09, 0.03…).

### Step 4b design: interior/exterior mixing (planned)

**Evidence (raid 10).** At ground level in a building with large glass walls on two sides (x 6–13, z 329–331 on Ground Zero), the game's baked room setting `OutdoorAmbientVolume` is 0. So the outdoor ambience falls to 0.001 indoors, and only the game's indoor layers play, which include rain that sounds like it's on a roof right above you. The facade glass doesn't block our rays (wall closure 0.36–0.6, glass share 0.06), so fix 3h, which looks for glass hits, barely acted. Many of that room's windows are permanently broken (user, raid 10), so reading them as open air is right. The problem is the game's level, not the probe: a room open to the outside still plays no outdoor ambience. The user also hears it "cut in and out a bit".

**Investigation done (6 Nov):** see [AMBIENCE.md](AMBIENCE.md): how the game builds ambience, what fix 3 changes, a game bug that makes indoor rain cut out, and the options to decide together before building.

**Original plan.** Investigate first, then build:
1. **How the game builds indoor ambience.** Which sources play rain-on-roof, wind and the outdoor bed indoors (AmbientSoundBlender layers, the weather/rain system); what each room's baked values are; and why it cuts in and out (room-to-room changes in the baked level, or layers switching).
2. **An "outdoor exposure" measure** in place of the glass-hit guess. In which horizontal directions can the outside be reached: rays that leave the building (escape, or travel well past the room and land outdoors), plus the room's open portals (doors, windows) from the baked portal data. Facade glass that our rays pass through: detect it with a second ray against all collision layers, and treat it as glass (muffled), not open air.
3. **Outdoor ambience level indoors from exposure:** open → near full; glass → quieter and muffled; solid → the game's level. Smoothed over 1–2 s so it doesn't cut in and out.
4. **Rain on the roof only under the real roof.** Check whether more building lies above the ceiling (a second ray from just above the ceiling hit). On lower floors of a tall building, turn the roof-rain layer down; under a thin roof or on the top floor, keep it.
5. **Later, if feasible:** make outdoor ambience come *from* the windows and openings, not from everywhere.

Testing: the glass building from raid 10, a high floor of a tall building, a room with one open window, rain and wind.

### Next raid: check 0.3.3 (no AI needed)

1. The glass office threshold: stand on it, look in and fire (should sound inside), then from inside look out and fire (should sound outside). If the muzzle side decides too much or too little, change "3g Muzzle weight".
2. The Nakatani basement stairs: should now be fully indoor with more echo and room reverb. Tune with "5a Hard room reverb boost dB".
3. Inside a glass building in rain or wind: the weather should come through, muffled. Tune with "3h Ambience through glass".
4. F10 where anything's off.

Raid 10 result: threshold and stairwell/office "good"; stairwell "maybe more reverb"; glass ambience not solved (see step 4b).

### Raid 9 check of 0.3.2

Same as below, plus: walk out of the small spaces you marked in raid 8 and fire just outside, half a metre out, then a metre. The indoor sound should be gone once you're out from under the roof. Keep pressing F10 where something's off.

Raid 9 result: "a lot better"; remaining items fixed in 0.3.3.

### Raid 8 check of 0.3.1

Keep fixes 1, 3, 3c (Enclosure), 4 and 6 on. **Press F10 wherever something sounds off** (too indoor, too outdoor, echo wrong); it writes the spot to `marks.csv`, so there's no guessing from times.

1. The covered bench and the small glass office from raid 7: they should now sound mostly outdoor / less roomy, with the echoes giving the closeness.
2. A normal room and a big hall: they should still sound indoor. If a small concrete room now sounds too dry, say so (that's the size factor).
3. An open-fronted shed or garage, and under an overhang next to a building.
4. If glass spaces are still too indoor, lower "3f Thin and glass surfaces count"; raise it if they're now too open.

### Testing step 4 (raid 7)

All in F12 → Real Sound Simulation. Keep fixes 1, 3, 3c (Enclosure) and 4 on as before.

1. **Ground fix.** In open ground, fire a few shots. With 3c in Enclosure mode they should now sound fully outdoor (`ownshots.csv` indoorWeight near 0).
2. **Echoes on/off.** Turn on "6 Own gun echoes". In each place below, fire single shots with it on, then off, then on:
   - open ground (expect little, maybe a late echo off a far building);
   - next to one big wall, firing along it and then away from it (a slap from the wall);
   - a spot covered on three sides (close, dense echoes);
   - standing in a doorway, firing into the room and then out of it (into: loud and close in front; out: weaker, from behind);
   - in a room and in a big hall.
3. **Full-auto.** A burst inside and outside: the echoes should follow the burst and stop with it.
4. **Tune.** If echoes are too loud or too quiet overall, change "6a Echo level dB". If into-vs-out-of-a-room differs too little, raise "6c Muzzle directivity dB". If it gets cluttered, lower "6b Max echoes per shot". Turn "6d Far echoes" off and on in open ground with buildings 30–250 m away.
5. Optional: fix 5 at 0, +6 and +12 dB with echoes on and off.

Raid 7 result: "sounds much much better", "overall huge improvement". Two spots too indoor: a covered bench (metal frame, wood planks, roof, open sides) and a small glass office near the end of the raid; addressed in 0.3.1.

`echoes.csv` records every echo played (kind, surface, path, delay, level, filter, direction), and `summary.txt` has an OWN GUN ECHOES section.

### Step 1–3 test results (raid 6)

- Probe: rooms 0.82–1.0 (overhead ~0.94); covered outdoor spots 0.65–0.75 (overhead ~0.8); "open" ground only went down to 0.2–0.4 because the ground just below level counted as walls. Fixed in 0.3.0.
- Enclosure mode: open-ground shots still got 13–25% indoor recording, and the aim cone read ~0.37 when firing into open air (its lower rays hit the ground ~3.5 m ahead). Same fix.
- Doorway: no doorway swaps; the one fast change was a smooth fade while moving sideways along a wall just inside.
