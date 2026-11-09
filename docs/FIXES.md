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

**0.6.30: the ring follows your turning (`3o`, 1).** `Fixes/OwnShotTurn.cs`. On a shot with both recordings, the open side is remembered, and the change in your facing since then pans them apart and dulls whichever has swung behind you. Check: at a half-open spot, fire and turn around with `3o` at 0 and 1.

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

### 6.1b: Dull the shot by its route (0.6.40)

**Problem.** The game muffles a gunshot only above occlusion 0.60, and in another room that number is the route's cost. raid_20261109_001706: a bot 39 m away with 3–5 walls on the straight line read 0.17, because one opening joins the rooms. Under 60 m, 2 of 96 bot shots were muffled. User: "the sound should be muffled (less distinct) regardless".

**Fix.** A low-pass of ours on the gun's voices, from the geometry rather than the cost:

- Steps = 1 if a wall is on the straight line (one ray, every 0.1 s while firing) + the turn at the nearest opening / 90° (at most 135°, fading out within 1 m of it) + 0.5 per extra opening.
- Cutoff = 16 kHz × 2^(−1.5 × steps × `6.1c`), never below `6.1d`. Smoothed over 150 ms; the first shot starts already dulled.

| Situation | Cutoff |
|---|---|
| Straight through a doorway | 15 kHz |
| Clear line, door slightly to the side | 12 kHz |
| Wall in the way, one door, small turn | 4.5 kHz |
| Wall in the way, one door, 45° | 3.4 kHz |
| Wall in the way, one door, 90°, or two doors at 45° | 2 kHz |
| Three or more doors, 90° or more | 800 Hz |

Median on three saved Factory raids (under 60 m, not muffled by the game): 2.3 kHz, 800 Hz and 4 kHz.

**Realism.** Sound bending round an edge loses highs first, so the direction is real. Real rooms give a gentler slope than a 12 dB-per-octave filter, and hard walls bounce some highs along, so the amounts are estimates; a long route at the floor may be too dull.

**Limits.** Bot gunshots with a route only: same room, both outdoors or no baked route are unchanged. Steps aside on the game's muffled channel. Tone only, though losing the highs makes a shot a little quieter. The opening count depends on the route file (`10b`). Not yet confirmed that a filter on these voices works through the Meta spatializer.

**Check.** Bots in other rooms, `6.1b` off and on; `6.1c` at 2 to hear it plainly. routes.csv `dullHz` (0 = not dulled; a burst's first row can read 0); summary ROUTE DULLING.

## 9p: Roof rain counts the floors above (0.6.29)

**Problem (raid 40).** The top-roof test looked down from 40 m. It said "top roof" at spots with floors above (probably upper floors solid only from below), so roof rain read 0.9–1.0. An on/off rule from 5 up-rays was all that kept it out, and it flickered.

**Fix.** From a ceiling a path reaches, cast up, counting surfaces from either side; surfaces under 1 m apart are one slab. Each floor above takes off its material's transmission at 1 kHz from `Materials.cs` (25 dB if untagged), scaled by `9q`. A path keeps its best result, so the real roof reached via a stair shaft still counts in full.

**Limits.** It only sees what has a collider. **Check.** The Capital and its escalators in rain: roof rain gradual, none under two concrete floors unless near a stair. Summary: floors above ceilings.

## 9r: Roof rain by roof material (0.6.31)

The most common surface above you (`AcousticProbe.RoofMat`) sets a volume factor on the roof rain from `Materials.RoofRainGain`: thin metal 2.8, thick metal 1.6, thin wood and plastic 1.4, fabric 0.7, others 1. Not measured; set by ear. The mod can now raise the rain above the game's own level on a loud roof. Same recording, so no true patter yet (own sounds are logged for step E).

## 9s: Sound reacts to real changes (0.6.32)

**Problem (raid 41 tag 4).** Probe v2's memory was set by your speed, so stopping held on to the last place for 3 s and creeping into a different room reacted slowly.

**Fix.** A 0.4 s estimate runs beside the 3 s memory. Three checks compare them: roof rain per upward path, the share of late sound that gets out, and the arrival direction. A difference larger than the fast estimate's own noise (3 sigma, with minimum differences) replaces the memory with the fast estimate, at most every 0.25 s. Consumers then smooth over 0.35 s for a second.

**Check.** Walk a hallway into a roofed room and stop, repeat slowly, stand by a window for 30 s. It should settle within about 0.6 s; summary: changes while standing still should be few. Deep inside, with very few paths escaping, it can't tell and stays at 3 s.

**0.6.33.** `9s` keeps a speed cap on the memory, grown back over about 1.5 s after you stop; a change must also be 1.6× as a ratio; direction needs 20 paths and 90 degrees. **`9t` (0.3):** roof rain has a minimum level before roof, floors and material, because the game's baked level is 0 or about 0.05 in some places.

**0.6.34.** `9t` is one level instead of a minimum, so the game's per-room value can't move the rain (0 = the game's value). **`9u` (1 dB/m):** a bounced path to the roof fades with distance after 6 m. `9s` no longer checks direction. Under 8 escaping paths a rise keeps the long average. Check: walk up and down a concrete stairwell in rain with `9u` at 0 and 1, and read `roofLossDb` in probe2.csv.

**0.6.36, `9v` (on, live).** `CountFloors` casts on the probe's full mask and counts a surface on a non-blocking layer when both horizontal sides of its collider are at least 4 m. Both counts are cached, so the switch is live. Check: a roofed office with floors above in rain, `9v` on and off; `floorsNow` and `floorsOldNow` in probe2.csv. **`9w` (on, live):** game says indoors and its rain volume is 0, so `AmbienceMix` fades roof rain out over 1.5 s; rain2.csv `noRainGate`.

## 6.2: Through-wall thump (0.6.37)

**Problem.** Sound between rooms only travels the door and window routes, so nothing of a shot comes through the wall itself, and a shot with no route is just "fully blocked".

**Fix.** `Fixes/ThroughWall.cs` plays one extra low-passed voice from the true direction for blocked bot shots (within 60 m, another room) and explosions (within 100 m, a wall between). Level = `6.2a` − distance (0 dB at 5 m or closer) − wall loss × `6.2d`. It uses fix 6's voice pool, past the headroom cap, at most 4 at once.

**0.6.38, walls can stop a sound (`6.2e`, on, live).** A gunshot has a reach of 60 dB (`6.2f`), an explosion 80 (`6.2g`), a suppressed gun 20 less. Spent = distance + full wall loss (not scaled by `6.2d`, which only sets the level now). Spent at or past the reach: no thump, reason `stopped by walls`. The last 6 dB fade. Per wall: the material's 125 Hz loss, or 15 dB for thick metal, plus 6 dB per doubling above 0.2 m, up to +12. Earth (soil, mud, gravel, grass, snow, terrain) is 75 dB flat. Fences, grates and broken glass aren't walls.

| In the way | Wall loss | Gunshot heard up to | Grenade heard up to |
|---|---|---|---|
| Thin wood, curtain, thin metal | 4–7 dB | 60 m (full range) | 100 m (full range) |
| Drywall, thick wood, thick metal | 15 dB | 60 m | 100 m |
| One concrete wall, 0.2 to 0.8 m | 24–36 dB | 60 m | 100 m |
| Two concrete floors, 0.3 m each | 55 dB | 9 m | 88 m |
| Three concrete floors, 0.3 m each | 83 dB | never | never |
| Earth or terrain | 75 dB | never | 9 m |

**Check (bots on).** A fight on another floor or behind a wall, with `6.2` on and off; thump.csv for walls, materials, thickness and `stopped by walls`. Look for `earth` on shots from a basement directly below you: that would be terrain running under the building. Tune `6.2a`, `6.2b`, `6.2d`, `6.2f` and `6.2g` by ear.

## 6.3: Explosion echoes (0.6.39)

**Problem.** Raid 35: three grenades in a stairwell got only the game's own sound, while your gunshots there got echoes.

**Fix.** `Fixes/ExplosionEchoes.cs` catches explosions on the same hook as 6.2 and fires 25 rays from the blast. A wall echoes if the blast and you can both reach it: a mirror bounce if the true bounce point checks out, else a weaker scattered echo. Level = the gun echo rule + 6 dB. The loudest `6.3b` (6) play, each cut to `6.3c` (1.5 s), on fix 6's voices.

| Scene | Paths | Level before `6.3a` |
|---|---|---|
| Grenade 6 m away in your concrete room | 10, 14, 18 m | about −8, −11, −13 dB |
| Round a corner, far wall reaches you both | 16 m | about −12 dB |
| Outdoors 40 m away, building 30 m past it | about 100 m | about −28 dB, about 175 ms after the bang |
| Another room, no wall you share | none | no echoes |

**Check (no bots needed).** Grenades in a concrete room and outdoors near a building, `6.3` on and off; echoes.csv sample `explosion`; does Bring Back Concussion duck them? Tune `6.3a` to `6.3d` by ear.

### 6.3e: Room echoes from the opening (0.6.48)

**Problem.** raid_20261109_102037: 8 of 11 explosions had no echo, because one bounce needs a wall both the blast and you can reach. Through a door or round a corner there usually isn't one.

**Fix.** A blast fills its room and that pours out of the door, so an explosion with a route (`Fixes/BlastFromOpening.cs`) also plays `6.3g` (4) echoes from the opening nearest you. Path = route + 6, 12, 20, 32 m (then 46, 62). Level = +3 − 20 log10(path / 2 m) − 2 dB per echo + `6.3f`. Each is 0.8 × duller than the last, starting 0.8 × the blast's own route cutoff. They fade out over the second half of `6.3c`.

| Blast | Echo 1 | Echo 2 | Echo 3 | Echo 4 |
|---|---|---|---|---|
| 5.6 m away, 7 m route | −13 dB, 22 ms | −19 dB, 39 ms | −24 dB, 62 ms | −29 dB, 97 ms |
| 10.4 m away, 12.6 m route | −16 dB, 24 ms | −21 dB, 41 ms | −25 dB, 65 ms | −30 dB, 100 ms |
| 37.3 m away, 39 m route | −24 dB, 22 ms | −27 dB, 40 ms | −30 dB, 63 ms | −34 dB, 98 ms |

**Limits.** Only where a route is found, so not the hideout or both outdoors. Nothing comes from your own room's walls. Levels are worked out by hand.

**Check.** A grenade in the next room, `6.3e` off and on, then `6.3f` by ear; echoes.csv `kind` `Room`.

## 6.4: Explosions from the opening (0.6.41)

**Problem.** raid_20261109_083938: a grenade 15 m away through two walls played from its true spot, with a thump at −25 dB. User: "i sort of wish grenades sounded more from the entrance and (felt (thud from teh source)".

**Fix.** Airborne sound from a blast reaches you through openings, while the heavy low end comes through the structure from the true spot. `Fixes/BlastFromOpening.cs`:

- Right after the game sets up the blast's one-shot occlusion, it has already found the blast's room. Of the baked routes between that room and yours, the shortest is used; a shut door counts as 15 m. Guns read the game's finished route (6.1), but for a one-shot that only arrives after the sound starts.
- The voice goes to `listener + direction(opening nearest you) × route length`, by `6.4a`, fading out within 1 m of the opening. It stays there while it rings.
- The game's occlusion follows the voice's transform, so the container gets an offset back to the true spot (`SetPositionOffset`). The game doesn't clear that offset when a container is reused, so a postfix on `AudioSourceContainer.Clear` does.
- `6.4b` dulls it with the rule from 6.1b, at its own strength `6.4c` (0.5 since 0.6.46: raid_20261109_100114's blasts went to 1.5 kHz at the gunshot strength and felt weak; now about 5 kHz). Our own filter goes back to neutral when the sound ends.
- `6.2h` raises only the explosion's thump, which stays at the true spot and still needs a wall on the straight line.

| Grenade in raid_20261109_083938 | Thump now | With `6.2h` +12 |
|---|---|---|
| 75 s, 27.9 m, one wall | −21 dB | −9 dB |
| 949 s, 15 m, two walls | −25 dB | −13 dB |

**0.6.42, bounces.** Each grenade has its own voice that the game keeps on the grenade every frame, so it's re-placed in LateUpdate like 6.1, with the route re-read every 0.1 s while the grenade moves or the voice plays. It follows `6.4`, `6.4a` and `6.4b`. Its occlusion follows the grenade itself, so it needs no offset. No thud for bounces.

**0.6.42, thump in the open (`6.2i`, −6).** An explosion with no wall in the way gets the thump too: the same formula with no wall loss, plus `6.2i`.

| Distance | Open-air thump |
|---|---|
| Up to 20 m | 0 dB (full) |
| 28 m | −3 dB |
| 40 m | −6 dB |
| 60 m | −10 dB |
| 80 m | −12 dB |
| 100 m | −14 dB |

**0.6.43, length (`6.2j`, 1.5 s).** How long an explosion's thump plays, 0.2 to 5 s. It can't run past the end of the recording. Since 0.6.44 it fades out over the second half of the length, so a short setting doesn't cut off.

**Limits.** The game's grenade group only, so not rockets. Same room, both outdoors or no baked route stay at the true spot. Flight time still uses the straight distance. It may feel disorienting the way 6.1 did on Factory: `6.4` off and `6.4a` at 0.5 are for that. A bot's pin and throw sounds aren't moved. The game only creates its spatial audio system when it loads a raid, so the hideout has no rooms or routes: `6.4`, `6.4b` and bounces do nothing there and it writes no logs. Since 0.6.45 an explosion's thump and echoes do work there, using the game's listener.

**Check.** Grenades in another room, `6.4` off and on, then `6.2h` and `6.2i` by ear, in the open as well; blast.csv `moved`, `reason`, `pathM`, `openings`, `turnDeg`, `movedDeg`, `cutoffHz`; thump.csv explosions with `walls` 0; summary BLAST FROM THE OPENING.

## 6.5: Closed doors muffle by material (0.6.47)

**Problem.** The game's route cost (`PropagationJob`) adds a shut door's closure level straight on and caps the total at 1, so a door at 1.0 could use up the whole score, plywood or steel. raid_20261109_102037: a grenade behind a closed door in Capital, closure 1.00. User: "doors I dont think closed should muffle so much especially not wood doors".

**Fix.** `Fixes/ClosedDoors.cs` holds down the closure the route data uses, by the door's material × `6.5a`. The material is the door's biggest by collider size, so a metal handle doesn't decide a wooden door. Never raised, and an open door stays open.

| Door | Closure used |
|---|---|
| Cardboard, fabric | 0.2 |
| Thin wood | 0.3 |
| Plastic, glass | 0.4 |
| Thick wood | 0.5 |
| Thin metal | 0.6 |
| Thick metal | 0.8 |
| Can't tell | 0.7 |

**Limits.** Hand-set values. Every sound through a door is affected, not only grenades, so bots behind doors are easier to hear. `6.5` off is the game's values. Doors only exist once the world has loaded, so they're read from the scene when the raid has them and it's applied again over the next 15 s. Before 0.6.52 the lookup crashed in SPT (`GameWorld.FindDoor`, null `World`), so no real door was held down and a route through a shut door also stopped `6.4`.

**Check.** A closed door with a grenade or a bot behind it, `6.5` off and on, including a door shut during the fuse; summary CLOSED DOORS for the materials read; `untagged`, `door elsewhere` or `door not found` there means those doors get the 0.7. A blast through a shut door has `closure` above 0 in blast.csv.

## 6.6: Blast recording by how open it is (0.6.49)

**Problem.** The game has indoor and outdoor grenade recordings and picks one, with no in-between, by its own indoor boxes at the blast point. Those are a separate map from the room map. raid_20261109_105838: two grenades near a garage front, room map outdoors, no occlusion and no dulling, and the user heard them as fully indoors. Which recording played wasn't logged, so this is the likely cause rather than a measured one. At 27 m the game also plays about 85% of its distant recording.

**Fix.** `Fixes/BlastRecording.cs`, on `BetterAudio.PlayAtPointDistant`. 48 rays over the upper half of the sphere, 30 m long, from 0.6 m above the blast. Share open: 0.08 or less is all indoor, `6.6a` or more is all outdoor, a smooth equal-power blend between. The stronger recording takes the game's voice; the weaker is a second call to the same game method, so it has the same distance, delay, occlusion, opening and dulling, and 6.2, 6.3 and 6.3e skip it. Under −16 dB it isn't played. `6.6b` widens the close recording's reach for that call only.

**Limits.** Starting values. A hall with a very high roof or a yard hemmed in by tall walls can read wrong. Beyond 200 m there's no indoor recording. The open-air thump (`6.2i`) is separate and still adds low end.

**Check.** blastrec.csv: `gameEnv` against `outdoorWeight` at a garage front, deep inside and in the open. `6.6` off and on; `6.6b` at 10 and 25.

**0.6.53, `6.6c`.** The rays only look around the blast. raid_20261109_114154: all 6 changed picks turned the game's Outdoor into Indoor (open share 0.00 to 0.23) with the player outside. User, beside a garage: "it still doesnt sound like its outdoor mix is right when its comes out the front of the entrance". When you're outside (enclosure probe under 0.45, fading out by 0.8) the outdoor share can't fall below `6.6c`, or half of it if the game also has the blast indoors. No probe reading counts as inside.

## 6.7: Glass from other rooms by route (0.6.49)

**Problem.** A window breaking in another room plays from the pane, with route loss only. User: "a few walls and turns" away and "it sounded a bit loud".

**Fix.** `BlastFromOpening`, in the game's one-shot occlusion pass for the window sound, which the game skips for a window of your own room. Same route, placement and dulling rule as a blast (`6.7a`), plus `6.7b` dB per opening, 16 at most.

**Limits.** Starting values. Only where a baked route exists.

**Check.** Break a window from another room, `6.7` off and on; glass.csv shows the route, cutoff and loss.

## 6.8: Blasts around a corner (0.6.50)

**Problem.** `6.4` needs a baked route, and routes only exist between rooms. Where the room map has you and the blast both outdoors there's none, so a blast behind a building played clear from its true direction. raid_20261109_105838: 4 of 12 blasts, one 28 m away with stone and concrete on the straight line.

**Fix.** `BlastFromOpening` and `Acoustics/CornerSearch.cs` (rebuilt in 0.6.51). Only when the straight line is blocked: level fans from you and from the blast, plus the openings probe v2 remembers, then points tested shortest way first for one or two corners. Close blasts use the small search (about 290 rays) in the same frame; with at least 50 ms of travel left the bigger one (about 960) runs over 2 frames. An answer is reused for 3 s within 3 m of the source and 1.5 m of you. The blast plays from that direction at that length, arriving later by `6.4d`, dulled by the `6.1b` rule at `6.4c`'s strength and `6.8a` dB quieter per corner. With no way round it stays at the true spot, a step duller and twice `6.8a` quieter, 18 dB at most. `6.8b` does the same for bot gunshots, position and tone only.

**Timing.** The thump keeps the straight line with a 1.5 m head start: by the Kinney-Graham formula a 60 g to 180 g charge's front is under 1% faster than sound at 28 m, and its lead by then totals 1.1 to 2 m (3 to 6 ms). Ground and building vibration, which travels faster, isn't modelled. A longer way round therefore puts the bang after the thump.

**Limits.** Starting values. Level fans only, so nothing over a wall or up a stairwell. Grenade bounces and glass aren't included. A small object on the line, like a car, counts as blocked.

**Check.** A grenade behind a building, from the front and from behind an entrance, `6.8` off and on; blast.csv `reason`, `turnDeg`, `pathM`, `cutoffHz`.

## 6.9: Shock wave (0.6.53)

**Problem.** User: explosions "just lack the sense of a shock wave that the player can feel". raid_20261109_114154: the thump is a recording with its highs removed, so it has no sharp front, and at +12 dB it's at full volume for anything within 20 m in the open, so a blast at 3 m has no more thump than one at 20 m.

**Fix.** `Fixes/ShockWave.cs`, three parts, all at the blast's arrival time.

- **Pressure hit.** A generated clip: a 2 ms rise, a short push and pull (Friedlander shape, stretched so headphones can play it), then a 42 to 80 Hz boom dying over about 0.2 s. It plays in the middle of the head with a slight lean towards the blast. Level is Kinney-Graham peak pressure for 100 g TNT, with 0 dB at 5 m, plus `6.9a`: +6 dB at 3 m (held at full volume), −7 at 10 m, −13 at 20 m, −17 at 30 m. Walls cost what they cost the thump, and a blast through a wall keeps only what's under 200 Hz. Fades out over the last 40% of `6.9b`. Flashbangs and smoke don't get it.
- **Thump.** A voice can't go over full volume, so above it `6.2` adds a second voice exactly in step, up to +6 dB.
- **Dip.** Footsteps, the environment and ambience (the Main, AmbientIn and AmbientOut mixer volumes) dip by up to `6.9c`, less with distance and walls, for about 0.38 s. Explosions and guns are on the Guns bus and don't dip.

**Limits.** The clip's shape, the 200 Hz, and the dip's depth and timing are by ear. The Guns bus may squash the front; not measured. Headphones limit how much can be felt. Bring Back Concussion cuts sound on the same mixer controls inside its radius: any control something else moves during a dip is let go and not put back, so a dip can't fight it or leave a channel quiet. That also means the dip may not be heard inside that radius.

**Check.** A grenade at 3, 10 and 25 m and behind a wall, `6.9` off and on; shock.csv; summary SHOCK WAVE.

**0.6.54, through the building.** raid_20261109_121839: every blast had a wall on the straight line and the hit averaged volume 0.06, even at 6 m in the same room. Published work on blasts in buildings (mostly small-scale, read from abstracts): a confined blast is a train of shrinking reflected pulses and then a slower gas pressure that lasts until the room vents; openings carry the wave between rooms; being behind a corner cut peak pressure by 43 to 60% in one study; channelling along a passage wins some of that back.

- **Way.** The stronger of the way through the openings (or around a corner, `6.8`) and the straight line through the walls. Same room or a clear line counts as open. Pressure is worked out over the length of the way, less `6.9d` per 90 degrees of turning, half that per extra opening, and the shut door's closure × 20 dB.
- **Tone.** Open or clear, untouched. Through openings, a 3 kHz low-pass halving per bend. A real path through a wall, 200 Hz.
- **Enclosed.** By how shut in the blast point is (the rays from `6.6`): up to `6.9e` dB, `6.9f` repeat hits spaced by twice the room's mean hit distance over the speed of sound (6 to 50 ms) at −5, −9, −13, −17 dB, and a 55 to 36 Hz swell of 0.25 to 0.7 s (`6.9g`), longer in a small or shut room. Corridors aren't treated specially.
- **Dip.** Applied on top of whatever else sets the control each frame, and put back only on a control nobody else has moved. Its depth follows the way.

## 10a, 10b: taken from Sound Engine Backport (0.6.35)

**10a.** `Fixes/FoleyPresets.cs`, a postfix on `BetterAudio.LoadGroupPreset` and a prefix on `BetterSource.CheckBinauralAllowed`. Character and Weaponary get SEB's curves, spread, occlusion radius and HRTF; levels and the mixer stay SPT's. Curves are read when the voices are built, so the switch needs a restart.

**10b.** `Acoustics/ExtraRoutes.cs` swaps the path of the game's route file (`SpatialAudioDataLoader`) when `seb_routes/AudioBakeData/<map>.audiobakedata` exists and the `.scene` list matches the map. Install: copy SEB's `acoustics/AudioBakeData` to `BepInEx/plugins/FFloridaMann-RealSoundSimulation/seb_routes/AudioBakeData`.

**Check.** Footsteps and reloads with `10a` on and off, restarting between. Summary "Fixes at end" for the routes line; routes.csv for fewer `noRoute`.

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
