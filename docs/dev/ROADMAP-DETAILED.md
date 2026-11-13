# Roadmap: the why and how

The design notes behind the plan: what each piece of planned work is for, how it would be built, and what it depends on. The headings match the two other plan docs:

- [ROADMAP.md](../../ROADMAP.md): the plan for players, short.
- [TRACKER.md](../TRACKER.md): the checklist, in order. Tick things off there, not here.

How things work now is in [DESIGN.md](DESIGN.md), and each feature's details in [FIXES.md](FIXES.md). Everything already built, with the old step-by-step plan and the raid checks, is in [HISTORY.md](HISTORY.md). The short codes in brackets, like `(W)`, are the old step names; they still match the logs, the changelog and setting descriptions.

---

## Road to 1.0

0.9 is a beta (13 Nov 2026) so players on other PCs and maps can report what sounds wrong. No new features until 1.0: only fixes, tests and clean-up.

### Beta feedback
Bug reports arrive as zips from the **Bug report - logs** switch: the raid folder, selfcheck, patches, errors, the player's mod list and BepInEx log. Sort each into a known issue, a fix for 1.0, or a clash with another mod (say so on the Forge page).

### Final tests, one area at a time
One raid per area on the public build, using the route in [TEST-ROUTE.md](../TEST-ROUTE.md), instead of a raid per change. The first route run on the current build becomes the reference, and `tools/compare_raids.py` compares later raids with it. The areas, and what each test covers, are in the tracker: your gun, outside sound and rain, other guns, walls and doors, explosions, hearing.

### Polish: a release build that does nothing it doesn't need
The user, 9 Nov 2026: the final mod logs nothing it doesn't need to work. Measured on Factory this gains no FPS (about 0.04 ms a frame), but it removes the load-in and raid-end spikes, about 40 MB of allocation and the log files.

- **Done in 0.9.0:** bug report logs off by default; the timing patches and audio-thread counters only run with them on.
- **Fix `9w` first.** `EnvProbe.RoomPostfix` returns before it records the room state when logs are off, so `EnvProbe.GameSaysIndoor` is always false and roof rain stops respecting no-rain rooms. Record the state, then check for logging.
- **One master diagnostics switch.** Off stops the CSV files, `summary.txt`, `patches.txt`, the profiler counter list, `Perf`, `BatchWait`, the overlay and the F9/F10/F11 keys. Keep `DiagRunner`: its raid start runs the `Reset()` of every system (now through the `Raid` class) and its `ReportOnce` guards the features. Keep `EnclosureProbe`, `OutdoorExposure` and `AcousticProbe` running; most of the sound depends on them.
- **Count the voices on their own.** `OwnEchoes` reads `DiagRunner.LastPlayingInContainers`. Count it separately at the same rate and stop building the container rows.
- **Load-in spikes**, before you have control, so not urgent, but they show in FrameDiag: `ClosedDoors` re-scans during the deploy countdown (77-114 ms, then about 60 ms every 3-4 s; scan once when ready), `AmbienceMix` 110-165 ms, `DoorwayBlend` 37-55 ms.
- **Check:** one raid with logging off and FrameDiag on: no spikes from the mod during play, and roof rain still fades in no-rain rooms.

### Settings clean-up `(S)`
- **Done in 0.9.0:** the public build shows 31 settings (an on/off page, then a few volumes per area); the developer build shows all of them. Defaults are the tested setup.
- **Left:** list every setting, what reads it and whether it's still needed; remove the A/B switches that have been decided, with their old code (`6g`, `9o`, `9p`, `9s`, `1d`, `1e`, `3n`, `3o` and the like); plain descriptions. Do it once, one area per raid, so nobody's settings are reset by a later update. A renamed key resets to default in BepInEx, so migrate old values on first load (as 0.9.0 does for the new plugin ID).

### Fika and co-op `(CO)`
- **Today:** works alongside Fika (review, 10 Nov 2026) and switches off on a headless host (0.7.11). Never tested with a second player.
- **Co-op echoes** (branch `fika-echoes`): another player's gun goes through the same echo priority as bots, with a bonus in the ranking only (about +6 dB), so a teammate next to you beats a distant bot, but a teammate behind three walls still loses to a bot shooting at you from 5 m. Each player's voices belong to their own copy of the game, so neither uses up the other's.
- **Check first:** that another human's gun is treated as someone else's, not as your own (since 0.6.61 any gun not in first person counts as someone else's).
- **Tests:** one raid as a Fika host on your own; then a two-player raid (shots, grenades, a close one with a headset, die and spectate) when a second player is available.

---

## After 1.0

In order: weather, the headsets revamp, footsteps and body sounds, bots feel blasts, the supersonic crack, their room rings, echo tuning for big fights. The rest any time.

### Hearing

**Headsets revamp `(5)`.** Today every headset protects the same (`Concussion - headset protection %`, 0.7.10). The revamp gives each headset its own protection and sound, by how it's really built, optionally simulated, with its own switch at the top of F12.
- The DSP (`RealSoundSimulation.Dsp`, with its offline bench in `tools/HeadsetBench`) already exists and can keep improving offline.
- Comes after the sound around you is finished, because electronic ear-pro reacts to everything loud: your echoes, other guns from the openings, through-wall thumps, other guns' echoes, weather. Then re-check the indoor/outdoor mix through it, because ear-pro amplifies ambience.
- **Concussion by what's worn:** electronic ear-pro clips the peak, so less ringing and a shallower, shorter hearing cut, while the felt hit stays. No ear-pro is the worst case.
- **Grenades through a headset** (user, 13 Nov 2026): grenades are too quiet with headsets, and far too quiet with high-tier ones; "we want to recreate the feeling of the blast". Keep the thump and pressure while the clamp takes the sting off the bang.
- **Which headset is on:** decide from the game's own headset template, not the earpiece check. `headsetmix.csv` (raid_20261111_194709) showed the two disagreeing for minutes at a time.

**Ear ringing from blasts by loudness** (user, 13 Nov 2026). Grenades add to the same ring as gunfire (`5r`), by the pressure that reaches you, like concussion; indoors it stacks faster.

**Gunfire ear ringing, final pass `(5r)`.** The noise at your ears from the probes, indoors and outdoors.

### The world around you

**Weather `(W)`.** In rain the user hears less echo and less open-air sound (raid 19), yet the logs show echo levels almost unchanged (dry −22.1 dB, medium rain −22.4, heavy −24.9). So it's masking by the rain bed, which now also comes indoors. Research how real weather changes what you hear, then decide what the echoes and the outdoor mix should do:
- rain noise by intensity, and how much it masks;
- humidity and temperature change air absorption (high humidity absorbs *less* treble at normal temperatures);
- wet surfaces: water fills pores, so reflections get slightly stronger and soil and grass absorb less;
- wind bends sound with height, and gusts make it waver;
- drops hitting nearby roofs, cars and puddles as sound sources.

**Our own roof rain sounds.** Patter loops per roof material made offline (tin, wood, concrete, tarp; three intensities), each drop a tiny impact ringing at that material's frequencies, as one layer over the game's rain. Listened to as WAVs before they go in the game. Until then the roof material only changes the level (`9r`).

**The ground between you and a distant shooter `(G)`.** Grass takes mids and treble off a far shot, asphalt doesn't. One filter per gun, no voices; needs the ground sampled along the path. Never a separate ground echo: at these delays the ground changes the tone, and the recordings already hold a bounce. Details: [MATERIALS.md](MATERIALS.md).

**Distant buildings blocking far fights.** Nothing past about 250 m is muffled by terrain or buildings. Independent; needs different, long rays.

**Rooms with more character.** On Factory most gunshots sound basically the same across the map (user, 11 Nov 2026: a small item, after 1.0). Give halls and small rooms more of their own sound. Related:
- the room changes the through-wall thump: a small hard room pushes more through a wall than a big hall;
- your own room counts for the shock wave's sound too (`ShockWave.FireCore`), as it already does for concussion.

### Other people

**Footsteps and body sounds `(F)`.** Built in 0.7.15-0.7.22 and moved to the `body-sounds` branch on 13 Nov 2026 (user, 11 Nov: "I really dont like the footstep sounds"). Footsteps, gear, looting, voices, meds and food from the opening they come through, through walls and floors, and above or below you: muffling by floors, floor tone, a knock from above and a boom from below, a height layer, the F8 test. One person always sounds from one place. Bring it back after 1.0 and tune it by ear; the shared echo voices have changed since, so it needs re-checking against them.

**Bots feel blasts too `(B)`.** The same blast pressure that concusses or deafens you does the same to bots, through SAIN where it's installed. In Fika it runs only where the bots are controlled. Its own switch, since it changes how fights play.

**Their room rings too `(RT)`** (user, 11 Nov 2026). Today the room tail (0.7.4) measures only your room, so a bot firing in a big hall next door brings no hall ring.
- Measure the shooter's room with a small sweep (like the muzzle room, 37 rays) the first time they fire from a spot, cached by position for a few seconds; the nearest few shooters only.
- No extra voices if the ring is an `AudioReverbFilter` on the shot's own tail voice (the spare voice it rings out on), sized from their Rt60 and brightness and heard through the route like the shot. That moves the cost to the audio thread, so cap it (about 3 shooters) and drop it first when the echo voices are busy. Fallback: one voice per shot from the room-tail pool.
- Muffled by the way it reaches you; shooters outdoors get none.

### Guns

**Supersonic crack `(C)`.** Today the game has four crack clips (9 mm, 5.45, 7.62, shotgun), played at the shooter's position and delayed like the report. The crack should vary per round, from the ammo's speed and the bullet's size:
- **Speed:** faster gives a sharper, shorter N-wave; subsonic gives none. Just above Mach 1 the crack gets much longer and weaker.
- **Size:** a wider bullet gives a longer, stronger crack; a longer bullet slightly shortens and weakens it (Whitham: duration and peak grow with diameter, length^-1/4).
- **Miss distance:** level falls about as b^-3/4; duration grows about as b^1/4.
- **Build, in order:** pitch-shift, shorten and level the four existing clips by those rules (cheap, keeps the game's recordings); later, synthesise the N-wave itself. Either way one voice per passing round, as now. A listening reference: Garand Thumb's video of many calibres fired over the camera (nothing from it ships).

**Maybe, at the end:**
- **EFT 1.0 recordings on full-auto guns `(6b)`.** Sound Engine Backport's recordings are single shots, and the game loops one recording while the trigger is held. Play one take per round fired instead, with varied takes and a tail on release. A maybe because it adds load (about one voice per bursting gun, capped at 2-3 overlapping takes).
- **Every gun `(7)`.** A pass over each gun's recordings: loudness across guns and calibres, suppressed against unsuppressed, near/far and indoor/outdoor sets that match, full-auto loops and tails. Needs the recordings' real loudness, measured from the output. Last, so every gun is judged in the finished acoustics.

### Big fights and performance

**Echo tuning for big fights `(E)`.** Once everything that makes echoes exists, tune it together:
- **End quiet echoes early** with a short fade: an echo holds its voice for its whole 2.5-5 s recording though it's inaudible long before (raid 35: 84 echoes from about 3.5 s of your firing filled all 96 voices).
- **Full-auto:** keep the full set of echoes on the first round and the tail, and only the loudest one or two through the middle of a burst; each round masks the last one's echoes. Judge by ear, since the mid-burst echoes also carry the room.
- **Give up in stages:** fewer echoes per shooter, then shorter ones, then a zero-voice tier (raise the reverb send of the sound already playing), never a hard stop.
- **Masking:** thin out everyone else's echoes while you're firing.
- **Lower the ceiling when the audio thread falls behind.** Voice count isn't the only limit; every voice costs that thread.
- **Test on Factory and Labs,** which have the most echoes at once.

**Probe cost.** The whole mod costs about 0.16 ms a frame in play (FrameDiag, 10 Nov 2026); the acoustic probe is the largest steady part (about 0.10 ms). Same results for less work: spread probing over frames, skip it when nothing has moved, reuse buffers. Fix one thing at a time and compare a FrameDiag raid with those numbers.

---

## Not part of this mod

GC and spawn stutters are separate mods (Raid GC Throttle and The Bot Loading Freeze Fix), on branch `claude/gc-and-spawn-stutters`.
