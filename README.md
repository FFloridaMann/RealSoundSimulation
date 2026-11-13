# Real Sound Simulation

A simulated audio overhaul for SPT. Instead of playing a recording at a volume, it works out how each sound would travel through the map to reach you: through which doorways, off which walls, through what material, and how long it takes.

**The full description, with what you'll hear in a raid, the concussion system, performance and credits, is in [FORGE-PAGE.md](FORGE-PAGE.md).** This README covers installing, settings, bug reports and building from source.

## Requirements

- SPT 4.1.
- Optional: [Bring Back Concussion](https://forge.sp-tarkov.com/mod/2550/bring-back-concussion) for the concussion part.
- Don't run it alongside [Sound Engine Backport](https://sp-mod.com/mod/3314/sound-engine-backport) or True Acoustics. Sound Engine Backport's routes between rooms are already in the download.

## Install

1. Download the zip from the Forge or the GitHub releases.
2. Extract it into your SPT folder (the one with `EscapeFromTarkov.exe`). It adds `BepInEx/plugins/FFloridaMann-RealSoundSimulation`.
3. Start the game and open F12 → **Real Sound Simulation**.

To remove it, delete that folder and the `com.ffloridamann.realsoundsimulation` files in `BepInEx/config`. Settings from before 0.9.0 (`ffloridamann.realsoundsimulation.cfg`) are carried over once.

## Settings

The public build shows 31 settings, everything else keeps its tested value:

| Section | What's in it |
|---|---|
| 01 FEATURES (on or off) | One switch per feature: your gun, other guns, explosions, hearing, outdoors, walls, footsteps and handling |
| 02 GENERAL | Sound quality, bug report logs and mark key |
| 03 YOUR GUN | Echo and room tail volume |
| 04 OTHER GUNS | Echo volume |
| 05 EXPLOSIONS | Thump and echo volume |
| 06 HEARING | Concussion strength, headset protection, adrenaline, ear ringing |
| 07 OUTDOORS AND WALLS | Roof rain volume |

Almost everything applies straight away, so you can switch a feature off and on in a raid to hear what it does. The developer build (`.\deploy.ps1 -Dev`) shows every setting, about 200, in sections 11 to 21.

## Reporting a bug

1. In F12 → Real Sound Simulation → **02 GENERAL**, turn on **Bug report - logs**.
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
.\deploy.ps1                          # builds the public version and installs it into G:\SPT
.\deploy.ps1 -Dev                     # the developer version, every setting in F12
.\deploy.ps1 -SptDir "D:\Games\SPT"   # or anywhere else
.\tools\package.ps1                   # the release zip in dist\, with the route files
```

The build references the game's DLLs from your SPT folder, set in `Directory.Build.props` (`SptDir`). `tools/HeadsetBench` renders and measures the headset model offline: `dotnet run -c Release --project tools/HeadsetBench -- bench_out`.

Developer build only: EFT 1.0 gun recordings are built once from the Sound Engine Backport download. They're the game's audio, so they never go into this repository:

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

- The routes between rooms (shipped in the release download) and the footstep and handling settings come from [Sound Engine Backport](https://sp-mod.com/mod/3314/sound-engine-backport) by red_the_fox (MIT), used with his permission.
- The soft concussion cut adapts [Bring Back Concussion](https://forge.sp-tarkov.com/mod/2550/bring-back-concussion) V1.2.1 by harmony (MIT), used with their permission.

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Real Sound Simulation itself is MIT licensed: see [LICENSE](LICENSE).
