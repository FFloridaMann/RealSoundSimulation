# Tracker

One line per piece of work, in work order. Tick a box when it's done (on GitHub you can tick it straight from the phone).
Why each step sits where it does: [ROADMAP.md](ROADMAP.md).

**Now:** 0.6.20 + server GC change (25 → 64) waiting for a raid · **Next build:** 6.1 routes.csv

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

## ⏭ 6.0a · Audio-thread timing (measure)
- [x] Built on branch `claude/selfcheck-foreign-patches-block-timing`
- [x] Merge into main (as 0.6.20)
- [x] Add a "how close to the deadline" measure: Unity's audio counters, if the game offers them (0.6.20)
- [ ] Raid with it on; read audio-counters.txt (are there any?) and the summary's AUDIO THREAD

## ⏭ 6.0b · Ray batch wait on busy CPUs (measure)
- [x] Log the batch wait per frame (timing.csv, slowframes.csv; 0.6.20)
- [ ] Heavy Streets raid, sort slowframes.csv by frameMs
- [ ] Decide: ray quality by core count / next-frame batch / neither

## ⏭ 6.1 · Portal positioning ← **next**
- [ ] routes.csv: each bot gunshot's opening and route (measure only, switch off by default)
- [ ] Raid + review routes.csv
- [ ] Move bot sounds to the opening they come through
- [ ] Check: bot gunshots underground vs above ground
- [ ] Check: bot gunshots when you cross a threshold

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

## 6.3 · Echoes for bot gunshots
- [ ] Measure voice peaks (raids that measure voices only)
- [ ] Echoes per shooter, not per round
- [ ] Audibility gate
- [ ] Loudest-first queue with a ceiling (start 24)
- [ ] Zero-voice tier for the rest
- [ ] F12 settings: ceiling, threshold, tier switch

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

---

## Any time
- [ ] Distant obstruction (past ~250 m)
- [ ] Server settings (solutions doc phase 1)

## Not in Real Sound Simulation (separate mods)
GC and spawn stutters, branch `claude/gc-and-spawn-stutters`. Fix before using its GC mode:
- [ ] Ceiling check can collect every frame (needs a minimum interval)
- [ ] Game switching GC off at raid start can undo Manual mode and break the restore
