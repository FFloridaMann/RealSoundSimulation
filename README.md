<!-- DESCRIPTION START: replaced with the final text from DESCRIPTION-DRAFT.md -->
# Real Sound Simulation

A simulated audio overhaul for SPT. The game picks a recording and a volume; this mod works out how each sound would actually travel through the map to reach you, and plays what would arrive.

## How it works

Thousands of times a second it traces rays through the map's real geometry: walls, floors, ceilings, doorways, windows and open sky. Every surface uses measured acoustic values for its material, so concrete, metal, wood and glass each reflect, absorb and let through sound differently. From that it works out, for each sound that matters, which paths reach you, how long they take at the speed of sound, which way they arrive from, and how much each wall, corner and doorway takes off.

## What you'll notice in a raid

- **Your gun.** Echoes come back off the surfaces actually around you, delayed by their real distance. A concrete stairwell rings hard, a wooden room sounds warmer, an open field barely answers. Walk through a doorway and the shot blends from inside to outside as the space opens up.
- **Fights nearby.** A firefight in the next room reaches you through the doorway it would really come through. The longer and more twisted its route, the duller it gets. Behind a wall, how much gets through depends on what the wall is made of and how thick it is, and a solid wall can leave only a low thump, or nothing.
- **Grenades.** The blast comes from the opening it travels through. The pressure wave arrives first and pushes harder in small closed rooms than in big halls. Fragments whizz past along their real flight path, and concussion depends on the blast pressure that reaches you, not just distance.
- **Rain and weather.** Rain is traced down from the real roof. You hear how many floors are above you and what the roof is made of, and it fades as you go deeper inside or down a stairwell. Outdoor sound reaches you through the windows and doors it can actually get through.
- **Everything else.** Closed doors muffle by material, glass breaking in another room comes from the right place, and footsteps and handling sound like EFT 1.0.

Work in progress: built and tested by one player, so expect rough edges.
<!-- DESCRIPTION END -->

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
| 1. General | The concussion switches, gun recordings, sound quality, footsteps and handling, bug report logs and their mark key |
| 2. Your gun | Echoes, room reverb, indoor/outdoor blend |
| 3. Other guns | Sound from the doorway, muffling by route, echoes |
| 4. Explosions | Thump, echoes, shock wave, concussion, fragments |
| 5. Rain and outdoors | Outside sound indoors, roof rain |
| 6. Doors and walls | Sound through walls, closed doors, breaking glass |
| 7. Performance | How many echo voices other guns may use |
| 8. Diagnostics | The overlay and the probes |

The everyday settings show by default. Tick **Advanced settings** at the top of F12 for the fine-tuning ones. Almost everything applies straight away, so you can switch a feature off and on in a raid to hear what it does.

## Reporting a bug

1. In F12 → Real Sound Simulation → **1. General**, turn on **Bug report logs**.
2. Play one raid. Wherever something sounds wrong, press **F10**: the spot is saved with what the mod measured there. If F10 clashes with another mod, change **Bug report mark key** right below the switch.
3. After the raid, open your SPT folder (the one with `EscapeFromTarkov.exe`) and then the **RealSoundSimulation Logs** folder.
4. Send the newest **Bug report** zip, and say roughly when and where in the raid it happened.

Turn Bug report logs off again afterwards; it writes several MB per raid.

## Performance

In a typical raid the mod uses about 2% of one CPU core. Most of that is the sound tracer, which runs alongside the game on spare cores. **Sound quality** in General trades steadiness deep inside buildings against CPU.

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

- Footstep and handling settings and the routes between rooms come from [Sound Engine Backport](https://sp-mod.com/mod/3314/sound-engine-backport) by red_the_fox (MIT).
- The soft concussion cut adapts [Bring Back Concussion](https://forge.sp-tarkov.com/mod/2550/bring-back-concussion) by harmony (MIT).

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
