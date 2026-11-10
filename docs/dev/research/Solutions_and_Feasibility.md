# Making SPT audio realistic *and* consistent: solutions and feasibility

*Solutions doc. Read [01_How_SPT_Tarkov_Audio_Works.md](How_SPT_Tarkov_Audio_Works.md) first. The `#n` references point to the inconsistency table in its section 6.*

## The strategy in one paragraph

BSG already ships most of the hard parts: a baked room/portal graph, door states, edge diffraction, a speed-of-sound delay, a crack layer, and Meta XR ray-traced reverb from baked maps. What's wrong is the **glue**. Propagated sounds aren't moved to the portal they come through. Scores are averaged in ways that change with distance and CPU. There's a 250 m cliff. Materials and air absorption are ignored, and the crack and flyby are placed wrongly. So the plan is **not** to replace the engine. It is:

1. retune the server-side JSON, which costs nothing at runtime,
2. fix the managed glue with small Harmony patches, and
3. only then add a few well-bounded new features.

Every item below has a **performance cost rating** and a **feasibility rating**. The items that would wreck performance are collected in section 5.

Ratings used:
- **Perf:** `none` (server data only) · `negligible` (<0.05 ms/frame) · `low` (<0.2 ms) · `moderate` (0.2–1 ms or extra voices) · `heavy` (>1 ms, voice explosions or audio-thread risk). These are engineering estimates; verify them with FrameDiag (section 6).
- **Feasibility:** `easy` (hours) · `medium` (days) · `hard` (week+) · `not practical`.

---

## Phase 0: instrument before changing anything (do this first)

You can't tune consistency you can't see. Build a tiny debug plugin, or add it to FrameDiag:

| Probe | How | Why |
|---|---|---|
| Per-container overlay: room ID, `PropagationEffect`, `ObstructionEffect`, which stages ran (T/D/R), priority, current gun mixer group | Read `AudioSourceContainer` and the occluder's public fields; draw with `OnGUI` or Gizmos | See #2, #4, #5, #7 happen live |
| Dump each group's AudioSource rolloff mode and custom curve, `maxDistance`, spatialBlend, and any `AudioLowPassFilter` curve | `AudioSource.rolloffMode`, `GetCustomCurve(AudioSourceCurveType.CustomRolloff)` on borrowed sources | The prefab curves can't be read from code; the dB math in Phase 2 depends on them |
| Voice counts | `AudioSettings.GetConfiguration().numRealVoices / numVirtualVoices`, plus a count of playing `BetterSource`s | Exceeding real voices causes **voice stealing**, which drops sounds at random. That is an inconsistency in itself |
| Time in `SpatialAudioSystem.Update/LateUpdate`, `BetterAudio.Update`, and your own patches | FrameDiag / Stopwatch around Harmony prefix/postfix | Budget for everything below |
| Audio-thread health | Log `OnAudioFilterRead` duration on your listener limiter | Catch GC stalls early |

Test spots:

| Spot | Tests |
|---|---|
| Factory | stairs, floor above/below |
| Interchange | big portal graph |
| Customs dorms | multi-storey |
| Woods / Customs fields | the 250–600 m open-ground cliff |
| Streets | dense room pairs, worst-case load |
| Reserve D2 | isolated bunker |

**Perf:** negligible (debug only). **Feasibility:** easy.

---

## Phase 1: server-only retune (zero client cost, survives client updates best)

Everything here lives in `database/settings.json → config.AudioSettings`. SPT 4's typed `AudioSettings` record keeps unknown fields through `[JsonExtensionData]`, so a C# server mod can edit `DatabaseService` settings at load. Or test by hand-editing the JSON copy first. Keep the original in [data/settings_AudioSettings.json](../../../data/settings_AudioSettings.json).

| # | Change | Path | From → to (starting point; tune by ear) | Fixes | Perf |
|---|---|---|---|---|---|
| 1.1 | Same quality on every PC | `OcclusionSettings.locationOcclusionSettings.*.(initialRaysCount / edgeSearchRayCount / positionChangeThreshold / routesCompressionFactorByQuality)` | Set the Medium and Low entries equal to High (12 / 16 / 24 rays, 0.25 m, 1.0 routes) | #3 | low (a <8-core CPU does High-tier rays, still only one container per frame) |
| 1.2 | Push the 250 m cliff out | `…commonSettings.maxDistance` | 250 → **500** | #4 (partly; the full fix is 2.4) | low (longer raycasts through the BVH; still round-robin) |
| 1.3 | Make walls matter | `…transmissionSettings.absorptionPerUnit`, `minClearPathScore`, `obstacleMinThickness` | 0.3 → **2.5**; 0.5 → **0.3**; 0.1 → **0.05** | #6. A 20 cm wall now keeps about 61% energy and is no longer "clear" | none |
| 1.4 | Stop HRTF flipping on handling sounds | `AudioGroupPresets[Weaponry, Collisions, Windows].DisabledBinauralByDistance` | true → **false** | #9 | negligible (a few more HRTF voices up close) |
| 1.5 | Less EQ double-counting of direction | each group's `obstructionEQPreset.rotationCoefficient` (0.2) and `positionEqThresholds` | rotationCoefficient 0.2 → **0.05**; raise the above/below/behind floors toward `levelFreq` | #10. Let HRTF carry direction; keep a small, deliberate vertical darkening | none |
| 1.6 | Calmer occlusion fades | `…commonSettings.smoothingFactor` | 20 → **12** (pair with 2.8; on its own this just slows the fades) | #16 partly | none |
| 1.7 | Reverb as a distance cue | `MetaXRAudioPluginSettings.audioGroupAcousticSettings[*].mono.reverbSendDb / earlyReflectionsSendDb` | Small (±2 dB) changes only. These are the values your Gun Audio "reverb boost" changes in code. Do it here and drop that code | — | none at the same sends; raising `reverbReach` or enabling reverb on more groups costs Meta XR CPU |
| 1.8 | Footstep audibility as gameplay knobs, documented | `PlayerSettings.MovementRolloffMultipliers`, `BaseMaxMovementRolloff` | Leave until Phase 0 tells you the rolloff curve shape, then convert to dB targets (walk = ref, run +6, sprint +9, crouch −10) | consistency of steps | none |

What Phase 1 **cannot** fix: positioning (#1), score averaging (#2), collider topology (#5), the mixer switch (#7), crack and flyby (#14), suppressor Loudness (#15), update rate (#8, #16). Those are client code.

**Feasibility:** easy. **Risk:** values interact. Change one row at a time and use the Phase 0 overlay.

---

## Phase 2: client glue fixes (small Harmony patches on managed code)

Rule: **never try to Harmony-patch a Burst job's `Execute`** (`PropagationJob`, `ProcessHitsAndCombineJob`, etc.). It runs from `lib_burst_generated.dll` and your patch won't fire. Patch the managed schedulers and readers around it instead.

### 2.1 Put propagated sounds at the portal they come through ★ biggest realism win
**Fixes:** #1, and the "upstairs sounds level" and "which door?" complaints. **Perf:** negligible to low. **Feasibility:** medium.

The route data is already computed every update; it just isn't used.

1. **Postfix `SoundPropagationJobScheduler.Schedule`.** Remember `isReversed`, the route count actually evaluated (`max(min(350, len), ceil(len × qualityFactor))`), and the listener position.
2. **Postfix `SoundPropagationJobScheduler.Complete`.** Scan `_lowestCost[0..n)` for the argmin route `i`. That's at most a few hundred floats.
   - Read `AudioRouteData r = _routesSettingsData[i]`.
   - The nodes in `_routeNodesData` from `r.NodeStartIndex` are `[Emitter, Portal…, Listener]`. Zero-vector portals are skipped by the job.
   - The portal nearest the listener is the **last portal node**. If `isReversed`, it is the **first** portal node.
   - The path length is `_lowestDistance[i]`.
3. **Compute the virtual position** (the usual diffraction virtual-source trick):

   `virtualPos = listener + normalize(portalNearListener − listener) × pathLength`

   Direction comes from the doorway or stairwell. Distance loudness comes from the real path length (this also fixes "path length unused").
4. **Stabilise it.**
   - Hysteresis on route choice: only switch if the cost improves by more than 0.05.
   - Lerp the virtual position over about 150 ms.
   - Blend from the true position at propagation score 0 to the virtual position by score 0.3, so a sound just around a doorframe doesn't jump.
5. **Apply it to the container's playing `BetterSource`s** after the game positions them, and restore before the game positions them again.
   - Do it in a very-late `LateUpdate` with `[DefaultExecutionOrder(int.MaxValue)]`, and restore in a very-early `Update`.
   - Gun queues re-`Pose()` every Update; bone-parented sources are re-posed by animation.
   - **This is the one fiddly part.** The occlusion math uses the container's spine position, not the AudioSource's, so moving the AudioSource doesn't feed back into the solver.

Meta XR will then compute HRTF and reverb from the portal side, which is physically right: the sound enters your room through that opening.

Not covered: one-shot propagated sounds (`MultiSourceSpatialAudioOccluder`, used for impacts and interactive objects). Extend to them later.

### 2.2 Combine obstruction paths physically, not by average
**Fixes:** #2. **Perf:** negligible (the score change) or low (forcing diffraction). **Feasibility:** easy.

- **Postfix `SpatialAudioOcclusionCombinedCalculator.GetScore`.** Treat each stage's `1 − score` as surviving energy and **sum** the energies (or take the best path):

  `score = 1 − clamp01((1−T) + (1−D) + (1−R))`  for the stages that ran

  A clear diffraction path around a corner now means "audible", not "half occluded".
- **Make the stages independent of distance tier.** Patch the `TDScheduled` branch in `OnLateUpdate` so diffraction runs for Low priority too. Reflection can stay High/Med only; it's the expensive one.
- **Cost:** diffraction for far sources is 2 × 8–16 edge rays plus validation, still for only one container per frame.

### 2.3 Stop the gunshot mixer flip-flopping
**Fixes:** #7. **Perf:** negligible. **Feasibility:** easy.

Prefix `WeaponSoundPlayer.UpdateMixerGroup` with per-instance state. Switch to `Guns/Occluded` above **0.60**, and back only below **0.45**. Better still, decide once at the first shot of a burst and hold it until `Release`. The rest of the occlusion change is already carried by the per-source LPF and volume.

### 2.4 Remove the 250 m cliff properly
**Fixes:** #4. **Perf:** low. **Feasibility:** easy to medium.

Prefix `TransmissionCalculator.StartCalculation`. When `distance > maxDistance`, don't return `NoPath`. Instead cast **one** cheap ray, listener → source, against terrain and buildings only:
- If it hits, report a partial block, e.g. remaining energy 0.3. Terrain and building edges are diffracting, not total.
- If it doesn't hit, report `ClearPath`.

Let 2.5 (air absorption) and the far sample tier do the distance darkening. The result: open-field shots at 300–800 m are no longer "behind a wall", and hills still dull them.

### 2.5 Add air absorption (distance makes things dull, not just quiet)
**Fixes:** #11. **Perf:** negligible (a cutoff on filters that already run). **Feasibility:** easy.

Postfix `SpatialAudioOccluder.ApplyOcclusionToSource`, or prefix `BetterSource.SetLowPassFilterParameters`.

- Set `lowPass = min(lowPass, AirCutoff(d))`, using the **path length** from 2.1 when propagated.
- `AirCutoff` is fitted to ISO 9613-1 at 20 °C / 70% RH: 8 kHz loses ≈76.6 dB/km, 4 kHz 22.9, 2 kHz 9.0, 1 kHz 5.0. Rough points where the loss reaches about 3 dB:

  | Distance | Cutoff |
  |---|---|
  | 40 m | ~9 kHz |
  | 100 m | ~5 kHz |
  | 300 m | ~2.2 kHz |
  | 1 km | ~600 Hz |

  Use those as a curve. Optionally key it to map weather and temperature.
- **Gunshots:** the far tier samples already contain distance darkening. Apply only the *difference* beyond the tier's nominal distance, or apply at 50% strength, to avoid double-dulling. Footsteps, voices, impacts and doors get the full curve.
- This also replaces the never-used `DistanceHiCutFrequency`.

### 2.6 Pick the gunshot distance tier by perceived distance
**Fixes:** #12. **Perf:** negligible. **Feasibility:** easy.

In a `WeaponSoundPlayer.FireBullet` prefix, compute an "effective distance" for `PickClipsByDistance`:
- the path length from 2.1 if propagated, or
- the straight-line distance × (1 + 1.5 × obstruction score) if obstructed.

A shot 60 m away behind a building then uses the mid sample, not a muffled close one.

**Conflict:** ManimalInterchange also patches `SoundBank.PickClipsByDistance`. Patch the *argument at the call site* (`FireBullet`), not the bank method.

### 2.7 Real suppressor and muzzle-device loudness
**Fixes:** #15. **Perf:** negligible. **Feasibility:** easy. You already have most of the maths in `RealAudio/GunLoudness.cs`, which is display-only today.

At `FireBullet` (you already prefix `SuperBetterAudioQueue.Enqueue` for volume):
- **Suppressed:** ΔdB = `Loudness × 0.6`. VAL −39 → −23 dB, 556-MINI −12 → −7 dB. This stays inside the measured 15–28 dB insertion-loss range from the old note.
- **Brakes and comps:** +`Loudness × 0.15` dB overall. They should be louder mainly to the sides, which needs 2.12, so keep it small for now.
- Convert ΔdB to both a **volume** change and a **rolloff (MaxDistance)** change consistent with the curve from Phase 0. For an inverse-distance curve, `rolloff × 10^(ΔdB/20)`.
- **Subsonic + suppressed** gets no crack (2.9), so it becomes quiet past ~100 m "for free". **Supersonic + suppressed** still cracks.

This replaces your hand-tuned "suppressed/unsuppressed range multiplier" and "subsonic volume" settings with values derived from the item data.

### 2.8 Deterministic update rate
**Fixes:** #8, #16. **Perf:** low to moderate (linear in N). **Feasibility:** medium.

- Rewrite the scheduling in `SpatialAudioSystem.method_25 / LateUpdate`, which assume a single `_scheduledProcessor`, to advance up to **N = 3** processors per frame.
- Always include any container within 25 m that played a sound this frame.
- Fix the smoothing: postfix `CombinedSpatialAudioOccluder.OnUpdateOcclusionEffects` to lerp by **time since that container's last update**, not `Time.deltaTime`.
- **Budget:** each pass is roughly 50–130 `RaycastCommand`s on worker threads. Three per frame is about 0.1–0.3 ms. Measure it; make N a config value.

### 2.9 Rebuild the supersonic crack
**Fixes:** #14. **Perf:** negligible (one short one-shot per bullet that passes near you). **Feasibility:** medium.

Prefix `SonicBulletSoundPlayer.FireBullet` (return false) and replace it with this geometry (from the old note):

| Step | Formula |
|---|---|
| Closest approach | `s* = dot(L − p0, u)`, `b = |L − (p0 + u s*)|` |
| Speed at distance s | from `ammo.InitialSpeed × weapon.SpeedFactor` with a simple linear drag, or `BallisticCoeficient` |
| Mach angle | `θ = asin(c / v)` |
| Emission point | `s_e = s* − b / tan θ`. Emit only if `v(s_e) > c` and `s_e > 0` |
| Arrival time | `t = time_bullet(s_e) + |L − P(s_e)| / c`. This always lands before the report at `|L − p0| / c` |
| Level | `−15·log10(b / 1 m)` relative to the clip (Δp ∝ b^−¾) |

- Play at `P(s_e)`, spatialised, on the Weaponry or Impacts group, using `PlayAtPointDelayed`/`AddToAudioSourceQueue` with `dspTime + t`.
- **Don't** skip it when the shooter is occluded. The crack travels with the bullet, near you.
- Keep BSG's four crack clips; scale pitch slightly by caliber.

### 2.10 Spatial flybys
**Fixes:** the flyby part of #14. **Perf:** negligible. **Feasibility:** easy.

Patch `FlyingBulletSoundPlayer.FrameCoroutine`:
- Use `BetterAudio.PlayAtPoint` at the closest-approach point instead of `PlayNonspatial` + pan, so you get HRTF and elevation.
- Allow 2 at once.
- Fix `Random.Range(0, len − 1)` to `Random.Range(0, len)`.
- Supersonic rounds close enough for a crack: crack plus a quiet whiz. Subsonic: whiz only.

### 2.11 Smooth the indoor/outdoor snap
**Fixes:** #13. **Perf:** negligible. **Feasibility:** easy (fixed times) to medium (portal-distance blend).

Prefix `BetterAudio.TransitToEnvironment`: use a symmetric 0.35 s. Better: drive the transition by the listener's distance to the nearest outdoor portal, from `SpatialAudioDataContainer.StaticPortalDataList`, so it fades as you walk through the door.

### 2.12 Headsets as gain, not radius (applies to your Headsets mod)
**Fixes:** the distance distortion from radius buffs (explanation §5). **Perf:** none. **Feasibility:** easy.

In SPT the vanilla headset `RolloffMultiplier` is dead code; only your `ProtagonistHearing` patch creates a radius effect. Scaling MaxDistance stretches the whole loudness and filter curve, so a headset changes how far away things *seem*.

- **Better:** keep the radius physical. Put the headset benefit into the listener chain you already own: compressor makeup gain on quiet sounds, plus clamping on loud ones.
- **Realism option:** a localisation cost for passive or talk-through muffs, by lowering `MetaSpatialAudioSource.HrtfIntensity` slightly (for example 1.0 → 0.85) while worn. This matches the hearing-protector studies cited in the old note.

---

## Phase 3: new features (bounded, opt-in)

### 3.1 Material-aware transmission and the "two colliders" fix
**Fixes:** #5, #6. **Perf:** low (with caching). **Feasibility:** medium.

Postfix `TransmissionCalculator.CompleteCalculation`.

- **Material loss.** Read the centre-ray hits from `_l2sHits` and `_s2lHits` (managed `NativeArray<RaycastHit>`). Map `hit.collider`, cached by `colliderInstanceID`, to EFT's `BallisticCollider.TypeOfMaterial`, then to a mass-law TL table. The `EFT.Ballistics.MaterialType` enum already separates Concrete, Stone, WoodThin and WoodThick, MetalThin and MetalThick, Glass, Tile, Plastic, Cardboard, Fabric and more, so the bullet material tags give you the sound table for free:

  | Material | Approx. TL |
  |---|---|
  | glass | ~25 dB |
  | wood | ~30 dB |
  | drywall | ~35 dB |
  | brick or concrete | 45–55 dB |
  | thin metal | ~30 dB |

  Scale by thickness and set `RemainingEnergy = 10^(−TL/10)` mapped onto the score curve.
- **Different colliders.** When the two rays hit different colliders, cast one short back-ray from each hit to find each obstacle's far face. Sum the two TLs instead of returning `NoPath`.
- **Cost:** up to 2 extra raycasts for the centre ray only, plus a dictionary lookup.

### 3.2 Bots hear through the same portals you do (SAIN bridge)
**Fixes:** AI and player hearing disagreeing. **Perf:** moderate if uncapped, low with the caps below. **Feasibility:** medium to hard.

- On each AI sound event, for the closest ≤6 bots in range:
  - find the bot's room with `AudioRoomStorage.FindActualCurrentRoom` and look up the `RoomPair`,
  - **schedule BSG's own `PropagationJob`.** It's a public struct, and the game already schedules that exact generic, so the Burst-compiled version is used,
  - read the best route as in 2.1.
- Feed SAIN:
  - an occlusion multiplier from cost, replacing SAIN's binary line-of-sight `CalcOcclusionMod`,
  - a perceived position at the bot-side portal ± error growing with path length.

  Bots then search **doorways, not walls**.
- **Caps:** cache per (roomPair, 0.5 s); skip bots in the same room (use SAIN as-is); skip bots beyond SAIN's audible range.
- You already patch SAIN's `HearingAnalysis.CheckIfSoundHeard`, so this extends existing work.
- If you ever need custom Burst code, Burst 1.8's `BurstRuntime.LoadAdditionalLibrary` exists in your `Unity.Burst.dll`. Build the job in a Unity 2022.3 project.

### 3.3 Outdoor slapback and echo for gunshots
**Fixes:** "every field sounds the same". **Perf:** moderate (extra voices). **Feasibility:** medium.

Meta XR's baked maps give room reverb and short early reflections, not long discrete echoes.

- Per listener cell (re-scan when you move more than 5 m), cast 8–12 horizontal rays up to 300 m and keep the 2–3 strongest large reflectors: hits on terrain, cliffs or building faces with a near-vertical normal.
- For each remote shot more than 30 m away, plus your own shots:
  - schedule a filtered copy of the tail clip at `(d1 + d2) / c` after the shot,
  - level: −(spreading + absorption + 4 dB reflection loss),
  - position: at the reflector point.
- Cap at 2 echoes per shot and 6 echo voices total. Only for delays above 50 ms; below that, leave it to Meta XR.

### 3.4 Through-wall + doorway dual rendering (gunshots and explosions only)
**Perf:** moderate to heavy (doubles voices). **Feasibility:** medium.

After 2.1, a shot in the next room comes only from the doorway. In reality you also hear a dull thump through the wall. For gunshots and explosions within 60 m, when both the transmission energy (3.1) and the propagation path are significant, play a second, heavily low-passed voice at the true position.

**Do not** do this for footsteps or voices. Hard cap at 4 dual voices.

### 3.5 Muzzle directivity and brake side-blast
**Perf:** negligible. **Feasibility:** easy to medium.

- Shooter facing vs. listener angle → 0 dB on-axis, −3 dB at 45°, −6 at 90°, −10 behind, with extra HF roll-off off-axis. These are placeholder values from the old note.
- Brakes add +5 to +10 dB in the 70–150° band.
- Apply as volume and LPF at `FireBullet`.

---

## 4. Not recommended, or not practical

| Idea | Why not |
|---|---|
| **Replace Meta XR with Steam Audio** (or run both) | The spatializer plugin is a Unity project setting baked into `globalgamemanagers`, and BSG's `MetaSpatialAudioSource` components are on every pooled source. You'd need a binary edit, shipping a native plugin and rebuilding the source prefabs. Running both doubles DSP. **Not practical** |
| **Steam Audio pathing via the C API as a side simulator** | Possible in principle (`iplSimulatorRunPathing` on its own thread), but you'd export each map's colliders into a Steam Audio scene at raid load. That's seconds of load and lots of memory, to duplicate the portal graph BSG already baked. **Not worth it** |
| **Rebake BSG `.audiobakedata` or Meta `.xramap` for vanilla maps** | Needs the map scenes in a Unity 2022.3 project (asset ripping) and BSG's editor-only bake tool (`Audio.SpatialSystem.Editor.SpatialAudioTool` needs UnityEditor APIs). ManimalInterchange shows `.xrageo` can be shipped for *custom* areas. Use it only for targeted fixes on a custom map piece. **Hard** |
| **Wave-based or GPU acoustics** | No supported public route; large-studio tech |
| **Full real-time reflection tracing for all sounds** | See section 5 |

---

## 5. Things that WILL wreck performance (don't build them this way)

| Pattern | Why it hurts | Do this instead |
|---|---|---|
| **`OnAudioFilterRead` on many sources** (per-voice C# DSP) | Runs on the audio thread every ~20 ms block, can be **interrupted by GC** ("Won't Fix" in Unity), and any allocation crackles everything | One allocation-free filter on the **listener** (your `OutputLimiter`) is fine. Per-source effects: use the existing `BetterSource` LPF/HPF and mixer groups. Keep `SelfSoundFilter` to the 2–4 body-sound sources it's on now |
| **Raycasting every source every frame at full quality** | ~130 rays × 30–60 sources = thousands of queries per frame, plus job sync points on the main thread | Keep round-robin; raise to N=3 (2.8); prioritise sources that just played |
| **Calling `Physics.Raycast` synchronously in hot Harmony patches** (FireBullet, per step) | Main-thread stalls in firefights | `RaycastCommand.ScheduleBatch` and read the results next frame, or cache per cell (3.3) |
| **Extra voices for every path, reflection and echo** | Past the real-voice limit Unity **steals voices**: sounds vanish at random, which is the opposite of consistency. Meta XR HRTF and reverb cost scale per voice | Hard caps (3.3, 3.4); only gunshots and explosions get extra paths; Phase 0 voice counter |
| **Raising `reverbReach`/enabling Meta acoustics on all groups and stereo sources** | Per-source ray-traced reverb cost | Small dB changes on existing sends (1.7) |
| **LINQ, closures, `GetComponent`, string building in per-frame patches** (`SuperBetterAudioQueue.Update`, `BetterSource` updates) | GC every frame leads to audio-thread GC interrupts too | Cache by instance ID; static buffers; no allocations |
| **Rebuilding route search in C# on the main thread** | Streets has 108k room pairs; route sets are large | Reuse BSG's Burst `PropagationJob` (3.2) or ship your own Burst lib |
| **Re-merging Meta acoustic maps or moving them every frame** | Meta notes re-merges cost CPU | Never touch map transforms |
| **Disabling `ObservedPlayerAudioSourceLimiter` / culling to "hear everything"** | Voice explosion on big maps | Leave culling on; fix audibility in the solver |

---

## 6. Your mods: keep, replace or remove

You said to treat these as band-aids. Here's where each fits once the glue is fixed.

| Your feature (earlier audio mod) | Verdict | Reason |
|---|---|---|
| Listener-bus `OutputLimiter` / "Hearing safe" / earpro clamp tiers | **Keep (core)** | Correct place (one owner of the listener bus); nothing in vanilla does it |
| Tinnitus / TTS / ringing, BringBackConcussion integration | **Keep** | Listener model; keep a single owner for ringing |
| Breathing / heartbeat / gas-mask / grenade "thump" synth | **Keep** | Body sounds; small, few voices |
| Blended full-auto loops (Gun Audio) | **Keep** | Sample-level improvement; independent of propagation |
| "Suppressed/Unsuppressed range multiplier", "Subsonic volume…" | **Replace** with 2.7 + 2.9 | Derive from item `Loudness` and ammo speed instead of hand multipliers |
| "Reverb boost dB", "Early reflections boost dB", "Minimum reverb reach" | **Move to server JSON** (1.7) | Same parameters, zero code, no patch fragility |
| Headset "Hearing boost" via `ProtagonistHearing` (radius) | **Replace** with gain-based headset (2.12) | Radius scaling distorts distance perception |
| "Let shots overlap", per-shot pitch/volume variation | **Keep** | Fine; check they don't fight 2.3 |
| Bot deafening via SAIN patch | **Keep**; extend with 3.2 | Same hook |
| `GunLoudness` stat display | **Promote** into 2.7 | The maths is already there |

**Fragility note.** Several of your patches bind to obfuscated names: `BetterAudio.method_8`, `float_1`, `float_2`, `Player._tinnitus`. These change between client builds, which is a likely reason things "probably broke". The fixes above bind to **named** members (`SpatialAudioOcclusionCombinedCalculator.GetScore`, `SoundPropagationJobScheduler._lowestCost`, `WeaponSoundPlayer.UpdateMixerGroup`, …), which BSG keeps stable across builds much more often. Wrap every patch in the per-class try/catch your `Host` already uses.

**Conflict watch:**
- **DoorOverhaul** changes portal open/close (`Door.CheckOcclusionPortal`), so it feeds 2.1 and 3.2. Test with it on.
- **ManimalInterchange** patches `PickClipsByDistance`; use the call-site approach in 2.6.
- **SPTBattleAmbience** plays fake gunfire on the Gunshots group. With 2.1 to 2.7 it will sound more convincing, and that makes it more misleading. Consider moving it to the `Distant` group or turning it off for "honest cues".

---

## 7. Recommended order and expected payoff

| Step | Items | Payoff | Total perf | Effort |
|---|---|---|---|---|
| A | Phase 0 instrumentation | You can see every problem | negligible | 1 day |
| B | Phase 1 server JSON (1.1–1.6) | Same sound on every PC; walls matter; no HRTF flicker | ≈0 | hours |
| C | 2.1 portal positioning, 2.2 path combining, 2.3 mixer hysteresis | **Biggest realism and consistency jump**: doors and stairs read correctly, no distance-tier jumps | negligible–low | 3–5 days |
| D | 2.4 cliff, 2.5 air absorption, 2.6 tier by perceived distance | Long range sounds right and stays consistent | low | 1–2 days |
| E | 2.7 Loudness, 2.9 crack, 2.10 flyby | Gunfire physics: cans differ, crack-thump, spatial snaps | negligible | 3–4 days |
| F | 2.8 update rate, 2.11 snapshot, 2.12 headset | Steady behaviour in big fights | low–moderate | 2–3 days |
| G | 3.1 materials, 3.2 bot portals | Walls by material; AI consistent with you | low–moderate | 1–2 weeks |
| H | 3.3 echoes, 3.4 dual paths, 3.5 directivity | Polish | moderate | 1 week |

Overrides worth recording as deliberate gameplay departures from physics: footstep rolloff multipliers, crack level versus report, vertical EQ darkening, headset gain. Keep a table of these in your config descriptions, as the Hunt and Squad lessons in the old note suggest.

---

### Sources (external, used for performance and feasibility claims)
- Meta XR Acoustic Ray Tracing: acoustic maps (runtime = interpolation of baked data; diffraction is the costly part). <https://developers.meta.com/horizon/documentation/unity/meta-xr-acoustic-ray-tracing-unity-map/>
- Unity `OnAudioFilterRead` (audio thread, ~20 ms blocks). <https://docs.unity.com/en-us/engine/current/script-reference/unityengine/monobehaviour/onaudiofilterread>
- Unity issue: OnAudioFilterRead stutter from GC interrupts (Won't Fix). <https://issuetracker-mig.prd.it.unity3d.com/issues/audio-stuttering-occurs-when-heavy-processing-is-performed-while-onaudiofilterread-is-in-use>
- Burst modding support (`LoadAdditionalLibrary`). <https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/modding-support.html>
- Steam Audio C API simulation (`iplSimulatorRunPathing`). <https://valvesoftware.github.io/steam-audio/doc/capi/simulation.html>
- Physical constants (ISO 9613-1 absorption, Mach cone, suppressor dB): see the earlier note, `Tarkov audio and realistic audio sim.md`.
