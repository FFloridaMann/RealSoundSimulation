# How Real Sound Simulation works

This document explains the code: what runs where, what it hooks in the game, and how the pieces talk to each other. For *why* each fix exists, read [FIXES.md](FIXES.md). For the log files, read [DIAGNOSTICS.md](DIAGNOSTICS.md). For the game's own audio system, read [../01_How_SPT_Tarkov_Audio_Works.md](../01_How_SPT_Tarkov_Audio_Works.md).

Current version: **0.2.2** (see [CHANGELOG.md](CHANGELOG.md)).

## The three projects

| Project | Runs where | What it is |
|---|---|---|
| `src/RealSoundSimulation.Dsp` | Anywhere (.NET Standard 2.1, no Unity) | The headset model: filters, compressor, limiter, hiss. Pure maths, so the bench and the game run identical code. **Not hooked into the game yet.** |
| `tools/HeadsetBench` | Your PC (.NET 8 console app) | Renders test WAVs through every headset preset and measures them. Never touches the game. |
| `src/RealSoundSimulation` | Inside Tarkov (BepInEx plugin) | Diagnostics (watch and log) and fixes (change sound). Installed to `G:\SPT\BepInEx\plugins\FFloridaMann-RealSoundSimulation`. |

The server is not modified. Everything here is client side, so changing it never needs a server restart, only a game restart.

## Inside the plugin

```
Plugin.cs                    entry point: settings (F12), installs every patch, adds the four components
│
├── Acoustics/               measure the space around you
│   ├── EnclosureProbe.cs    component: 32 rays every 0.1 s -> Enclosure/Horizontal/Overhead/MeanHit, and the room
│   │                        measure (wall x roof closure, size factor); aim cone on demand
│   ├── Surfaces.cs          what a ray hit: material tag (windows registered as glass), closure for the room measure
│   ├── CountedRay.cs        Physics.Raycast / room lookup wrappers that count themselves (COST)
│   ├── Loudness.cs          diagnostics: loudness of each gun's indoor vs outdoor recordings (summary)
│   ├── OutdoorExposure.cs   component (step 4b): indoors, which directions reach the outside and through what; floors
│   ├── AcousticProbe.cs     probe v2 (0.6.0): bouncing rays to open sky, batched; ambience + gunshot outputs; 0.6.4 guided rays, measured build-up
│   │                        above and roof material. Measure only in 0.4.0
│   └── OwnEchoes.cs         fix 6: finds reflecting surfaces when you fire and schedules echoes;
│                            EchoVoices component: the pool of 24 echo AudioSources
│
├── Diagnostics/             watch and log; never change sound
│   ├── PatchSelfCheck.cs    at startup: does every game member we rely on still exist? -> selfcheck.txt
│   ├── TimingPatches.cs     stopwatch around the game's audio Update/LateUpdate
│   ├── ShotProbe.cs         one row per remote gunshot -> shots.csv
│   ├── EnvProbe.cs          indoor/outdoor switches + ambience level -> transitions.csv, ambience.csv
│   ├── DiagRunner.cs        component: raid start/end, occlusion samples, source scans, F9 overlay, summary
│   ├── Perf.cs              cost per part (calls, ms, raycasts, room lookups) for the summary's COST section
│   ├── RayView.cs           0.6.0: draws probe v2's rays in the world (F11)
│   ├── MetaAcousticsCheck.cs counts Meta XR acoustic map/geometry components for the summary
│   ├── SharedPatches.cs     0.6.20: at raid start, which other mods patch our (and planned) targets, loaded plugins, the
│   │                        mixer's snapshots and groups as they are now -> patches.txt
│   ├── AudioThreadProbe.cs  0.6.20: pass-through filter on the AudioListener for the raid; times the gap between DSP
│   │                        blocks (late blocks = the audio thread can't keep up) -> timing.csv, overlay, summary
│   ├── AudioCounters.cs     0.6.20: lists the Unity profiler counters the game offers, records the audio ones each
│   │                        frame (DSP load, voices, if present) -> audio-counters.txt, audiocounters.csv, summary
│   ├── RouteLog.cs          0.6.22: measure only (6.1): the game's best route and opening for each bot shot -> routes.csv
│   ├── BotShots.cs          0.6.21: every sound a bot's gun queued or lost; which were cut by the next one -> botshots.csv
│   ├── BatchWait.cs         0.6.20: how long the main thread waits for probe v2's ray batch and the muzzle measure
│   │                        -> timing.csv, slowframes.csv, overlay, summary
│   └── DiagLog.cs           one folder per raid, buffered CSV writing, errors.txt
│
└── Fixes/                   change sound; each switchable live (defaults = the tested setup)
    ├── GunSoundSource.cs    setting 0 (0.6.3): swap a gun's banks to SEB's (seb_sounds/, background-loaded) or back
    ├── GunMixerFix.cs       fix 1: stop muffled/clear flip-flop on gunshots
    ├── FirstBurstOcclusion.cs 1d (0.6.21): a bot's first burst starts muffled if a wall is in the way
    ├── OwnShotTurn.cs       3o (0.6.30): your shot's indoor and outdoor recordings shift as you turn
    ├── ThroughWall.cs       6.2 (0.6.37): low thump through walls for blocked bot shots and explosions
    ├── FoleyPresets.cs      10a (0.6.35): footstep and handling group settings from SEB
    ├── ShotOverlap.cs       1e (0.6.23): shots ring under the next one; a gun gives up voices if the pool is empty
    ├── (Acoustics/Materials.cs) 0.6.25: the one table of what each surface does to sound
    ├── PortalPositioning.cs 6.1 (0.6.24): a bot's gun voices placed at the opening, from Acoustics/PortalRoutes.cs (the game's route result)
    ├── ObstructionScoreFix.cs fix 2: how the three wall/corner/echo checks are combined
    ├── DoorwayBlend.cs      fix 3: component; smooth indoor/outdoor blend at doorways
    ├── GunEnvironmentBlend.cs fix 3c: your own gunshots mix indoor/outdoor recordings (Doorway or Enclosure mode);
    │                        fix 5: own-gun room reverb boost
    ├── OffMapFix.cs         fix 4: sounds outside the baked map are treated as outdoors
    ├── DistantThroughOpenings.cs  9k (0.6.8): off-map distant sounds come through the openings when you're inside; 0.6.12: also a BetterAudio.PlayAtPoint postfix (both overloads) to take them as they start
    ├── RainDropoutFix.cs    fix 7: the game's indoor rain fading to silence between rooms; 0.6.9: same-clip crossfades
    │                        become a volume ramp
    ├── RoomTone.cs          8g (0.6.9): "wind" room tones follow the outside's level (RoomToneController.ChangeRoomTone
    │                        postfix; BetterSource.UpdateSourceVolume prefix scales the occlusion factor, like 9k)
    ├── OutdoorTail.cs       3m (0.6.11): the gun's outdoor tail from the opening you fire out of, on fix 6's voices
    │                        (FireBullet / StopFiringLoop prefix + finalizer, PickClipsByDistance and Enqueue postfixes;
    │                        AcousticProbe.ExitAlong for the opening)
    ├── AmbienceMix.cs       fix 8 (component, LateUpdate): outdoor level from exposure, indoor fade, roof rain, direction cue
│   ├── BedDirection.cs      fix 9 (0.6.0): makes the game's own outdoor beds directional; 0.6.7 also the indoor roof rain player
    └── OpeningSounds.cs     fix 8 phase 4: 3D copies of the outdoor beds at up to 3 openings
```

### Startup order (`Plugin.Awake`)

1. Bind every setting (F12 sections "0. Gun sounds", "1. Diagnostics" and "2. Fixes (all off means vanilla audio)").
2. Run `PatchSelfCheck`, which writes `BepInEx/RealSoundSimulationLogs/selfcheck.txt`. **A fresh timestamp on this file is how you know the plugin loaded.**
3. Install the Harmony patches: timing, shot probe, env probe, then fixes 1, 2, 3, 3c, 4 and 6. Each install is wrapped in `Safe()`, so one failing patch doesn't stop the others; failures are written to the BepInEx log and `RealSoundSimulationLogs/errors.txt`.
4. Add four components to the plugin's GameObject: `DiagRunner` (diagnostics), `DoorwayBlend` (fix 3), `EnclosureProbe` (acoustics) and `EchoVoices` (fix 6). All four run every frame.

The whole of `Awake` is wrapped too: if loading fails, `errors.txt` says "PLUGIN FAILED TO LOAD" with the reason.

### What runs every frame

| Component | Does | Cost |
|---|---|---|
| `DiagRunner.Update` | Detects raid start/end. Mark key (F10): one row in marks.csv. Every 0.5 s: reads every sound container's occlusion state. Every 15 s: scans playing sources. Every 5 s: writes timing. 10×/s: ambience sample. 0.6.19: all of that is skipped for 2 s after the bot count changes (`Diagnostics back off after a spawn s`); the bot count itself is still read every frame. | Dominated by the 15 s source scan (`FindObjectsOfType`), which is why it is slow (off by default since 0.6.15). |
| `DoorwayBlend.LateUpdate` | In raid: works out "outdoorness" from the nearest doorway (always, because fixes 3c and 4 use it). If fix 3 is on: applies it to the mixer snapshots and ambience. | A loop over the outdoor doorways of the current area, a few hundred distance checks at most. |
| `EnclosureProbe.Update` | In raid, every 0.1 s: 32 raycasts around your head (if "Enclosure probe" is on). | 320 short raycasts per second. |
| `OutdoorExposure.Update` | In raid, every 0.25 s: 5 rays up (always); indoors also 24 level rays, up to 8 room lookups each, and one "in between" ray per direction that reaches outside. | Logged in the summary (avg/max ms per update). |
| `EchoVoices.Update` | Stops all echoes when fix 6 is switched off; frees looping echoes left open past 15 s. | A loop over 24 voices. The echo work itself happens when you fire, not per frame. |
| `AudioThreadProbe.OnAudioFilterRead` (0.6.20) | On the audio thread, once per DSP block: one Stopwatch read and a few counters. | Nothing on the main thread; read every 5 s. |
| `AudioCounters.Sample` (0.6.20, from `DiagRunner.Update`) | Every frame in raid: one read of each recorded Unity audio counter (up to 24). | A few reads per frame; no allocation. |
| `BatchWait.FrameEnd` (0.6.20, from `AcousticProbe.LateUpdate`) | Every frame probe v2 runs: adds up the frame's batch wait, writes a slowframes.csv row for a slow previous frame. | Two Stopwatch reads per batch; no allocation. |

### Game code we hook

Every hooked member is listed in `PatchSelfCheck.cs` and checked at startup (89 targets in 0.4.0). After any SPT or Tarkov update, read `selfcheck.txt` first: a MISSING line names the fix that will break.

| Hooked member (game class.method) | Kind | By | Purpose |
|---|---|---|---|
| `SpatialAudioSystem.Update` / `LateUpdate`, `BetterAudio.Update` | prefix + postfix | TimingPatches | time the game's audio work |
| `WeaponSoundPlayer.FireBullet` | prefix, `Priority.First` (never skips) | Setting 0 | when not mid-burst: swap the gun's six SoundBank fields to SEB's by name, or back; recompute `_firingLoopLength`. Runs before 3c and 6 so they see the banks in use |
| `WeaponSoundPlayer.FireBullet` | postfix | ShotProbe | log each remote shot |
| `WeaponSoundPlayer.UpdateMixerGroup` | postfix | ShotProbe | count mixer flips |
| `WeaponSoundPlayer.UpdateMixerGroup` | prefix (replaces when on) | Fix 1 | hysteresis on the gun mixer choice |
| `SpatialAudioOcclusionCombinedCalculator.GetScore` | postfix | Fix 2 | recombine the obstruction scores |
| `BetterAudio.TransitToEnvironment(EnvironmentType)` | postfix | EnvProbe | log the game's snapshot switches (still logged while fix 3 skips them) |
| `BetterAudio.TransitToEnvironment` (both overloads) | prefix (skips while fix 3 active) | Fix 3 | stop the game's 0.1 s / 0.5 s snapshot switch |
| `EnvironmentSoundBlendSystem.OnPlayerRoomChanged` | postfix | EnvProbe | log the baked-room indoor/outdoor switches |
| `AmbientSoundBlender.BlendToIndoor` / `BlendToOutdoor` / `BlendToTargetValue` / `CalculateAndApplyMixerParameters` | prefix (skips while fix 3 active) | Fix 3 | stop the game's ambience fades |
| `WeaponSoundPlayer.FireBullet` | prefix + postfix + finalizer | Fix 3c, fix 5 | prefix: mark "my own shot", work out the indoor weight (aim cone in Enclosure mode); postfix: apply the reverb boost; finalizer: clear the mark |
| `WeaponSoundPlayer.StopFiringLoop` | prefix + finalizer | Fix 3c | same marking for the burst tail |
| `SoundBank.PickClipsByDistance` | postfix | Fix 3c | for marked own shots near a doorway: indoor clip in slot 1, outdoor in slot 2, balanced by outdoorness |
| `ParallelSoundPropagationCalculator.OnUpdate` | postfix | Fix 4 | replace "fully blocked" for off-map sounds |
| `PrecipitationAmbientBlender.CurrentTargetVolume` (getter) | postfix | Fix 8 | roof rain × 8b under floors (indoor player, with fix 7) |
| `PrecipitationAmbientBlender.SetClip` | prefix (replaces when on; always logs) | Fix 7 | always pick the rain clip, even if it's the same one |
| `AudioRoomStorage.FindActualCurrentRoom` | called (not patched) | OutdoorExposure | which baked room a point along a ray is in |
| `WeaponSoundPlayer.FireBullet`, `StopFiringLoop` | prefix + finalizer | Fix 6 | mark "my own gun" and remember the aim direction |
| `SoundBank.PickClipsByDistance` | postfix | Fix 6 | for marked own picks: remember the outdoor recording of the same tier as the echo source |
| `SuperBetterAudioQueue.Enqueue` | postfix | Fix 6 | for each sample your gun schedules: compute the echoes once per shot and schedule them at the sample's start + delay; end a loop's echoes when the next sample starts |

Read directly (public): `WindowBreaker.ObstructiveCollider` (Surfaces registers windows as glass), `BallisticCollider.TypeOfMaterial`.

Setting 0 also loads bundles itself: `AssetBundle.LoadFromFileAsync` for `seb_sounds/*.bundle` (dependencies first, from `index.txt`), then `LoadAllAssetsAsync<SoundBank>()`. Bundles stay loaded for the session. Public fields written: `WeaponSoundPlayer.Body`, `BodySilenced`, `Tail`, `TailSilenced`, `Doublet`, `DoubletSilenced`.

Fields read through `AccessTools.FieldRefAccess` (private in the game): `WeaponSoundPlayer._isFiring`, `WeaponSoundPlayer._firingLoopLength` (setting 0), `BaseSoundPlayer.playersBridge`, `WeaponSoundPlayer._queue`, `WeaponSoundPlayer._currentMixerGroup`, `AmbientSoundBlender._currentOutdoorFilterValue`, `AmbientSoundBlender._masterMixer` (fix 3 waits until it is set), and the `SpatialAudioSystem` container/processor tables.

### How a fix is switched

Each fix reads its F12 setting **every time it runs**, so switching takes effect immediately in raid:

- Fixes 1, 2, 3c and 4 are patches that return early (do nothing) when switched off.
- Fix 3 is a component. Switched on, it sets `DoorwayBlend.Active`, which makes its prefixes skip the game's own switching, and drives the mixer itself. Switched off, it clears `Active` and hands control back by calling the game's `TransitToEnvironment(current environment)` and `EnvironmentSoundBlendSystem.method_0()`.

### Shared values between parts

| Value | Written by | Read by |
|---|---|---|
| `DoorwayBlend.TargetOutdoorness` (0 deep inside … 0.5 doorway … 1 outside, unsmoothed) | DoorwayBlend, every frame in raid | Fixes 3c and 4, EnvProbe (ambience.csv) |
| `DoorwayBlend.DebugPortalId/DebugS/DebugLat` | DoorwayBlend | EnvProbe (ambience.csv) |
| `EnclosureProbe.Enclosure/Horizontal/Overhead/MeanHit`, per-ray `Dirs/HitDist/HitNormal/HitPoint/HitCollider`, aim-cone `AimDirs/AimHit*` | EnclosureProbe, every 0.1 s (aim cone when you fire, reused within a frame) | Fix 3c (Enclosure mode), fix 6 (echoes), probe.csv, ownshots.csv |
| `DoorwayBlend.Outdoorness` (smoothed value actually applied; NaN when fix 3 is off) | DoorwayBlend | EnvProbe |
| `GunMixerFix.HeldBack`, `ObstructionScoreFix.Changed`, `GunEnvironmentBlend.Blended`, `OffMapFix.Applied`, `OwnEchoes` counters | the fixes | raid summary |

## Rules the code follows

- **Nothing allocates on the audio thread** (matters once the headset DSP is hooked in; the bench fails if it does).
- **No patches on Burst job `Execute` methods**: they are native code, so a patch would never run. Patch the managed code that schedules them or reads their results.
- **Patch named members, not obfuscated `method_N` / `float_N` ones**, wherever possible. Exception: fix 3 calls `EnvironmentSoundBlendSystem.method_0`; it is in the self-check.
- **Setting names never contain `=` `'` `"` `\` `[` `]`**, tabs or newlines: BepInEx throws and the plugin fails to load (this happened in 0.2.0).
- **Every fix starts off and is switchable live**, and the logs record which fixes were on.
- **Docs are updated in the same change as the code.**

## Building and installing

```powershell
dotnet build RealSoundSimulation.sln -c Release
Copy-Item src\RealSoundSimulation\bin\Release\netstandard2.1\RealSoundSimulation.dll, src\RealSoundSimulation\bin\Release\netstandard2.1\RealSoundSimulation.Dsp.dll G:\SPT\BepInEx\plugins\FFloridaMann-RealSoundSimulation\
```

`deploy.ps1` does both; run it with `powershell -ExecutionPolicy Bypass -File deploy.ps1` if scripts are blocked. A running game keeps the old copy in memory: restart Tarkov (not the server) and check that `selfcheck.txt` has a new timestamp.
