# Diagnostics: what gets logged and how to read it

Everything goes to `G:\SPT\BepInEx\RealSoundSimulationLogs\`. Logging is controlled in F12 → Real Sound Simulation → "1. Diagnostics" (`Write logs`, on by default). It runs whether or not any fix is on.

```
RealSoundSimulationLogs\
  selfcheck.txt           written at every game launch
  patches.txt             0.6.20: latest copy of the raid-start shared-patch report (also in each raid folder)
  audio-counters.txt      0.6.20: latest list of the Unity profiler counters this game offers (also in each raid folder)
  errors.txt              only if something in the plugin failed
  raid_YYYYMMDD_HHMMSS\   one folder per raid
    settings.txt  summary.txt  containers.csv  shots.csv  transitions.csv
    ambience.csv  probe.csv    ownshots.csv    timing.csv   sources.csv   rolloff_dump.txt
```

CSV times (`t`) are seconds since the raid started (`shots.csv` too since 0.6.21; before that it was seconds since the game started). Files are flushed every 2 s. If the game hangs or is killed, the CSVs keep everything up to the last flush, but `summary.txt` is not written.

## Files written outside raids

### selfcheck.txt
Written at every launch. One line per game member the plugin relies on: the fix it belongs to, OK or MISSING, and the member's name. It ends with "All targets present." or a list of what's missing.
- **New timestamp = the plugin loaded.** An old timestamp after a launch means it didn't (check `errors.txt`).
- After an SPT or Tarkov update, a MISSING line names the fix that will stop working.

### errors.txt
Only appears if something failed. It's appended to, never cleared, and each line is timestamped. "PLUGIN FAILED TO LOAD" means nothing from Real Sound Simulation is running. Other lines name the part that failed (for example "fix 3 doorway blend failed once"). Each failure is reported only once per session.

## Files written per raid

### patches.txt (raid start, 0.6.20)
Which other mods touch the same game methods, written once every plugin has loaded (at launch, other plugins may not have patched yet). Read it before a raid with another audio mod installed (Sound Engine Backport, the old Real Sound Simulation mods).
- **Plugins loaded:** every BepInEx plugin, GUID, name and version, and whether Sound Engine Backport's client (`red.soundenginebackport.client`) is present.
- **Mixer:** the master mixer's name, its snapshots in order (fix 3 expects `[0]=Outdoor [1]=Indoor`; SEB's 1.0 mixer adds `Bunker`), the gunshot mixer groups and the spatializer plugin. If another mod replaced the mixer, this is where it shows.
- **Methods another mod patches:** `SHARED` = a method this plugin patches too, so Harmony run order decides the result; `FOREIGN` = a method only in the self-check as a planned target (the fix number follows). Under each: every patch, ours and theirs, with kind (prefix, postfix, transpiler, finalizer), priority (higher runs first; the last postfix wins), owner GUID and the patch method. A foreign prefix that can return false (SEB's `FireBullet`) skips the game's method, not our prefix.
- The summary's SHARED PATCHES line repeats the counts and the shared method names.

### settings.txt (raid start)
- CPU logical cores and the game's audio quality tier (12 or more logical cores → High, 8–11 → Medium, fewer → Low; `AudioUtils.GetQualityByLogicCores`).
- Sample rate, DSP buffer, voice limits, spatializer plugin.
- Which fixes were on at raid start.
- The map's baked audio data (rooms, portals, routes).
- The occlusion settings actually in effect (after the server applied its values), as JSON.

### summary.txt (raid end)
The quick read. Sections:
- **Fixes at start / end** and how often each fix acted (fix 1 held back, fix 2 changed, fix 3c blended, fix 4 applied).
- **PERFORMANCE:** milliseconds the game's audio code took per frame (average, max, frames over 1 ms); active occlusion processors; containers; playing sources against the voice limit.
- **OCCLUSION:** container samples by priority; how many were propagated (different room) or over the 0.55 muffle line.
- **GUNSHOTS:** remote shots logged, with line of sight, beyond 250 m, on the muffled mixer, and mixer flips.
- **INDOOR/OUTDOOR TRANSITIONS:** the game's snapshot switches against its room switches, and how far apart they were in time and distance.
- **OWN GUN ECHOES:** fix 6 (since 0.4.1 also the loudness of each gun's indoor against outdoor recordings): samples echoed, echoes per sample, by kind, mean level and path, voices used and stolen, surfaces hit.
- **COST (0.5.3; logging parts since 0.5.5):** what each part of the plugin costs on the main thread, including the diagnostics' own per-frame work, the 15 s source scan, the ambience/probe rows and saving the log files: calls/s, avg and max ms per call, ms per second of game time (100 ms/s = one full CPU core), raycasts/s, room lookups/s, and a total. Nested work is counted under the part that did it. The F9 overlay shows the running total (`CA2 cost … ms/s`).
- **OUTDOOR EXPOSURE:** updates, cost per update, the extra layers the "what's in between" ray uses, and the 25 most common things between you and the outside (layer:object:material:collider type and size → how it was counted).
- **RAIN:** rain clip picks, how many the game would have silenced, how many fix 7 prevented.
- **META XR ACOUSTICS:** how many Meta acoustic map, geometry and control-zone components are in the scene and active. If the map count is 0, Meta's room reverb isn't running and fix 5 has nothing to act on.
- **AUDIO THREAD (0.6.20):** block length, blocks seen, gap average and max, late blocks (> 1.5× and > 2× a block, with their share) and stalls, then a verdict: healthy, marginal (over 5% past 1.5×), or LATE (over 1% past 2×: expect crackle).
- **UNITY AUDIO COUNTERS (0.6.20):** each recorded audio counter's raid avg and max with its unit; for a DSP/CPU percent counter, the share of frames over 50% and 80% of the audio deadline and which scale (0–1 or 0–100) was assumed. "None available" means this build exposes no audio counters.
- **PROBE V2 BATCH WAIT (0.6.20):** frames, average and max wait per frame for the ray batch, the share of frames that really waited, the muzzle measures' average and max wait, and how many slow frames went to slowframes.csv.
- **SHARED PATCHES (0.6.20):** whether SEB's client is present, how many methods another mod patches that we patch (named) or plan to, and the mixer line (see patches.txt).
- **AudioSource maxDistance by group:** the audible ranges actually used.

### containers.csv (every 0.5 s, every sound container)
A *container* is the game's grouping of sounds at one spot (one per player, plus one per one-off sound with occlusion).

| Column | Meaning |
|---|---|
| id, dist, maxDist | container id, distance to you (m), its range (m) |
| priority | how much work the game spends on it: High / Medium / Low (by distance as a share of range), OutOfRange (beyond range: no checks run), `-` (no occlusion processor) |
| room, roomOutdoor, sameRoomAsListener | baked room name (`[out]` = outdoor, `invalid` = off the baked map), flags |
| prop, obst, occl | propagation, obstruction, final occlusion (0 clear … 1 blocked) |
| gunOccludedMixer | 1 if occl > 0.55, the game's muffled-gun line |
| ranT, ranD, ranR | which obstruction checks ran: transmission, diffraction, reflection (blank = this container doesn't use that calculator) |
| T, D, R | each check's score (1.0 usually means "no path found") |
| sources | sounds in the container |
| occluder | the game class doing the work (CombinedSpatialAudioOccluder for players, ContinuousPropagatedSpatialAudioOccluder for one-off sounds such as SPTBattleAmbience, …) |

### botshots.csv (0.6.21, one row per sound a bot's gun queued or lost)
`t`, `clock` (same clock as FrameDiag), `shooterId`, `event` (`body` = a shot or firing loop, `tail`, `muted` = not played because the voice's occlusion volume was 0, `noqueue` = not played because no voices were free), `clip`, `lengthS`, `delayS` (time of flight until it starts), `dist`, `occl`, `occludedMixer` (1 = the muffled gun mixer), `occlVolume` (the voice's occlusion volume factor), `firstBurstSeeded` (1 = setting `1d` started this burst muffled), `cutPrevS` and `cutPrevShare` (this sound ended the same gun's previous sound early: seconds lost and share of that sound; empty if nothing was cut). A firing loop being ended by its tail is the game's design and is not counted. With `1e` on (0.6.23), a cut means a third sound took back the oldest of the gun's two voices. The summary also counts gun voices taken from the gun closest to finishing because the pool was empty. The summary's BOT GUNSHOTS section has the totals.

### routes.csv (0.6.22, each bot shot, at most 4/s per gun; only with `Log portal routes` on)
What portal positioning (ROADMAP 6.1) would do. Moves no sound. `t`, `clock`, `shooterId`, `reason` (`route`, or why there is none: `outdoor` both in the common outdoors, `sameRoom`, `outOfRange`, `noRoute` none baked, `notComputed` the calculator hasn't run for this room pair, `busy` its job is still running), `straightM`, `los`, `dy` (shooter height minus yours: floors), `sourceRoom`, `listenerRoom` (baked room IDs), `reversed`, `routes` (baked routes for this room pair), `bestCost`, `secondCost` (close values = the route may flip), `pathM` (length along the route), `portals`, `nearX/Y/Z` (the opening nearest you), `nearPortalM`, `closure` (the portal's closure level, e.g. a shut door), `trueAz`, `portalAz` (relative to where you face), `angleDeg` (between the opening and the true direction), `prop`, `occl`, `routeChanged` (1 = this shooter's best opening moved over 1.5 m within 3 s), `movedDeg` (0.6.24: how far setting `6.1` has actually moved this gun's voices from the true direction). The summary's PORTAL POSITIONING line has guns handled, the average and maximum angle moved, and how often the route changed or was held back. The summary's PORTAL ROUTES line has the totals.

### shots.csv (each remote gunshot, at most 4/s per gun)
`shooterId, dist, rolloff` (audible range m), `silenced`, `mixer` (Gunshots or Occluded), `occl/prop/obst` of the shooter's container, `shooterRoom/listenerRoom`, `los` (1 = straight line of sight on the game's occlusion layers), `beyond250`.

### transitions.csv (each indoor/outdoor switch)
| Column | Meaning |
|---|---|
| system | `snapshot` = the game's mixer switch (trigger volumes); `room` = baked audio room changed between indoor and outdoor |
| now | indoor / outdoor |
| x, y, z | where you were |
| fadeS | the game's fade time: 0.1 in / 0.5 out for snapshots, 0.3 out for ambience |

Snapshot switches are logged even while fix 3 skips them: they record what the game *wanted* to do.

### probe2.csv (0.6.0, 8 per second)
Probe v2 (DESIGN.md §2.6): `level` (0–1) and `levelDb`; its parts `direct`, `traced` (after bounces) and `tail` (room build-up), `remaining` (still bouncing after 3 reflections); `escapeRate` (p) and `absorption` (a) used for the build-up; `meanFreePathM`, `rt60S` (estimate); `skyOpen`, `enclosed`, `coverage` (upward cone covered) and `roofMat`; `cutoffHz` (brightness); `arrivalAz` (relative to where you face, 0 ahead, +90 right) and `arrivalEl`; `focus` (0 all round … 1 one direction), `diffuseShare`; gunshot `room`, `size`, `hardRoom`; `raysPerS`. From 0.6.4: `escapeRate` is the energy leak rate of legs after 5+ reflections; `escapeRateOld` and `tailOld` are 0.6.3's ray-counted leak rate (legs after 2–3 reflections) and the build-up it would give, for comparison; `deepEscShare` / `deepRemShare` = of the energy at the 3rd reflection, the share that got out in reflections 4–8 / was still bouncing after 8; `escapes` = paths in the last two sweeps that got out; `openings` = remembered openings; `relErr` = the level's statistical error (relative); `smoothS` = the averaging time fix 9 uses. From 0.6.5: `focusEarly` (direct/bounced part, after the agreement scaling). 0.6.5–0.6.6 also had `focusExit`, `exitAz`/`exitEl`, `exitCount` (where escaping paths left the building); from 0.6.7 these are `focusBuildUp`, `buildUpAz`/`buildUpEl`, `buildUpCount` (where escaping paths arrive from at you, ~3 s), plus `roofLevel` (roof rain traced from the roof, 0–1), `roofAz`/`roofEl` (where it arrives from), `roofFocus`, `roofCount` (paths that reached the roof, ~3 s). The summary's ACOUSTIC PROBE V2 section counts back faces hit, far sides skipped, and upward escapes the roof test rejected; from 0.6.7 also roof tests, `9i` and `9h`, and AMBIENCE MIX lists both directional groups (outdoor beds, roof rain) as movable/fixed. The overlay's probe line shows the focus actually applied and the roof rain level.

### paths.csv (0.6.0, at each F10)
One row per direction of probe v2: `azimuthDeg`/`elevationDeg` of its first leg (relative to where you face), `fate` (Sky, SkyAfterBounces, Trapped, LongInterior), `casts`, the energy that escaped `direct`/`reflected` or is `remaining`, `geo` (gunshot room measure), `cutoffHz`, `viaGlass`, and `hits` (object:material@distance for each hit, in order). From 0.6.4: 192 rows (dir 0–127 even directions, 128–191 guided), `guided`, `weight` (importance weight; multiply the energies by it), `finalFate` (where a deep path ended after up to 8 reflections; `fate` is its own result after 3). From 0.6.5: `exitAz`/`exitEl` for paths that got out: where they left the building.

Also from 0.6.0: `marks.csv` ends with `v2Level, v2CutoffHz, v2Focus, v2SkyOpen, v2Coverage, v2Room, v2Rt60`; `ownshots.csv` ends with `probe` (v1/v2: which one decided the shot), `v2Room, v2MuzzleRoom, v2Size, v2HardRoom, v2Rt60`. The F9 overlay has a "probe v2" line. The summary has an ACOUSTIC PROBE V2 section, and AMBIENCE MIX says how many beds could be made directional.

### ambience.csv (10 per second)
For fix 8 (empty with it off): `exposureApplied` (the smoothed outdoor value it set), `indoorLayers` (0.5.8; level of the indoor layers, 1 enclosed … 0 open air; was `indoorFade`), `bedPan` (−1 left … 1 right), `bedCutoffHz` (direction-cue high-cut; 22000 = none), `roofRainFactor`. (`glassLeak`, fix 3h, was removed in 0.5.8.) Since 0.5.0: `openingShare` (share moved to the 3D opening sounds), `openingVoices` (how many are playing), `opening1Az` / `opening1Gain` (first opening's direction relative to where you face, and its gain).

| Column | Meaning |
|---|---|
| x, y, z | your position |
| snapshotIndoor, roomIndoor | what each of the game's two systems currently says |
| outdoorAmbience | outdoor ambience level 0–1 (the game's, or fix 3's when it's on) |
| doorwayTarget | outdoorness from the nearest doorway: 0 deep inside, 0.5 doorway, 1 outside (calculated even when fix 3 is off) |
| doorwayApplied | the smoothed value fix 3 applied (blank when fix 3 is off) |
| doorwayPortals | doorways considered this frame |
| portalId | the doorway used (0 = none) |
| throughPlaneM | your distance through that doorway's plane: negative inside, positive outside (blank = orientation unknown, centre distance used) |
| sidewaysM | your sideways distance from the doorway's centre |

### probe.csv (twice a second)
`x, y, z`, `roomIndoor`, `doorwayTarget`, and the [enclosure probe](FIXES.md#enclosure-probe-022) values: `enclosure`, `horizontal`, `overhead` (0 open … 1 enclosed) and `meanHitM` (average distance to what the rays hit). From 0.3.0 the ground no longer counts, so open ground reads near 0 (raid 6, before the change: 0.2–0.4). From 0.3.1: `wallClosure`, `roofClosure`, `room` (their product), `sizeFactor` and `thinShare`; from 0.3.2 `zenithClosure` (roof right above you); see [the room measure](FIXES.md#enclosure-probe-022).

### ownshots.csv (each of your own shots and burst tails)
| Column | Meaning |
|---|---|
| kind | shot or tail |
| source | Doorway or Enclosure (setting 3d) |
| gameEnvironment | the recording set the game would have used (indoor/outdoor, from its trigger volumes) |
| doorwayTarget | doorway value at that moment |
| enclosure, horizontal, overhead | probe values at that moment |
| aimEnclosure, aimNearestM | the 9-ray cone along the shot: how enclosed (0–1) and the nearest surface (m); Enclosure mode only |
| indoorWeight | the indoor share used (0 = outdoor recording, 1 = indoor) |
| applied | 1 if fix 3c changed what played |
| reverbBoostDb | the own-gun reverb boost actually applied (setting 5, plus 5a × hardRoom since 0.3.3) |
| wallClosure, roofClosure, room, sizeFactor, thinShare | the room measure at that moment (0.3.1) |
| aimClosure | closure of the 9-ray cone along the shot (0.3.1); what Enclosure mode uses for aim |
| zenithClosure | roof right above you (0.3.2) |
| muzzleRoom, muzzleRoof, muzzleSize | the room measure taken at the muzzle (0.3.3) |
| muzzleFromHeadM | distance from your head to the muzzle |
| roomUsed | the head/muzzle mix that decided the weight (setting 3g) |
| hardShare, hardRoom | share of bare hard surfaces; how much this is a hard closed room (0.3.3) |
| glassShare | share of glass among the surfaces around you (0.3.3) |

### exposure.csv (4 per second; step 4b phase 1, measure only)
See [AMBIENCE.md](AMBIENCE.md) for what this measures and why.

| Column | Meaning |
|---|---|
| listenerIndoor | 1 if you're in an indoor baked room (only then are the rays cast) |
| roomOutdoorFloor | the room's baked outdoor-ambience floor (`OutdoorAmbientVolume`); 0 = no outside heard deep in the room |
| openRays, glassRays, otherRays, blockedRays | of 24 directions: reach the outside through open air / through glass / through something else that isn't a sound blocker / never |
| barsRays, brokenRays | 0.4.1: through window bars or grilles (counted 80% open) / through a broken window whose leftover shard collider is bigger than a shard (counted open) |
| exposureOpen, exposureGlass, exposureOther | the same, weighted by how near the opening is (1 / (1 + m / 15) since 0.5.4; covered outside × 0.5), divided by 48 |
| nearestOpenM / Az, nearestGlassM / Az | distance and direction (relative to where you face) to the nearest open and glass opening |
| ceilingM, ceilingMat | first surface straight above you and its material |
| floorsAbove, roofMat | surfaces above that ceiling (−1 = open sky above you) and the topmost one's material (the real roof) |
| outdoorValue | the outdoor value from exposure, which fix 8 applies (`previewOutdoorValue` before 0.5.8) |
| openSectors | 0.5.8: how many of the 6 fixed world sectors (60°) have any outside in them; the opening sounds play from those. Before 0.5.8: `openings, op1Az, op1DistM, op1Weight, op1Glass` (the old grouping, only logged) |
| openSideAz, oneSided | 0.4.2: which way the openness lies relative to where you face (0 ahead, +90 right, ±180 behind), and how one-sided it is (0 all round … 1 one side only) |

### rays.csv (each F10 press, 0.5.0)
All 48 exposure directions at that moment: `ray`, `azimuthDeg` (relative to where you face), `elevationDeg` (0 or 15), `wallM` and `wallCollider` (first sound-blocking surface: layer:object:material), `outsideAtM` (where it reached an outdoor room; empty = never), `path` (Open/Glass/Bars/Broken/Other/Blocked), `betweenCollider` (what's in between on the other layers), `lastSampleRoom` / `lastSampleM` (the last baked room the probe looked up along the ray), `weight`. Read this when a spot's outside sound is wrong: it shows whether a window was missed, blocked by a collider, or whether the space outside it counts as indoors.

### rain.csv (each time a rain player picks a clip)
`environment` (Indoor/Outdoor player), `intensity`, `clip`, `targetVolume`, `gameWouldSilence` (1 = the game's own code would have faded the rain out here; fix 7), `fixOn`.

### rain2.csv (0.6.8, every 0.25 s: the game's indoor rain player)
One row per source (the player has two, for crossfades): `clip`, `channels`, `playing`, `virtual` (Unity dropped it for lack of voices), `mute`, `gameVolume` (what the game set), `ourFactor` (roof rain factor applied), `volume` (what plays), `spatialBlend`, `spread`, `spatialize`, `mixer`. Raid 27: the car dealer's roof rain went silent at some spots while our factor held.

0.6.9: rain2.csv ends with `fadeComp` (how much roof rain is raised to undo the indoor-layer fade; 1 = none). 0.6.10: then `lowpassHz` (the roof rain's path muffling as applied; "off" = none), and probe2.csv ends with `roofCutoffHz` (how bright the roof paths arrive).

### roomtone.csv (0.6.9, each room tone change, `8g`)
`room`, `clip` (the room tone clip), `roomToneVolume` (the room's own level), `wind` (1 = clip name contains "wind": it follows the outside's level), `gain` (the 8g gain at that moment). The summary's AMBIENCE MIX lists every room tone heard, and which mixer volumes the indoor-layer fade moves.

0.6.9 elsewhere: probe2.csv `levelDb` floors at −60 dB and ends with `roofLevelAll` (the 0.6.7 all-paths roof measure; `roofLevel` is now per upward path). ownshots.csv ends with `weightUsed` (3c's weight after `3j`; `indoorWeight` is as measured). The RAIN summary counts same-clip crossfades done as a volume ramp.

0.6.11: ownshots.csv rows are now written when the shot or tail call finishes (same rows as before) and end with `muzzleRoomSphere, v2MuzzleRoomSphere` (the muzzle measures without `3k`'s forward lobe; `muzzleRoom` / `v2MuzzleRoom` are with it) and `tailPlayed, tailLevelDb` (relative to the shot), `tailDelayMs, tailAz` (relative to where you face), `tailThrough` (open / glass / bars / open (fallback); when no tail played, the reason: mostly outdoor already, aim cone closed, no tail bank, fix 6 off…). The summary has OUTDOOR TAIL FROM THE OPENING and MUZZLE LOBE (mean sphere minus lobe muzzle room).

0.6.37: thump.csv, one row for each 6.2 decision: `kind` (gun, explosion), `played`, `reason` (why not), `distM`, `walls`, `materials` (material@thickness, joined with +), `thickM`, `lossDb` (before `6.2d`), `volume`, `cutoffHz`, `delayMs`, `hasRoute`, `clip`.

0.6.38: thump.csv adds `spentDb` (distance + `lossDb`) and `reachDb` after `lossDb`. `reason` can be `stopped by walls` (spentDb at or past reachDb, `6.2e` on). `materials` says `earth` for soil-type tags and terrain. The THROUGH-WALL THUMP summary line counts `stopped by walls` by gun and explosion.

0.6.36: probe2.csv ends with `floorsOldNow`, the floors counted on the main solid layers alone (`floorsNow` follows `9v`).

0.6.34: probe2.csv ends with `floorsNow` (floors above the ceiling straight overhead, -1 = none measured) and `roofLossDb` (how much `9u` took off the roof rain reaching you, negative dB).

0.6.14: probe2.csv ends with `memoryS` (how long the running sums remember right now: 3 s standing, 0.5 s moving fast) and `speedMs` (your speed, smoothed). Summary: OUTDOOR TAIL counts tails from the 3 m fallback; OWN GUN ECHOES counts loop echoes skipped for one-round loops and started 0.2 s late on longer ones; PERFORMANCE adds "playing sources in sound containers" (counted at every container sample, 0.5 s) beside the 15 s full scan and our echo voices; AMBIENCE MIX's indoor-layer mixer volumes and room tones heard now survive to the summary.

0.6.15: `Write logs` works live (off in raid: no more rows; settings.txt and summary.txt still written). The 15 s source scan (sources.csv, rolloff_dump.txt) is off by default (`Source scan interval s` = 0; it caused ~56 ms hitches); the summary says so. containers.csv drops to every 1 s above 50 containers. COST gains `alloc KB/s` per part and a total (main-thread bytes allocated during each part; "n/a" if the runtime has no per-thread counter).

0.6.18: timing.csv ends with `aliveBots` (everyone alive but you). New spawns.csv: one row per change in that count, `t, clock, aliveBots, delta`; `clock` is wall-clock time (HH:mm:ss.fff) to match FrameDiag's frames.log, so slow frames can be lined up with spawn waves. Summary: alive bots max and spawned count.

0.6.19: containers.csv rows are built without allocating (the 0–1 scores have three fixed decimals now: `0.500`, not `0.5`). For `Diagnostics back off after a spawn s` (2) after each change in the bot count, no container, ambience, probe or timing rows are written, the overlay is not refreshed and the log files are not saved, so the plugin adds nothing to the frames a spawn wave makes long; spawns.csv keeps being written. The summary counts the frames skipped (after the alive bots line). Side effect: the echo headroom cap (fix 6) uses the playing-source count from the last container sample, so during the back-off it keeps the count from before the spawn.

### distant.csv (0.6.8, every 0.25 s per sound, `9k`; 0.6.9 columns at the end)
0.6.9 adds `heardEl`, `outVolume` (the AudioSource's volume as it plays), `occlFactor`, `mixer`, `spatialBlend`, `spatialize`, `reverbZoneMix`, `playing`: if a sound is heard while `outVolume` is tiny, something other than its volume carries it (a reverb send, another source).
Each off-map sound moved through the openings: `source` (id), `distanceM`, `trueAz` (where it really is, relative to where you face), `heardAz` (where it's placed), `transmission` (0–1, how much gets in from its direction), `gain` (√ of that, blended by enclosed), `enclosed`, `clip`. The summary has a DISTANT GUNFIRE THROUGH OPENINGS line; ACOUSTIC PROBE V2 counts exit points found and not found. paths.csv gains `exitPointAz`, `exitPointM`.

### gunsounds.csv (setting 0, 0.6.3: each time a gun switches between SPT and SEB, or can't)
`player` (id), `firstPerson`, `gun` (the gun's Body bank as the game set it, e.g. `akm_main`), `auto` (1 = can fire full-auto), `set` (SPT/SEB), `reason` (why it stays SPT: `full-auto gun (0a off)`, `not in SEB`, `SEB loading`, `SEB load failed`, `seb_sounds not built`; empty when switched to SEB), `banksSwapped` (how many of the six banks came from SEB). One row per change, not per shot. The summary's GUN SOUNDS line counts SEB banks loaded, bundles, load failures and switches.

### marks.csv (each press of the mark key, F10)
Where you were and what was measured when you pressed it: position, `roomIndoor`, `doorwayTarget`, `enclosure`, `wallClosure`, `roofClosure`, `zenithClosure` (0.3.2), `room`, `sizeFactor`, `thinShare`, `meanHitM`, the time and indoor weight of your last own shot, and `surfaces` (what the probe rays hit, as material:count). From 0.4.0 also the exposure columns (exposureOpen/Glass/Other, open/glass/blocked rays, nearest open/glass opening, roomOutdoorFloor, outdoorValue (`previewOutdoorValue` before 0.5.8), ceiling, floors above, roof material). Also logged as a line in the BepInEx log.

### echoes.csv (each echo played by fix 6)
One row per echo. A sample with no echo found writes one row with kind `none`.

| Column | Meaning |
|---|---|
| sample | `shot` (single shot), `loop` (full-auto firing loop) or `tail` (end of a burst) |
| taps, tap | echoes for this sample, and this echo's index (0 = loudest) |
| kind | `Spec` mirror reflection, `Diff` scattered, `Aim` what you fired at, `Far` far reflector |
| material | the surface's bullet material tag (`None` = untagged) |
| pathM, delayMs | path length (out and back) and its delay at 343 m/s |
| levelDb | level relative to the shot, including setting 6a |
| directivityDb | the muzzle-directivity part of that level |
| cutoffHz | low-pass actually used (air, surface, scatter, head shadow) |
| azimuthDeg, elevationDeg | direction it comes from, relative to where you face (0 ahead, +90 right, ±180 behind) |
| stolen | 1 if an older echo was stopped to make room |

### timing.csv (every 5 s)
Frames in the window (÷5 = fps), plus the average and max ms of `SpatialAudioSystem.Update`, `SpatialAudioSystem.LateUpdate` and `BetterAudio.Update`, the active occlusion processors and the container count.
0.6.20 adds the audio thread (`Time the audio thread`, on): `audioBlocks` in the window, `audioBlockMs` (one DSP block: dspBuffer samples ÷ sample rate, ~21.3 ms for 1024 at 48 kHz), `audioGapAvgMs` / `audioGapMaxMs` (time between consecutive blocks), `audioLate15` (gaps over 1.5× a block), `audioLate2x` (over 2×), `audioStalls` (over 100 ms: alt-tab, loading freezes). Voices cost the audio thread, not the frame; a steady share of late blocks is the crackle condition and the ceiling the voice budget (ROADMAP) is set against. Unity double-buffers, so one late block is not always audible. 0.6.20 also ends timing.csv with `batchWaitAvgMs` / `batchWaitMaxMs` (probe v2's ray batch: how long the main thread waited per frame, muzzle measures included) and `batchWaitedFrames` (frames where the batch was not finished yet).

### audiocounters.csv (0.6.20, every 5 s; only if the game offers audio counters)
`t`, then for each recorded counter `<name>_avg` and `<name>_max` over the window, in the counter's own unit (listed in audio-counters.txt). Sampled every frame, including during the spawn back-off.

### slowframes.csv (0.6.20, one row per slow frame while probe v2 runs)
A row for every frame over 25 ms, or with a ray-batch wait of 1 ms or more: `t`, `clock` (HH:mm:ss.fff, the same clock as spawns.csv and FrameDiag's frames.log), `frameMs`, `batchWaitMs` (waiting for the continuous ray batch), `muzzleWaitMs` (waiting for the per-shot muzzle measure), `rays` (the batch's size), `aliveBots`. Each row describes the frame before the one it was written in (Unity reports a frame's length at the start of the next). Written during the spawn back-off too, since spawn frames are the ones to look at. Read it sorted by `frameMs`: if `batchWaitMs` is near zero on the worst frames, the probe is not what makes them slow (ROADMAP 6.0b).

### sources.csv (every 15 s)
Sound objects in the scene, how many are playing, and the voice limits. Playing near `realVoices` (196) means sounds are being cut.

### rolloff_dump.txt
One entry per kind of sound source seen: volume-over-distance curve, range, spatial blend, whether 3D headphone processing (HRTF) is on, filters, and components. This is where audible ranges (gunshots 600–1000 m, footsteps about 64 m) come from.

## Live overlay (F9)

Top left, refreshed 4 times a second. The header shows the audio tier, your room, active processors and shot stats; since 0.6.20 an `audio thread` line (block ms, gap avg and max, late and stalled blocks in the current 5 s window). Then the nearest containers, one per row:
- **P** = heard through doorways (propagated); **O** = blocked in the same space (obstructed).
- Distance, priority, propagation, obstruction and occlusion. Occlusion turns red above 0.55.
- Which checks ran (T, D, R) and their scores, then the room (`=` means the same room as you).

## Raid history

| Raid | Map | AI | Tier | Plugin | Notes |
|---|---|---|---|---|---|
| 20261106_082358 | Ground Zero | no | Medium | 0.1.0 | first run; no gunfire |
| 20261106_085319 | Ground Zero | yes | Medium | 0.1.1 | game hung on weapon inspect; no summary |
| 20261106_092717 | Ground Zero | yes | High | 0.1.1 | SMT on; about 2× fps |
| 20261106_105707 | Ground Zero | no | High | 0.2.0 | fixes 1, 3, 4 on (2 on Game); sound "def improved"; doorway blend still jumped at room boundary; fix 3 ambience error for first ~30 s |
| 20261106_112836 | Ground Zero | no | High | 0.2.1 | fixes 1, 3, 3c, 4 on; "improved again"; no errors; 3c blended 107; the two indoor/outdoor systems agreed within 0.5 m; one doorway still jumped 0.15 → 0.69 |
| 20261106_135110 | Ground Zero | no | High | 0.2.2 | fixes 1, 2 (IgnoreFailedPaths), 3, 3c (Enclosure), 4 on; 5 at 0. No errors. No doorway swaps. Probe: rooms 0.82–1.0, covered 0.65–0.75, open only 0.2–0.4 (ground counted, fixed 0.3.0). 3c blended 203; "seems to work" but outdoor shots a little indoors. Meta XR: 1 map, 35 geometry active |
| 20261106_141255 | Ground Zero | no | High | 0.3.0 | fixes 1, 2 (IgnoreFailedPaths, ParallelPaths by the end), 3, 3c (Enclosure), 4 on; 6 (echoes) on mid-raid. No errors. "Sounds much much better", "overall huge improvement". 984 echoes, 4.0 per sample, 17 voices stolen. Too indoor: covered bench (room ~0.75–0.85 from the roof alone; indoor 0.5–0.6) and a small glass office at the end (0.90–0.98; indoor 0.84–1.0; windows read as concrete) |
| 20261106_143431 | Ground Zero | no | High | 0.3.1 | fixes 1, 2 (ParallelPaths), 3, 3c (Enclosure, thin 0.5), 4, 6 on. No errors. "Sounds pretty good." Bench 0.12 indoor. 1,221 windows registered; 31 glass echoes. 7 F10 marks: just outside small spaces, roof closure still 0.48–0.84 from the roof edge, shots ~0.4 indoor (fixed 0.3.2) |
| 20261106_145146 | Ground Zero | no | High | 0.3.2 | all fixes on. No errors. "A lot better." Stepping out of small spaces now outdoor (zenith works). Still: head/muzzle on opposite sides of the glass office threshold sounded wrong; Nakatani stairs only ~0.55 indoor (size cut); glass buildings too little rain/wind. Fixed in 0.3.3 |
| 20261106_150727 | Ground Zero | no | High | 0.3.3 | all fixes on. No errors. Threshold (muzzle, mean 0.63 m from head) and stairwell "good"; stairwell boost ~+4–5 dB, "maybe more reverb". Glass building (x 6–13, z 330): game room outdoor ambience 0 → 0.001; facade glass not hit by rays (glass share 0.06) → 3h barely acted. Led to roadmap step 4b |
| 20261106_153937 | Ground Zero | no | High | 0.4.0 | all fixes on except 7 (no rain this raid, so 7 untested). No errors. Exposure probe 0.16 ms avg / 3 ms max per update. Facade glass found by name (Nakatani_tower_windows, frame_glass, sliding door glass). Wrongly counted as glass: window bars (Window_defender) and shards left in broken frames (StuckPiece). 5 marks (what sounded weird: asked). Stairwell between floors: 79–94% indoor, reverb +3–5 dB; "a little quieter?" |
| 20261106_160206 | Ground Zero | no | High | 0.4.1 | No errors. Tower flicker gone (deep inside steady 0.02–0.04). Just inside the tower entrance (marks 3–5), 0.3–0.5 m sideways swings outdoor ambience 0.06 ↔ 0.95 (fix 3 sideways fade × steep curve); exposure preview steady 0.59–0.71 there. Gun clips compressed: loudness n/a. Shards are MeshColliders (real shard shape) → should count as glass; bars are one low-poly sheet per window (0.1×3.4×4.0 m). Stairwell shots w 0.62–0.97, 5a +3–5 dB, 6e active. 76 echo voices stolen |
| 20261106_162805 | Ground Zero | no | High | 0.4.2 | No errors. Fix 8 A/B: "works pretty well"; 1 level jump > 0.15/0.1 s in 4,584 samples; pan cue 626 samples, behind-dulling 346; 6 bed sources, 1 indoor rain player; 274 glass/bar echo candidates. No rain (fix 7 / 8b untested). 2 marks (kiosk area, tower entrance) |
| 20261106_171932 | Ground Zero | no | High | 0.4.2 | **Rain (medium).** Fixes 7 and 8 on. Fix 7: 10 clip picks, 0 would-silence (bug not triggered). Nakatani exit hallway (marks 2–3, x 0 to −9, z 329): open windows barely found (1 open dir at 18 m, 2 glass at 12 m; exposure 0.006–0.07), so roof rain was heard instead of window rain. Exit stairs down (marks 4–5, y 21.3 / 16.8, 4 floors above): roof rain still 0.3 |
| 20261106_173555 | Ground Zero | no | High | 0.5.0 | **Rain (medium).** Error: opening sounds crashed at setup (filter added before the source), so none played and fix 8's update failed every frame. Roof-rain target flipped 0/0.3/1 about every 0.5 s (single up-ray), giving 34 interrupted indoor rain crossfades (clicking). Nakatani hall (marks 4–7): `rays.csv` showed the facade "window" collider counted as glass, and crossings read at 8–18 m. User: worst when facing a close wall with ears toward the glass |
| 20261106_175230 | Ground Zero | no | High | 0.5.1 | **Rain (medium).** No errors. Opening voices up to 12. Hallway "much better". Mark 1 (98.6, 290.1): rain on top and moving, because the opening-1 direction swung 40 → 172 → −126° within seconds. Mark 2 (97.6, 292.6): rain from inside, because rays reached an "outdoor" room 3–6 m away then hit escalators and columns (covered entrance area). "Comes from one location, not ambiently" |
| 20261106_180453 | Ground Zero | no | High | 0.5.2 | **Rain (medium).** No errors; opening voices max 24. Capital building (user's start, x 114–118, z 265): the room map has interior patches as outdoor. At tag 3, 19 open rays at 0.6–1.1 m, all "covered", but the level ignored that (0.78), so rain loud indoors with 3D on or off. Tag 1: left bias from that patch. Tag 2: level 0.89 → 0.02 in 0.25 s stepping out of it. Tag 4 (51, 145): "okay" |
| 20261106_182045 | Ground Zero | no | High | 0.5.3 | **Rain.** No errors. COST total 6.3 ms/s: exposure 0.40 ms avg / 3.1 max at 3.8/s, enclosure 0.25 avg, one-off spikes 52 ms (window registry) and 138 ms (fix 8 setup). Capital: no rain from inside, but doubled/flanging (correlated copies). Shot-out lobby windows 12–14 m away: almost all rays Blocked or roofed-rejected (canopy), exposure ~0.01. Nakatani extract windows before/after shooting: ~0.03 both. Stairwell all blocked |
| 20261106_190951 | Ground Zero | no | High | 0.5.6 | **Rain (medium).** "A lot better at thresholds." Nakatani hallway: the exposure value jumped 0.23 → 0.42 → 0.17 → 0.13 every 0.125 s while walking (all rays re-measured at once), so the rain cut in and out. Garage entrance (43–48, 22.6, 266): an outdoor room, exposure 0.13–0.67, indoor channel off, so no roof rain. COST: source scan **51.5 ms** every 15 s (hitch); exposure 0.77 avg / 12 max ms; total 15.6 ms/s |
| 20261106_211043 | Ground Zero | no | High | 0.5.8 | **Rain (medium).** "Interior transitions sound better." Two problems. (1) Quiet rain deep inside sounds doubled ("like two gunshots at once"): mark 2 (Nakatani, exposure 0.05) had 12 opening-sound copies of the same loops playing at once (garage: 15-20), each started at a different point, so every distinct drop is heard several times. (2) Garage (mark 1, 42.4, 22.7, 265.4; one side open, one concrete slab above) "sounds two storeys underground": only 3 of 48 directions counted as outside (9 m away; 2 more hit a parked car), value 0.14. Walls and roof are ONE collider (Road_descent_cover), so every roofed point beyond it was rejected as "your own ceiling". No physics for the concrete room filling up with the rain sound from the opening. 3i switched Soft to Current mid-raid. |
| 20261106_214805 | Ground Zero | no | High | 0.6.0 | **Rain. Fixes 9 and 9a on.** "Really good", garage "big improvement", hallway good. Capital back: rain through concrete walls. Mark 1 (105.3, 25.0, 276.9): 6/128 paths escaped (porch, stuck shards); level 0.18–0.23 (−7 dB), 85% build-up (p 0.016, a 0.06), focus 0.07, arrival az jumping −180…+36. Whole raid: arrival step median 36° per 0.125 s, level step p90 0.7 dB, focus < 0.3 in 94% of rows. 6 beds movable, 0 fixed. COST: v2 21.8 ms/s (0.12 ms/frame, max 54), total 37 ms/s |
| 20261106_220805 | Ground Zero | no | High | 0.6.1 | **Rain, 9 + 9a on.** "Sounds better." Mark 1 (101.7, 29.7, 317.4, Capital): rain from the back wall. 10 of 11 escaping paths had passed through `Capital_Indoor_1F_COLLIDER` (low-detail whole-floor shape, the 5% full-strength leak; one down through the floor to TEMP_GROUND 48 m below) -> fixed in 0.6.2. Distant gunfire (SPTBattleAmbience) through building walls (needs openings positioning, step 6). In and out in the hallway as fewer paths escape (marks 4-5: 8 and 2 of 128). Garage area (mark 3) 93 of 128 escape. COST: v2 16.1 ms/s (was 21.8), total 30.5 ms/s |
| 20261107_090244 | Ground Zero | no | High | 0.6.2 | **Rain, 9 + 9a on.** Sound from inside the building "improved"; outdoor ambience still too loud too far into the building. Lowering `9e` afterwards was better but then it jumped in and out. Mark 1 (139.0, 29.7, 265.5, Capital): 2/128 paths out (one via shattered glass), traced −24 dB, level −14 dB = 88% build-up (p 0.002, a 0.052). Mark 2 (118.5, 25.0, 265.4): 5/128 out, no build-up, level swinging −15.7…−21.3 dB within 1.5 s. Enclosed rows: build-up a median 62% of the level. -> 0.6.4 (measured build-up, guided rays, smoothing by confidence). No errors.txt. COST: v2 16.3 ms/s, total 32.2 ms/s |
| 20261107_094202 | Ground Zero | **yes** (forgot to turn off) | High | 0.6.4 | **Rain, 9 + 9a + 9f on, 29 min.** Mark 1 (106.4, 29.7, 313.6, Capital 1F): rain from both sides. Mark 2 (114.3, 29.8, 310.2): rain should come from the openings, not bounce loudly off the walls. Mark 3 (101.1, 29.7, 315.0): rain from behind; "almost like the bounce is deciding the direction instead of how loud it is". **Measured build-up worked:** 3–5× below the old formula (mark 1 tail 0.014 vs tailOld 0.05–0.07), level −18 dB (was −14). **But:** at marks 1–3 no path really reached outside. The only escapes (0–2 per window) went down through the 1F floor to `TEMP_GROUND_COLIDER` 48 m below and back up through the building (raycasts don't hit the back faces of one-sided floor meshes), and with focus ≈ 1 each one set the direction alone (arrival jumping −173…+23° within 3 s). The level was 85–90% build-up, which had no direction of its own. Bots: 492 shots from 16 shooters, 65 within 50 m, gun mixer flip-flops 32; user: gunfights "pretty good" for locating, the receiving end "could maybe be better". COST: v2 0.153 ms/frame at ~107 fps, 16.3 ms/s, 3,335 rays/s; total 31.4 ms/s |
| 20261107_105224 | Ground Zero | no | High | 0.6.6 | **Rain, 9 + 9a + 9f on, `9g` High by the end, 17 min, 12 marks.** "Overall an improvement", garage (mark 12) "sounds good", deep rooms quieter (Capital marks 1–6: −17 to −31 dB, was −14 to −18). Tag 1 (138.8, 25.0, 275.5): overlay "focus near" jumps 0–0.5 (1–6 paths out; ESS scaling gives 0.25 for one path; it is × the near part's share, ~5% there, so barely audible). Tag 2 (123.1, 29.7, 283.3): distant gunfire (SPTBattleAmbience; no bots) through the walls: not built yet (exit points + gunfire through openings). Tags 3–6: rain should come from the building's front opening; still a little "through the walls", wind too; "maybe it was better when sounds from openings slid to 100%" (more focused). Data: the build-up's direction pointed at each escape's last contact, which is mostly a floor spot (exitEl −40 to −75°), from 3–8 escapes per 3 s at marks 4–6, so it wandered; focus 0.07–0.3 → spread 140–170° (all round). Car dealer (marks 7–11, 92.3, 28.9, 218.6; 9–11 one spot): roof rain "stops or gets quieter suddenly"; heard on the 2nd floor, not the 1st. Data: roof rain follows the old probe's floors-above count (one ray straight up): on the 1st floor near (100–102.5, 24.4, 227) it flipped 0→1→0 within 0.5 s (702.4, 836.5–839.5), so roof rain jumped 30% → 100% → 30% at the edge of the mezzanine. Nakatani hallway: good with one ear towards the main area, too loud with both (headset: Turtle Beach Stealth 700 Gen 2 Max). COST at High: v2 0.165 ms/frame at ~192 fps, 31.7 ms/s, 8,828 rays/s; total 46.2 ms/s (4.6% of a core). Back faces hit 590,753 (6.7% of rays), far sides skipped 128,081, upward escapes rejected 2,617 |
| 20261107_112558 | Ground Zero | no | High | 0.6.7 | **Rain, 9 + 9a + 9i on, `9g` High → Ultra, `9d` 22 → 25 dB, 8 openings 0.87 → 0.70 during the raid; 7 marks.** Tag 1 (104.4, 19.1, 174.6; 4 floors above): rain heard with `9c` MetaHrtf, not with the panner. The probe level there was 0 (−40 dB floor of the level), so the rain came through Meta's own reflections/reverb send, which bypasses the outdoor mixer volume fix 9 sets. Car dealer tags 2–5 (93.0–94.0, 28.8, 217.4–219.4; within 2 m): roof rain silent at 2, back at 3 and 4, gone at 5; "overall rain sounds better in this location". Our roof rain factor was steady (0.55, roof level 0.28–0.33, ~1,650 roof paths per 3 s) and the game switched no room or indoor/outdoor state at the tags, so the on/off is in the game's indoor rain player itself (its own volume, playback or voice) or in how it is played once made directional (2 sources movable); not logged yet. Mark 6 (119.0, 29.7, 306.4, Capital top floor): roof level 0.95. Stairwell (mark 7, −15.4, 22.0, 335.1; 4 floors above): rain audible though one ray goes out: level −28 to −31 dB, all build-up (13 escapes per 3 s at Ultra), roof rain 0.01. COST: total 60.9 ms/s (6.1% of a core) at Ultra, 16,459 rays/s |

Findings per raid are in [../00_WORKING_NOTES.md](../00_WORKING_NOTES.md).
