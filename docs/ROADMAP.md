# Roadmap

The order of work and why. Agreed 6 Nov 2026, reordered 8 Nov 2026.

- **Each step ends with a test raid**; the next step uses what that raid showed.
- **Step numbers are names, not positions.** They stay fixed so older notes, the changelog and settings still match. For example, the headset is still "step 5" though it now comes after step 6.
- **Checklist of every step and its substeps: [TRACKER.md](TRACKER.md).**
- How things work right now: [DESIGN.md](DESIGN.md). Details of each fix: [FIXES.md](FIXES.md).

## At a glance

| Order | # | Step | Status |
|---|---|---|---|
| 1 | 1 | Doorway fix + enclosure probe | Tested |
| 2 | 2 | Own-gun room reverb (setting 5) | In use |
| 3 | 3 | Own-gun indoor/outdoor recording blend (3c) | Done |
| 4 | 4 | Echoes for your own shots | Done, tuning continues |
| 5 | 4b | Outside sound heard indoors | Mostly built; roof holes left |
| 6 | 6.0a | Audio-thread timing (measure) | Done (raid 34); no audio-load counter exists |
| 7 | 6.0b | Ray batch wait on busy CPUs (measure) | Done (raid 34); no fix needed |
| 8 | **6.1** | **Portal positioning** | **In progress:** routes logging built (0.6.22), not raided |
| 9 | M | Realistic materials (+ 4b roof material) | Planned |
| 10 | 6.2 | Through-wall sound by material | Planned |
| 11 | 6.3 | Echoes for bot gunshots and grenades | Planned |
| 12 | W | Weather research and retune | Planned |
| 13 | 5 | Headset simulation | Planned (moved after step 6) |
| 14 | 6b | SEB recordings on full-auto guns | Planned |
| 15 | C | Supersonic crack by calibre | Planned |
| 16 | 7 | Gun audio overhaul, every gun | Planned, long |
| 17 | E | Final echo tuning and optimising | Planned, last |
| any time | – | Distant obstruction; server settings | Planned |

Not in this roadmap: **GC and spawn stutters**. That's a separate project, likely separate mods (user, 8 Nov 2026), on branch `claude/gc-and-spawn-stutters`. Its two pieces that belong to Real Sound Simulation's own logging (garbage-free container rows, backing off after spawns) moved here as 0.6.19.

---

## Done

### 1 · Doorway fix + enclosure probe
- **What:** keep the same doorway through a crossing; an enclosure probe with logging.
- **Status:** tested (raid 6). No doorway swaps. Once the ground is ignored (0.3.0), the probe tracks open, covered and room spaces well.
- **Why first:** the probe had to be checked against real spaces before anything relied on it.

### 2 · Own-gun room reverb (setting 5)
- **Status:** in use. `5` at 0 dB; `5a` +6 dB × how hard the room is (0.3.3; stairwells, raids 10–13). Meta's acoustic map is confirmed running (raid 6).
- **Why here:** cheap. It showed how much the game's own Meta room acoustics can do for your shots before we built echoes ourselves.

### 3 · Own-gun recording blend by enclosure and aim (fix 3c, Enclosure mode)
- **Status:** done. Raid 10: thresholds, stairwell and office "good". Further tuning goes with the material step (M).
- **History:** heard in raids 6–8.
  - 0.3.0 fixed the ground.
  - 0.3.1 measures a room as walls × roof, scales it by size and recognises windows (raid 8: "sounds pretty good").
  - 0.3.2 added a test for a roof right above you, because stepping just outside a small space still sounded indoor.
- **Why here:** uses the probe and is low risk. Covers "covered on three sides" and part of "into vs out of a room".

### 4 · Echoes for your own shots
- **What:** synthesised early reflections, with air absorption and surface materials built in. Semi-auto and full-auto (hooked on the gun's sound queue).
- **Status:** done; tuning continues (stairwell reverb via 5a). Raid 7: "much much better", "overall huge improvement".
- **Why here:** the main "dynamic" effect. Air absorption and materials are shared with it, so they were built together.

### 4b · Outside sound heard indoors
- **What:** how much of the outside you hear indoors, and how. Rain, wind and distant sounds come through windows, glass walls and openings; rain on the roof only under the real roof; no cutting in and out.
- **Status:** phases 1, 2 and 4 are built and in use (0.4.0–0.5.8; phase 4 = sound from the openings). Raid 20: "a lot better at thresholds".
- **Left to do:** roof holes. Phase 3 (roof material) moved to the material step (M).
- **Why here:** the doorway blend (fix 3) only fixes the *switch*. Indoors, the level still came from the game's per-room setting, which is 0 in places with whole glass walls. Before the headset, because ear-pro amplifies ambience.
- Current behaviour: [DESIGN.md](DESIGN.md) §3.

---

## Next: measure, then step 6

### 6.0a · Audio-thread block timing (diagnostic)
- **Why it matters:** voices cost the audio thread, not the frame. Each ~20 ms block must finish before the sound card needs it, or you hear crackle. This, not the 196 real-voice slots, is the real ceiling for the voice budget (below). It's also the only way to see crackle caused by other mods' per-source filters.
- **Plan:** time each block from the listener's `OnAudioFilterRead` (no allocations; the Phase 0 "audio-thread health" probe). Log per raid the slowest block and the share of blocks over 50% and 80% of the deadline. Show it on the F9 overlay.
- **Status:** built in **0.6.20** (merged from branch `claude/selfcheck-foreign-patches-block-timing`, with the shared-patch report). **Raid 34:** 6.8% of blocks over 1.5× their length, 0.09% over 2×.
- **What was built:** it times the *gap between* blocks, which counts blocks that ran late. For how close the other blocks came to their deadline, 0.6.20 also records Unity's own audio counters, if the game offers them: audio-counters.txt lists what's there. **Raid 34: the game offers only 2 audio counters (memory, both read 0), so there is no DSP load.** The late-block count is all there is, and the voice budget has to be set from that.

### 6.0b · Ray batch wait on busy CPUs
- **The concern:** probe v2's ray batch shares the worker threads with the game's occlusion rays, Burst jobs and SAIN's vision rays. On fewer cores in a big fight, the batch finishes late and the main thread waits for it in LateUpdate.
- **Measure first:** built in **0.6.20**: the batch wait per frame in timing.csv, and every slow frame in slowframes.csv (with clock time to match FrameDiag). **Raid 34 (Factory, 16 logical cores): average wait 0.001 ms, maximum 0.53 ms, 0% of frames waited, so neither fix below is needed.** Re-check only if it matters on a PC with fewer cores.
- **Possible fixes:**
  - **`9g Ray quality` by core count**, the way the game picks its own tiers: High at 12+ logical cores, Medium at 8–11, Low below 8. The manual setting overrides it.
  - **Next-frame batch:** collect the batch at the start of the next frame (as 0.6.15 did) instead of in the same LateUpdate (0.6.16 moved it so the batch never spans FixedUpdate). This needs a sync point that's safe for physics, e.g. finishing before FixedUpdate runs.
- **Why here:** independent of step 6; it only matters on PCs with fewer cores.

### 6.1 · Portal positioning ← next
- **What:** a sound from another room comes from the doorway, window or stairwell it reaches you through, not through the wall (solutions doc 2.1).
- **First build, measure only** (behind a switch, off): **built in 0.6.22** (`Log portal routes`, routes.csv): for each bot gunshot, which opening and route it would take and how far. Not raided yet. Then move the sound there, using the design in solutions doc 2.1 (virtual position from the opening and path length, hysteresis on route choice, blend in by propagation score).
- **Covers:**
  - bot gunshots heard on both sides of a floor (user, 8 Nov: "I can hear bot gunshots underground and above ground");
  - bot gunshots changing when *you* cross a threshold.
- **Why first in step 6:** it adds no voices (it moves the one the game already plays), so it doesn't wait for the voice budget. Everything else in step 6 needs the correct source position first.

### M · Realistic material acoustics
- **What:** research how each surface type actually behaves (absorption and scattering by frequency, transmission loss, resonance of thin metal and glass) and replace the rough tables in fix 6 and the room measure. Includes 4b phase 3 (roof material). User idea, raid 9; raid 13: "still some tweaking to do on how the materials behave".
- **Retune afterwards**, because sound propagation will change (user, 7 Nov):
  - 9l's rain floor;
  - 9k's exit credits (0.35 up / 0.05 away);
  - the roof rain reference (0.85 per upward path);
  - the roof rain's ×0.7 dulling per reflection (0.6.10);
  - the echo levels.
- **Why here:** before 6.2, whose through-wall loss depends on material, so that loss is built once on real numbers. It builds on the material tags fixes 6 and 3c already read. The research itself is desk work and can happen any time before then.

### 6.2 · How much gets through walls, by material
- **What** (solutions doc 3.1 and 3.4):
  - a material-based through-wall loss for remote and distant sounds;
  - a through-wall layer for gunshots and explosions within ~60 m: the deep thump, with its cutoff as a slider (see the tuning note under the voice budget).
- **Before it,** the voice budget's two changes: reserve 16 → 32, and the mod's own non-echo voices counted in the cap.
- **Why here:** needs 6.1's positions, the material tables (M) and the budget changes. Adds up to 4 voices at once.

### 6.3 · Echoes for bot gunshots and grenades
- **What:** bot echoes under the voice budget (below): per shooter, not per round; an audibility gate; one loudest-first queue with a ceiling; two tiers.
- **Grenades and other explosions** (user, 8 Nov 2026; raid 35: three grenades in a stairwell got only the game's own sound while your gunshots there got echoes): a few echoes per explosion, through the same queue. Explosions are rare and loud, so they pass the audibility gate and cost few voices. They must play on a mixer that Bring Back Concussion's cut-off reaches, as gun echoes do, so they duck with everything else. Needs finding the explosion's play path in the game code.
- **Why here:** needs 6.1's source positions and 6.0a's measured ceiling. It's the one addition that grows with the size of the fight.

---

## After step 6

### W · Weather and acoustics research
- **Why:** in rain the user hears less gunshot echo and less open-air sound (raid 19). Logs show the echo levels unchanged (dry −22.1 dB, medium rain −22.4, heavy −24.9, the last from more far echoes). So it's masking by the rain bed, which fix 8 now also brings indoors.
- **Research how real weather changes what you hear,** then decide what steps 4 (echoes) and 4b should do:
  - rain noise level by intensity, and how much it masks;
  - humidity and temperature: air absorption changes (high humidity absorbs *less* high frequency at normal temperatures);
  - wet surfaces: water fills pores, so reflections get slightly stronger and soil and grass absorb less;
  - wind: bends sound with height, and gusts make it waver;
  - drops hitting nearby surfaces (roofs, cars, puddles) as sound sources.
- **When:** the research can happen any time. Its echo-level changes go after the material retune (M) and before the headset, so echo levels are tuned once.

### 5 · Headset simulation (moved after step 6)
- **What:** bring the headset simulation into the game, then tune it. The DSP itself (`RealSoundSimulation.Dsp`, with its bench) already exists and can keep improving offline meanwhile.
- **Why here** (user, 8 Nov 2026: "shouldnt headsets come after step 6?"): electronic ear-pro reacts to everything loud around you:
  - your own echoes (4);
  - bot gunshots moved to the openings (6.1);
  - through-wall thumps (6.2);
  - bot echoes (6.3);
  - the material and weather retunes.

  After all of them, it only needs tuning once. Then the 4b indoor/outdoor mix is re-checked through it, because ear-pro amplifies ambience.

### 6b · SEB recordings on full-auto guns
- **Problem:** setting `0 Gun sounds` can use Sound Engine Backport's EFT 1.0 recordings, but they're single shots, and the game loops one Body recording while the trigger is held. So full-auto guns stay on SPT (`0a` off). The user runs SEB on single fire and SPT on full auto (user, 7 Nov).
- **Fix:** while the trigger is held, play one SEB single-shot take per round fired (from `FireBullet`, at the gun's fire rate, with varied takes and a tail on release) instead of the loop.
- **When:** independent of the environment work; any time before step 7, which judges every gun's recordings.

### C · Supersonic crack by calibre and velocity
- **Today:** the game has four crack clips (9 mm, 5.45, 7.62, shotgun), played at the shooter's position and delayed like the report. Solutions doc 2.9 rebuilds where and when they play; this item is the sound itself.
- **Reference:** Garand Thumb's video of many guns and rounds fired over the camera, a side-by-side of how each calibre's crack sounds at a fixed miss distance (user, 8 Nov 2026). It's a listening reference and a check on the mapping, not a source of clips: its audio is compressed and it's his, so nothing from it ships.
- **How the crack should vary,** per round, from the ammo's speed and the bullet's diameter and length:
  - **Speed:** a faster round gives a sharper, shorter N-wave; subsonic rounds give none. Just above Mach 1 the crack gets much longer and weaker, so speed matters most for rounds near the speed of sound.
  - **Size:** a wider bullet gives a longer and stronger crack. In Whitham's theory, the N-wave's duration and peak pressure both grow with the diameter, while a longer bullet slightly shortens and weakens it (length^-1/4). (Corrected 8 Nov 2026 from "bigger, longer = longer, lower".)
  - **Miss distance:** level falls about as b^-3/4; duration grows about as b^1/4.
- **Two ways to build it, in order:**
  1. pitch-shift, shorten and level the four existing clips by those rules (cheap, keeps BSG's recordings);
  2. synthesise the N-wave itself (a few ms of signal).

  Either way it's one voice per passing round, the same as now.
- **When:** after 2.9 (where and when the crack plays comes first); before step 7, since it's part of how every gun sounds.

### 7 · Gun audio overhaul, every gun (last, long)
- **What** (user, 6 Nov): go through every weapon's sound banks: body, tail, silenced, near/mid/far tiers, indoor/outdoor sets. Make each one sound right and consistent:
  - loudness across guns and calibres;
  - suppressed against unsuppressed;
  - near/far and indoor/outdoor recordings that match;
  - full-auto loops and tails.
- **Needs** a way to measure the recordings' real loudness. The game keeps them compressed, so measure the output while they play.
- **Why last:** after the environment work (4b–6) and the headset, so every gun is judged in the finished acoustics. It changes guns' loudness, so the headset's compressor thresholds get a final check afterwards.

### E · Final echo tuning and optimising (last)
- **What** (user, 8 Nov 2026): once everything that makes echoes exists, tune it all together:
  - how many echoes each sound gets (own shots, bots, grenades);
  - how long the tails ring;
  - optimising voices: an echo holds its voice for the whole 2.5–5 s recording though it is too quiet to hear long before that, so end quiet ones early with a short fade (raid 35: 84 echoes from about 3.5 s of your own firing filled all 96 voices).
- **Test on Factory and Labs.** Small, hard, enclosed maps will have the most echoes going at any one time (user, 8 Nov 2026).
- **Why last:** echo amounts and tail lengths depend on everything before it, so they only need tuning once.

---

## Any time
- **Distant obstruction:** nothing past ~250 m is muffled by terrain or buildings. Independent; uses different, long rays.
- **Server settings** (phase 1 of the solutions doc). Independent.

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
     **1 and 2 built in 0.6.4** (user, 7 Nov: "Yes, build 1 and 2"). Raid 25: the build-up is 3–5× lower, but the direction is wrong deep inside (see 4b).
     **4b. Built in 0.6.5** (user: "build 0.6.5"; not raided yet). After raid 25: (a) **leak fix:** rays pass up through one-sided floor meshes from below (back faces aren't hit), so paths that fall through a hole in the 1F floor to the map's catch plane 48 m below come back up through the building as fake escapes. Cast with back faces on, reflect off them, and give the roof test after every escape the same treatment. Exit faces of glass we just passed through are skipped, so a window isn't counted twice. (b) **Direction weighted by what you hear:** the bounced/direct part keeps its earliest-arrival direction, but its focus is scaled by how much of the level it is and by how many paths agree (one path can't pin the direction any more). The build-up part gets a direction of its own: towards where the escaping paths left the building (each escape's last contact point: the window glass or frame it went through, or the last surface it came off), collected over ~3 s, and never fully focused (a diffuse field surrounds you). Deep inside, the rain then comes softly from the openings' side and wide, instead of pinned to one wall.
     1. **Build-up from the paths that get out** (fixes the loudness): continue the bounce-by-bounce fade that the escaping paths actually show, instead of one room-wide formula, and trace further (3 → 8 bounces) so less is estimated. Needs enough escaping paths, so it ships with 2.
     2. **Guided rays + remembered openings + smoothing by confidence** (fixes the in and out). About half the rays go where outside was found last time, each weighted down so totals stay right (user: "what if we weighted the rays?"). Openings once found are kept and re-checked every sweep: the user's "fixed rays" idea, fixed onto real openings instead of fixed directions, which would leave permanent gaps between them (the 0.5.x cutting in and out). The fewer paths escape, the longer the level is averaged.
     3. **Ray quality setting — built in 0.6.6** (`9g`, Low / Medium / High / Ultra; user idea; raid 25 measured 0.6.4 at 0.153 ms per frame at ~107 fps, 16.3 ms per second, the same as 0.6.2): Low / Medium / High / Ultra = rays per second (now ~2200/s, 16 ms per second of game time). Noise falls with the square root of the ray count, so Ultra (4×) halves it at ~4× the cost.
     **5. Built in 0.6.7** (after raid 26; user: "fix b I agree but if there is roof rain on the floor above you would hear that coming down the stair case"):
       a. **Direction from where the sound arrives, not where it left:** the build-up's direction from the FIRST leg of every escaping path (deep ones too), energy-weighted over ~3 s: by reciprocity that is the direction the outside's energy arrives from at your position (the acoustic intensity), e.g. through your room's door towards the open part of the building. 0.6.5 used each escape's last contact point, mostly a floor spot (exit elevation −40 to −75°), which wandered. Plus a `Direction strength` setting (how pointed vs enveloping a given focus sounds; user: "maybe it was better when sounds from openings slid to 100%"), and the overlay shows the focus actually applied instead of the raw near-part value.
       b. **Roof rain from how much roof you can see:** probe v2's upward paths whose first hit is the top roof (nothing above it), cosine-weighted and smoothed, instead of the old probe's one-ray floors-above count, which flipped 0→1→0 at the car dealer's mezzanine edge (roof rain 30% → 100% → 30% in 0.5 s). Under a double-height showroom: full; under the mezzanine: partly (you still see the high roof beside it). Built as roof rain *traced* from the roof (paths that reach the top roof's underside, any number of bounces), so the floor below hears it down the stairwell, from the stairwell's direction.
     **6. Built in 0.6.8** (after raid 27): (a) in `9c` MetaHrtf mode, switch off Meta's own reflections and reverb send on the beds and the roof rain (parameters 5 and 11), which leaked rain at a spot where the level was 0; (b) let the outdoor level go down to −60 dB (it stopped at −40); (c) log the game's indoor rain player every 0.25 s (playing, virtual, the game's volume, ours, blend, spread, clip) to find the car dealer on/off.
     4. **Exit points + distant sounds through openings — built in 0.6.8** (`9j`, `9k`; user: "build .6.8 and give me a toggle for Exit points and the ambient gunfire mod through openings"). Was on hold until 0.6.5 had been raided (user, 7 Nov: "maybe we hold on two until after testing .6.5"; don't forget it). Raid 26 tested 0.6.5; distant gunfire still through the walls (tag 2), so this is next after 5a, whose arrival directions it reuses. 0.6.5 already points the build-up towards each escape's last contact point, a first step; the full version finds the exact point where the path leaves cover (step 6; user, raids 23–24): find where each escaping path leaves the building (halve the last leg, test for sky), so the rain comes from the window or door. Then SPTBattleAmbience's distant gunfire (`BetterAudio.PlayAtPoint`, 450–600 m, off the baked map) is re-aimed through those openings and set by the probe's level when you're indoors, instead of through the walls from its true direction. **Own switch** (user: some people won't want it applied to the ambient gunfire mod).
5. **Then one change per build**, in the order of the table at the top (reordered 8 Nov 2026; this list used to be: roof holes, phase 3 (roof material), step 6, weather research, material research, the Tarkov/3D-audio review, step 5, step 7).

## Voice budget for step 6 (8 Nov 2026, design; not built)

**Raid 35 (8 Nov 2026) adds three facts.** (a) Your own firing alone can fill all 96 echo voices, so bot echoes need their own share and must never come out of yours. (b) The audio thread did not notice: the 5 s window holding that peak had 15 blocks over 1.5× and 0 over 2×, the same as a quiet window (17 and 0). One raid, 16 logical cores. (c) The game's gun voices are a separate pool from ours, and ours run at low priority (220), so echoes running out drop echoes, not gunshots. **Every echo type has to count against the one headroom cap**, or own + bot + explosion echoes could add up to more voices than Unity has.

Step 6 adds sounds the game does not play today. Portal positioning adds none (it moves the voice the game already plays). The through-wall layer adds one voice per qualifying shot. Bot echoes are the one that can run away, because they scale with the fight and not with your trigger finger. The numbers the logs already give:

| Measure | Value | Source |
|---|---|---|
| Real voices Unity plays at once | ~196 | summary.txt audio line (`realVoices`) |
| Most the game itself had playing | 40, in a 157-source fight | raid 31 |
| Own-echo pool | 96, capped at real voices − game sources playing − 16 | 0.6.14, 0.6.16 |
| Echoes stolen / dropped by the cap | 0 / never engaged | raid 32 |

So about 140 voices of headroom after the game's worst case and the 16 reserve. Per-round echoes for bots would not fit it: 10 bots at 5 rounds/s between them, 4 echoes each, 1.5 s per echo, keep ~30 voices busy on average and peak far higher; 20 bots on full auto blow through 140 in a second. The budget makes the worst case a number we choose:

1. **Per shooter, not per round.** As fix 6 already does for your own full-auto loops (one set of echoes runs with the loop, started from the matching point in the recording), a firing bot gets one or two echo voices however fast it fires.
2. **Audibility gate.** Only shots whose arriving level after occlusion and distance is above a threshold get echoes at all. Most shooters in a big fight fail it.
3. **One queue, loudest first, hard ceiling.** All extra voices go through one queue ordered by arriving level, with a fixed cap for bot echoes (start at 24). At the cap the quietest candidate is dropped before it starts, as the headroom cap does for own echoes now.
4. **Two tiers.** Shooters that pass the gate but miss the ceiling get the zero-voice treatment: their existing voice's reverb send raised by the listener's room measure, as fix 5 does for your own shots. Only the top few get discrete echoes.
5. **Fixed allocation order:** through-wall layer (4 at once, gunshots and explosions within ~60 m) first, then own echoes, then explosion echoes, then bot echoes. Your own shots never lose a voice to a bot's.

**Through-wall tuning note (user, 8 Nov 2026):** for the deep thump/thud of the through-wall layer, a low-pass around **50 Hz** sounds really good on the user's PC and headset. Build it as a slider (through-wall cutoff Hz, default 50) rather than a constant: the right value depends on the headset and on the wall. Explosions keep more below the cutoff than gunshots (low frequencies pass walls more), so the explosion copy should sit at or above that default and the gunshot copy below it, both on the same slider scale.

Worst case under the budget: 8 audible shooters × 2 + 4 through-wall + the own-echo cap stays well under 60 of the ~140. CPU scales with voices too (HRTF and filters run per voice), so the bounded version is a few percent of a core and the unbounded one is what would hurt. `sources.csv` already logs real and virtual voices each scan, and the summary counts drops at the cap, so the first raid shows whether the budget ever engages. Settings to add: bot echo ceiling, audibility threshold, and a switch for the zero-voice tier, all live in F12.

**What the cap does not count (8 Nov 2026).** The headroom cap subtracts only sources in occlusion containers (players and objects). The beds, rain, wind, room tone, roof rain, your body sounds and anything another mod plays are not counted; they live in the fixed reserve of 16. Today that is enough. With fix 8's opening copies (up to 3 per bed), the old mods' breathing and ringing, and SEB's extra buses, the uncounted load reaches 20–25 voices, more than the reserve, and the cap would hand out voices Unity does not have (it then steals the quietest). The 40 "playing" peak in raid 31 did count everything, but from a scan every 15 s (a sample, not a peak), and that scan is off by default since 0.6.15, so nothing counts the beds now. Two changes:

1. **Reserve 16 → 32**, and name what lives in it in the code comment: beds, rain, wind, room tone, roof rain, body sounds, other mods.
2. **Count the mod's own non-echo voices** in the cap: the opening copies, the moved beds, the outdoor tails. The mod starts every one of them, so no scene scan is needed.

What scales and what does not: beds and body sounds are constant (8–12, up to ~25 with everything on); footsteps, gear and voices grow slowly with nearby characters; game gunshots grow with the fight; per-round bot echoes would grow with fight × fire rate, which is the only row that explodes and the one the rules above are for. Per-round gun playback (6b, SEB takes on full auto) adds about one voice per gun while it is bursting, provided the previous take fades when the next starts and overlap is capped at 2–3 takes; echoes stay per burst, never per round.

**Measure before raising anything.** Three or four raids on Streets, Factory and Labs with bots (Factory and Labs should have the most echoes at once), `Source scan interval s` at 2 (not 15) so `sources.csv` catches peaks, plus the listener block timing from the Phase 0 list, because the real ceiling is the audio thread (HRTF and filters per voice inside each 20 ms block), not the 196 slots. Then set the pool (96), the reserve and the bot share from the measured peak and the block slack rather than from the arithmetic above. Caveat (8 Nov 2026 review): the source scan costs ~56 ms per run (raid 31, FrameDiag), so at 2 s it puts a hitch into the raid every 2 s. Use it only on raids that measure voices, not on raids that compare frame times. The block timing is its own roadmap row (audio-thread block timing, above), and so is the worker-thread wait.

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
