# Changelog

Newest first. Each version: what changed, and whether it has been run in game.

## 0.6.11 — 7 Nov 2026 (inside shot, outside tail: forward-lobe muzzle measure and the outdoor tail from the opening)
**Run in game:** not yet. From a build request for raid 29 tag 1 (barrel out of a window, the whole shot sounded indoor; user: "inside shot, outside tail"). ownshots.csv at 122–130 s: muzzle 0.49 m from the head, v2 muzzle room 0.72–0.98, aim closure 0.11–0.35, indoor weight 0.61–0.66, after 3j 0.80–0.85.
- **New `3k Muzzle lobe`** (1; 0 = the sphere as before): the muzzle's room measure (probe v2's 64 directions, and the 32-ray one when 9a is off) weights each direction by ((1 + cos to the aim) / 2)^k, room, roof and size alike. Out of a window the muzzle reads mostly outdoor although half its sphere looks back into the room; firing into a room from a doorway reads more indoor. The head measure stays a sphere. ownshots.csv adds `muzzleRoomSphere` and `v2MuzzleRoomSphere`; the summary has MUZZLE LOBE (mean sphere minus lobe).
- **New `3m Outdoor tail from the opening`** (on) and **`3l` trim** (0 dB): when 3c mixes the shot mostly indoor (> 0.5 after 3j) and the aim cone is open, the gun's outdoor Tail recording also plays from the opening the shot goes out of: the aim cone's first ray (forward first) that leaves cover before its wall; through glass at the pane (9d loss, 1.5 kHz), bars at the grate (−1 dB, 12 kHz), else the exit point; 3 m along the aim if none is found. Delay = distance / 343 m/s; level = shot × open share × indoor weight × 3l. Additive: 3c's two body slots are unchanged. On fix 6's voices (needs 6 on), one per trigger pull, not counted in 6b.
- **Differs from the request:** it also plays on the tail at the end of a burst, not only on "shot" samples. A gun that can fire full-auto plays every trigger pull (single shots too) as loop + tail, never as a "shot"; tag 1 was an AK-74, so a shot-only tail would never have played there. Never on the loop.
- **ownshots.csv** rows are written when the shot call finishes (same rows as before), so they can carry `tailPlayed, tailLevelDb, tailDelayMs, tailAz, tailThrough` (or why it didn't play). Summary: OUTDOOR TAIL FROM THE OPENING (played, mean level and delay, reasons not played).
- **Cost:** the opening search runs at most 5 times a second (up to ~90 short casts).

## 0.6.10 — 7 Nov 2026 (roof rain: its own directional mode, muffled by its paths)
**Run in game:** not yet. User: "Yes, build it" (after the panner review: keep the panner for the beds; try HRTF only where up/down matters).
- **New `9m Roof rain directional mode`** (UnityPanner): the roof rain's own mode; `9c` now sets only the outdoor sounds (its key is unchanged, so your value stays). Try `9m` = MetaHrtf with `9c` on the panner.
- **Roof rain muffled by its paths:** the probe records how bright each roof path is when it reaches the roof (the dullest surface it passed or reflected off, x0.7 per reflection) and the roof rain gets that as a low-pass, on a filter the plugin adds only where the game has none. Straight up stays 22 kHz; down a stair shaft after three bounces ~5 kHz. Until now roof rain had no muffling at all, only level, so rain down a shaft sounded like rain overhead, just quieter. The x0.7 per bounce is a placeholder for the materials overhaul.
- **Logs:** probe2.csv `roofCutoffHz`; rain2.csv `lowpassHz`; the summary shows each group's mode.
- **Before the next raid (user's side):** headset surround / "Superhuman Hearing" and Windows spatial sound (Sonic, Atmos) off, so only the plugin spatialises.

## 0.6.9 — 7 Nov 2026 (raid 28 fixes: doubled gunshots, roof rain, stairwell wind and gunfire, rain floor)
**Run in game:** not yet. The exposure probe stays on (user). Two reviews of raid 28's logs agreed on the causes; the user said "build" for all but turning the exposure probe off.
- **Echo voices (fix 6):** pool 48 (was 24); when full, the voice that ends soonest is reused, not the one that started first. Raid 28: every sample was a full-auto loop plus a tail, the pool was full, and 274 echoes were cut off mid-sound (256 by tail echoes) - the likeliest cause of the doubled/stuttering shot.
- **New `6f Early echo trim dB`** (0 = no change): added to echoes under 25 ms. 414 of 1,216 echoes arrived that early, some at −4.5 dB 9 ms after the shot. Judge by ear after the voice fix.
- **New `3j Blend steepness`** (1): fix 3c leaves an even indoor/outdoor mix faster (0.6 → 0.79, 0.4 → 0.21); same crossover. 132 of 202 samples were between 0.25 and 0.75. ownshots.csv gains `weightUsed`.
- **Roof rain (9i):** the roof level is now per upward path (first leg > 30° up). Under a roof right overhead in a room with big openings it read 0.42 ("half a roof") because most paths escaped sideways first; the roof radiates the same either way. probe2.csv keeps the old measure as `roofLevelAll`.
- **Roof rain under two floors:** 0 when the exposure probe's up-rays count two or more floors above (held 0.5 s), whatever the roof test says (tag 4: 0.68 under two floors). One floor above stays the probe's call (raid 26's mezzanine edge).
- **Roof rain and the indoor fade:** the plugin reads which mixer volumes the game's indoor-layer fade moves; if the rain's is one, roof rain is compensated so openings around you don't turn the roof down (tags 2–3: 0.65 × 0.53–0.68). Summary: "indoor-layer mixer volumes"; rain2.csv `fadeComp`.
- **Rain crossfades (fix 7):** a crossfade to the same clip is a volume ramp instead (tag 2: six room changes in 3 s each crossfaded the clip into a second copy of itself, dipping to 0.07). Correction to the raid 28 review: fix 7 didn't cause these, the game did. Summary counts them.
- **New `8g Indoor wind follows openings`** (on): room tones whose clip name contains "wind" follow how much of the outside reaches you (full above −10 dB, down to −40 dB) instead of how enclosed you are (last tag: wind four floors down a stairwell). New roomtone.csv logs every room tone and its clip, and the summary lists them, so the "wind" match can be checked.
- **New `9l Rain through the building dB`** (−32; −60 = off): deep inside, the outdoor sound never drops below this, muffled to ~350 Hz, faded in by how enclosed you are (tags 1 and 4: "not enough of the interior"; a building in rain is never silent).
- **9k:** every escaping path counts - side openings by how they face the battle (as before), openings to the sky above at 0.35 from any direction, side openings facing away at 0.05 (last tag: the only way out was up the stair shaft, so it found nothing and the direction jumped). distant.csv gains `heardEl, outVolume, occlFactor, mixer, spatialBlend, spatialize, reverbZoneMix, playing`, to find what carried the gunfire heard at −45 dB.
- **probe2.csv `levelDb`** floors at −60 dB like the applied level (was −40).
- **Self-check:** 8 new targets.
- **Materials overhaul (user, 7 Nov):** sound propagation will change with realistic materials. The new constants (roof reference, 9k's 0.35/0.05, the rain floor) are geometry or settings, not per material, and should be retuned after it.

## 0.6.8 — 7 Nov 2026 (exit points, rain from openings, distant gunfire through openings; Meta leak fixed)
**Run in game:** not yet (installed 11:51). User: "build .6.8 and give me a toggle for Exit points and the ambient gunfire mod through openings".
- **Exit points:** for escaping paths while you're inside (~200 a second), where the escaping leg leaves cover (roof tests along it, refined): the window or door it went out of.
- **`9j Rain from openings`** (on): indoors, the outdoor sound's direction points at the exit points instead of where the sound first reaches you.
- **`9k Distant gunfire through openings`** (on, needs fix 4): when you're inside, the ambient gunfire mod's distant battles (off-map, ≥ 80 m) are moved to come through the openings that face them, at the level that gets in (√ of the probe's transmission from that direction), blended with how enclosed you are. Replaces fix 4's doorway muffling for them. distant.csv.
- **Meta HRTF leak fixed:** in `9c` MetaHrtf mode, Meta's own reflections and reverb send are off on the moved sources (raid 27 tag 1: rain heard where the level was 0).
- **Level floor −60 dB** (was −40).
- **rain2.csv:** the game's indoor rain player every 0.25 s, to find the car dealer roof rain on/off (raid 27 tags 2–5).
- 4 new self-check targets (112).

## 0.6.7 — 7 Nov 2026 (direction from where the sound arrives; roof rain traced from the roof)
**Run in game:** raid 27 (11:25, rain). Meta HRTF leaks rain where the level is 0 (its own reverb send); car dealer roof rain on/off by tag with our factor steady; stairwell faint rain from the build-up. Fix 9 only.
- **Raid 26 (0.6.6):** "overall an improvement", garage good, deep rooms much quieter. Still: rain a little "through the walls" deep inside (wind too), "maybe it was better when sounds from openings slid to 100%"; car dealer roof rain stopping or getting quieter suddenly; overlay "focus near" jumping 0–0.5.
- **Build-up direction from where the sound arrives:** the first leg of every escaping path (deep ones ×2), energy-weighted over ~3 s, instead of where it left the building (0.6.5: mostly floor spots 40–75° below you, from 3–8 escapes, so it wandered).
- **New `9h Direction strength`** (1 = as measured, up to 3): how pointed the outdoor sound and roof rain are indoors.
- **Roof rain traced from the roof (`9i`, on):** the top roof's underside is a sound source; paths that reach it give the level (full right under it, partly under a mezzanine, down the stairwell from the floor below) and the direction. The game's indoor rain player is made directional like the outdoor beds. User: "if there is roof rain on the floor above you would hear that coming down the staircase". Replaces the one-ray floor count that flipped 30% → 100% → 30% at the car dealer's mezzanine edge.
- Overlay: the focus actually applied (was the raw near-part value, which jumped) and the roof rain level. Logs: probe2.csv `focusExit/exitAz/exitEl/exitCount` renamed `focusBuildUp/buildUpAz/buildUpEl/buildUpCount`, new `roofLevel, roofAz, roofEl, roofFocus, roofCount`.
- `9c` stays UnityPanner (user, raid 26: MetaHrtf sounds worse in the Nakatani hallway).

## 0.6.6 — 7 Nov 2026 (probe v2 ray quality setting)
**Run in game:** raid 26 (10:52, rain, `9g` High). "Overall an improvement" → 0.6.7. At the default (Medium) it is exactly 0.6.5.
- **New `9g Ray quality`** (user idea): Low / Medium / High / Ultra = 320 / 640 / 1280 / 2560 paths a second, averaged over 1 / 2 / 4 / 8 sweeps, so the window stays ~0.6 s and more paths really mean less noise (Ultra: half the noise of Medium at ~4× the cost, about 0.6 ms per frame at 107 fps). Changes live. DESIGN.md §7.
- Room for up to 1024 paths in flight (was 256) and a per-frame start cap that grows with the rate, so Ultra isn't throttled at 60 fps.
- Summary and settings line show the quality; COST line renamed "acoustic probe v2 (batched, guided, rays by 9g)".

## 0.6.5 — 7 Nov 2026 (probe v2: leak through floors fixed, direction by what you hear)
**Run in game:** not yet (installed 10:46 as part of 0.6.6). Affects fix 9 (ambience) and, slightly, 9a.
- **Raid 25 (0.6.4):** the measured build-up was 3–5× lower than the old formula (−18 dB at mark 1, was −14). But deep in the Capital the rain came from both sides and from behind. No path really got out there: the only "escapes" fell through a hole in the ground floor to the map's catch plane 48 m below and came back up through the building (raycasts don't hit the back of one-sided floors). With focus ≈ 1, one or two of them set the direction alone.
- **Leak fix:** every cast hits back faces too, and a back face reflects like a front face. The far side of a pane or object a path has just passed through is skipped (no second loss). Every escape gets the roof test, not only level and downward legs.
- **Direction by what you hear:** the direct/bounced part counts by its share of the level and only firmly when several paths agree; the build-up gets its own direction, towards where escaping paths left the building over ~3 s, never fully focused. Deep inside: wide, leaning towards the openings.
- Logs: probe2.csv `focusEarly, focusExit, exitAz, exitEl, exitCount`; paths.csv `exitAz, exitEl`; summary counts back faces hit, far sides skipped, upward escapes rejected. Overlay: focus near / exits.
- Expect: deep rooms quieter than 0.6.4 (part of their build-up came from the fake escapes), the rain direction steadier and wider.

## 0.6.4 — 7 Nov 2026 (probe v2: measured build-up, guided rays, smoothing by confidence)
**Run in game:** raid 25 (09:42, rain, AI on). Build-up 3–5× lower; direction wrong deep inside (fake escapes through the floor) → 0.6.5. Affects fix 9 (ambience) and, slightly, 9a (gunshot room readings).
- **Raid 24 (0.6.2):** sound from inside "improved"; outdoor ambience still too loud far into the building. At mark 1 (Capital) the room build-up was 88% of the level. Lowering `9e` helped, but what was left came from 2–5 paths and jumped in and out.
- **Build-up measured, not assumed.** Half the paths keep bouncing after their 3rd reflection, up to 8, to measure where the remaining energy goes in this building (in energy: an escape through closed glass now counts at −22 dB, not like an open door). Only the energy still bouncing after 8 uses the room formula. Smoothed over ~3 s. 0.6.3's value is logged as `tailOld` for comparison.
- **Guided rays (`9f`, on):** 64 of every 192 paths aim at the openings already found (windows, doors), each counted as p_uniform / p_mixture so totals stay right. Openings are remembered (fade over ~2 s unless found again) and re-checked every sweep. The user's "weighted rays" idea, and "fixed rays" done as rays fixed on real openings.
- **Smoothing by confidence:** fix 9 averages the level for 0.35 s when many paths get out, up to 2.5 s when few do; the direction over at least 1 s.
- Ray view: 192 paths, guided ones fainter, colour = where the path finally ended. probe2.csv and paths.csv: new columns (DIAGNOSTICS.md). Overlay: paths out, openings, error, smoothing time.
- Cost: more rays deep inside (~4,000/s expected, was ~2,200). Next build: a ray quality setting.

## 0.6.3 — 7 Nov 2026 (setting 0: gun sounds from SPT or SEB)
**Run in game:** not yet. With the default (`0 Gun sounds` = SPT) nothing sounds different from 0.6.2.
- **New setting 0, "Gun sounds"**, at the top of F12 (section "0. Gun sounds"): SPT (default) or SEB, Sound Engine Backport's EFT 1.0 recordings, without SEB's engine. Switches at the next trigger pull, never mid-burst. FIXES.md "Setting 0".
- **Why:** compared on 7 Nov, SEB's gun banks hold the same recordings as SPT's plus 6 to 48 single-shot takes per sound (AK-74 295 single-shot clips against SPT's 9, M4A1 306 against 27). The SPT recordings they share are the same audio. An AKM single shot measured within 0.5 dB per band of SPT's loop-start + tail; the gain is variety, not quality.
- **`0a SEB on full-auto guns`** (off): the game loops one Body recording while the trigger is held on any gun that can fire full-auto. SEB's are single shots, so those guns stay SPT until playback is patched.
- **New tool `tools/build_seb_sounds.py`:** copies SEB's 39 gun bank bundles and the 28 they reference with new internal names (Unity can't load two bundles with the same internal name), into `BepInEx/plugins/FFloridaMann-RealSoundSimulation/seb_sounds/` with `index.txt`. Not in git.
- **Logs:** new `gunsounds.csv`; a GUN SOUNDS line in the summary; setting 0 in settings.txt; 11 new self-check targets.
- SEB's server mod is no longer installed (moved to `G:\SPT\_removed_mods`), so the game loads SPT's own banks and the switch decides.

## 0.6.2 — 6 Nov 2026 (bug fix: rays leaked through solid low-detail shapes)
**Run in game:** not yet (installed 22:16).
- **Solid objects on low-detail layers now block completely.** 0.6.0 gave them a small leak, done by chance: 5% of rays passed **at full strength**. The Capital has a whole-floor low-detail collider (`Capital_Indoor_1F_COLLIDER`, no material tag) in front of its real walls. In raid 23 at mark 1, 10 of the 11 paths that "reached outside" had gone through it, through the back and side walls and once down through the floor to the ground 48 m below. That put the rain behind you and made the level jump whenever a lucky path got through. Sound through walls belongs to step 6 (by material and thickness), not to this probe.

## 0.6.1 — 6 Nov 2026 (probe v2: steadier, direction from the earliest sound)
**Run in game:** raid 23 (22:08, rain). "Sounds better." Still: at mark 1 (Capital) the rain came from the back wall; distant gunfire (SPTBattleAmbience) through the building walls; in and out in the hallway as fewer paths reach outside. Cause of the first: the bug fixed in 0.6.2.
Raid 22 (0.6.0, 9 and 9a on): "really good", garage "big improvement". Still: in the back of the Capital building rain came through the concrete back and side walls, some ambient sounds were placed oddly, and the audio changed in and out a little. Logs: at the Capital mark only 6 of 128 paths reached outside; 85% of the level was room build-up, spread all round (focus 0.07). The direction jumped a median 36° per 0.125 s (often 140°) and the level 0.2–2 dB. Focus was under 0.3 for 94% of the raid. v2 cost 21.8 ms/s.
- **Direction from the earliest sound** (the precedence effect: you place a sound where its first arrival comes from). Escaping paths weigh by e^(−path length / 20 m) for direction and focus. The build-up no longer widens the sound, so it stays on the openings' side instead of coming through the back walls.
- **Direction turns over ~1 s**; the level still follows in 0.35 s.
- **Less noise:** the last two sweeps (256 paths) are aggregated, and 640 paths/s instead of 512.
- **Build-up leak rate from late bounces** (legs after 2+ reflections): the energy still bouncing is deep in the space, so back rooms come out quieter; a single hard room (the garage) the same.
- **Cost:** each collider's kind is worked out once; hit labels are only built for F10 dumps (they allocated on every hit).

## 0.6.0 — 6 Nov 2026 (probe v2)
**Run in game:** not yet (installed 21:40, game closed). The new switches 9 and 9a are **off**: with them off, everything sounds as in 0.5.8.
- **New: acoustic probe v2** (diagnostics, on). 128 rays over the whole sphere that bounce like sound until they reach open sky, losing energy by material and passing glass, thin planks and bars at a loss; the rest of a room's ringing is completed with Sabine's rule. Batched on worker threads. Design: AMBIENCE.md §12–13, DESIGN.md §2.6. Raid 21 asked for it: the garage read as underground and quiet rain flammed.
- **New fix 9, "Ambience from probe v2"** (off, test): the outdoor beds' volume (dB) and muffling (Hz) are written directly, bypassing the game's curve (which also cut the lows indoors); the game's own rain and wind sources become directional (no copies, so no flamming); indoor layers and roof rain follow the probe. Settings 9b–9e.
- **New fix 9a, "Gunshots from probe v2"** (off, test): the gunshot indoor/outdoor mix and the 5a/6e hard-room boosts use probe v2's room measure (head and muzzle).
- **New: probe ray view** (F11) draws every probe v2 path in the world.
- **Logs:** new `probe2.csv` and `paths.csv`; extra columns at the end of `marks.csv` and `ownshots.csv`; an overlay line; ACOUSTIC PROBE V2 in the summary.
- User decisions (6 Nov): physical glass (−22 dB, `9d`); set volume and muffling directly; build both probe uses at once with a switch each; make the game's own sounds directional rather than copying them.

## 0.5.8 — 6 Nov 2026 (clean-up)
**Run in game:** raid 21 (21:10, medium rain): "interior transitions sound better". Still: quiet rain deep inside sounds doubled, and the garage reads as deep underground (see DIAGNOSTICS raid 21, ROADMAP probe v2). With the settings you already had, nothing should sound different. Part of the consolidation (ROADMAP.md).
- **Defaults are now the tested setup.** Every raid since raid 10 ran with: every fix on, `2 Obstruction score` ParallelPaths, `3d` Enclosure, `3i` Soft, `8f` HRTF on. The code defaults were "everything off", so a reset or a fresh install would have given an untested setup. Your config file keeps its own values; the section name is unchanged, so nothing was reset. Every fix is still switchable, and all off is still the game's own audio.
- **Removed fix 3h "Ambience through glass".** Fix 8 replaced it in 0.4.2; with fix 8 on it did nothing. Its line stays in the config file, unused (BepInEx keeps unknown entries).
- **Removed dead code:** `AmbienceMix.IndoorFade()` (replaced by `IndoorLayers()` in 0.5.7) and the 0.5.0 opening grouping in the exposure probe (the opening sounds use the 6 fixed sectors since 0.5.2; the grouping was only logged).
- **Setting descriptions corrected:** `Outdoor exposure probe` (runs everywhere, 48 directions, drives fix 8), `3c` (acts everywhere in Enclosure mode), `8` (what it does now), `8b` (no longer needs fix 7), `8d` (how early the indoor sounds fade). No setting was renamed.
- **Code comments** for the exposure probe, fix 8 and the opening sounds now describe what they do now. The history stays in this changelog and AMBIENCE.md.
- **Logs:** `ambience.csv` drops `glassLeak`, and `indoorFade` becomes `indoorLayers`. `exposure.csv` and `marks.csv`: `previewOutdoorValue` becomes `outdoorValue`. `exposure.csv`: `openings, op1Az, op1DistM, op1Weight, op1Glass` become `openSectors`.

## 0.5.7 — 6 Nov 2026
**Run in game:** not yet (installed 19:23). Included in 0.5.8.
- **Exposure is measured continuously.** About 2 directions are re-measured every frame, cycling through all 48 (about 290 rays per second, the same as before). Each re-measure is nudged by a random amount, up to half the ray spacing, and the totals are rebuilt every frame.
  - Raid 20: measuring all 48 at once 8 times a second made the value jump around while walking (0.23 → 0.42 → 0.17 → 0.23 → 0.13 entering the Nakatani hallway), because rays found and lost the windows together. The game's steep volume curve made each jump an audible cut.
  - Now one direction changes at a time, the nudges cover every angle over time, and the cost no longer spikes (up to 12 ms before).
- **Indoor layers follow enclosure** (fix 8). The indoor channel (roof rain, room tone, indoor wind) now follows how enclosed you are, not the game's room map.
  - Raid 20: the garage entrance is an *outdoor* room under a 3.6 m concrete ceiling. The probe read it as very enclosed (13–16% open), which turned the outdoor rain down, but the indoor channel was off. The result was dry and quiet.
  - Now covered spots get roof rain and indoor character, fading out toward open air. 8d sets how early openings around you fade them.

## 0.5.6 — 6 Nov 2026
**Run in game:** raid 20 (19:09, medium rain). "Generally sounds a lot better at thresholds." Still: the outdoor rain cut in and out at hard points entering the Nakatani hallway, and the garage entrance had no roof rain (both addressed in 0.5.7). Includes 0.5.5. COST: source scan 51.5 ms every 15 s (a real hitch); exposure 0.77 ms avg / 12 ms max; total 15.6 ms/s.
- **One continuous measure, one speed** (raid 19: transitions laggy and "like the game is playing different audio loops").
  - Exposure is now measured outdoors too, so the outdoor value no longer jumps at the room boundary (it went 0.55 → 1 in one step at the Capital entrance). It updates 8 times a second (was 4).
  - The outdoor level, the opening sounds' share, the direction cue, the indoor-layer fade and the roof rain all follow it with the same ~0.35 s smoothing. Before, the level took about 2 s to catch up (1 s smoothing at 4 updates/s), the opening sounds dropped out in 0.6 s, and the indoor layers switched instantly.
  - The opening sounds and the direction cue scale with how enclosed you are: full deep inside, fading to nothing in open air. They no longer switch at the room boundary.
- **Roof rain** is set directly on the game's indoor rain player (volume follow, 0.5 s hold), with no crossfade requests.
  - 0.5.4 asked an uninitialised rain player to crossfade at raid start. That crashed and kept retrying, so roof rain only changed on the game's own room-change crossfades and depended on which way you walked (raid 19, tag 3).
  - It no longer needs fix 7.

## 0.5.5 — 6 Nov 2026
**Run in game:** never on its own (built while the game ran; shipped inside 0.5.6).
- **The logging is in the COST section too**, to check whether it causes hitches (user suspicion). New rows:
  - diagnostics per frame: container sampling, overlay, timing;
  - the source scan every 15 s (`FindObjectsOfType`; the likeliest single spike);
  - the ambience/probe rows;
  - saving the log files every 2 s (about 15 files, on the main thread).

  Nested work is counted once. Nothing else changed.

## 0.5.4 — 6 Nov 2026
**Run in game:** raid 19 (18:47, rain). Roof rain crashed at raid start (an uninitialised rain player) and then changed only on room changes, depending on which way you walked. Transitions lagged about 2 s, with layers moving at different speeds. The doubled rain was gone ("less echoing in the rain"). Under a single concrete carport there was no roof rain: the game treats it as outdoors, so its indoor rain player is silent there.
- **New setting 3i Small space rule** (fix 3c): Current (default, the 0.3.1 rule down to 50%), Soft (at least 85%), Off. Raid 18: the Capital's small interior rooms (walls 2–3 m, room 0.72–0.93) got only 65–76% indoor recording because of this rule. Gunshot behaviour is otherwise unchanged; the default keeps the current setup.
- **Opening sounds no longer double up.** Each 3D copy starts at its own random point in the rain/wind loop. Copies started in step with the game's bed summed into a hollow, flanging "doubled" sound (raid 18: "like 2 gunshots at once").
- **Distance counted once.** An opening's weight is now 1 / (1 + distance / 15 m), not 1 / (1 + (distance / 5 m)²). How many rays an opening covers already accounts for distance. Raid 18: a wall of shot-out windows 13 m away counted 13%; now about 50%.
- **Covered outside counts again.** A roofed point the room map calls outdoor counts as outside at half weight, *unless* its roof is the same ceiling you're standing under (the room map's interior patches, raid 17). Raid 18: 0.5.3's strict open-sky rule also rejected the real outside beyond windows, under the building's canopy and overhangs, so shot-out windows changed nothing. `rays.csv` now shows `covered-outside` / `roofed-rejected` and both roof names (`roofOverOutside`, `yourCeiling`).

## 0.5.3 — 6 Nov 2026
**Run in game:** raid 18 (18:20, rain). No errors. Tag 1 (Capital): no more rain from inside, but the rain sounded doubled and distorted (fixed in 0.5.4). Shot-out windows (tags 2–4) and the Nakatani extract windows (tags 6–7) changed almost nothing, because of the strict sky rule and the double distance weighting (fixed in 0.5.4). Stairwell (tag 8): all blocked, correct. COST: 6.3 ms/s total (0.6% of a core); one-off raid-start spikes of 52 ms (window scan) and 138 ms (setup).
- **"Outside" means the game's room map says outdoor *and* there's open sky above** (nothing sound-blocking within 40 m straight up). It applies to the level, the 3D sources and the bisection.
  - Raid 17, inside the Capital building (the user's start area): the room map calls large interior patches outdoor. The probe found "outside" 0.6 m away in about 20 directions, with escalators and indoor walls beyond.
  - 0.5.2's covered check only weakened the 3D sources, not the outdoor level, so rain played loud indoors even with 3D off (8e = 0).
  - It also caused the left-ear bias (tag 1) and the jump from 0.89 to 0.02 when stepping out of the patch (tag 2).
  - Now those points simply aren't outside, and each direction keeps looking until it reaches real sky (past the glass).

- **Cost logging.** The summary gains a COST section: for each part (enclosure sweep, muzzle room, aim cone, echo search, outdoor exposure, ambience mix with opening sounds), its calls per second, average and maximum ms per call, ms per second of game time, and raycasts and room lookups per second. Work done inside another part is counted once. The F9 overlay shows the total ms per second.

## 0.5.2 — 6 Nov 2026
**Run in game:** raid 17 (18:04, rain). No errors; opening voices up to 24. Tag 4, elsewhere: "all round this building sounds okay". In the Capital building, rain was too loud and sounded "inside the building", also with 3D off. The cause was the room map's outdoor patches indoors (fixed in 0.5.3), not the 3D audio.
- **Opening sounds come from 6 fixed directions** (every 60° in world space), like a ring of speakers. Each one's volume follows how open its direction is.
  - Raid 16: the up-to-3 "openings" were re-sorted every quarter-second and their sources swung to new directions, passing through the listener. The user heard rain "directly on top of me, moving", "like the direction is decided once".
  - Sources are now 120° wide and overlap, so the rain sounds like an area, not a point ("comes from one location, not ambiently"). Volume changes between directions are smoothed over about 1 s.
- **"Window" objects:** a "window" collider with no glass tag counts as glass only if a glass surface is actually drawn there (glass material on its own, its parent's or its children's renderers). Otherwise it's an open window. This keeps the Nakatani tower's empty frames open (raid 15), but a glass wall built the same way stays glass (user question: a long glass wall inside a concrete building).
- **"Outside" must have sky above.** Where a direction reaches an outdoor room, a ray goes straight up from just past that point. A roof within 30 m means a covered area (lobby, vestibule, canopy), and that direction counts 35%. Raid 16: rain came "from inside the building" through an entrance area the game's room map calls outdoor. `rays.csv` marks these directions as "covered".

## 0.5.1 — 6 Nov 2026
**Run in game:** raid 16 (17:52, rain). No errors; opening sounds played (up to 12 voices). Hallway "sounds much better". Rain sometimes sounded on top of the listener and moving, sometimes came from inside a building, and came from one point rather than all around (all fixed in 0.5.2). Rain crossfades: none needed holding, no dropouts.
- **Opening sounds crash fixed.** In 0.5.0 the setup added a filter to an object before any audio source, which Unity refuses. Setup failed, no opening sound played (raid 15: "voices max 0"), and fix 8's per-frame update failed from then on, freezing the direction cue.
- **Roof rain no longer flips.**
  - Floors above are counted on five upward rays (straight up and four tilted 20°), and the middle answer is used.
  - A new roof-rain level must hold for 2 s before it's applied.

  Raid 15: one vertical ray kept catching beams, ducts and stairs, so the level jumped between 0, 30% and 100% every half-second ("cutting in and out").
- **Rain crossfades are never interrupted (fix 7).** A crossfade requested while one is running now waits until it finishes. Before, the game, and fix 8's roof rain, restarted the incoming rain loop at a random point mid-fade, which is a jump or click (raid 15, Nakatani hall). The summary counts these as "crossfades held".
- **Exposure:**
  - Where a direction crosses outside is now found to within about 1 m (three halvings between room-map samples). Before, a window 6 m away could read as "outside at 8–18 m" and barely count.
  - A "window" object that isn't tagged as glass counts as an **open** window. Raid 15: the Nakatani tower's window shape (`Nakatani_tower_windows_01_COLLIDER`), where bullets pass through, was counted as glass. The user hears these as open windows.

## 0.5.0 — 6 Nov 2026 (roadmap step 4b, phase 4 + fixes from the rainy raid)
**Run in game:** raid 15 (17:35, rain). The opening sounds crashed at setup, so they never played (fixed in 0.5.1). Roof rain was "better", but it flipped and clicked (fixed in 0.5.1). The Nakatani hall windows read as glass, and too far away (fixed in 0.5.1).
- **Phase 4: sound from the openings** (part of fix 8, setting **8e Sound from openings**, 0.7; 0 = off).
  - Indoors, the open directions are grouped into up to 3 openings. Each gets a 3D copy of the outdoor beds (wind, outdoor rain, day/night background), placed in its direction and routed through the same mixer channel, so fix 8's level and muffling still apply.
  - 70% of the outdoor sound moves to the openings, split between them by strength; the flat bed keeps 30%. Total loudness is unchanged.
  - Glass openings are duller. Without HRTF, openings behind you are also duller. Wide openings sound wide.
  - New **8f HRTF on opening sounds** (off) runs them through the game's 3D audio plugin.
- **Exposure:** 24 directions around you (15° apart, was 12 at 30°). Raid 14: the Nakatani exit hallway's open windows were barely found.
- **F10 now also dumps every exposure direction** to `rays.csv`: wall distance and collider, where it reached outside, the path type, what's in between, and the last room sampled. This is for diagnosing spots like that hallway.
- **Roof rain** (fix 8, with fix 7):
  - full with only the roof above you;
  - **8b** (30%) with one floor above;
  - **none with two or more** (was 30% for any).

  Raid 14 and the user: no roof rain inside tall buildings, or down the Nakatani exit stairs.
- Logs: `ambience.csv` gains openingShare, openingVoices, opening1Az and opening1Gain; `exposure.csv` gains openings and the strongest opening's direction, distance, weight and glass share.

## 0.4.2 — 6 Nov 2026 (roadmap step 4b, phase 2)
**Run in game:** raids 13 and 14 (raid 14 in medium rain: the Nakatani exit hallway's windows were barely found, and roof rain was heard down the underground stairs; both addressed in 0.5.0). Raid 13 (16:28, Ground Zero, no AI). Loaded (self-check 16:25), no errors. Fix 8 A/B tested: "works pretty well". Over 4,584 samples the applied outdoor level jumped by more than 0.15 per 0.1 s only once; the pan cue was active in 626 samples and the behind-dulling in 346. Six outdoor bed sources were found, plus 1 indoor rain player. 274 echo candidates off glass facades and bars. No rain yet, so fix 7 and the roof-rain cut are still untested.
- **New fix 8, "Outdoor sound indoors from openings"** (off by default; needs fix 3 on). Indoors, the exposure measure now sets the outdoor ambience level (volume and muffling), smoothed over about 1 s and never below the room's baked level:
  - open sides and broken windows count fully;
  - intact glass counts by **8c Through glass** (0.45);
  - window bars count 80%.

  It replaces fix 3's doorway fade for the level, which removes the sideways 6% ↔ 95% swings at wide entrances (raid 12). The doorway blend still drives the indoor/outdoor reverb. It also replaces fix 3h.
- **Indoor sounds fade near openings** (8d, 0.6 × exposure). Deep interior rooms keep their full indoor character.
- **Roof rain under floors** (8b, 0.3 when there are floors above you). Only with fix 7 on, because changing the rain volume makes the game re-pick its clip, which without fix 7 can fade the rain to silence.
- **Direction cue** (8a, 0.6). Indoors, the outdoor beds (wind, outdoor rain, day/night background) are panned toward the open side, by how one-sided it is, and dulled when the open side is behind you. It's a cue until phase 4's 3D emitters, and stays as their fallback.
- **Exposure probe:** shards in broken frames now count as glass when their collider is a mesh (raid 12: they are, shaped like the shard). New `openSideAz` and `oneSided` columns.
- **Echoes (fix 6) off glass facades and window bars.** The aim cone also checks the glass and bar layers the sound-blocking rays pass through. Intact glass reflects as glass; bars are a weak, dull reflector.
- Logs: `ambience.csv` gains exposureApplied, indoorFade, bedPan, bedCutoffHz and roofRainFactor. The summary gains an AMBIENCE MIX section.
- Self-check: 97 targets, all verified.

## 0.4.1 — 6 Nov 2026
**Run in game:** raid 12 (16:02, Ground Zero, no AI). Loaded (self-check 15:57), no errors. The tower flicker is gone. Remaining: just inside wide entrances, stepping sideways swings outdoor ambience 6% ↔ 95% (fix 3's sideways fade), which the phase 2 exposure value would hold steady (~0.6–0.7). Gun clips are compressed in memory, so recording loudness can't be read (n/a). Shard colliders are real shard meshes, so the size rule was wrong and they should count as glass.
- **Fix 3h no longer flickers.** Raid 11: going in and out of the Nakatani tower, the outdoor ambience cut on and off deep inside. Fix 3h raised it by the glass share the 32-ray probe saw, and one or two rays hitting a window or not flipped it between 0 and 0.12 every second. It's now smoothed over about 2 s. Phase 2 replaces it with the exposure measure.
- **Stairwells louder (fix 6).** In hard, closed rooms all echoes are raised by the new setting **6e Hard room echo boost dB** (+4) × how hard and closed the room is, and up to 3 extra echoes are played (was 2). Raid 11: stairwell gunshots were "quieter than a stairwell should be".
- **Recording loudness** is measured: each gun's indoor against outdoor recordings (RMS and peak, first 0.4 s), shown in the summary under OWN GUN ECHOES. It checks whether the indoor set is quieter, which would explain quieter stairwells.
- **Exposure probe:**
  - Window bars and grilles count as mostly open (80%), not glass.
  - A shard left in a broken frame counts as glass only if its collider is shard-sized (≤ 0.6 m). Larger, the window counts as broken, i.e. open.
  - The summary lists each thing between you and the outside with its collider type and size, to check both rules.
  - New columns `barsRays` and `brokenRays` in `exposure.csv`.

## 0.4.0 — 6 Nov 2026 (roadmap step 4b, phase 1)
**Run in game:** raid 11 (15:39, Ground Zero, no AI). Loaded (self-check 15:37), no errors. Exposure probe 0.16 ms per update; facade glass found by name. Bars and broken-frame shards misread as glass; outdoor ambience flickered in the tower (fix 3h); stairwell gunshots quieter. No rain, so fix 7 is untested. All addressed in 0.4.1 except fix 7.
- **New: outdoor exposure probe** (Diagnostics, on, **measures only, changes no sound**). Indoors, every 0.25 s:
  - 24 directions are checked against the game's own baked room map: which reach the outside, how far away, and through what (open, glass, or other).
  - It also records what is above you: the ceiling and its material, floors above, and the roof material.

  Logged to `exposure.csv`, added to F10 marks, and shown on the F9 overlay. It also shows the value phase 2 would set for outdoor ambience. See [AMBIENCE.md](AMBIENCE.md).
- **New fix 7, "Fix rain cutting out between rooms"** (off by default). Fixes a game bug: moving between rooms with the same rain-on-roof clip, or up and down in rooms where roof rain follows your height, faded the rain to silence. Every rain clip pick is logged to `rain.csv`, with whether the game would have silenced it.
- Summary gains OUTDOOR EXPOSURE (cost per update, what stood between you and the outside) and RAIN sections.
- Self-check: 89 targets, all verified.

## 0.3.3 — 6 Nov 2026
**Run in game:** raid 10 (15:07, Ground Zero, no AI). Loaded (self-check 15:03), no errors. Threshold (muzzle) and stairwell/office: "good". Stairwell: "maybe more reverb" (the boost reached about +4–5 dB of the +6). Glass ambience: barely acted, because big facade glass doesn't block the probe rays (glass share ~0.06). Inside, the game's own room setting has outdoor ambience at 0, so only its indoor "rain on the roof" layer plays. This led to roadmap step 4b.
- **Fix 3c: the room is measured at the muzzle too.** Raid 9: on a glass office's threshold, head outside and muzzle inside (looking in) sounded outdoor; head inside and muzzle outside (looking out) sounded indoor, because everything was measured at the head. The room, roof and size readings are now a mix of head and muzzle. New setting **3g Muzzle weight** (0.7).
- **Hard closed rooms keep their full indoor sound.** Raid 9, the Nakatani basement stairs: walls 0.81–0.94, roof 1, all concrete and thick metal, but the small-space rule cut them to about 55% indoor. The size cut now fades out as the room gets hard (bare concrete, stone, tile, thick metal) and closed.
- **More stairwell character.** Hard closed rooms get up to 2 extra echoes (fix 6). They also get a boost to the game's room reverb on your own shots: new setting **5a Hard room reverb boost dB** (+6), scaled by how hard and closed the room is.
- **Rain and wind through glass** (fix 3). Inside a space walled with glass, the outdoor ambience level is raised toward full, still muffled by the indoor filter. New setting **3h Ambience through glass** (0.5). Raid 9: glass buildings let too little weather in.
- Logs: `ownshots.csv` gains muzzle readings, the room value used, hardShare, hardRoom and glassShare. Its reverbBoostDb is now the boost actually applied. `ambience.csv` gains glassLeak.

## 0.3.2 — 6 Nov 2026
**Run in game:** raid 9 (14:51, Ground Zero, no AI). Loaded (self-check 14:48), no errors. "A lot better." Stepping out of small spaces now drops the indoor sound. Still heard: head/muzzle on opposite sides of a threshold; stairwell not indoor enough; glass buildings let in too little rain and wind (all in 0.3.3).
- **Fix 3c, Enclosure mode: "is there a roof right above you?"** Five extra rays per sweep go straight up and 20° off vertical. The roof only counts as far as they agree (roof closure = the smaller of the two). Raid 8: about 1 m outside a small space's door, the angled upward rays still caught its roof edge and overhang (0.58–0.84), so shots taken visibly outside got about 40% of the indoor recording. Now the indoor share fades out over about 1 m past the roofline.
- Logs: `zenithClosure` added to `probe.csv`, `ownshots.csv` and `marks.csv`.

## 0.3.1 — 6 Nov 2026
**Run in game:** raid 8 (14:34, Ground Zero, no AI). Loaded (self-check 14:30), no errors. "Sounds pretty good." Bench now 0.12 indoor; 1,221 windows registered as glass (31 glass echoes, against 8 in raid 7). 7 marks: small spaces still sounded indoor when stepping just outside (fixed in 0.3.2).
- **Fix 3c, Enclosure mode: new room measure** (from raid 7: a covered bench and a small glass office sounded too indoor). The indoor share now needs walls *and* a roof (wall closure × roof closure), so a roof alone isn't a room. Tiny spaces get less of the indoor recording (size factor 0.5 at walls ~2 m away, 1 from ~6 m), because the game's indoor recordings carry a long room tail; the echoes supply a small space's sound. Aim direction only counts under a roof. Details in FIXES.md.
- **Windows are recognised as glass.** A window blocks rays with a separate collider that has no material tag, so windows read as the building's concrete. All `WindowBreaker` colliders are now registered as Glass at raid start, for the probe and the echoes; broken windows let rays through.
- **New setting 3f "Thin and glass surfaces count"** (0.5): how much glass, thin wood planks, cardboard and fabric count as walls. Chain fences and grates always count as nearly open.
- **New Diagnostics setting "Mark key"** (F10): marks the spot you're standing on in `marks.csv`, with what the probe measures there and the surfaces around you. Use it where something sounds wrong.
- Logs: `probe.csv` and `ownshots.csv` gain the new measures (wallClosure, roofClosure, room, sizeFactor, thinShare; aimClosure). The summary shows how many windows were registered.
- Self-check: 80 targets, all verified.

## 0.3.0 — 6 Nov 2026 (roadmap step 4)
**Run in game:** raid 7 (14:12, Ground Zero, no AI). Loaded (self-check 14:09), no errors. "Sounds much much better", "overall huge improvement". Echoes turned on mid-raid: 984 played, 4.0 per sample, surface tags found on almost every hit (Concrete 500, MetalThin 217, MetalThick 143, WoodThin 60, …), 24 of 24 voices busy at peak, 17 stolen. Two spots too indoor (fixed in 0.3.1).
- **New fix 6, "Own gun echoes"** (off by default): your own shots get up to 4 short echoes from the real surfaces around you (walls and ceilings by mirror reflection, what you fire at, and building faces or hills up to 250 m). Each echo is delayed by its path at 343 m/s, quieter by distance, the muzzle's directivity and the surface material, and duller by air absorption, surface and (from behind) head shadow. Hooked on the gun's own sound queue, so single shots, full-auto loops and burst tails all get echoes with the same timing. Settings 6, 6a–6d. Logged to `echoes.csv` and a new OWN GUN ECHOES section in `summary.txt`.
- **Enclosure probe fix (from raid 6):** floor-like surfaces (normal more than about 45° upward) no longer count as enclosure, in the 32-ray sweep or the aim cone. Before, the ground just below level read as walls, so open ground scored 0.2–0.4 and every own shot outdoors in Enclosure mode got 15–25% of the indoor recording. Expect lower `enclosure`/`horizontal` outdoors and a lower `aimEnclosure` when firing into open air.
- Self-check: 79 targets, all verified against the decompiled game. Setting names checked.

## 0.2.2 — 6 Nov 2026
**Run in game:** raid 6 (13:51, Ground Zero, no AI). Loaded (self-check 13:46), no errors. Sticky doorway: no doorway swaps all raid. Enclosure mode heard: "seems to work", but outdoor shots sounded a little indoors (the ground bug, fixed in 0.3.0). Meta XR acoustics confirmed running (1 map, 35 geometry). Fix 5 not tried (left at 0).
- **Fix 3:** sticky doorway. The doorway used last frame stays in the running through a crossing (another must be 1 m closer to take over). `ambience.csv` logs which doorway was used and your distance through and beside it.
- **New: enclosure probe** (Diagnostics, on by default, changes no sound): 32 rays every 0.1 s, logged to `probe.csv`.
- **Fix 3c:** new **Enclosure** mode (setting 3d) that blends your own gunshot's indoor/outdoor recordings from the probe plus a 9-ray cone along the shot (setting 3e Aim weight). Doorway mode stays the default. Every own shot and tail is logged to `ownshots.csv`.
- **New fix 5 (experiment):** own-gun room reverb boost, which raises or lowers the sends of the game's hidden Meta reverb copy of your shots.
- **Correction:** the earlier claim that your own shots get no room acoustics was wrong. The game already plays a reverb-only copy through Meta XR (see FIXES.md, fix 5).
- Summary gains a META XR ACOUSTICS section. Self-check: 75 targets, all verified.
- Added [ROADMAP.md](ROADMAP.md).

## 0.2.1 — 6 Nov 2026
**Run in game:** raid 5 (11:28). Loaded (self-check 11:24), no errors, the ambience startup error is gone.
- **Fix 3 (smooth doorways):** outdoorness is now measured through the doorway's plane, with a sideways fade, instead of by distance from its centre. That removes the jump at the room boundary (0.24 → 1.0 in raid 4).
- **Fix 3:** waits for the game's ambience blenders to be set up (about 30 s into a raid) instead of throwing an error every frame until then.
- **New fix 3c, "Blend own gunshots at doorways"** (off by default): near a doorway, your own shot and burst tail play as a mix of the indoor and outdoor recordings.
- Self-check grew to 70 targets, all verified against the decompiled game. Setting names checked for characters BepInEx rejects.

## 0.2.0 — 6 Nov 2026
**Run in game:** loads after the setting-name fix below; fixes not yet heard.
- Added four fixes, all off by default and switchable live in F12 (see [FIXES.md](FIXES.md)):
  1. Gun mixer hysteresis
  2. Obstruction score (Game / IgnoreFailedPaths / ParallelPaths)
  3. Smooth doorways
  4. Off-map sounds as outdoor
- Self-check grew to 62 targets, all verified against the decompiled game.
- `settings.txt` and `summary.txt` record which fixes were on and how often each acted. `ambience.csv` gained `doorwayTarget`, `doorwayApplied` and `doorwayPortals`.
- **Bug, fixed in the same version:** the first build named its settings section `2. Fixes (all off = the game's own audio)`. BepInEx rejects `=` and `'` in setting names, so the plugin failed to load. It's renamed to `2. Fixes (all off means vanilla audio)`, and a load failure is now also written to `errors.txt`.

## 0.1.1 — 6 Nov 2026
**Run in game:** raids 2 and 3.
- Added the doorway logging: `transitions.csv`, `ambience.csv`, and a transitions section in the summary.
- Added an `occluder` column to `containers.csv`.
- Plugin errors are now also written to `RealSoundSimulationLogs/errors.txt`, because BepInEx's `LogOutput.log` wasn't being written.

## 0.1.0 — 6 Nov 2026
**Run in game:** raid 1.
- First build, diagnostics only: self-check, timing, shot probe, occlusion samples, source scan, rolloff dump, F9 overlay, raid summary.
- Headset DSP library and offline bench (not hooked into the game).
