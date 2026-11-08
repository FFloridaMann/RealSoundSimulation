# Tracker

One line per piece of work, in work order. Tick a box when it's done (on GitHub you can tick it straight from the phone).
Why each step sits where it does: [ROADMAP.md](ROADMAP.md).

**Now:** 0.6.23 (shot overlap) and 0.6.24 (gunshots from the opening) waiting for a raid

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
- [ ] Raid: compare `6.1` on and off with a bot in another room
- [ ] Decide: footsteps and other sounds too?
- [ ] Check: bot gunshots underground vs above ground
- [ ] Floors with no baked route (raid 36: 9 shots from 5 m below): needs the through-floor path in 6.2, not "fully blocked"
- [ ] Check: bot gunshots when you cross a threshold
- [x] A bot's first burst starts un-muffled, then gets cut down (raid 34): `1d First burst muffled` (0.6.21)
- [ ] Raid: compare `1d` on and off in a distant fight (raid 35: never triggered)
- [x] Shots "cut off" in distant fights (raid 34, mark 1): log what ends them early (botshots.csv, 0.6.21)
- [ ] Shots cut by the same gun's next sound: the only cause seen so far (raid 36: 89 of 224 sounds, average 1.85 s lost, 62 tails and 27 shots; raid 35: 9 of 47; 0 muted, 0 no free voices in both). Same mechanism as the STM-9 with the FRT. The old Gun Audio mod's "Let shots overlap" fixed it and was removed 5 Nov
- [x] Port the overlap into Real Sound Simulation: `1e Shots ring under the next` (0.6.23)
- [ ] Raid: compare `1e` on and off (bots and your STM-9)
- [x] With overlap: if the game's gun voices run out, take the voices of the gun closest to finishing so a shot never plays silent (0.6.23)
- [x] shots.csv: time is the game clock, not raid seconds (fixed 0.6.21)

## M · Realistic materials
- [ ] Research each surface (absorption, transmission, resonance)
- [ ] Replace the tables in fix 6 and the room measure
- [ ] Roof material (4b phase 3)
- [ ] Retune: `9l` rain floor, `9k` exit credits, roof rain 0.85 and ×0.7, echo levels

## 6.2 · Through walls, by material
- [ ] Voice reserve 16 → 32
- [ ] Count the mod's own non-echo voices in the cap
- [ ] Through-wall loss by material
- [ ] Through-wall thump layer + cutoff slider (default 50 Hz)
- [ ] Separate cutoffs: explosions vs gunshots

## 6.3 · Echoes for bot gunshots and grenades
- [ ] One headroom cap counting every echo type; bots get their own share
- [ ] Log voices over time by type (only peaks exist today; raid 35's peaks summed to 208 of 196)
- [ ] Measure voice peaks (raids that measure voices only)
- [ ] Echoes per shooter, not per round
- [ ] Audibility gate
- [ ] Loudest-first queue with a ceiling (start 24)
- [ ] Zero-voice tier for the rest
- [ ] F12 settings: ceiling, threshold, tier switch
- [ ] Grenade and explosion echoes (find the explosion's play path; must duck with Bring Back Concussion)

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
- [ ] End quiet echoes early (each holds a voice for the whole 2.5–5 s recording)
- [ ] Full-auto: full echoes on the first round and the tail, only the loudest 1–2 through the burst
- [ ] Test on Factory and Labs (the most echoes at once)

---

## Any time
- [ ] Distant obstruction (past ~250 m)
- [ ] Server settings (solutions doc phase 1)

## Possible bug fixes later
- [ ] Bring Back Concussion restores mixer volumes from before the blast when its fade ends. If you cross a doorway during those seconds, the indoor ambience can stay at the old level until it next changes (fix 3 only writes it when it changes)

## Not in Real Sound Simulation (separate mods)
GC and spawn stutters, branch `claude/gc-and-spawn-stutters`. Fix before using its GC mode:
- [ ] Ceiling check can collect every frame (needs a minimum interval)
- [ ] Game switching GC off at raid start can undo Manual mode and break the restore
