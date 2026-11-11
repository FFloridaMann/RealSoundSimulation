# Tracker

One line per piece of work, in work order. Tick a box when it's done (on GitHub you can tick it straight from the phone).
Why each step sits where it does: [ROADMAP.md](dev/ROADMAP-DETAILED.md).

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
- [x] Decide: footsteps and other sounds too? Yes (user, 10 Nov 2026): step F
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
- [x] Research each surface: [MATERIALS.md](dev/MATERIALS.md) (absorption by frequency, transmission loss; no usable scattering data)
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
- [x] `6.5` door lookup crashed in SPT; doors now read from the scene (0.6.52)
- [ ] Raid: `6.5` off and on behind closed doors; check the CLOSED DOORS line reads real materials
- [x] Explosion recording by how open the blast is; close recording reaches further (`6.6`-`6.6b`, 0.6.49; starting values)
- [x] Outdoor recording when you're outside (`6.6c`, 0.6.53; starting value)
- [ ] Raid: beside a garage with a grenade inside, `6.6c` at 0 and 0.75
- [x] Shock wave: pressure hit, closer thump, dip in other sounds (`6.9`-`6.9c`, 0.6.53; by ear)
- [x] Long baked routes checked against the corner search; clear windows play from the window (0.6.57; hand-set limits)
- [ ] Raid: a grenade in a building you're outside of; check blast.csv and glass.csv `reason` and `pathM`
- [x] Concussion by blast pressure, with an ears level and a concussion level (`6.9k`-`6.9m`, 0.6.60; hand-set)
- [ ] Raid: grenades in the same room, a small room, a big hall, through an open door and behind a solid wall; check concdose.csv and set `6.9l` and `6.9m` by ear
- [x] Your room builds up a blast that comes in from elsewhere (`6.9n`, `6.9o`, 0.7.1; hand-set)
- [ ] Raid: a grenade in a hall while you're in a small side room with the door open, `6.9n` off and on; check concdose.csv `yourRoomDb`
- [x] Soft concussion cut: world goes at once, blast tail fades, felt layers survive (`6.9h`-`6.9j`, 0.6.58; by ear)
- [ ] Raid: a grenade inside 6.5 m, `6.9h` off and on; set `6.9i` and `6.9j` by ear; check concussion.csv
- [x] Separate depth for the thump while concussed (`6.9p`, 0.7.7; user asked for its own Hz slider)
- [ ] Raid: set `6.9p` by ear inside the concussion cut; thump.csv `cutoffHz` shows the value used
- [x] Separate depth for the open-air thump, no wall in the way (`6.2k`, 0.7.8; user asked for its own Hz slider)
- [ ] Raid: an outdoor grenade and one in your room, set `6.2k` by ear; thump.csv `walls` 0 rows show it
- [ ] Ask harmony how he wants the credit and year written (`THIRD_PARTY_NOTICES.md`)
- [x] Fragment pass-bys along the real flight path, with cracks up close (`6.7f`-`6.7i`, 0.6.56; by ear)
- [ ] Raid: your own grenade and a bot's through a door, `6.7f` off and on; check passby.csv and the audio thread's late blocks
- [x] Fragment hits from other rooms by route (`6.7c`-`6.7e`, 0.6.55; starting values)
- [ ] Raid: a grenade in another room, `6.7c` off and on; check fragments.csv
- [x] Shock wave through the building: by route, corners, enclosed boost, repeat hits, slow push (`6.9d`-`6.9g`, 0.6.54; by ear)
- [ ] Raid: `6.9` off and on at 3, 10 and 25 m, in the same room, around a corner and behind a shut door; set `6.9a`, `6.9c`, `6.9e`, `6.9f` and `6.9g` by ear
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
- [x] Your own room counts for blasts, concussion part (user, 10 Nov 2026): built in 0.7.1 as `6.9n`/`6.9o`, see above
- [ ] Your own room counts for the shock wave sound too: the hit's closed-room boost, repeat hits and push still use only the blast's room (`ShockWave.FireCore`)

## 6.3 · Echoes for bot gunshots and grenades
- [x] One headroom cap counting every echo type (0.6.61; the thump and shock layers still go first, at most a few)
- [x] Your gun's guaranteed share of echo voices; nothing else can touch it (`6.3n`, 0.6.61)
- [x] Log voices over time by type (voices.csv, 0.6.61)
- [ ] Measure voice peaks (raids that measure voices only)
- [x] Echoes per shooter, not per round (`6.3h`, `6.3j`, 0.6.61)
- [x] Audibility gate (`6.3m`, 0.6.61)
- [x] Rank bots by arriving level, with a ceiling (`6.3k`, 24, 0.6.61)
- [x] Each shooter's first echo before another's third (0.6.61)
- [x] A shooter keeps its echo count through a burst (0.6.61)
- [x] At the limit, replace the quietest playing echo with a 40 ms fade (`6.3p`, 0.6.61; only bot echoes can be replaced)
- [x] F12 settings: ceiling, threshold, your gun's share (`6.3k`, `6.3m`, `6.3n`, 0.6.61)
- [ ] Raid with bots: your own echoes never pushed out by bot echoes; how many bot echoes don't start
- [ ] Tune from raids: if it sounds off or there are lots of drops, change the limits, even if your own gun loses some echo bounces (user, 9 Nov 2026)
- [x] Grenade and explosion echoes (`6.3`, `6.3a`-`6.3d`, 0.6.39; starting values)
- [ ] Raid: explosion echoes in a concrete room and outdoors, `6.3` on and off; do they duck with Bring Back Concussion?
- [ ] Tune `6.3a` to `6.3d` by ear

---

## W · Weather
- [ ] Research rain, humidity, wet surfaces, wind, drops
- [ ] Decide what echoes (4) and 4b should do
- [ ] Retune (after M, before the headset)

## 5 · Headset (moved after step 6)
- [x] For v1.0, any headset takes away ringing and most of the hearing cut, one slider (`6.9q`, 0.7.10, user 10 Nov 2026)
- [ ] Raid check: a close grenade with and without a headset
- [ ] Later, in the revamp: each headset's own protection, optionally simulated (replaces the flat `6.9q`)
- [x] Headset sound processing + bench (offline)
- [ ] Headsets revamp: bring the headset processing into the game, with its own on/off switch at the top of F12 (user, 10 Nov 2026)
- [ ] Tune it
- [ ] Re-check the 4b mix through it
- [x] Ear ringing from gunfire with no ear pro (`5r`, 0.7.13): ported from the earlier audio mod's "Gunfire hearing": calibre, muzzle device, barrel and ammo, subsonic left out, the measured room instead of the indoor flag, others' shots through walls by occlusion; the game's own ring sound; switch at the top of F12
- [ ] Raid check 5r (0.7.14 tiers): a few shots then a lot, without a headset, in a small room and outdoors; pistol with normal and subsonic ammo; suppressed; with a headset; summary GUNFIRE HEARING
- [ ] Final pass: simulate the noise level at your ears with the probes, indoors and outdoors (user, 10 Nov 2026)

## 6b · SEB on full-auto guns
- [x] `0 Gun sounds` switch SPT / SEB (0.6.3)
- [ ] One SEB take per round while the trigger is held
- [ ] Varied takes, tail on release, 2–3 takes overlap at most

## F · Footsteps and everyday sounds (user, 10 Nov 2026)
- [x] Footsteps, gear, looting, voices and everything the hands play (meds, food, reloads) from other rooms come from the opening, muffled by their route (6.1's rules) (0.7.15)
- [x] Above or below you: through the floor by material and thickness (6.2's measure), at the true position so the height stays; footsteps a dull thud, airborne sounds very muffled; airborne sounds take a stairwell route when there is one (0.7.15; user: "hard to tell if someone is above or below you")
- [x] Own on/off switch at the top of F12, off by default (0.7.15)
- [x] Through walls, doors and windows too, by material; both ways at once when the quieter is within 10 dB (0.7.17)
- [ ] Raid: footsteps upstairs, downstairs and in the next room, a bot healing or looting, switch on and off; summary BODY SOUNDS
- [ ] Raid: a bot behind a thin wooden wall, behind concrete with a doorway, behind a closed door; second way on and off
- [x] EFT 1.0's extra step layer (`PlayerStepLayerAudioController._source`, outdoors only) treated as footsteps; when it gives its source back (going indoors) ours is made neutral first (user: "add it")

## B · Bots feel blasts too (user, 10 Nov 2026)
- [ ] Find what SAIN and the game read for a bot's hearing and aim after an explosion
- [ ] Use the same blast pressure (`ShockWave.Dose`): concussed or deafened by the route, the walls and the blast's room
- [ ] Without SAIN: the game's own bot hearing; with SAIN: its hearing and aim
- [ ] Own on/off switch at the top of F12 (it changes how fights play)
- [ ] Raid: grenades into a room of bots, on and off

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
- [x] Short bursts: give a ringing tail its own voice instead of the next tail stopping it (two voices per gun, raid 38; 1f, 0.7.12, other guns only)
- [x] 1f reserve removed, at most 8 ringing (0.7.18): it ran once in raid_20261110_221827
- [ ] Raid check 1f: distant semi-auto and short bursts ring out; summary shows spare voices used and none short
- [ ] End quiet echoes early (each holds a voice for the whole 2.5–5 s recording)
- [ ] Full-auto: full echoes on the first round and the tail, only the loudest 1–2 through the burst
- [ ] Zero-voice tier for shooters who miss the ceiling, with an F12 switch (moved from 6.3)
- [ ] Give things up in stages: fewer echoes per shooter, then shorter, then zero-voice, never a hard stop
- [ ] Masking: thin out everyone else's echoes while you're firing
- [ ] Lower the echo ceiling when the audio thread falls behind
- [ ] Retune the voice priority from raids (`6.3j`, `6.3k`, `6.3m`, `6.3n`, `6b`); your gun's echo count can come down if it helps the whole mix
- [ ] Test on Factory and Labs (the most echoes at once)
- [ ] Release build, 1: `9w` no longer depends on logging (`EnvProbe.RoomPostfix` records the room state first)
- [ ] Release build, 2: one master diagnostics switch; keep `DiagRunner`'s raid resets and the three probes running
- [ ] Release build, 3: count `LastPlayingInContainers` on its own for `OwnEchoes`, stop building container rows
- [ ] Release build, 4: one raid with FrameDiag and logging off: `DiagRunner` spikes gone, roof rain still fades in no-rain rooms
- [ ] Code pass, 1: acoustic probe (~0.10 ms/frame) and enclosure probe, same results for less work
- [ ] Code pass, 2: `ClosedDoors` re-scanning every 3–4 s (~60 ms) during the deploy countdown
- [ ] Code pass, 3: load-in spikes in `AmbienceMix` (110–165 ms) and `DoorwayBlend` (37–55 ms)

## CO · Fika and co-op (before v1.0)
- [x] Read-only review against Fika 2.4.6 (10 Nov 2026): Fika patches none of this mod's methods; another player's shot reaches `FireBullet` as third-person, so it's treated as another gun (a bot's), not your own; each grenade explodes once on each machine; raid detection works; Fika is CC BY-NC-SA, so it's only looked up by name
- [x] Headless host: the mod stays off with no graphics, and turns itself off at raid start if Fika says it's the headless host (0.7.11)
- [x] Bug report folders get the process ID when Fika is installed, so two copies on one PC don't share a folder (0.7.11)
- [ ] Two-player test raid, both with bug report logs on: shots at 20 and 80 m, through a doorway and from another room; grenades from each player and a bot (one blast and one concussion result per grenade on each side); a close one with a headset; doors; die and spectate, then a second raid
- [ ] Only if the test shows it: another player's full-auto gets a tail and echoes mid-burst when the network lags (Fika ends a burst after 0.1 s without a shot); hold their end-of-burst echoes about 150 ms
- [ ] Echoes for the other player's gun, through the bot echo system
- [ ] Player 2 bonus in the ranking only (start about +6 dB); their loudness doesn't change
- [ ] Voice and culling work holds up with two players and bots (needs 6.3 and E first)
- [ ] Co-op raid with both players running the mod: echoes heard, no extra cut-offs

---

## Any time
- [ ] G: ground between you and a distant shooter (grass vs asphalt; a filter, never an echo)
- [ ] Distant obstruction (past ~250 m)
- [ ] Server settings (solutions doc phase 1)

## S · Settings clean-up (near the end)
- [ ] List all 158 settings, what each does, and what still reads it
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
