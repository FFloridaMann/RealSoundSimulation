# The fixes

How everything works together now: [DESIGN.md](DESIGN.md). Each fix: the problem in the game, the evidence from our raid logs, exactly what the fix changes, its settings, and how to check it works. All are in F12 → Real Sound Simulation → **"2. Fixes (all off means vanilla audio)"**, switch live in raid. Since 0.5.8 their defaults are the tested setup (every fix on, `2` ParallelPaths, `3d` Enclosure, `3i` Soft, `8f` on); with every fix off you get the game's own audio.

Status key: **Built** = in the plugin, not yet heard in game. **Heard** = heard in game, logs show it acting, but not fully confirmed. **Verified** = heard in game and confirmed in logs.

| # | Fix | Status | Since | Notes |
|---|---|---|---|---|
| 0 | Gun sounds: SPT or SEB (a choice, not a fix) | Built | 0.6.3 | Default SPT. Full-auto guns stay SPT unless 0a |
| 1 | Gun mixer hysteresis | Built | 0.2.0 | Raid 4 had no AI, so it has had nothing to act on yet |
| 2 | Obstruction score | Built | 0.2.0 | Left on Game in raid 4 |
| 3 | Smooth doorways | Heard | 0.2.0, reworked 0.2.1, 0.2.2 | Raids 4–5: "improved". 0.2.1 removed the startup error and most of the jump. Raid 6 (0.2.2, sticky doorway): no doorway swaps, no jumps |
| 3c | Blend own gunshots (doorway or enclosure) | Heard (both modes) | 0.2.1, Enclosure mode 0.2.2, ground fix 0.3.0, room measure 0.3.1, roof-above test 0.3.2, muzzle + hard rooms 0.3.3 | Raid 5: Doorway mode "improved again". Raid 6: open ground got indoor recording (ground counted; fixed 0.3.0). Raid 7: a roof-only bench (0.5–0.6 indoor) and a small glass office (0.84–1.0) too indoor; 0.3.1 changes the measure |
| 5 | Own gun room reverb boost (experiment) | Heard (5a, hard rooms) | 0.2.2, 5a 0.3.3 | Not tried yet. Raid 6 confirmed Meta's acoustic map is running (1 map, 35 geometry), so it has something to act on |
| 6 | Own gun echoes | Heard | 0.3.0 | Raid 7: "sounds much much better", "overall huge improvement". 4.0 echoes per sample, surfaces tagged on nearly every hit |
| 7 | Fix rain cutting out between rooms | Built (on in raid 14; the bug wasn't triggered: 0 of 10 picks) | 0.4.0 | Game bug found in step 4b; see below and AMBIENCE.md |
| 8 | Outdoor sound indoors from openings | Heard | 0.4.2 | Raid 13: "works pretty well", no swings. Raid 14 (rain): two misses. The Nakatani exit hallway's open windows were barely found (exposure 0.07). Roof rain stayed at 30% down the underground exit stairs. Needs fix 3 |
| 4 | Off-map sounds as outdoor | Heard | 0.2.0 | Raids 4–5: acted about 10,000 times per raid |

Background terms used below:
- **Occlusion** (0–1): how blocked a sound is. 0 = clear, 1 = fully blocked. The game builds it from *obstruction* (walls between you, same space) and *propagation* (sound travelling through doorways between rooms).
- **Snapshot**: a saved state of the game's audio mixer (reverb, EQ, volumes). The game has two: Outdoor and Indoor.
- **Portal / doorway**: an opening between baked audio rooms (doors, windows, gaps).

---

## Setting 0: Gun sounds, SPT or SEB (0.6.3)

F12 → Real Sound Simulation → **"0. Gun sounds"** (top of the list). Not a fix: it picks which recordings guns use.

**What it is.** Sound Engine Backport (SEB, by red_the_fox, MIT) ships EFT 1.0's gun sound banks. They have the same bank names as SPT's (`akm_main`, `akm_tail`, …) but hold 6 to 48 single-shot takes per sound (indoor/outdoor × close/distant/far × suppressed), where SPT holds one full-auto loop plus a tail and, for many guns, few or borrowed single shots (the M4A1's are MCX recordings, the MP5's another rifle's).

**Evidence (7 Nov).** Decoded both versions: AK-74 363 clips (295 single shots) against 101 (9); M4A1 374 (306) against 113 (27); MP5 239 (192) against 60 (9); AKM 90 (72) against 18 (0). Clips with the same name are the same audio (median difference 0.0 s, 0.1 dB); SEB re-encodes most from PCM to ADPCM. An AKM single shot (SEB take 01 against SPT's loop cut + tail) matched within 0.5 dB in every band and in decay. SEB's 8 AKM takes differ from each other (attack correlation 0.31–0.62). So the gain is variety shot to shot, not a better recording.

**What it changes.**
- On each trigger pull, never during a burst (`_isFiring`), the gun's six banks (Body, BodySilenced, Tail, TailSilenced, Doublet, DoubletSilenced) are swapped to SEB's by name, or back to the ones the game set.
- `_firingLoopLength` is recomputed as the game does, since the full-auto beat is Body length ÷ 16.
- The patch is a `WeaponSoundPlayer.FireBullet` prefix that runs first, so fixes 3c and 6 work on whichever banks are in place.
- **Full-auto guns stay SPT unless `0a` is on.** For any gun that can fire full-auto (`Non_auto` false) the game loops one Body recording while the trigger is held and cuts it to the tail on release. SEB's recordings are single shots, so a burst would be one shot repeating every ~3 s, and semi fire would play only the first beat (Body length ÷ 16) of the take. Turn `0a` on once playback queues one recording per round.
- **Loading.** SEB's banks are copied by `tools/build_seb_sounds.py` with new internal (CAB) names, because Unity can't hold two bundles with the same internal name. They load in the background the first time a gun needs them; until then that gun stays SPT. A bank with a clip that didn't resolve is skipped (logged) and that gun keeps SPT's version of it.

**Settings.** `0 Gun sounds` SPT (default) / SEB. `0a SEB on full-auto guns` off.

**How to check.** `gunsounds.csv` has a row each time a gun switches, or can't (reason: `full-auto gun (0a off)`, `not in SEB`, `SEB loading`, `SEB load failed`, `seb_sounds not built`). The summary's GUN SOUNDS line counts banks loaded and switches. LogOutput shows `SEB gun sounds: <bundle> ready, N banks`.

**Install.** Build once: `python -I tools/build_seb_sounds.py` (needs UnityPy; reads `~/Downloads/SPT-SEB-1.0.1.zip` by default, writes `G:\SPT\BepInEx\plugins\FFloridaMann-RealSoundSimulation\seb_sounds`). SEB's own server mod must **not** be installed, or "SPT" would already be SEB's banks.

---

## Fix 1: Gun mixer hysteresis

**Problem.** Every remote gun has two possible mixer channels: normal, and "Occluded" (heavily muffled). On every shot, the game picks Occluded if occlusion is above 0.55, otherwise normal (`WeaponSoundPlayer.UpdateMixerGroup` → `AudioWeaponUtils.GetGunshotOcclusionMixerGroup`). The occlusion value jumps around between updates, so a fight right at the line flips between muffled and clear.

**Evidence.** Raid 2: one bot 40 m away flipped 5 times in 27 s, its occlusion swinging 0.97 → 0.41 → 0. Raid 3: 7 flips.

**What changes.** `GunMixerFix` replaces `UpdateMixerGroup` (same safety checks as the original):
- Going muffled needs occlusion **above 0.60**; coming back needs it **below 0.45**. Between the two, the gun stays as it is.
- A change must persist for **0.5 s** of shots before it is applied.
- **The first shot after 2 s of quiet** from that gun uses the game's 0.55 rule straight away, so a new fight is never late to sound right.

**Settings.** `1 Gun mixer hysteresis` (on/off), `1a Muffle above` 0.60, `1b Unmuffle below` 0.45, `1c Hold s` 0.5.

**Check.** A bot firing bursts behind cover 30–60 m away should stay one way, not flicker. Logs: `summary.txt` "gun mixer group changes during fire" drops; "fix 1 held back N" counts the flips it prevented.

**Limits.** It does not change *how* muffled the Occluded channel is, only when it's chosen. The underlying occlusion swings are still there (fix 2 reduces some of them).

---

### 1d: First burst muffled (0.6.21)

**Problem (raid 34).** A quiet bot further away than its own footsteps carry (~80 m) has a processor marked `OutOfRange`, and the game does not compute occlusion for those, so the value stays 0. When it fires, its gun voices raise the container's range to 600 m and the calculation starts. That takes several frames and is smoothed, so the first rounds of its first burst played un-muffled on the open gun mixer, then got cut down. Fix 1's "fresh shot" rule read the same 0, and its hold kept the open mixer for up to another 0.5 s.

**Fix.** A prefix and postfix on `WeaponSoundPlayer.RegisterSourceForOcclusionProcess`, which runs only when a gun borrows new voices, before its first sound is queued. If that shooter's occlusion was never computed, one line-of-sight test; if blocked, `CombinedSpatialAudioOccluder`'s smoothed value starts at 1 (propagation if the shooter is in another room, obstruction if in yours), the real calculation is forced to start, and the voices and mixer pick up the new value at once. The game's own result then takes over. The real value can't be computed in that call, because the obstruction checks are a job chain over several frames.

**Limits.** Only a bot's first burst: later ones keep the last computed value, even if the bot has moved since. A blocked line can still be a doorway the sound gets through, so a first burst may start slightly too muffled for a moment.

**Check.** `1d` off and on, a distant bot fight: botshots.csv `firstBurstSeeded` = 1 on those rows, `occl` already high and `occludedMixer` 1 on the first row. Summary: BOT GUNSHOTS.

### 1e: Shots ring under the next (0.6.23)

**Problem (raid 36).** A gun has two voices that alternate, and `SuperBetterAudioQueue.Update` ends the older one at the exact start of the next sound with no fade. A semi-auto gun's next shot chops the last one's decay, and a new burst chops the last burst's tail: 89 of 224 bot sounds, on average 1.85 s lost. Your STM-9 on the FRT sounded like one looping shot for the same reason.

**Fix.** The game's `Update` with one change: only a firing loop is ended when the next sound starts. Applies to every gun. Two voices remain, so a third sound takes back the oldest. If `BorrowWeaponAudioQueue` finds no free voices, the gun closest to finishing (not firing, never yours) releases its two and the shot plays, so a fading tail is cut rather than a shot going silent.

**Check.** `1e` off and on with bots firing short bursts. Summary BOT GUNSHOTS: the cut count should fall sharply; "gun voices taken" should normally be 0.

## Fix 2: Obstruction score

**Problem.** When you and a sound are in the same space with something between you, the game runs up to three checks (`SpatialAudioOcclusionCombinedCalculator`): **transmission** (through the obstacle), **diffraction** (around its edges) and **reflection** (bouncing off surfaces). Each gives 0 (clear) to 1 (blocked), and the game takes the **average**. But a check that *fails to find a path* returns 1.0, so a thin wall (0.2) plus a failed corner search (1.0) averages to 0.6, which is over the 0.55 muffle line.

**Evidence.** Raid 2: diffraction ran 389 times, and 64% of the time it returned 1.0 (no path). The median result was 1.0.

**What changes.** `ObstructionScoreFix` recombines the scores after the game computes them. Three modes:

| Mode | Result | Example: T 0.2, D failed (1.0) |
|---|---|---|
| Game | average of the checks that ran | 0.6 (muffled) |
| IgnoreFailedPaths | average of the checks that found a path; if all failed, the game's value | 0.2 |
| ParallelPaths | sound is blocked only if every path is: T × D × R | 0.2 |

ParallelPaths is the more physical one (sound reaches you by whichever path is open). IgnoreFailedPaths is the more conservative change. Compare both by ear.

**Settings.** `2 Obstruction score`: Game / IgnoreFailedPaths / ParallelPaths.

**Check.** Gunfire and footsteps behind thin cover should be less muffled. Logs: "fix 2 changed N scores by >0.05" in `summary.txt`; fewer rows over 0.55 in `containers.csv`.

**Limits.** Only affects sounds whose obstruction check ran. Past a sound's range the game marks it "OutOfRange" and runs no checks at all (raid 3: shots beyond about 250 m had obstruction 0). That needs a separate fix.

---

## Fix 3: Smooth doorways

**Problem.** Two separate game systems decide whether you're inside, and both switch abruptly:
1. **Mixer snapshot.** Trigger volumes (`EnvironmentManager`) call `BetterAudio.TransitToEnvironment`, which switches the whole mix between the Outdoor and Indoor snapshots in **0.1 s going in** and **0.5 s going out**.
2. **Ambience.** The baked audio rooms drive `EnvironmentSoundBlendSystem`. Inside, it is meant to fade the outdoor ambience by your distance to the doorway; on stepping out, `BlendToOutdoor` snaps it to full in 0.3 s.

**Evidence.**
- Raid 2: standing in the doorway, the outdoor ambience value was **0**, then jumped to **1** in one sample as you stepped out. So the distance fade never happened at that building.
- The two systems mostly switch within 0.15 s and 0.4 m of each other, but raid 3 had one spot 12 m and 4.7 s apart, and 4 snapshot switches with no matching room switch. In those places you get two separate jumps.

**What changes.** `DoorwayBlend` works out one value every frame, **outdoorness**: 0 deep inside, 0.5 in the doorway, 1 outside. How it's measured (since 0.2.1):
- Find the nearest open doorway that leads outdoors. Inside, only doorways of the room you're standing in count, so a neighbouring building's door through a wall doesn't. Closed doors don't count.
- Measure how far you are **through the doorway's plane**: negative inside, positive outside. That gives 0.5 on the plane, falling to 0 at **1.5 m** inside and rising to 1 at **1.0 m** outside.
- Also measure how far you are **to the side** of the doorway's centre. Within 0.75 m it's fully the doorway's value. Between 0.75 and 1.75 m it fades to the baked room's own answer (0 inside, 1 outside). So walking along an outside wall past a door stays outdoors.
- Which way is "outside" for each doorway is worked out once from the game's room data: the side away from the centre of the indoor room it belongs to. If that can't be determined for a doorway, it falls back to plain distance from the doorway's centre (the 0.2.0 method).
- **Sticky doorway (0.2.2):** the doorway used last frame stays in the running on both sides of the crossing, even though inside only the current room's doorways normally count. Another doorway has to be at least 1 m closer to take over. It's dropped once you're more than about 3 m away.
- The value is smoothed over about 0.12 s.

Why the change: 0.2.0 used distance from the doorway's *centre*. The game's room boundary sits about a metre from that centre, so the value jumped as you crossed it. In raid 4 it went from 0.24 to 1.0 in one step, which the smoothing turned into a quick ~0.3 s change, and you could hear it. Measuring through the plane passes smoothly through 0.5 wherever the room boundary is.

It then drives:
- **Mix:** both snapshots at once, Outdoor weighted by outdoorness and Indoor by the rest (`AudioMixer.TransitionToSnapshots`). In the doorway it's half and half.
- **Outdoor ambience:** from the room's own designed level (deep inside) up to full by the time you reach the doorway.
- **Room tone:** full until the doorway, faded out by the time you're 1 m outside.

While it's on, the game's own snapshot switch and ambience fades are skipped. Turning it off hands control back to the game immediately.

**Settings.** `3 Smooth doorways` (on/off), `3a Blend inside m` 1.5, `3b Blend outside m` 1.0.

**Bug fixed in 0.2.1.** The game sets up its ambience blenders about 30 s into a raid. Before that, setting them threw an error every frame (raid 4, `errors.txt`), and the ambience part did nothing until the blenders were ready. It now waits until they're set up.

**Check.** Walk slowly in and out of a building with it off, then on: no pop, and the change happens over about two steps. Logs: `ambience.csv` columns `doorwayTarget` and `doorwayApplied` ramp smoothly through 0.5 at the doorway. `transitions.csv` still shows the game's switch attempts, but they're skipped.

**Limits / not handled yet.**
- Wind, rain, footstep-surface layers and room-tone emitters have their own switches and still change at the boundary.
- **Other people's gunshots** still change when *you* cross a threshold. Their muffling switches from "same room, blocked by walls" to "different room, heard through doorways", which are two different calculations. Not fixed yet.
- Windows count as doorways.

---

## Fix 3c: Blend own gunshots (Doorway or Enclosure)

**Problem.** Every gun has two sets of recordings: Outdoor and Indoor, for the shot itself and for the tail at the end of a burst. For your own gun, the game picks one set from the indoor trigger volumes. So your gunshot swaps from one recording to the other the instant you cross a threshold, and it never depends on which way you're aiming or what's around you.

**Evidence.** Your report after raid 4: "the sound of gun shots changes sort of abruptly when crossing the threshold". That raid had no AI, so those were your own shots.

**What changes.** The game's gun player can already play two clips at once with a volume balance between them (slot 1 at `balance`, slot 2 at `1 − balance`). It normally uses this to crossfade near and far recordings for remote shots. Your own shots are at distance 0 and only ever use one clip, so the second slot is free. Near a doorway (outdoorness between 0.02 and 0.98), `GunEnvironmentBlend`:
- picks an Indoor and an Outdoor recording from the same distance tier the game chose;
- plays the Indoor one in slot 1 at `1 − outdoorness`, and the Outdoor one in slot 2 at `outdoorness`.

It applies to the shot (`FireBullet`) and to the burst tail (`StopFiringLoop`). It only touches **your own** first-person shots: remote shooters keep their own environment.

**Where the indoor weight comes from** (setting `3d Own gunshot blend source`):
- **Doorway** (0.2.1): 1 − the doorway value from fix 3. It only acts near doorways (weight between 0.02 and 0.98); everywhere else the game's pick stands.
- **Enclosure** (0.2.2, reworked 0.3.1): from the [enclosure probe](#enclosure-probe-022). It acts everywhere; at either extreme it plays just one recording. Since 0.3.1 it works in four steps:
  1. **Room** = wall closure × roof closure (see the probe section). Since 0.3.2 the roof only counts if there's one right above your head. Mapped 0.15 → fully open, 0.65 → fully indoor. A roof alone (a bench shelter, a canopy) is no longer a room, and walls without a roof aren't either. Those spaces get their character from the echoes (fix 6), not from the indoor recording.
  2. **Aim:** the closure of the 9-ray cone along your shot, mapped 0.25 → 0 and 0.85 → 1. It's blended in by `3e Aim weight` × roof closure, so firing into or out of a room still leans the mix, but firing at a wall in the open doesn't make the shot "indoor".
  3. **Size:** × the size factor. It's 0.5 when the walls average 2 m away and 1 from 6 m. The game's indoor recordings carry a long room tail, which suits a normal room or hall but not a tiny furnished space. There the echoes give the short, dense sound.
  4. **At the muzzle too (0.3.3).** When you fire, the room, roof and size are also measured at the muzzle (the same 37 rays, reused while the muzzle stays within 0.15 m for 0.1 s). Each is a mix: head × (1 − `3g`) + muzzle × `3g` (0.7). The muzzle is what fills a room with sound. Raid 9: on a threshold, looking in from outside sounded outdoor and looking out from inside sounded indoor.
  5. **Hard rooms (0.3.3).** The size cut fades out as the room gets hard and closed (hard room = how closed × share of bare concrete, stone, tile or thick metal). A bare concrete stairwell keeps the full indoor recording, while a small furnished or glass space still gets less. Raid 9: the Nakatani basement stairs were cut to about 55% indoor.
  6. Before 0.3.1 the measure was the plain enclosure value mixed with the aim enclosure. Raid 7 showed a covered bench at 0.5–0.6 indoor (roof only) and a small glass office at 0.84–1.0.

**Settings.** `3c Blend own gunshots at doorways` (on/off), `3d Own gunshot blend source` (Doorway / Enclosure), `3e Aim weight` 0.4, `3g Muzzle weight` 0.7, `3i Small space rule` Current / Soft (≥ 85%) / Off (0.5.4), `3f Thin and glass surfaces count` 0.5 (Enclosure mode: how much glass, thin wood planks, cardboard and fabric count as walls; chain fences and grates always count as nearly open). Doorway mode uses fix 3's doorway value, so it works with fix 3 on or off. It sounds best with fix 3 on, though, because then the reverb blends too.

**0.6.9: `3j Blend steepness`** (1; **0.5 since 0.6.14**: at a garage mouth a measured change of 0.13 became 0.50). Raid 28: 132 of 202 samples mixed the two recordings between 0.25 and 0.75, and two different recordings of one shot at similar levels can flam. The weight is reshaped around 0.5 (x = 2w − 1 → sign(x)·|x|^(1/(1 + 2·3j))): at 1, 0.6 → 0.79 and 0.4 → 0.21; same crossover, near-even mixes rare. 0 = as before. ownshots.csv: `indoorWeight` as measured, `weightUsed` after 3j.

**0.6.11: `3k Muzzle lobe`, and `3m` / `3l` the outdoor tail from the opening** (raid 29 tag 1, barrel out of a window: whole shot indoor; "inside shot, outside tail").
- **3k** (1): the muzzle's room measure weights each direction by ((1 + cos to the aim) / 2)^k, so out of a window the muzzle reads mostly outdoor (the sphere read 0.72–0.98 because half of it looks back into the room). 0 = the sphere. ownshots.csv: `muzzleRoomSphere`, `v2MuzzleRoomSphere` beside the lobe values in `muzzleRoom` / `v2MuzzleRoom`.
- **3m** (on): while 3c mixes the shot mostly indoor (> 0.5 after 3j) and the aim cone is open, the gun's outdoor Tail recording also plays from the opening the shot goes out of (first aim-cone ray that leaves cover before its wall; at the glass pane or bars if any, with 9d's loss and 1.5 kHz or −1 dB and 12 kHz; else the exit point; fallback 3 m along the aim), delayed by distance / 343 m/s, at shot × (1 − aim closure) × indoor weight × `3l`. Additive: the two body slots stay. Single shots and burst tails (a full-auto-capable gun's single shot is loop + tail), never the loop. Fix 6's voices, so it needs 6 on. ownshots.csv: `tailPlayed, tailLevelDb, tailDelayMs, tailAz, tailThrough` (or the reason it didn't play).
- **How to check:** at the tag 1 window (93.5, 26.5, 228), single shots muzzle out facing out, then from a metre back, then firing into the room: `muzzleRoom` well below `muzzleRoomSphere` facing out; `tailPlayed` 1 with `tailDelayMs` ≈ 2.9 × the distance to the window in metres and `tailAz` toward the window. A/B 3m off/on, then 3k 0/1.

**Check.** Doorway mode: fire while standing in a doorway and while stepping through one; there should be no sudden swap. Enclosure mode: open air against a spot covered on three sides, and firing into a room against firing out of it. Logs: "fix 3c blended N" in `summary.txt`; `ownshots.csv` has one row per own shot or tail, with every measurement and the weight that was used.

**Limits.**
- The two recordings are mixed by straight volume, so exactly halfway can be up to about 3 dB quieter than either side.
- Full-auto picks the recording at the start of a burst. A burst you start outside and carry inside keeps its starting mix until the tail, which is picked fresh.

---

**0.6.26: open sky (`3n`, 1).** With probe v2, the room part of the weight is multiplied by (1 − open sky × 3n), so a spot with open space behind you doesn't count as a full room just because the muzzle faces one. Raid 39 mark 7: 42% open sky, 82–89% indoor. Check: outside a building with open space behind, fire into it with `3n` at 0 and 1.

## Enclosure probe (0.2.2)

Not a fix by itself: it measures, and fix 3c (Enclosure mode) uses it. The planned echoes will too. Setting: `1. Diagnostics → Enclosure probe` (on by default). It changes no sound unless something uses it.

Every 0.1 s it casts 32 rays, spread evenly over a sphere, from your head out to 40 m. They test the same collision layers the game's occlusion uses: buildings and terrain, not players or small props. A hit counts fully within 3 m and less with distance, down to nothing at 40 m. Rays pointing more than 10° down are left out of the scores, because the ground is always there, but their hits are kept for the echoes.

**Room measure (0.3.1).** Used by fix 3c's Enclosure mode:

| Value | Meaning |
|---|---|
| wall closure | rays within 30° of level: each hit counts 1 up to 10 m away, falling to 0 at 40 m, times its surface's closure |
| roof closure | the same, for rays more than 30° up; since 0.3.2 capped by the zenith closure |
| zenith closure | 0.3.2: 5 rays, straight up and 20° off vertical. Raid 8: about 1 m outside a small space's door, the angled upward rays still hit its roof edge (0.58–0.84), so shots visibly outside got about 40% indoor recording. Capping by the zenith test fades the roof out over about 1 m past the roofline |
| room | wall closure × roof closure |
| size factor | 0.5 when the hits average 2 m away, rising to 1 at 6 m |
| thin share | how much of the closure was lost to thin or see-through surfaces (for the logs) |
| aim closure | the same closure, over the 9-ray aim cone |

Surface closure: 1 for solid surfaces (concrete, metal, thick wood, untagged); setting `3f` (0.5) for glass, thin wood, cardboard and fabric; 0.1 for chain fences, grates and shattered glass.

**Windows (0.3.1).** A window blocks rays with a separate collider that carries no material tag, so windows used to read as the concrete around them (raid 7: 8 glass echoes in a whole raid, much of it in a glass office). At the first lookup each raid, every window's colliders are registered as Glass. A broken window's blocking collider is switched off by the game, so rays then pass through.

**Ground (0.3.0).** A hit on a floor-like surface (its normal points more than about 45° upward: ground, floors, gentle slopes) counts as open, in the sweep and in the aim cone. Raid 6 showed why: rays just below level hit the ground a few metres away and counted as walls, so open ground read 0.2–0.4 instead of near 0, and firing into open air read an aim enclosure of about 0.37. Steep slopes and hillsides still count.

| Value | Meaning |
|---|---|
| Enclosure | 0–1, weighted share of level-and-upward rays that hit (open field about 0, small room near 1) |
| Horizontal | same, rays within 30° of level (walls) |
| Overhead | same, rays more than 45° up (roof or overhang) |
| MeanHit | average distance of the rays that hit |

Logged twice a second to `probe.csv`. Cost: 32 short raycasts per 0.1 s.

---

## Fix 5: Own gun room reverb boost (experiment)

**Background.** Earlier I said your own shots get no room acoustics. That was wrong. The game's gun sources (`ReverbSuperSource`) play a hidden, **reverb-only copy** of your own (stereo) shot through Meta XR's room acoustics, at your position. Meta's acoustics are built from each map's geometry. The copy uses the server's "stereo" settings: for gunshots, 0 dB reverb and 0 dB early reflections. What it can't know is which way you're aiming.

**What it does.** It adds the setting's value to both of those sends on your gun's sources, after each shot is set up. At 0 it puts back the game's own values.

**Settings.** `5 Own gun room reverb boost dB` (−12 to +12, 0 = game). `5a Hard room reverb boost dB` (+6, 0.3.3): added on top in hard, closed rooms, scaled by how hard and closed they are (0 in the open, the full amount in a bare concrete stairwell).

**Check.** Indoors and outdoors, compare 0, +6 and +12 dB. `summary.txt` has a "META XR ACOUSTICS" section showing whether Meta's acoustic map is actually loaded in that raid. If it isn't, this setting can't do anything.

---

## Fix 6: Own gun echoes (roadmap step 4)

**0.6.9 (raid 28, "doubled gun reverb or sound").** The voice pool is 48 (was 24), and when it's full the voice that ends soonest is reused, not the one that started first. In raid 28 every sample was a full-auto loop plus a tail with ~6 echoes each; the pool was full and 274 echoes were stopped mid-sound (256 by tail echoes), which reads as a doubled or stuttering shot. New `6f Early echo trim dB` (0 = no change) lowers echoes arriving under 25 ms (414 of 1,216 did, some at −4.5 dB after 9 ms): they're physically real and fuse with the shot as colour, so judge them by ear after the voice fix.

**Problem.** Your own shot is a stereo recording, the indoor or the outdoor set (fix 3c can mix them), plus the game's hidden Meta reverb copy (fix 5). None of that knows where the walls actually are or which way you fired. So it picks how "inside" you are, but it isn't *dynamic*: a wall 2 m to your right, a building face 40 m ahead, and firing into a room against firing out of it all sound alike (raid 6 feedback).

**What it does.** For each of your shots, it plays up to 4 echoes. Each echo is a delayed, quieter, duller copy of the shot, played from the surface it bounced off.

1. **Find the reflecting surfaces** (when you fire; `Acoustics/OwnEchoes.cs`):

   | Kind | Where it comes from |
   |---|---|
   | mirror (`Spec`) | Each wall or ceiling the enclosure probe hit: the point on it straight out from you, checked with one extra ray. Path = 2 × your distance to the wall. This is the classic slapback. |
   | scattered (`Diff`) | A probe hit on a surface with no mirror point in reach (corners, clutter). Weaker and duller. |
   | aim (`Aim`) | The 9-ray cone along your shot. What you fire *at* scatters energy back, so firing into a room is loud and close, and firing out of it isn't. |
   | far (`Far`) | 16 level rays out to 250 m, re-scanned when you move 5 m or after 5 s: building faces, cliffs, hills. Distinct late echoes outdoors, from 30 m away. |

   Ground and floors are ignored, because the recordings already contain the ground bounce. Paths under 3 m (under about 9 ms) are left to Meta's own early reflections.

2. **Level** of each echo, relative to the shot:
   - **Distance:** −20·log10(path / 2 m). So a 4 m path is −6 dB, 20 m is −20 dB and 100 m is −34 dB.
   - **Muzzle directivity:** a gun is much louder in front of the muzzle. The echo loses 0 dB toward where you aim, rising to the full "6c" value (10 dB) straight behind.
   - **Surface,** from Tarkov's own bullet material tags:

     | Surfaces | Loss | Brightness (low-pass) |
     |---|---|---|
     | concrete, stone, asphalt, tile, thick metal, water, untagged hard | −0.5 dB | full |
     | glass, thin metal | −1 dB | full / 16 kHz |
     | thick wood | −1.5 dB | 9 kHz |
     | thin wood, gravel | −3 dB | 6 kHz |
     | soil, grass, mud, snow | −4 dB | 4 kHz |
     | fabric and other soft surfaces | −8 dB | 2.5 kHz |
     | chain fence, grate | −10 dB | 6 kHz (mostly see-through) |

   - **Scattered, aim and far echoes:** −3 dB, times the cosine of the angle they hit at, times their share of the ray set.
   - **Your trim:** setting "6a Echo level dB".

   Echoes arriving within 4 ms and 30° of each other are merged. The loudest N (setting "6b") are played, each capped at −3 dB. Hard closed rooms (stairwells) get up to 3 more (0.3.3: 2; 0.4.1: 3). Since 0.4.1 all their echoes are also raised by `6e Hard room echo boost dB` (+4) × how hard and closed the room is, because a real stairwell is loud from the sound building up. Raid 11: stairwell shots were quieter than a stairwell should be.

3. **Colour.** A low-pass filter at the lowest of these:
   - **Air absorption** for the path. This is section 2.5 of the solutions doc, applied here first: about 9 kHz at 40 m, 5 kHz at 100 m and 2.2 kHz at 300 m.
   - **The surface's** brightness from the table above.
   - **Scattered** echoes are further dulled to 75%.
   - **Head shadow:** an echo arriving from straight behind you is dulled to 55%. Echoes use plain stereo panning, not HRTF, so this is the front/back cue.

4. **Timing.** The fix hooks the gun's own sound queue (`SuperBetterAudioQueue.Enqueue`), so every sample the game schedules for your gun gets echoes:
   - single shots;
   - the full-auto firing loop;
   - the tail at the end of a burst.

   Each echo starts at the sample's start time plus path ÷ 343 m/s. A firing loop's echoes stop where the game stops the loop (when the tail starts), each shifted by its own delay.

5. **Output.** A pool of 24 plain AudioSources on the game's "Guns/Gunshots" mixer group, so game volume and the in-game headset treat echoes like the shot itself. If all 24 are busy, the oldest is stopped; `summary.txt` counts these.

**Which recording.** Echoes play the *outdoor* recording from the same distance tier. It's the driest version of the shot, since the indoor set already has a room baked into it. Silenced weapons echo their silenced recording.

**Settings.**
- `6 Own gun echoes` (on/off).
- `6a Echo level dB`: 0, range −18 to +12.
- `6b Max echoes per shot`: 4, range 1–8.
- `6c Muzzle directivity dB`: 10, range 0–20.
- `6d Far echoes` (on/off).
- `6e Hard room echo boost dB`: +4, range 0–12 (0.4.1).

**Glass facades and window bars (0.4.2).** The aim cone also casts against the glass and bar layers, which the sound-blocking rays pass through. Intact glass gives glass reflections and bars give weak, dull ones (grate). Shards and other obstructions are ignored.

**Check.** Steps are in [ROADMAP.md](ROADMAP.md#testing-step-4-next-raid-no-ai-needed). Logs:
- `echoes.csv`: one row per echo played, with kind, surface, path, delay, level, directivity, filter, and direction relative to where you face.
- `summary.txt`, OWN GUN ECHOES section: echoes per sample, by kind, mean level and path, voices used and stolen, and which surfaces were hit. "None" means no bullet material tag was found on that collider.

**Cost.** Per shot: one cached sweep (or a fresh one if older than 0.15 s), up to 12 check rays, the 9-ray aim cone (shared with fix 3c), and 16 far rays only after you move 5 m. Echo voices: up to 4 per sample, from a separate pool of 24. The game itself uses about 50 of its 196 voices.

**Limits.**
- No HRTF on the echoes: left/right panning plus a duller sound from behind, so above and below aren't placed. Routing echoes through Meta's spatializer is a possible next step.
- Only first-order reflections. The longer reverb is still the recording plus Meta's reverb copy.
- 32 probe rays and 16 far rays can miss a narrow building.
- The levels are a physical estimate. Tuning by ear ("6a", "6c") is expected.
- Bot gunshots get no echoes yet (roadmap step 6).

---

**0.6.25: measured surfaces (`6g`, on).** The echo level and low-pass per surface come from `Acoustics/Materials.cs` (Meta XR's presets; [MATERIALS.md](MATERIALS.md)). Level is the average of the 176–775 Hz and 775–3400 Hz bands, where a rifle has its energy. Wood, glass and metal are no longer dulled. Off = the earlier hand-set values. Check: fire near a plywood wall, then concrete, with `6g` off and on.

## Fix 3h: Ambience through glass (0.3.3; removed in 0.5.8)

**Removed in 0.5.8.** Fix 8 replaced it in 0.4.2 (with fix 8 on it did nothing). Kept here as history.

**Problem.** Raid 9: glass buildings let too little rain and wind in. The game sets each indoor room's outdoor-ambience level from baked data, regardless of whether its walls are concrete or glass.

**What changes.** While fix 3 is driving the ambience, the room's level is raised toward full by: the glass share of the surfaces around you (from the probe, with windows registered as glass) × 2 × `3h`. With all-glass walls at 0.5, that gives the full outdoor level; with half-glass walls, halfway. The indoor tone filter still applies, so the weather is muffled, as through a window. The change is smoothed over 0.3 s.

**Settings.** `3h Ambience through glass` (0.5; 0 = the game's level). Needs fix 3 on.

**Check.** Inside a glass building in rain or wind. `ambience.csv` `glassLeak` (0–1) is how far the level was raised.

**Smoothing (0.4.1).** Raid 11: deep inside the Nakatani tower, the outdoor ambience cut on and off. One or two of the 32 probe rays hitting a window or not flipped the raise between 0 and 0.12 every second. It's now smoothed over about 2 s.

**Limit (raid 10).** Big facade glass doesn't block our rays, so it reads as open air, not glass, and this fix barely acts there (glass share about 0.06). It only works where windows are real `WindowBreaker` windows. To be replaced by the exposure measure in roadmap step 4b.

**Broken glass.** When a window is shot out, the game switches off the collider that blocks rays, so the probe reads the hole as air (an opening). Shards left in the frame aren't on the ray layers either, so they count as air too. They're registered as glass only if a ray hits one.

---

## Fix 7: Rain cutting out between rooms (0.4.0)

**0.6.9: same clip = volume only.** When the game starts a rain crossfade and the clip wouldn't change, the playing copy's volume is ramped to the new target over the same time instead (S-curve). Raid 28 tag 2: six room changes in 3 s (targets 0.386 → 0.702) each crossfaded the clip into a second copy of itself at another loop position; the sum dipped ~6 dB in the middle (0.07) and flammed. These crossfades were the game's, not fix 7's.

**Problem (a game bug).** Indoors, each baked room can play its own "rain on the roof" clip. The game's rain player (`PrecipitationAmbientBlender`) crossfades to a fresh copy of that clip whenever you change room. It does the same whenever, in rooms where roof rain follows your height, the volume target moves by more than 0.05. It picks the clip in `SetClip`. But its check returns "nothing new" when the clip is the *same* one as last time, and `SetClip` then sets **no clip**, so the crossfade fades the rain to silence. The next crossfade brings it back. Raid 10: "it cuts in and out a bit".

**What changes.** `SetClip` is replaced with what it was meant to do: while it's raining, always pick the room's clip, or the default for that environment, even if it's the same clip. The crossfade then goes from rain to rain.

**Settings.** `7 Fix rain cutting out between rooms` (on/off).

**Check.** Walk between rooms and up and down stairs in rain. Logs:
- `rain.csv`: one row per clip pick: which player (indoor/outdoor), intensity, clip, target volume, and `gameWouldSilence`.
- `summary.txt`, RAIN section: how often the game would have silenced the rain, and how often this prevented it.

---

## Fix 8: Outdoor sound indoors from openings (step 4b phase 2, 0.4.2)

**How it works now: [DESIGN.md](DESIGN.md) §2.2 and §3.2.** The list below is how it was built up, version by version; where it disagrees with DESIGN.md, DESIGN.md is current. Background: [AMBIENCE.md](AMBIENCE.md). **Needs fix 3 on**, because fix 3 is what takes the ambience over from the game.

**Problem.** Indoors, the game sets the outdoor ambience from a fixed per-room value set by hand, plus one opening. Fix 3 replaced that with a doorway fade. Raids 10–12 showed what went wrong:
- glass buildings with broken windows were silent (room value 0);
- stepping sideways just inside a wide entrance swung the outside between 6% and 95%;
- the outside never had a direction.

**What changes:**
- **Level.** The outdoor value, which sets the outdoor beds' volume and muffling through the game's own curves, comes from the exposure probe, smoothed over about 1 s, and never below the room's baked floor:
  - open directions and broken windows count fully;
  - intact glass counts by `8c`;
  - bars count 80%, other obstructions 50%;
  - near openings count far more than far ones.

  The doorway blend still drives the reverb snapshot at thresholds.
- **Indoor layers** (room tone, indoor wind and rain) are multiplied by 1 − `8d` × exposure.
- **Roof rain:** the indoor rain player's volume is multiplied by `8b` when the probe finds floors above you. Only with fix 7 on.
- **Direction cue (until phase 4):** indoors, the outdoor beds are panned toward the open side (× `8a` × how one-sided it is), and get a high-cut down to 5 kHz as the open side moves behind you. Our own filter is added to each bed source; it's off when not needed.

- **Sound from openings (phase 4, 0.5.0; fixed sectors 0.5.2).** Indoors, the exposure directions are summed into 6 fixed world sectors (60° each): weight, weighted direction and glass share. Each sector has a 3D copy of every outdoor bed that's playing, 4 m out in its weighted direction (it only moves within its sector), 120° wide so neighbouring sectors overlap, on the bed's own mixer channel. (0.5.0–0.5.1 used up to 3 re-assigned "openings", which swept across the listener; raid 16.)
  - **What counts as outside (0.5.3, 0.5.4):** the room map must say outdoor. If the point has a roof above it, it counts as "covered outside" at half weight, unless that roof is the same ceiling you're standing under (interior patches the room map wrongly calls outdoor). 0.5.3 required open sky outright, which also rejected real outside under canopies. Distance weight per direction: 1 / (1 + d / 15 m) (0.5.4). The game's room map calls some lobbies, entrance areas and big interior patches outdoor (raids 16–17). Such points don't count, and the direction keeps looking further out. (0.5.2 only weakened those directions to 35%, and only for the 3D sources.)
  - The share moved to the openings is `8e` (0.7); the flat bed keeps the rest. Openings split it by power (√ of their strength share), so total loudness stays the same.
  - Glass openings get a high-cut, down to 2.5 kHz when all-glass. Without HRTF (`8f`), openings behind you are also duller, the ear-shadow cue. Spread follows the opening's width.
  - The bed volumes the game writes (crossfades, day/night balance) are captured each frame after the game's own updates, and written back × (1 − share). When you go outdoors or switch it off, they're given back in full.
- **Roof rain (0.5.0, direct since 0.5.6):** full with only the roof above you; × `8b` with one floor above; off with two or more. A new level must hold 0.5 s, then it's applied smoothly to the game's indoor rain player's own volume (no crossfade requests, so it no longer needs fix 7).
- **Continuous measuring (0.5.7):** the exposure directions are re-measured a couple per frame (round robin, ~290 rays/s), each nudged randomly by up to half the ray spacing, so the value changes one direction at a time. It used to change all 48 at once.
- **Indoor layers (0.5.7):** with fix 8 on, the indoor channel follows enclosure (1 when enclosed, 0 in open air; 8d sets how early openings fade it), not the room map. Covered outdoor spots get roof rain.
- **One speed (0.5.6):** exposure is measured indoors and outdoors (8/s). The outdoor level, the opening sounds' share (× how enclosed you are), the direction cue, the indoor fade and the roof rain all follow it with the same ~0.35 s smoothing.

**Settings.** `8 Outdoor sound indoors from openings` (on/off), `8a Direction cue strength` 0.6, `8b Roof rain under floors` 0.3 (one floor above), `8c Through glass` 0.45, `8d Indoor sound fade near openings` 0.6, `8e Sound from openings` 0.7, `8f HRTF on opening sounds` on (0.5.8 default).

**Check.**
- Glass building with broken windows: outdoor sound in, louder near the windows.
- Wide entrances: no swings when stepping sideways.
- Facing away from an open side: duller, panned toward it.
- Deep interior: unchanged.
- Lower floors of a tall building in rain: no roof rain under two or more floors.

Logs: `ambience.csv` (exposureApplied, indoorLayers, bedPan, bedCutoffHz, roofRainFactor), `exposure.csv` (openSideAz, oneSided), and the summary's AMBIENCE MIX section.

---

## Fix 9 and 9a: probe v2 (0.6.0, test switches)

How it works: [DESIGN.md](DESIGN.md) §2.6, §3.2b, §5.1b. Design and review: [AMBIENCE.md](AMBIENCE.md) §12–13.

**0.6.9 changes (raid 28):**
- **Roof rain (9i)** is measured per upward path (first leg > 30° up; reference 0.85), not per path of every direction: under a roof right overhead with big openings around, most paths escaped sideways first and it read 0.42. With two or more floors above (the exposure probe's up-rays, held 0.5 s) it is 0. If the game's indoor-layer fade also moves the rain's mixer volume (read from the blenders' parameter names, shown in the summary), roof rain is compensated for it, so openings around you don't turn the roof down.
- **`8g Indoor wind follows openings`** (on): room tones whose clip name has "wind" in it follow how much of the outside reaches you (full above −10 dB of it, −1 dB per dB below, at most −40 dB) instead of how enclosed you are. Last tag: wind four floors down a stairwell. roomtone.csv logs every room tone so the clip names can be checked; other room tones are unchanged.
- **`9l Rain through the building dB`** (−32; −60 = off): the outdoor sound's floor deep inside, muffled to ~350 Hz, faded in by enclosed. Tags 1 and 4: "not enough of the interior".
- **9k** (0.6.12: sounds are taken in the frame they start, via a `BetterAudio.PlayAtPoint` postfix, so they no longer begin loud from their true direction) credits every path that leaves the building: side openings by how they face the battle (cos², as before), openings to the sky above at 0.35 from any direction, side openings facing away at 0.05. Last tag: the only way out was up the stair shaft and 0.6.8 found nothing. distant.csv now logs what each moved source actually outputs (volume, mixer, spatial settings, reverb zone mix).
- All of these are geometry or settings, not per material: retune after the materials overhaul.

**0.6.10, roof rain:** its own directional mode, `9m` (UnityPanner); `9c` sets only the outdoor sounds now. The roof rain is also low-passed by how its paths reach the roof (dullest surface on the way, x0.7 per reflection; probe2.csv `roofCutoffHz`), so rain down a stair shaft or round a mezzanine is duller than overhead in either mode. Before 0.6.10 it had no muffling, only level.

**Problem.** Raid 21: the exposure probe counted only straight lines to points the room map calls outdoor, with a "your own ceiling" rule. A garage whose walls and roof are one collider read as deep underground (3 of 48 directions outside), and nothing modelled sound building up in a hard room. The opening sounds played up to 24 copies of the same loops, so quiet rain flammed.

**What changes.**
- `Acoustic probe v2` (diagnostics, on) measures by bouncing rays to the open sky (§2.6).
- **Fix 9** (`9 Ambience from probe v2`, off): the outdoor beds' volume and muffling are set directly from it; the game's own beds become directional (no copies); indoor layers and roof rain follow it.
- **Fix 9a** (`9a Gunshots from probe v2`, off): the gunshot indoor/outdoor mix and the hard-room boosts use its room measure.

**Settings.** `9`, `9a` (off), `9b Game sounds directional` 1, `9c Directional mode` UnityPanner / MetaHrtf, `9d Glass loss dB` 22, `9e Sound build-up in rooms` 1, `9f Guided rays` on (0.6.4), `9h Direction strength` 1, `9i Roof rain from probe` on (0.6.7), `9j Rain from openings` on, `9k Distant gunfire through openings` on (0.6.8), `9g Ray quality` Medium (0.6.6: Low / Medium / High / Ultra = 0.5× / 1× / 2× / 4× the paths and the cost; DESIGN.md §7). `Probe ray view key` F11.

**0.6.4 (raid 24: outdoor ambience too loud far inside; lowering `9e` helped but then it jumped in and out).** At mark 1 (Capital) 2 of 128 paths got out: −24 dB from them, −14 dB with the build-up (88% build-up). The build-up was one room formula for the whole interior with a leak rate counted in rays. Now the build-up is measured by tracing half the paths on to 8 reflections, in energy (glass at its real loss), smoothed over ~3 s. Guided rays aim 64 of every 192 paths at the openings already found, weighted so the totals stay right, so the level from the few paths that get out is steady. Fix 9 averages longer when the probe is unsure (0.35 → 2.5 s). DESIGN.md §2.6.

**0.6.5 (raid 25: deep in the Capital the rain came from both sides and from behind; "the bounce is deciding the direction instead of how loud it is").** No path really got out at those marks. The only escapes fell through a hole in the ground floor to the map's catch plane 48 m below and came back up through the building, because raycasts don't hit the back of one-sided floor meshes. One or two of those, at full focus, set the direction while 85–90% of the level was build-up with no direction. Fixed: back faces are hit and reflect, every escape is roof-tested, and the direction is weighted by what you hear (the build-up points towards where escaping paths left the building, never pinned). DESIGN.md §2.6.

**0.6.7 (raid 26).** Deep inside, the rain still sounded a little "through the walls": the build-up pointed at each escape's last contact, mostly a floor spot, from only 3–8 escapes, so it wandered and stayed wide. Now it points where escaping paths **arrive from** at you (their first leg, by reciprocity the direction the outside's energy reaches you from), and `9h Direction strength` sets how pointed it sounds. Roof rain at the car dealer jumped 30% → 100% → 30% where the old one-ray floor count flipped at the mezzanine edge; it is now traced from the roof itself (`9i`), level and direction, so the floor below hears it come down the stairwell (user, raid 26).

**0.6.8 (raid 27; user: "build .6.8 and give me a toggle for Exit points and the ambient gunfire mod through openings").** Exit points: where each escaping path leaves cover, i.e. the window or door. `9j Rain from openings`: indoors the rain comes from those. `9k Distant gunfire through openings`: when you're inside, the ambient gunfire mod's distant battles (off-map sounds fix 4 already handles) come through the openings that face them, at the level that gets in, instead of through the walls. Also: Meta HRTF's own reflections/reverb switched off on the moved sources (raid 27 tag 1: rain heard where the level was 0), the outdoor level can go down to −60 dB, and the game's indoor rain player is logged (rain2.csv) to find the car dealer roof rain on/off. DESIGN.md §2.6, §3.2b.

**Check.** Run the test route ([TESTING.md](../TESTING.md)) with 9 on (and 9a on, or one at a time). Logs: `probe2.csv` (8/s), `paths.csv` (every path at each F10), marks.csv v2 columns, ownshots.csv `probe` and v2 columns, the overlay's "probe v2" line, the summary's ACOUSTIC PROBE V2 section and "directional beds: N movable, N fixed".

---

## 6.1: Bot gunshots from the opening (0.6.24)

**Problem.** A bot in another room was heard from its true direction, through the wall. Raid 36: half the openings the sound really reaches you through were over 33 degrees from that direction, 40% over 45, the worst 150.

**Fix.** The game already works out the best route for every shooter in another room (`PropagationJob`) and uses only its cost. `Acoustics/PortalRoutes.cs` reads the finished result: the opening nearest you and the route's length. `Fixes/PortalPositioning.cs` then places that gun's two voices at `listener + direction(opening - listener) x route length`, in LateUpdate (the game re-poses them in Update, so nothing needs restoring; the occlusion maths uses the container's own position).

**Stabilising.** Route changes need a cost improvement over 0.05. Blended in by the propagation effect (0 at 0, full at 0.3). Direction and distance smoothed over 150 ms. Faded out within 1 m of the opening. Routes re-read every 0.1 s per firing gun.

**Limits.** Gunshots only. No route means the true position: the same room, both outdoors, or no baked route between the rooms (raid 36: shots from a floor below), which wait for 6.2's through-wall path. Flight time still uses the true distance.

**Check.** A bot firing from another room: `6.1` off (through the wall) and on (from the door or stairwell). `6.1a` at 0.5 is halfway. routes.csv `movedDeg`; summary PORTAL POSITIONING; COST line for its cost.

## 9p: Roof rain counts the floors above (0.6.29)

**Problem (raid 40).** The top-roof test looked down from 40 m. It said "top roof" at spots with floors above (probably upper floors solid only from below), so roof rain read 0.9–1.0. An on/off rule from 5 up-rays was all that kept it out, and it flickered.

**Fix.** From a ceiling a path reaches, cast up, counting surfaces from either side; surfaces under 1 m apart are one slab. Each floor above takes off its material's transmission at 1 kHz from `Materials.cs` (25 dB if untagged), scaled by `9q`. A path keeps its best result, so the real roof reached via a stair shaft still counts in full.

**Limits.** It only sees what has a collider. **Check.** The Capital and its escalators in rain: roof rain gradual, none under two concrete floors unless near a stair. Summary: floors above ceilings.

## Fix 4: Off-map sounds as outdoor

**Problem.** A sound placed outside every baked audio room has an "invalid" room. The propagation search then finds no route and returns **1.0, fully blocked** (`ParallelSoundPropagationCalculator.OnUpdate`). SPTBattleAmbience plays its fake distant battles 100–500 m away, often off the baked map.

**Evidence.** Raid 1: all 87 battle sounds (450–600 m) were in an invalid room with propagation 1.0, so they were fully muffled wherever you stood.

**What changes.** `OffMapFix`, for sources in an invalid room only:
- While you're outside: no propagation loss (0).
- While you're inside: the loss follows the doorway value, 0 at the doorway rising to 1 deep inside. This uses fix 3's outdoorness, which is calculated even when fix 3 is off.

**Settings.** `4 Off-map sounds as outdoor` (on/off).

**Check.** Distant ambient battles sound clearer while you're outside, and still fade when you go deep inside. Logs: "fix 4 applied N times" in `summary.txt`; `containers.csv` rows with room `invalid` show `prop` 0 while you're outside.

**Limits.** It affects any source off the baked map, not only SPTBattleAmbience. That includes a bot standing outside the baked area, which is also treated as outdoors (more correct than fully blocked).

---

## Planned (not built)

Order and status: [ROADMAP.md](ROADMAP.md).

- **Remote gunshots at your threshold:** smooth the switch between the "same room" and "different room" occlusion calculations when you cross a doorway.

From [../02_Solutions_and_Feasibility.md](../02_Solutions_and_Feasibility.md) and the raid findings, in rough order:
- **Distant obstruction:** run at least a cheap terrain/building check for sounds beyond their occlusion range (raid 3: nothing past about 250 m is ever obstructed).
- **Portal positioning (2.1):** sounds heard through a doorway should come *from the doorway*, not through the wall.
- **Air absorption (2.5):** high frequencies fade with distance; the game computes this but never applies it. (Fix 6 applies it to its own echoes; other sounds still lack it.)
- **Server settings (phase 1):** occlusion range, absorption, binaural toggles.
- **Headset DSP:** hook `RealSoundSimulation.Dsp` onto the listener, replacing the game's headset effect.
