# How SPT Tarkov's audio actually works

*Explanation doc, built from your install's game files. Written 6 Nov 2026.*

**What this is built from.** The `Assembly-CSharp.dll` and `Meta.XR.Audio.dll` in `G:\SPT\EscapeFromTarkov_Data\Managed` were decompiled. The server database in `G:\SPT\SPT_Runtime\SPT_Data\database` was read: `settings.json` → `config.AudioSettings`, `templates/items.json`, `bots/core.json` and `bots/types/*.json`. So were the baked audio files under `StreamingAssets`, your downloaded mods and your Real Sound Simulation mods. Every claim below that names a class or value was read straight from those files. Anything inferred is marked **(inferred)**. Anything not checkable without extracting Unity assets is marked **(not verifiable from code)**.

**Your build.** SPT targets EFT **0.16.9.5.40743** (`SPT_Data/configs/core.json`, and your raid logs), running on Unity **2022.3.43f1**. Live Tarkov 1.0.x audio patches (the Feb 2026 ambient low-pass fix, Reserve D2, the Jul 2026 medium-range gunshot fix, the Shoreline rework) are **not** in your client. Your earlier research note mixes those in.

---

## 1. The big picture in one diagram

```
Gameplay event (shot, step, voice, door, impact)
   │
   ├──► AUDIO PATH (what you hear) ─────────────────────────────────────────────┐
   │     SoundBank picks clip(s): environment (indoor/outdoor) × distance tier  │
   │     BetterAudio borrows a BetterSource (pooled AudioSource + Meta XR src)  │
   │     Unity rolloff over MaxDistance ("Rolloff") = basic distance loudness   │
   │     SpatialAudioSystem: one occlusion "container" per player/object        │
   │        same room / both outdoors → OBSTRUCTION (raycast T + D + R)         │
   │        different rooms → PROPAGATION (baked portal routes, Burst jobs)     │
   │        → one 0..1 score → volume curve + LPF/HPF cutoffs (+ gun mixer swap) │
   │     Meta XR Audio: HRTF + baked acoustic-map reverb/early reflections      │
   │     Mixer: indoor/outdoor snapshot, headset compressor/EQ, guns bus        │
   │                                                                           ▼
   │                                                                     your ears
   │
   └──► AI PATH (what bots hear) ── completely separate ─────────────────────────
         GlobalEventDispatcher.PlaySound(player, pos, power_in_metres, type)
         type ∈ {step, silencedGun, gun}; heard if distance < hearingSense × power
         (SAIN replaces most of this with its own model)
```

The most important structural fact is this: **what you hear and what bots hear are two unrelated systems.** Nothing the audio engine computes about walls, portals or doors reaches the vanilla AI.

---

## 2. BSG already built a rooms-and-portals propagation system (this corrects the old note)

The earlier research assumed Tarkov was "raycast occlusion only, no pathing". That is wrong for 0.16.x. The `Audio.SpatialSystem` namespace contains a full **room/portal propagation graph with baked routes**, plus a raycast **transmission + edge-diffraction + reflection** stage for sounds in the same room.

### 2.1 Baked data on disk

| Data | Location | What it is |
|---|---|---|
| `*.audiobakedata` (≈514 MB) | `StreamingAssets/AudioBakeData/` | BSG's own format: room pairs, the portal routes between them, portal geometry, bake limits |
| `*.xrageo` (565 files) + `*.xramap` (11 files, ≈771 MB total) | `StreamingAssets/Acoustics/` | Meta XR **Acoustic Ray Tracing** geometry and precomputed acoustic maps (reverb and early reflections) |

Room-pair counts, read from the first int of each bake file:

| Map file | Size | Room pairs |
|---|---|---|
| city_sound (Streets) | 158 MB | 108,128 |
| reserve_sound | 64.5 MB | 66,588 |
| shoreline_sound | 154 MB | 52,038 |
| custom_sound (Customs) | 33.5 MB | 17,680 |
| lighthouse_sound | 31 MB | 12,764 |
| shopping_mall_sound (Interchange) | 39 MB | 10,643 |
| sandbox_sound (Ground Zero) | 37 MB | 6,634 |
| laboratory_sound | 6.9 MB | 6,120 |
| factory_sound_rework | 11.5 MB | 5,557 |
| woods_sound | 0.8 MB | 1,699 |
| labyrinth_sound | 0.2 MB | 1,205 |

Bake limits (`LocationBakeSettings` defaults): indoor→indoor routes up to **7 portals / 200 m**, indoor→outdoor up to **3 / 300 m**, outdoor→outdoor up to **3 / 350 m**.

Mods can ship this data. **ManimalInterchange** carries its own `.xrageo` files, and **WTT-ClientCommonLib** has an `AudioBakeSidecarPatch` that loads extra bake data.

### 2.2 How a sound gets its occlusion (`SpatialAudioSystem`, `CombinedSpatialAudioOccluder`)

Each player gets **one container at the spine** (`ProcessSourceOcclusion(IPlayer…)`). Footsteps, gunshots, gear and voice from that player all share one occlusion result.

1. **Room lookup.** The source and the listener are each placed in a `SpatialAudioRoom`.
2. **Different rooms → propagation** (`ParallelSoundPropagationCalculator` → `SoundPropagationJobScheduler` → `PropagationJob`, which is Burst-compiled):
   - For every baked route between the two rooms, it finds the closest point on each portal (doors, windows, stairwells), walking the route.
   - Route cost combines:
     - bend angle at each portal: `((1−cos)/2)^diffractionExponent`
     - portal traversal cost: depth-scaled, with `minPortalCostPercent` near the portal
     - portal **closure level** (door state; `AudioPortalStateChangedEvent`)
     - wall occlusion of the front and back rooms
     - a height-change cost
     - a detour cost: `(1 − straightDist/pathLength) × distanceWeight`
   - The lowest-cost route wins, and its **cost (0..1) is the propagation score**.
   - The route's **path length is computed (`_bestDistance`) and then thrown away.** Nothing reads it.
3. **Same room, or both in common outdoor → obstruction** (`SpatialAudioOcclusionCombinedCalculator`), staged over several frames:
   - **Direct test:** transmission rays at quality factor 0.3. If the score is ≤ `minClearPathScore` (0.5), the path counts as **clear** and the calculation stops.
   - **Otherwise:** a second transmission pass with height sampling. **Edge diffraction** runs only if the source's priority is High or Medium.
   - **Reflection** (24/20/16 rays, up to 3 bounces) runs only if both transmission and diffraction scores are high **and** the source is High or Medium priority.
   - **Final score = the plain average of whichever stages ran.**
4. **Score → sound.** `SpatialAudioOccluder.CalculateFinalOcclusionParams / ApplyOcclusionToSource`:
   - Score × environment factor (indoor↔outdoor factor per group).
   - Volume comes from the group's `volumeCurve(score)` × `heightVolumeCurve(floors apart)`. Under propagation, it is clamped by `environmentVolumeThresholds` (the floor/ceiling volumes for isolated rooms).
   - LPF and HPF come from `FilterCutoffCalculator`:
     - `frequencyCurve(score)`
     - floor-height curve (propagation only)
     - per-direction **floors**: the LPF cannot go below `levelFreq`, `aboveFreq`, `belowFreq` or `behindFreq`, weighted by where the source is relative to your head (front/back/up/down)
     - isolation lerp
   - `rolloffScale` shrinks the AudioSource's MaxDistance slightly while propagated.
   - **Gunshots only:** if the occlusion effect is above 0.55, the shot's whole queue is moved to the `Guns/Occluded` mixer group (`AudioWeaponUtils.GetGunshotOcclusionMixerGroup`). Below that, it stays on `Guns/Gunshots`.

### 2.3 What the system does *not* do

- **It never moves the sound.** Under propagation the AudioSource stays at the true source position. A shooter in the next room is heard **through the wall**, only quieter and muffled, not from the doorway the sound actually came through. Searching for `virtual/apparent/portal position` finds nothing. This is the root of "upstairs sounds like it's on my floor" and "I can't tell which door they're behind."
- **It does not use path length.** Distance loudness always comes from the straight line, even when the sound travelled 40 m around a corridor to reach you.
- **It does not model air absorption.** `DistanceHiCutFrequency` and `DistanceLowCutFrequency` are calculated from each group's `distanceCurve` but **never applied to any filter**. They are dead values. The only distance darkening is whatever is baked into the far gunshot samples, plus any lowpass on the Unity prefab **(not verifiable from code)**.
- **Transmission ignores materials.** Wall loss is `exp(−absorptionPerUnit × thickness)` with `absorptionPerUnit = 0.3` (server value). A 25 cm wall of anything keeps about **93%** of the energy and counts as "clear". See section 6.

---

## 3. Every tuning value that matters lives on the server

`ClientAudioSettings` is filled from `settings.json → config.AudioSettings`. `SpatialAudioSystem.method_0()` applies `OcclusionSettings.locationOcclusionSettings`, and `BetterAudio.ApplySpatialSettingsFromBackend` applies the per-group JSON. **An SPT server mod can retune almost all of this without patching the client.** The full dump is in [data/settings_AudioSettings.json](../../../data/settings_AudioSettings.json), with a readable summary in [data/occlusion_presets_summary.txt](../../../data/occlusion_presets_summary.txt).

Key live values:

| Setting | Value | Meaning |
|---|---|---|
| `commonSettings.maxDistance` | **250 m** | Obstruction raycasts stop here (see the 250 m cliff in section 6) |
| `commonSettings.smoothingFactor` | 20 | Occlusion lerp speed (×dt) |
| `commonSettings.effectChangeThreshold` | 0.05 | Changes smaller than this aren't pushed to sources |
| `commonSettings.transmissionThreshold / diffractionThreshold` | 0.6 / 0.5 | Gates for the reflection stage |
| `commonSettings.positionChangeThreshold` | 0.25 / 0.35 / 0.4 m (High/Med/Low) | Movement needed before a recalculation |
| `transmissionSettings.absorptionPerUnit` | **0.3 per m** | Energy loss per metre of wall |
| `transmissionSettings.obstacleMin/MaxThickness` | 0.1 / 4.5 m | Thinner than 0.1 m = no loss; thicker than 4.5 m = blocked |
| `transmissionSettings.initialRaysCount` | 12 / 10 / 8 | Rays by CPU tier |
| `diffractionSettings.edgeSearchRayCount` | 16 / 12 / 8 | Rays by CPU tier |
| `reflectionSettings.initialRaysCount / maxReflections` | 24 / 20 / 16, 3 bounces | Rays by CPU tier |
| `propagationSettings.routesCompressionFactorByQuality` | 1.0 / 0.75 / 0.65 | Share of routes evaluated by CPU tier |
| `propagationSettings.diffractionExponent / distanceWeight / absoluteHeightWeight` | 1.1 / 0.35 / 0.35 | Route cost weights |

**CPU tiers.** `AudioUtils.GetQualityByLogicCores()` picks **High at ≥12 logical cores, Medium at 8–11, Low below 8.** The same fight therefore sounds different on different PCs.

**Group presets** (`AudioGroupPresets`): Gunshots, Weaponry, Impacts, Character, Environment, Collisions, Speech, Distant, Nonspatial(Bypass), Voip, Grenades, Windows. Each has `OverallVolume` (Speech 0.7) and binaural gating:

| Group | DisabledBinauralByDistance | DistanceToAllowBinaural | AngleToAllowBinaural | HeightToAllowBinaural |
|---|---|---|---|---|
| Weaponry, Collisions, Windows | **true** | 10 m | 12° | 2 m |
| everything else | false | — | — | — |

For the "true" groups, `BetterSource.CheckBinauralAllowed` **switches HRTF on and off** as distance, head yaw and height cross those limits. HRTF is off within 10 m, within ±12° of straight ahead, and inside 1.5 m. It is forced on behind you or more than 2 m above or below. Magazine, bolt and handling sounds therefore change timbre as you turn your head.

**Meta XR per-group sends** (`MetaXRAudioPluginSettings.audioGroupAcousticSettings`). Mono sources have reverb on, with `reverbSendDb` −3 to −8 dB, `earlyReflectionsSendDb` −3 to −7 dB and `reverbReach` 0.5. Stereo (first-person) sources have reach 0. `LightOcclusion` has reverb off.

**Footsteps** (`PlayerSettings`):

- Base audible radius: `BaseMaxMovementRolloff` = 90 m for other players (70 m for your own first-person steps).
- Movement multipliers: Run 0.85, Sprint 0.95, Duck 0.4, Prone 0.5, Turn 0.8, Jump 0.5, Search 0.2.
- `OutdoorRolloffMult` 0.98, `IndoorRolloffMult` 1.0.
- Step volume is scaled by the same ratio as the radius (`CalculateMovementVolumeDefaultMultiplier`).
- `EnvironmentSettings` holds season rain/wind ambience multipliers, plus `StepsVolumeMultiplier` 0.35 and `SurfaceMultipliers` for the seasonal step layer (snow, wet surfaces).

---

## 4. Gunfire

### 4.1 Report (`WeaponSoundPlayer.FireBullet`, `SoundBank.PickClipsByDistance`)

- **Banks:** `Body`, `Tail`, `Doublet`, plus `…Silenced` versions of each. Full-auto is a body loop cut on a 16-beat grid and followed by a tail (`StopFiringLoop`).
- **Distance tiers:** three clip tiers (close, mid, far) with crossfade zones between them, set by `BlendOptions.Values`. The tier is picked from **straight-line distance at the first shot** of a burst, and `Balance()` re-blends during auto fire.
- **Environment:** the clip set is chosen by the **shooter's** `Environment` (indoor or outdoor).
- **Speed of sound is real.** `_delay = distance / 340.29`, scheduled sample-accurately with `PlayScheduled(dspTime + delay)`. This settles the open question in the old note: a 300 m shot arrives about 0.88 s late.
- **Suppressors are a binary switch.** `IsSilenced = weapon has any SilencerComponent`, and that swaps to the Silenced banks. The items database gives every suppressor a `Loudness` from −12 (556-MINI) to −39 (VAL), and muzzle brakes up to **+40** (ST6012). The client code reads `Loudness` **only for the item stats screen.** Every suppressor sounds the same and brakes add nothing.
- **Occlusion:** the queue's two sources join the shooter's container. Semi-auto occlusion updates are released ⅙ of the clip after firing, so the tail plays with frozen occlusion. Above 0.55 the queue swaps to the `Guns/Occluded` mixer group.
- `SpatialAudioSystem.SetGunshotsLowPassFilterFrequency` sets the guns-bus `GunsLowPassFilter` to 22 kHz (it was 900 Hz under the old system).

### 4.2 Supersonic crack (`SonicBulletSoundPlayer`)

- It exists, but it is crude:
  - It plays only if the ammo's `InitialSpeed` is at least 340.29 m/s. Bullet slowdown is ignored.
  - It plays only if the bullet passes within ⅛ of the shooter distance.
  - It is skipped entirely if the shooter is occluded (`IsOccluded`).
- It is **played at the shooter's muzzle position**, not at the Mach-cone point near you.
- It is **delayed by the same amount as the report**, so it arrives together with the bang. In reality it arrives first, by `R/c − R/v`, which is about 0.54 s at 300 m.
- There are only four clips: 9 mm, 5.45, 7.62 and shotgun.

### 4.3 Flyby (`FlyingBulletSoundPlayer`)

- Plays within 10 m of the bullet's closest approach. It is **non-spatial**: `PlayNonspatial` with a stereo pan from `CalculatePan`, which gives no HRTF and no up/down information.
- Only one flyby can play at a time; the next is blocked until the clip ends.
- Subsonic and supersonic bullets get the same whiz.
- Off-by-one bug: `Random.Range(0, _sources.Length − 1)` never picks the last clip.

### 4.4 Explosions

Grenades use the `Grenades` group with `SuperSourceDistant`. Bots are told about explosions as `AISoundType.gun` with a power of 190 m (`BotsController`). Whether explosions get the speed-of-sound delay was not traced **(unverified)**.

---

## 5. Headsets, hearing and the listener

- Headset templates (`items.json`) carry `CompressorThreshold` (−20 to −25), `CompressorAttack` and `CompressorRelease`, `Distortion` (0.05–0.15), a three-band EQ, a high-pass at 180–250 Hz, `AmbientVolume` −50 and `DryVolume` −50/−60, per-bus compressor sends, and `RolloffMultiplier` (1.00 GSSh-01 to 1.135 ComTac VI).
- **In SPT, `RolloffMultiplier` does nothing for bot footsteps.** It is read only in `ObservedPlayerAudioController.OnRolloffChanged`, which handles online network players. SPT bots are `LocalPlayer : Player`, whose `ProtagonistHearing` getter returns 1. So vanilla SPT headsets change the DSP but not the hearing radius. Your Headsets mod patches `Player.ProtagonistHearing`, so it is the only thing creating a radius effect. **(High confidence; no SPT module references it either.)**
- Scaling MaxDistance to "hear further" stretches the whole loudness and filter curve, because DistancePercentage = 1 − (d / MaxDistance)². Any radius buff therefore also changes how far away a sound *seems*.
- The indoor/outdoor mixer snapshot switches globally on the **listener's** environment: `BetterAudio.TransitToEnvironment` takes 0.1 s going in and 0.5 s going out. That gives the audible "snap" at doorways.
- `BetterAudio.ProtagonistHearing` is the Perception skill buff (used by VoIP), not the headset.

---

## 6. Where the inconsistency comes from (ranked)

These are the concrete mechanisms behind "same situation, different sound".

| # | Mechanism | Where | Effect |
|---|---|---|---|
| 1 | **Propagated sounds keep their true position** | `SpatialAudioOccluder.ApplyOcclusionToSource` only sets volume and filters | Direction points through walls and floors instead of the doorway or stairwell; vertical confusion |
| 2 | **Obstruction score is a plain average of the stages that ran**, and which stages run depends on distance priority | `SpatialAudioOcclusionCombinedCalculator.GetScore` / `OnLateUpdate` | Same wall, different muffle at different distances, with a jump at ⅓ and ⅔ of MaxDistance. A clear diffraction path averaged with a blocked transmission path gives "half occluded" |
| 3 | **CPU-core quality tiers** | `AudioUtils.GetQualityByLogicCores` + quality arrays | Different ray counts and route coverage on 6-core vs 16-thread PCs |
| 4 | **The 250 m cliff** | `TransmissionCalculator.StartCalculation`: past `maxDistance` it returns `NoPath` (energy 0, score 1) | Inside the source's audible range but past 250 m, every sound counts as **fully obstructed even in open field**. Gunshots then flip to the Occluded mixer. A shot at 249 m is bright, at 251 m muffled **(inferred: depends on gunshot Rolloff > 250, which appears true)** |
| 5 | **Collider topology decides the result** | `ProcessHitsAndCombineJob`: transmission is computed only when the listener ray and the source ray hit **the same collider**; otherwise `NoPath` | One wall split into two colliders, or wall plus crate, gives "fully blocked". One thick collider gives "nearly clear" |
| 6 | **Material-agnostic, very weak wall loss** | `absorptionPerUnit` 0.3 | Plywood and concrete behave the same; a single wall often counts as clear |
| 7 | **Binary gunshot mixer switch at 0.55** | `GetGunshotOcclusionMixerGroup` | Timbre jumps as the score crosses the threshold, with no hysteresis |
| 8 | **Round-robin updates, one processor per frame** | `SpatialAudioSystem.method_25` (`MAX_UPDATED_PROCESSORS_PER_FRAME = 1`); Low priority also skips 2 frames | With 20+ active sources, each source's occlusion is refreshed every few hundred ms; moving targets lag behind their geometry |
| 9 | **HRTF on/off flipping** for Weaponry/Collisions/Windows | `BetterSource.CheckBinauralAllowed` + server presets | Handling sounds change timbre as you turn |
| 10 | **Direction-based LPF floors on top of HRTF** | `DirectionalWeightCalculator` + `positionEqThresholds` | Behind/above/below darkening is applied by EQ as well as by HRTF (double-counting, and it changes with head turns) |
| 11 | **No air absorption; distance curves unused** | `DistanceHiCutFrequency` is never consumed | Footsteps and voices don't darken with distance; gunshots darken only by sample tier, in steps |
| 12 | **Gunshot tier picked by straight-line distance** | `PickClipsByDistance` | A shot 60 m away around a building uses the "close" sample, muffled, instead of a distant one |
| 13 | **Listener snapshot snap** | `TransitToEnvironment` 0.1 s / 0.5 s | A global wet/dry jump at thresholds |
| 14 | **Crack timing and position wrong** | `SonicBulletSoundPlayer` | Crack and report arrive together, from the shooter |
| 15 | **Suppressor Loudness ignored** | binary `IsSilenced` | Every can sounds identical |
| 16 | **Smoothing speed depends on how busy the fight is** | `CombinedSpatialAudioOccluder.OnUpdateOcclusionEffects` lerps by `smoothingFactor × Time.deltaTime`, but runs only on that container's round-robin turn | With N active containers the occlusion fades up to N times slower. It is somewhat less, because each played sound also triggers an update. Quiet raid = snappy, big firefight = sluggish |

---

## 7. AI hearing (vanilla) and what SAIN changes

**Vanilla** (`AIData.TryPlayShootSound`, `BotHearingSensor`):

- Gunshot power = `GUNSHOT_SPREAD` **280 m**, suppressed = `GUNSHOT_SPREAD_SILENCE` **60 m** (`bots/core.json`). These are global: no caliber, ammo or device. The event is rate-limited to one per second per shooter.
- Footsteps and doors use `MovementContext` power. Door open = 10 m, breach = 35 m, jump = 25 m.
- Heard if `distance < CurrentHearingSense × power`. There are **no walls and no portals**.
- Simple sounds (steps) use a close/far chance ramp (scav 10→30 m, PMC 26→56 m, Knight 36→66 m).
- The heard position is randomised by ±distance/`DISPERSION_COEF`, horizontally only.
- `HEAR_DELAY_WHEN_PEACE` (0.75 s scav, 0 s PMC) delays the reaction.

**SAIN** (installed) replaces much of this:

- It hooks `BaseSoundPlayer.SoundEventHandler/SoundAtPointEventHandler` and `AIData.TryPlayShootSound`.
- Gun range = caliber base range + muzzle `Loudness` × suppressor factor (subsonic aware).
- Occlusion is a **line-of-sight multiplier** (`GUNSHOT_OCCLUSION_MOD`, `FOOTSTEP_OCCLUSION_MOD…`), plus an environment mismatch modifier and a bunker modifier.
- Earpiece, helmet, sprint and heavy breathing all count.
- It does not use BSG's portal routes either.

---

## 8. Your installed mods vs. the audio stack

| Mod | Touches | Notes |
|---|---|---|
| **The earlier audio mod** (Real Audio + Headsets + Gun Audio) | Listener bus limiter (`OutputLimiter` on AudioListener), per-source `SelfSoundFilter` (OnAudioFilterRead), headset configurators, `Player.ProtagonistHearing`, gun queue volume and overlap, full-auto loops, tinnitus/TTS, grenade thump, SAIN `CheckIfSoundHeard`, BringBackConcussion hooks | Mostly the **listener model and mix**. None of it touches propagation, positioning or occlusion, which is where most of the realism and consistency problems are. The single listener-bus owner is the right architecture. Per-source OnAudioFilterRead is the one performance risk (section 9) |
| **SAIN** | Bot hearing, shot sounds, door, vault, aim | See section 7. The best place to plug in a better audibility model |
| **DoorOverhaul** | `Door.CheckOcclusionPortal`, `WorldInteractiveObject.PlaySound/SetDoorState` | Changes how doors open and close portals, so it changes propagation (closure level). Test any portal work with it on |
| **ManimalInterchange** | `SoundBank.PickClipsByDistance`, ambient blend system, own `.xrageo` geometry | Proof that custom acoustic geometry and patched clip selection are shippable |
| **WTT-ClientCommonLib** | `AudioBakeSidecarPatch`, `DistanceAudioFadePatch`, `AudioLifecyclePatch`, `AudioArray.PlayWithOffset` | Loads extra bake data for custom content |
| **SPTBattleAmbience** | Plays fake distant battle sounds with `BetterAudio.PlayAtPoint` (default group 0 = Gunshots) | Sounds not caused by anything real. They break "honest cues", and SAIN or bots may react to them depending on the path **(inferred)** |
| **BringBackConcussion** | `TryStartContusion`, `DoContusion` | Concussion and tinnitus only |
| **Realism** | Only the stance overhaul and common lib are installed | Realism's deafening module is **not** present, so there is no double-applied deafening |
| inory-agonysfx | `Player.OnDead` | Death sounds only |

---

## 9. Performance model: what the engine already spends

- **Obstruction:** at most one container is advanced per frame. Each pass costs roughly 2×(8–12) transmission raycasts, plus 2×(8–16) edge-search rays and validation rays for High/Med sources, plus up to 24×3 reflection rays for close ones. They run as `RaycastCommand` batch jobs on worker threads.
- **Propagation:** Burst-compiled parallel jobs over the routes for one room pair (at least 350 routes, or all of them). This is cheap.
- **Meta XR:** HRTF per spatialised voice, plus ray-traced reverb from the baked `.xramap`. Meta says the runtime is a lightweight interpolation of the baked data, and edge diffraction is its most expensive part ([Meta docs](https://developers.meta.com/horizon/documentation/unity/meta-xr-acoustic-ray-tracing-unity-map/)).
- **Managed DSP:** `OnAudioFilterRead` runs C# on the audio thread every ~20 ms block and can be **interrupted by GC**. Unity marked that "Won't Fix" ([Unity issue](https://issuetracker-mig.prd.it.unity3d.com/issues/audio-stuttering-occurs-when-heavy-processing-is-performed-while-onaudiofilterread-is-in-use); [docs](https://docs.unity.com/en-us/engine/current/script-reference/unityengine/monobehaviour/onaudiofilterread)). One allocation-free filter on the listener is fine. Dozens of per-source filters are the classic cause of crackle.
- **Burst:** `PropagationJob`, the transmission jobs and the others are `[BurstCompile]` and run from `lib_burst_generated.dll`. **Harmony cannot patch their `Execute`**; it can only patch the managed code that schedules them or reads their results. Burst 1.8 does support loading an *additional* mod library compiled in a matching Unity 2022.3 project (`BurstRuntime.LoadAdditionalLibrary`, present in your `Unity.Burst.dll`; [Unity docs](https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/modding-support.html)).

---

## 10. Corrections to the earlier research note

| Old note said | Game files say |
|---|---|
| Raycast-and-filter only; no pathing | Baked rooms/portals graph with route costs, door states and height costs; transmission, edge diffraction and reflections inside a room |
| Speed-of-sound delay unknown | Implemented for gunshots: 340.29 m/s, sample-accurate |
| Crack not modelled separately | Separate crack layer exists, but at the wrong position and time |
| Reverb from authored zones | Meta XR acoustic ray tracing with baked acoustic maps on 11 maps; per-group sends; plus an indoor/outdoor mixer snapshot |
| Headsets extend hearing radius | Only for online observed players. Dead in SPT unless a mod patches it (yours does) |
| Suppressor Loudness unimplemented | Confirmed: display-only; suppression is binary |
| Bot hearing values unknown | 280 m gun / 60 m suppressed / 10 m door / 35 m breach / 25 m jump; per-type close/far ramps (above) |
| Tarkov 1.0.x patches apply | Your client is 0.16.9.5; none of the 1.0.x audio changes are present |
