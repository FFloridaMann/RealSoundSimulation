# Real Sound Simulation

A simulated audio overhaul for SPT. Instead of playing a recording at a volume, it works out how each sound would travel through the map to reach you: through which doorways, off which walls, through what material, and how long it takes.

**The full description, with what you'll hear in a raid, the concussion system, performance and credits, is in [FORGE-PAGE.md](FORGE-PAGE.md).** This README covers installing, settings, bug reports and building from source.

## Requirements

- SPT 4.1.
- Optional: [Bring Back Concussion](https://forge.sp-tarkov.com/mod/2550/bring-back-concussion) for the concussion features.
- Optional: [Sound Engine Backport](https://sp-mod.com/mod/3314/sound-engine-backport)'s files for EFT 1.0 gun recordings and EFT 1.0 routes between rooms. Its own plugin and server mod should not be installed alongside this one.

## Install

Copy the `FFloridaMann-RealSoundSimulation` folder (`RealSoundSimulation.dll` and `RealSoundSimulation.Dsp.dll`) into `BepInEx/plugins/`. To remove it, delete that folder.

## Settings

Press F12 and open **Real Sound Simulation**. The settings are grouped by what you hear:

| Section | What's in it |
|---|---|
| 01 GENERAL | The big switches: concussion, shot variation, gun recordings, sound quality, footsteps, bug report |
| 02 YOUR GUN | Mix (indoor/outdoor), echoes, room tail, the game's reverb |
| 03 OTHER GUNS | Direction, muffling, echoes, distant gunfire |
| 04 EXPLOSIONS: SOUND | Direction, the thump (volumes and all its depths together), echoes, recording |
| 05 EXPLOSIONS: SHOCK AND CONCUSSION | Shock wave, concussion strength and ears |
| 06 FRAGMENTS AND GLASS | Fragment pass-bys and hits, breaking glass |
| 07 RAIN AND OUTDOORS | Outside sound indoors, roof rain |
| 08 DOORS AND WALLS | Gunshots through walls, walls that stop sound, closed doors, doorways |
| 09 SOUND TRACER | Fine-tuning for the tracer (Advanced only) |
| 10 PERFORMANCE | How many echo voices other guns may use |
| 11 DIAGNOSTICS | The overlay and the probes |

Settings that belong together share a name, like **Thump - volume dB** and **Thump - depth through walls Hz**, and stay next to each other. The everyday settings show by default. Tick **Advanced settings** at the top of F12 for the fine-tuning ones. Almost everything applies straight away, so you can switch a feature off and on in a raid to hear what it does.

## Reporting a bug

1. In F12 → Real Sound Simulation → **01 GENERAL**, turn on **Bug report - logs**.
2. Play one raid. Wherever something sounds wrong, press **F10**: the spot is saved with what the mod measured there. If F10 clashes with another mod, change **Bug report - mark key** right below the switch.
3. After the raid, open your SPT folder (the one with `EscapeFromTarkov.exe`) and then the **RealSoundSimulation Logs** folder.
4. Send the newest **Bug report** zip, and say roughly when and where in the raid it happened.

Expect slightly lower FPS during that raid. When it ends, stay on the raid results screen for a few seconds before closing the game, so the zip can finish. Then turn Bug report logs off again; it writes several MB per raid.

## Performance

See [FORGE-PAGE.md](FORGE-PAGE.md#performance) for measured numbers and settings for slower PCs.

## Roadmap and changes

- [ROADMAP.md](ROADMAP.md): what's done and what's next.
- [CHANGELOG.md](CHANGELOG.md): what changed in each version.

## Building from source

```powershell
dotnet build RealSoundSimulation.sln -c Release
.\deploy.ps1                          # builds and installs into G:\SPT
.\deploy.ps1 -SptDir "D:\Games\SPT"   # or anywhere else
```

The build references the game's DLLs from your SPT folder, set in `Directory.Build.props` (`SptDir`). `tools/HeadsetBench` renders and measures the headset model offline: `dotnet run -c Release --project tools/HeadsetBench -- bench_out`.

EFT 1.0 gun recordings are built once from the Sound Engine Backport download. They're the game's audio, so they never go into this repository:

```powershell
python -m venv .venv; .venv\Scripts\pip install UnityPy
.venv\Scripts\python -I tools\build_seb_sounds.py
```

## For developers

How it all works, in detail, is in [docs/dev](docs/dev): [DESIGN.md](docs/dev/DESIGN.md) (start here), [ARCHITECTURE.md](docs/dev/ARCHITECTURE.md), [FIXES.md](docs/dev/FIXES.md), [DIAGNOSTICS.md](docs/dev/DIAGNOSTICS.md), [MATERIALS.md](docs/dev/MATERIALS.md), [AMBIENCE.md](docs/dev/AMBIENCE.md) and the [research](docs/dev/research) into the game's own audio. The step-by-step checklist is [docs/TRACKER.md](docs/TRACKER.md), and [docs/TESTING.md](docs/TESTING.md) says what to do in a test raid.

Rules the code follows:

- Nothing allocates on the audio thread.
- No Harmony patches on Burst job `Execute` methods; they run native code, so the patch would never fire.
- Patch named members only. Every patch target is checked at startup and listed in `RealSoundSimulation Logs/selfcheck.txt`.
- Setting names never contain `=` `'` `"` `\` `[` `]`, tabs or newlines. BepInEx throws on them and the plugin silently fails to load.
- Every feature can be switched in F12 and logs what it decided, so it can be compared in a raid and checked from the logs.
- Docs change in the same commit as the code.

## Credits

- The routes between rooms, the footstep and handling settings, and the optional EFT 1.0 gun recordings come from [Sound Engine Backport](https://sp-mod.com/mod/3314/sound-engine-backport) by red_the_fox (MIT), used with his permission.
- The soft concussion cut adapts [Bring Back Concussion](https://forge.sp-tarkov.com/mod/2550/bring-back-concussion) V1.2.1 by harmony (MIT).

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
