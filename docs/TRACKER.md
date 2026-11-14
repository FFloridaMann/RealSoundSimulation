# Tracker

The checklist, by milestone, in order. Tick a box when it's done (on GitHub you can tick it straight from the phone).

- The plan for players: [ROADMAP.md](../ROADMAP.md). Why each item is there and how it would be built: [ROADMAP-DETAILED.md](dev/ROADMAP-DETAILED.md), under the same headings.
- What's already built, and the old step-by-step plan: [HISTORY.md](dev/HISTORY.md) and [CHANGELOG-DETAILED.md](dev/CHANGELOG-DETAILED.md).
- Codes in brackets, like `(W)`, are the old step names; they still match the logs and setting descriptions.

**Now:** launching the 0.9 beta.

---

## 0.9 beta launch
- [x] Forge format: plugin ID and name, settings carried over, MIT licence, install steps, credits (13 Nov 2026)
- [x] Repo public; GitHub pre-release v0.9.0 with the zip, direct download link checked (13 Nov 2026)
- [ ] Test raid with the release zip: settings carried over, F12 shows 31 settings, `errors.txt` empty
- [ ] One raid on another map (Customs or Factory) on the public build
- [ ] Fill the Forge page's links: True Acoustics, The Bot Loading Freeze Fix, coffee
- [ ] Known issues on the Forge page (they're in the README)
- [ ] VirusTotal scan of the zip
- [ ] Forge page live: ID `com.ffloridamann.realsoundsimulation`, 0.9.0, SPT `~4.1.0`, MIT, source link, download link, VirusTotal link; add-ons off until 1.0

---

## 1.0

### Beta feedback
- [ ] Sort each bug report: a known issue, a fix for 1.0, or a clash with another mod (named on the Forge page)

### Final tests, one area at a time
Fix only what sounds wrong; no new features. Route: [TEST-ROUTE.md](TEST-ROUTE.md).
- [ ] Run the route once on the current build: that raid becomes the reference for `tools/compare_raids.py`
- [ ] **Your gun:** a small room, a big hall, a stairwell, a doorway and the open; firing then turning; one full-auto burst indoors
- [ ] **Outside sound and rain:** a rainy raid: deep inside, under a tin roof, a stairwell, by a window, walking in and out
- [ ] **Other guns:** a fight in the next room, one on another floor, one behind a building outdoors, a distant firefight; summary BOT GUNSHOTS and BOT ECHOES
- [ ] **Walls and doors:** shots behind a wooden wall, behind concrete, behind a closed door; break a window from another room
- [ ] **Explosions:** grenades in your room, the next room, through an open door, behind a solid wall, outdoors and beside a building; with and without a headset
  - [ ] Name what cuts gun sounds about 50 dB at a close blast (`blastmix.csv`, 0.7.22), and fix it if it's ours
- [ ] **Hearing:** a few shots, then a lot, without a headset (rifle, pistol, subsonic, suppressed); a close grenade with and without one
  - [ ] Mixer owner (0.7.22): a close grenade with no headset, a minute of firing with gunfire ear ringing on, then the same with a headset

### Polish: release build
- [x] Bug report logs off by default
- [x] Timing patches and audio-thread counters only with bug report logs on (0.9.0)
- [ ] `9w`: record the room state before checking for logging (`EnvProbe.RoomPostfix`)
- [ ] One master diagnostics switch; the probes and raid resets keep running without it
- [ ] Count `LastPlayingInContainers` on its own for `OwnEchoes`; no container rows with logging off
- [ ] Load-in spikes: `ClosedDoors` during the deploy countdown, `AmbienceMix`, `DoorwayBlend`
- [ ] Check: one raid with logging off and FrameDiag on: no spikes from the mod in play, roof rain still fades in no-rain rooms

### Settings clean-up `(S)`
- [x] Public build (31 settings) and developer build (every setting) (0.9.0)
- [x] Defaults = the tested setup (11 Nov 2026)
- [ ] List every setting, what reads it, and whether it's still needed
- [ ] Remove the decided A/B switches and their old code (`6g`, `9o`, `9p`, `9s`, `1d`, `1e`, `3n`, `3o` and the like), one area per raid
- [ ] Plain descriptions
- [ ] Migrate any renamed settings so values survive; back up the config first

### Fika and co-op `(CO)`
- [x] Works alongside Fika; off on a headless host (0.7.11)
- [ ] Commit the co-op echoes (branch `fika-echoes`)
- [ ] One raid as a Fika host on your own
- [ ] A two-player raid when a second player is available: shots, grenades, a close one with a headset, die and spectate

- [ ] **Publish 1.0**

---

## After 1.0

In order: weather, the headsets revamp, footsteps and body sounds, bots feel blasts, the supersonic crack, room reverb for other guns, echo tuning for big fights. The rest any time.

### Hearing
- [ ] **Headsets revamp** `(5)`: each headset's own protection and sound, its own switch
  - [ ] Concussion by what's worn: less ringing and a shorter hearing cut with electronic ear-pro, the felt hit kept
  - [ ] Grenades through a headset: keep the thump and pressure, take the sting off the bang (too quiet today, far too quiet with high-tier headsets)
  - [ ] "Headset on" from the game's own headset template, not the earpiece check
- [ ] Ear ringing from blasts too, by the pressure that reaches you; stacks faster indoors
- [ ] Gunfire ear ringing final pass `(5r)`: the noise at your ears from the probes, indoors and outdoors

### The world around you
- [ ] **Weather** `(W)`: research, then rain masking, humidity, wet surfaces and wind in the echoes and the outdoor mix
- [ ] Our own roof rain sounds by roof material
- [ ] The ground between you and a distant shooter `(G)`
- [ ] Distant buildings blocking far fights
- [ ] Rooms with more character (Factory gunshots sound much the same everywhere)
  - [ ] The room changes the through-wall thump
  - [ ] Your own room counts for the shock wave's sound (`ShockWave.FireCore`)

### Other people
- [ ] **Footsteps and body sounds** `(F)`: bring back from the `body-sounds` branch, re-check against the shared echo voices, tune by ear
- [ ] **Bots feel blasts too** `(B)`: through SAIN where installed; its own switch
- [ ] **Room reverb for other guns** `(RT)`: the nearest few shooters' rooms, no extra voices if possible

### Guns
- [ ] **Supersonic crack** `(C)` by calibre and speed
- [ ] Maybe: EFT 1.0 recordings on full-auto guns, one take per round `(6b)`
- [ ] Maybe: a pass over every gun's recordings and loudness `(7)`

### Big fights and performance
- [ ] **Echo tuning** `(E)`: end quiet echoes early, thin the middle of full-auto bursts, give up in stages, thin others while you fire, lower the ceiling when the audio thread falls behind; test on Factory and Labs
- [ ] Probe cost: the acoustic probe for less work per frame

---

## Known issues
- [ ] ADAR 2-15: slight static (about 0.3-0.5 s) at the end of tails after quick single shots. Not from cut voices (raid_20261113_155943, `tailcuts.csv`). Next check: the same strings with the mod off, to see if it's in the game's recording; then your echoes, the room tail and the outdoor tail off one at a time
- [ ] G36: never blends its indoor and outdoor recordings (raid_20261109_083938); it seems to have only one
- [ ] With Bring Back Concussion: its fade-back ending restores the mixer to before the blast, so crossing a doorway during those seconds can leave the indoor ambience at the old level until it next changes

## Not in Real Sound Simulation
GC and spawn stutters are separate mods, on branch `claude/gc-and-spawn-stutters`. Before using its GC mode:
- [ ] The ceiling check can collect every frame (needs a minimum interval)
- [ ] The game switching GC off at raid start can undo Manual mode and break the restore
