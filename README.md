# Real Sound Simulation

A ground-up rebuild of the SPT audio mods, based on the game-file study (`01_How_SPT_Tarkov_Audio_Works.md`, `02_Solutions_and_Feasibility.md`). Current version **0.6.58**: a gun sounds switch at the top of F12 (setting 0: SPT or Sound Engine Backport's EFT 1.0 recordings), diagnostics, an enclosure probe, an outdoor-exposure probe, and fixes 1, 2, 3, 3c, 4, 5, 6 (echoes for your own gunshots) 7 (rain cutting out), 8 (outdoor sound indoors from openings) and the acoustic probe v2 with its test switches 9 (ambience) and 9a (gunshots) (0.6.4–0.6.5: measured room build-up, guided rays, smoothing by confidence, direction by what you hear; 0.6.6: ray quality setting `9g`; 0.6.7: roof rain traced from the roof, direction strength `9h`; 0.6.8: exit points, rain from openings `9j`, distant gunfire through openings `9k`), all switchable live in F12. Since 0.5.8 the defaults are the tested setup (every fix on); turning every fix off gives the game's own audio. Plan: [docs/ROADMAP.md](docs/ROADMAP.md); checklist: [docs/TRACKER.md](docs/TRACKER.md). The soft concussion cut adapts [Bring Back Concussion](https://forge.sp-tarkov.com/mod/2550/bring-back-concussion) by harmony (MIT): see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Documentation

| Doc | Read it for |
|---|---|
| [docs/DESIGN.md](docs/DESIGN.md) | **Start here.** How indoor/outdoor sound works now: what is measured, what it drives, what you hear, every setting |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | How the plugin works: parts, startup, every game hook, how fixes switch |
| [docs/FIXES.md](docs/FIXES.md) | Each fix: the problem, the evidence, what it changes, settings, how to check it |
| [docs/DIAGNOSTICS.md](docs/DIAGNOSTICS.md) | Every log file and column, the F9 overlay, raid history |
| [docs/AMBIENCE.md](docs/AMBIENCE.md) | How the game's ambient audio works (layers, outdoor value, room settings, the rain dropout bug) and the options for step 4b |
| [docs/ROADMAP.md](docs/ROADMAP.md) | The agreed order of work, status, and what to test next |
| [docs/MATERIALS.md](docs/MATERIALS.md) | Measured and calculated acoustic values for the game's surface tags, against what the mod assumes |
| [docs/TRACKER.md](docs/TRACKER.md) | Checklist of every step and its substeps, in work order (tick boxes as they finish) |
| [docs/CHANGELOG.md](docs/CHANGELOG.md) | What changed in each version and whether it has run in game |
| [01_How_SPT_Tarkov_Audio_Works.md](01_How_SPT_Tarkov_Audio_Works.md) | The game's own audio system (research) |
| [02_Solutions_and_Feasibility.md](02_Solutions_and_Feasibility.md) | The full plan, including what isn't built yet |
| [TESTING.md](TESTING.md) | What to do in a test raid |
| [00_WORKING_NOTES.md](00_WORKING_NOTES.md) | Raw notes and raid findings |

These docs are updated in the same change as the code.

## Layout

| Path | What |
|---|---|
| `src/RealSoundSimulation.Dsp/` | Pure C# DSP (no Unity): headset model (passive leak + electronic talk-through, compressor, limiter, hiss). The offline bench and the game run the same code |
| `src/RealSoundSimulation/` | BepInEx plugin: `Diagnostics/` (self-check, overlay, CSV logs, timing; change no sound) and `Fixes/` (change sound; each switchable, on by default since 0.5.8) |
| `tools/HeadsetBench/` | Offline bench: renders WAVs per headset preset and measures footstep gain, clamp, recovery, hiss, frequency response, CPU and allocations |
| `01_…md`, `02_…md`, `00_WORKING_NOTES.md`, `data/` | The research: how the game's audio works, the solutions plan, working notes, extracted server audio settings |
| `docs/` | Architecture, fixes, diagnostics, changelog |
| `deploy.ps1` | Build and copy the plugin into `G:\SPT\BepInEx\plugins\FFloridaMann-RealSoundSimulation` (run with `powershell -ExecutionPolicy Bypass -File deploy.ps1` if scripts are blocked) |
| `TESTING.md` | What to listen for and what to do in a test raid |

## Build and run

```powershell
dotnet build RealSoundSimulation.sln -c Release
dotnet run -c Release --project tools/HeadsetBench -- bench_out   # writes bench_out/*.wav + report.md
.\deploy.ps1                                                       # installs the plugin into SPT
```

Paths to the game are in `Directory.Build.props` (`SptDir`).

Setting 0's SEB gun sounds are built once (and again after an SEB update) from the SEB download, into `G:\SPT\BepInEx\plugins\FFloridaMann-RealSoundSimulation\seb_sounds`. They're BSG audio, so they stay out of git. SEB's own server mod must not be installed.

```powershell
python -m venv .venv; .venv\Scripts\pip install UnityPy
.venv\Scripts\python -I tools\build_seb_sounds.py                   # default input: ~/Downloads/SPT-SEB-1.0.1.zip
```

## Version control

The project is a git repository (branch `main`, first commit 6 Nov 2026 at 0.5.7). Each build that goes into a raid gets a commit, so any earlier version can be rebuilt and compared. Logs, build output (`bin/`, `obj/`) and the bench WAVs (`bench_out/`) are not tracked (`.gitignore`). Line endings are kept as LF (`.gitattributes`). It is pushed to a **private** GitHub repo, https://github.com/FFloridaMann/RealSoundSimulation (`origin`). Push after each commit with `git push`.

## Rules this codebase follows

- **Nothing allocates on the audio thread.** The bench fails if `HeadsetProcessor.Process` allocates.
- **No Harmony patches on Burst job `Execute` methods.** They run native code, so the patch would never fire. Patch the managed schedulers and readers instead.
- **Patch named members, not `method_N`/`float_N`.** Every target is listed in `PatchSelfCheck` and verified at startup (see `BepInEx/RealSoundSimulationLogs/selfcheck.txt`).
- **BepInEx setting names (section and key) never contain `=` `'` `"` `\` `[` `]`, tabs or newlines.** BepInEx throws on them and the whole plugin silently fails to load. Descriptions can contain anything. After every deploy, confirm the plugin loaded: `BepInEx/RealSoundSimulationLogs/selfcheck.txt` must have a fresh timestamp.
- **Every feature is switchable in F12** and logs what it decided, so it can be A/B'd in raid and checked from logs.
- **Docs are updated in the same change as the code.**

## Credits

Footstep and handling settings (`10a`) and room routes (`10b`) come from [Sound Engine Backport](https://sp-mod.com/mod/3314/sound-engine-backport) by red_the_fox, MIT licence. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
