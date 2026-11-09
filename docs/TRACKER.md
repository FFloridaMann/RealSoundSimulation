# Tracker

One line per piece of work, in work order. Tick a box when it's done (on GitHub you can tick it straight from the phone).
Why each step sits where it does: [ROADMAP.md](ROADMAP.md).

**Now:** grenades from the opening, bounces and open-air thump (`6.4`, `6.2i`, 0.6.41-0.6.42) built, not raided: grenades in another room and in the open, in a raid (the hideout has the thump and echoes since 0.6.45, but no opening, bounces or logs). Route dulling (`6.1b`, 0.6.40) ran in two raids, not yet confirmed by ear. Explosion echoes (`6.3`, 0.6.39) also need a no-AI raid. 6.2 (0.6.38) needs a louder test and a `6.1` test on Factory · **Next:** bot echoes, the second half of 6.3. Further rain tuning is in step E

---

## ✅ 1 · Doorway fix + enclosure probe
- [x] Keep the same doorway through a crossing
- [x] Enclosure probe with logging
- [x] Tested in raid 6

## ✅ 2 · Own-gun room reverb (setting 5)
- [x] `5` at 0 dB
- [x] `5a` +6 dB in hard rooms (stairwells)

## ✅ 3 · Own-gun indoor/outdoor blend (3c)
- [x] Ground fix (0.3.0)
- [x] Room = walls × roof, size, windows (0.3.1)
- [x] Roof-right-above-you test (0.3.2)
- [ ] Material tuning → goes with **M**

## 🔧 4 · Echoes for your own shots
- [x] Built (semi-auto + full-auto)
- [x] Heard in raid 7: "much much better"
- [x] Echo pool 96, voice stealing, headroom cap
- [ ] Doubled-reverb tuning (`6f` early echo trim)
- [ ] Retune after **M**

## 🔧 4b · Outside sound heard indoors
- [x] Phase 1 and 2 (exposure, rain under the real roof)
- [x] Phase 4: sound from the openings
- [x] Probe v2: build-up, guided rays, ray quality `9g`
- [x] Roof rain traced from the roof, exit points `9j`, distant gunfire through openings `9k`
- [ ] Roof holes
- [ ] Phase 3: roof material → goes with **M**

---

## ✅ 6.0a · Audio-thread timing (measure)
- [x] Built on branch `claude/selfcheck-foreign-patches-block-timing`
- [x] Merge into main (as 0.6.20)
- [x] Add a "how close to the deadline" measure: Unity's audio counters, if the game offers them (0.6.20)
- [x] Raid with it on (raid 34, Factory, 8 Nov): only 2 audio counters exist (memory, both read 0), so no DSP load is available; late blocks: 6.8% over 1.5x, 0.09% over 2x ("marginal")

## ✅ 6.0b · Ray batch wait on busy CPUs (measure)
- [x] Log the batch wait per frame (timing.csv, slowframes.csv; 0.6.20)
- [x] Raid with it on (raid 34, Factory): avg wait 0.001 ms, max 0.53 ms, 0% of frames waited
- [x] Decide: **neither** (the probe never makes a frame slow on 16 cores; re-check only on a PC with fewer cores)

## ⏭ 6.1 · Portal positioning ← **next**
- [x] routes.csv: each bot gunshot's opening and route (measure only, switch off by default; 0.6.22)
- [x] Raid + review routes.csv (raid 36, Factory: 156 of 171 shots had a route; median 33 deg from the true direction, 40% over 45; best opening changed 7 times; 9 shots from a floor below had no baked route)
- [x] Move bot gunshots to the opening they come through (`6.1`, 0.6.24)
- [x] Raid: compare `6.1` on and off with a bot in another room (raid 38: kept on; moved 31 deg on average; user: on sounded better, given gunshots through walls are coming)
- [ ] Decide: footsteps and other sounds too?
- [ ] Check: bot gunshots underground vs above ground
- [ ] Floors with no baked route (raid 36: 9 shots from 5 m below): needs the through-floor path in 6.2, not "fully blocked"
- [ ] Check: bot gunshots when you cross a threshold
- [x] A bot's first burst starts un-muffled, then gets cut down (raid 34): `1d First burst muffled` (0.6.21)
- [ ] Raid: compare `1d` on and off in a distant fight (raid 35: never triggered)
- [x] Shots "cut off" in distant fights (raid 34, mark 1): log what ends them early (botshots.csv, 0.6.21)
- [ ] Shots cut by the same gun's next sound: the only cause seen so far (raid 36: 89 of 224 sounds, average 1.85 s lost, 62 tails and 27 shots; raid 35: 9 of 47; 0 muted, 0 no free voices in both). Same mechanism as the STM-9 with the FRT. The old Gun Audio mod's "Let shots overlap" fixed it and was removed 5 Nov
- [x] Port the overlap into Real Sound Simulation: `1e Shots ring under the next` (0.6.23)
- [x] Raid: `1e` on (raid 38): cuts 40% to 31%, average lost 1.85 s to 1.40 s; semi-auto now loses very little, short bursts still cut by the next tail
- [x] With overlap: if the game's gun voices run out, take the voices of the gun closest to finishing so a shot never plays silent (0.6.23)
- [x] shots.csv: time is the game clock, not raid seconds (fixed 0.6.21)

## M · Realistic materials
- [x] Research each surface: [MATERIALS.md](MATERIALS.md) (absorption by frequency, transmission loss; no usable scattering data)
- [x] One table, `Acoustics/Materials.cs`; fix 6 echoes use it (`6g`, 0.6.25)
- [x] Raid 39: compare `6g` on and off: "def sounds better", stays on
- [x] Probe v2 uses the table too (`9o`, 0.6.27)
- [x] Raid 40: `9o` on: rain deep in buildings "mostly sounds better"
- [x] Raid 41: the old top-roof test was wrong at 4,740 of 28,043 spots with floors above: `9p` confirmed
- [ ] Raid: `9r` in a metal trailer or under tin
- [x] Probe memory reacts to real changes instead of speed (`9s`, 0.6.32; raid 41 tag 4)
- [ ] Raid: walk into a roofed room and stop, repeat slowly, stand by a window (`9s` on and off)
- [ ] Decide: rays traced from ahead of you for the walking lag
- [x] Roof rain has its own level (`9t`, 0.6.33; one level from 0.6.34)
- [x] Roof rain fades down shafts (`9u`, 0.6.34; raid 43 tag 6)
- [ ] Raid: rainy stairwell with `9u` at 0 and 1
- [x] Floor count includes large low-detail floors (`9v`, 0.6.36; Capital meeting room tag)
- [ ] Raid: roofed office with floors above in rain, `9v` on and off (`floorsNow` against `floorsOldNow`)
- [x] Roof rain respects rooms the game gives no rain (`9w`, 0.6.36; Capital tags)
- [ ] Raid: Capital in rain with `9w` on and off, listen for fades at room edges
- [x] Shot log names the gun (ownshots.csv `gun`, 0.6.36)
- [x] Raid: one raid on SEB + True Acoustics (Real Sound Simulation out), 8 Nov: no blending between inside and outside; some general audio better; Real Sound Simulation continues
- [x] Take SEB's footstep and handling settings (`10a`, 0.6.35) and route files (`10b`, 0.6.35)
- [ ] Raid: `10a` on and off (restart between), footsteps and inspections
- [ ] Raid: `10b` on and off: fights in other rooms, routes.csv `noRoute`
- [ ] Decide: SEB's 1.0 mixer if `10a` isn't what was heard
- [ ] Look at True Acoustics' through-wall by material for 6.2
- [x] Credit red_the_fox (SEB, MIT): README and THIRD_PARTY_NOTICES.md
- [ ] Credit Kaan (True Acoustics, MIT) if anything of theirs is used
- [x] Decide: load SEB's route files (`10b`) instead of baking
- [x] Roof material (4b phase 3): floors above take off their material's loss (`9p`, 0.6.29)
- [ ] Retune: `9l` rain floor, `9k` exit credits, roof rain 0.85 and ×0.7, echo levels

## 6.2 · Through walls, by material
- [x] Voice reserve 16 → 32 (0.6.37)
- [x] Count the mod's own non-echo voices in the cap (0.6.37)
- [x] Through-wall loss by material and thickness (0.6.37; starting values)
- [x] Through-wall thump layer + cutoff slider, default 50 Hz (`6.2`, 0.6.37)
- [x] Separate cutoffs: explosions vs gunshots (`6.2c`, 0.6.37)
- [x] Walls can stop a sound: reach by distance and wall, earth 75 dB (`6.2e`-`6.2g`, 0.6.38; starting values)
- [ ] Raid with bots: a fight behind a wall or on another floor, `6.2` on and off; read thump.csv
- [x] Raid: explosions behind a wall reach the thump (raid_20261109_002546: 7 grenades, 5 played, 1 stopped by walls, 1 no wall). Still to judge by ear
- [ ] Raid: does terrain under a building count as `earth` for shots in the basement?
- [ ] Tune `6.2a`, `6.2b`, `6.2d`, `6.2f`, `6.2g` by ear
- [ ] Decide: where there's no route, drop the game's muffled voice so the thump plays alone
- [ ] Test on Factory (user, 9 Nov 2026): gunshots from the opening plus the thump from the true direction may be disorienting. `6.1` on and off, then `6.1a` at 0.5
- [x] Dull a bot's shot by its route: wall on the line, turn, openings (`6.1b`-`6.1d`, 0.6.40; starting values)
- [ ] Raid: `6.1b` on and off with bots in other rooms; does the filter work through the Meta spatializer; is `6.1d` too dull on long routes
- [x] Explosions from the opening, dulled by route; louder thump from the true spot (`6.4`-`6.4b`, `6.2h`, 0.6.41; starting values)
- [x] Grenade bounces from the opening; thump with no wall in the way (`6.2i`, 0.6.42; starting values)
- [x] Room echoes from the opening (`6.3e`-`6.3g`, 0.6.48; hand-worked levels)
- [ ] Raid: `6.3e` off and on with a grenade in the next room; `6.3f` by ear
- [x] Closed doors muffle by material (`6.5`, `6.5a`, 0.6.47; hand-set values)
- [ ] Raid: `6.5` off and on behind closed doors; check the CLOSED DOORS line reads real materials
- [x] Explosion recording by how open the blast is; close recording reaches further (`6.6`-`6.6b`, 0.6.49; starting values)
- [ ] Raid: grenades at a garage front, deep inside and in the open; check blastrec.csv `gameEnv` against `outdoorWeight`
- [x] Glass from other rooms by route (`6.7`-`6.7b`, 0.6.49; starting values)
- [ ] Raid: break a window from another room, `6.7` off and on; glass.csv
- [x] Blasts around a corner where both of you are outdoors (`6.8`, `6.8a`, 0.6.50; starting values)
- [ ] Raid: a grenade behind a building, front of and behind an entrance, `6.8` off and on; blast.csv `reason`
- [x] Corner search from both ends, reuse, probe openings, worker threads (0.6.51)
- [x] Bot gunshots around a corner (`6.8b`, 0.6.51; starting values)
- [x] Blast arrives by its route, thump first (`6.4d`, 0.6.51)
- [ ] Raid: bots firing behind buildings outdoors, `6.8b` off and on; thump before bang through a doorway, `6.4d` off and on
- [x] Blasts dulled less than gunshots (`6.4c`, 0.6.46; starting value)
- [ ] Try Bring Back Concussion's `Explosion Effect Radius` above 7 m (raid_20261109_100114: every blast 10 m or more away got no concussion)
- [ ] Raid: `6.4` on and off with grenades in another room; `6.4a` at 0.5 if disorienting; `6.2h`, `6.2i` and `6.2j` (length and fade, 0.6.43-0.6.44) by ear, close blasts first
- [ ] G36 never blends indoor/outdoor (raid_20261109_083938: 0 of 26 shots; AK-74, AK-12 and MP5 all blended). Looks like one recording. Test an AK-12 at the same spot, then the G36 with setting 0 on SEB
- [ ] Factory with `10b` off: raids before it read 0.34-0.52 "blocked" at 15-60 m, with it 0.18 (one raid each way can't separate this from where the bots were)
- [ ] Later (user, 9 Nov 2026): the room changes the thump. A shot in a small hard room pushes more through the wall than one in a big hall; with your head next to the wall it's bigger and less pinpointed. Today only total distance and the walls count

## 6.3 · Echoes for bot gunshots and grenades
- [ ] One headroom cap counting every echo type; bots get their own share
- [ ] Log voices over time by type (only peaks exist today; raid 35's peaks summed to 208 of 196)
- [ ] Measure voice peaks (raids that measure voices only)
- [ ] Echoes per shooter, not per round
- [ ] Audibility gate
- [ ] Loudest-first queue with a ceiling (start 24)
- [ ] Zero-voice tier for the rest
- [ ] F12 settings: ceiling, threshold, tier switch
- [x] Grenade and explosion echoes (`6.3`, `6.3a`-`6.3d`, 0.6.39; starting values)
- [ ] Raid: explosion echoes in a concrete room and outdoors, `6.3` on and off; do they duck with Bring Back Concussion?
- [ ] Tune `6.3a` to `6.3d` by ear

---

## W · Weather
- [ ] Research rain, humidity, wet surfaces, wind, drops
- [ ] Decide what echoes (4) and 4b should do
- [ ] Retune (after M, before the headset)

## 5 · Headset (moved after step 6)
- [x] Headset sound processing + bench (offline)
- [ ] Bring it into the game
- [ ] Tune it
- [ ] Re-check the 4b mix through it

## 6b · SEB on full-auto guns
- [x] `0 Gun sounds` switch SPT / SEB (0.6.3)
- [ ] One SEB take per round while the trigger is held
- [ ] Varied takes, tail on release, 2–3 takes overlap at most

## C · Supersonic crack by calibre
- [ ] Solutions doc 2.9: where and when the crack plays
- [ ] Mapping: speed, diameter, length, miss distance
- [ ] Option 1: shape the 4 existing clips
- [ ] Option 2 (maybe): synthesise the crack

## 7 · Gun overhaul, every gun (last)
- [ ] A way to measure real loudness while guns play
- [ ] Pass over every gun (body, tail, silenced, near/far, indoor/outdoor, auto loops)
- [ ] Final check of the headset's compressor

## E · Final echo tuning and optimising (last)
- [ ] Tune how many echoes each sound gets (own shots, bots, grenades)
- [ ] Tune tail length
- [ ] Rain (moved here 8 Nov, user): roof rain level (`9t`, `9r`, `9u`, `9v`, `9w`), the in-and-out deep inside, a distance fade for outside sound on bounced paths
- [ ] Own roof-rain sounds: patter loops per roof material, generated offline (or with step 7)
- [ ] Short bursts: give a ringing tail its own voice or fade it, instead of the next tail stopping it (two voices per gun, raid 38)
- [ ] End quiet echoes early (each holds a voice for the whole 2.5–5 s recording)
- [ ] Full-auto: full echoes on the first round and the tail, only the loudest 1–2 through the burst
- [ ] Test on Factory and Labs (the most echoes at once)

---

## Any time
- [ ] G: ground between you and a distant shooter (grass vs asphalt; a filter, never an echo)
- [ ] Distant obstruction (past ~250 m)
- [ ] Server settings (solutions doc phase 1)

## S · Settings clean-up (near the end)
- [ ] List all 125 settings, what each does, and what still reads it
- [ ] Remove decided A/B switches and their old code (`6g`, `9o`, `9p`, `9s`, `1d`, `1e`, `3n`, `3o`)
- [ ] Group by what you hear: own gun, bot gunfire, rain and ambience, diagnostics
- [ ] Migrate renamed settings so your values survive; back up the config first
- [ ] Defaults = the tested setup; plain-language descriptions

## Possible bug fixes later
- [x] Fire, then turn around: your own shot's ring stayed locked to your head (raid 40 tag 1): `3o` (0.6.30)
- [ ] Raid: fire and turn at half-open spots with `3o` at 0 and 1
- [ ] Bring Back Concussion restores mixer volumes from before the blast when its fade ends. If you cross a doorway during those seconds, the indoor ambience can stay at the old level until it next changes (fix 3 only writes it when it changes)

## Not in Real Sound Simulation (separate mods)
GC and spawn stutters, branch `claude/gc-and-spawn-stutters`. Fix before using its GC mode:
- [ ] Ceiling check can collect every frame (needs a minimum interval)
- [ ] Game switching GC off at raid start can undo Manual mode and break the restore
