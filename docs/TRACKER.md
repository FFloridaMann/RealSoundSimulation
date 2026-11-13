# Tracker

One line per piece of work. Tick a box when it's done (on GitHub you can tick it straight from the phone).
Each area ends in one **final test**: a raid that covers the whole area, instead of a test per change. Why things sit where they do, and the history of every step: [ROADMAP-DETAILED.md](dev/ROADMAP-DETAILED.md) and [CHANGELOG-DETAILED.md](dev/CHANGELOG-DETAILED.md).

**Now:** the push to v1.0 (below). Cleaned up 11 Nov 2026: the one-off raid checks were folded into a final test per area.

---

## Before v1.0, in order
- [x] Bring Back Concussion: harmony gave permission (11 Nov 2026)
- [ ] Final tests, one area at a time (each area below). Fix only what sounds wrong; no new features
- [x] Fixed test route and checklist ([TEST-ROUTE.md](TEST-ROUTE.md)) and `tools/compare_raids.py` to compare a raid with the reference (11 Nov 2026)
- [ ] Run the route once on the current build: that raid becomes the reference
- [ ] After one more test raid (user, 11 Nov 2026), in order, one test raid after each:
  1. [x] Mixer owner (built 13 Nov 2026, 0.7.22; test raid still to run: close grenade with no headset, fire for a minute with gunfire ear ringing on, then the same with a headset): every feature submits an offset per mixer key; one place reads the game's value, sums the offsets and writes once per frame (today: ConcussionProof, ShockWave dip, GunfireHearing, AmbienceMix, DoorwayBlend snapshots). Fixes PutBack restoring blast-time values after a headset change.
  2. [x] Voice pool (13 Nov 2026: own file, per-play output, blast reserve of 8): a budget per feature and a per-feature count in voices.csv (nine features share EchoVoices); no rewrite.
  3. [x] Code review (13 Nov 2026, docs/dev/CODE-REVIEW-2026-11-13.md); its steps 1-5 done: comment headers, body sounds to a branch, Raid class, voice pool, BorrowGunVoice
  4. Settings clean-up: delete settled A/B switches; body sounds behind a developer build; about 25 settings in the public build.
- [ ] Release build: no diagnostics work while logs are off, load-in spikes gone (see Release build)
- [ ] Settings clean-up: remove settled test switches, defaults = the tested setup (see S)
- [ ] Fika: commit the co-op echoes (0.7.16 work), one raid as a Fika host
- [ ] Commit, then the Forge fixes: plugin folder and GUID, LICENSE, install steps and dependencies (Bring Back Concussion optional), page text, credits
- [ ] Publish v1.0: repo public, Forge page live

---

## Your own gun
Built: indoor/outdoor blend by the measured room and aim (`3c`), smooth doorways (`1`, `3`), echoes off the real surfaces by material (`4`, `M`), measured room tail (0.7.4), open air glides to outdoor (0.7.4-0.7.6), shot-to-shot variation (0.7.3).
- [ ] Final test: firing in a small room, a big hall, a stairwell, a doorway and the open; firing then turning; one full-auto burst indoors

## Outside sound and rain
Built: wind, rain and background through the real windows and doors (`4b`), roof rain traced from the roof by material and floors (`9p`-`9w`), the rain dropout fix (`7`).
- [ ] Final test: a rainy raid: deep inside, under a tin roof, a stairwell, by a window, walking in and out

## Other guns
Built: from the opening they come through, muffled by the route (`6.1`, `6.1b`); around corners outdoors (`6.8b`); shots ring out under the next and on a spare voice (`1e`, `1f`); bot and player echoes with a voice limit that keeps yours first (`6.3h`-`6.3r`).
- [ ] Final test: a fight in the next room, one on another floor, one behind a building outdoors, a distant firefight; summary BOT GUNSHOTS and BOT ECHOES

## Through walls and doors
Built: loss by material and thickness, walls can stop a sound, a thump you feel (`6.2`); closed doors by material (`6.5`); glass breaking from the right place (`6.7`).
- [ ] Final test: shots behind a wooden wall, behind concrete, behind a closed door; break a window from another room

## Explosions
Built: from the opening, dulled by route (`6.4`); bounces (`6.2i`); the right recording for how open the blast is (`6.6`); echoes and room echoes (`6.3`, `6.3e`); fragments by route and whizzing past (`6.7c`, `6.7f`); around corners outdoors (`6.8`); the shock wave (`6.9`); concussion by pressure, a soft cut and your room (`6.9h`-`6.9o`); a crack before the cut and a sharp start (`6.9r`, `6.9s`, 0.7.22).
- [ ] Name what cuts gun sounds ~50 dB at a close blast (blastmix.csv, 0.7.22), and fix it if it's ours to fix
- [ ] Final test: grenades in your room, the next room, through an open door, behind a solid wall, outdoors and beside a building; with and without a headset

## Hearing: headsets and ear ringing
Built: any headset takes away ringing and most of a blast's hearing cut (`6.9q`); ear ringing from gunfire without a headset, by calibre, muzzle device and ammo (`5r`).
- [ ] Final test: a few shots, then a lot, without a headset (rifle, pistol, subsonic, suppressed); a close grenade with and without one

## Footsteps and body sounds (on branch body-sounds, not in main)
Built: footsteps, gear, looting, voices, meds and food from the opening, through walls and through floors (0.7.15-0.7.20); above or below: muffling by floors, floor tone, knock from above and boom from below, a height layer, the F8 test (0.7.22).
- [x] Decided (user, 11 Nov 2026, after the Customs raid and "one person, one place"): "I really dont like the footstep sounds I think they are better off". Off for 1.0; finish after release (developer build only until then). Moved off main to the `body-sounds` branch on 13 Nov 2026 (code review step 2), so changes to the shared echo voices no longer have to keep it working; bring it back from there after 1.0

## Fika and co-op
Built: works alongside Fika (review, 10 Nov 2026); off on a headless host (0.7.11); other players' guns echo and rank above bots (0.7.16 work, not committed).
- [ ] Commit the co-op echoes
- [ ] Final test: one raid as a Fika host on your own
- [ ] When a second player is available: a two-player raid (shots, grenades, a close one with a headset, die and spectate)

## Release build
- [x] Bug report logs off by default (`Bug report - logs`)
- [ ] With logs off, no diagnostics work at all (one master switch); the probes and raid resets keep running without it
- [ ] `9w` no longer depends on logging (`EnvProbe.RoomPostfix` records the room state first)
- [ ] Count `LastPlayingInContainers` on its own for `OwnEchoes`; stop building container rows when logging is off
- [ ] Load-in spikes: `AmbienceMix` (110-165 ms), `DoorwayBlend` (37-55 ms), `ClosedDoors` re-scanning during the deploy countdown
- [ ] Final test: one raid with logging off and FrameDiag on: no spikes from this mod in play, roof rain still fades in no-rain rooms

## S · Settings clean-up
- [ ] Two builds from one codebase (user, 11 Nov 2026), after the final tests: a **developer build** for the author alone (every setting, A/B switches, test keys, diagnostics; never published) and a **public build** with one short F12 page by what you hear (General, Your gun, Other guns, Explosions, Hearing, Footsteps and body sounds, Rain and ambience): an on/off per feature and one or two plain sliders each; every other value fixed at the tested setting
- [ ] List every setting, what it does and what reads it
- [ ] Remove decided A/B switches and their old code (`6g`, `9o`, `9p`, `9s`, `1d`, `1e`, `3n`, `3o` and the like)
- [x] Defaults = the tested setup (11 Nov 2026): 25 defaults set to the author's tuned values; Sound quality High (author plays Ultra), ear ringing off; diagnostics and body sounds left as they were
- [ ] Plain-language descriptions
- [ ] Migrate renamed settings so values survive; back up the config first

---

## After v1.0, in order
- [ ] **A** · Adrenaline after close calls: a built heartbeat, your character's own hard breathing, mask breathing from your own breaths (no outside recordings)
- [ ] **W** · Weather: rain, wet surfaces and wind in the echoes and the outside mix
- [ ] **5** · Headsets revamp: each headset's own protection and sound, its own switch
- [ ] Grenades through a headset (user, 13 Nov 2026): "grenades with headsets are two quiet. and far to quiet with high tier headsets ... we want to recreate the feeling of the blast": keep the thump and pressure while the clamp takes the sting off the bang
- [ ] **F** · Footsteps and body sounds: tuning by ear, if not finished for 1.0
- [ ] **B** · Bots feel blasts too (SAIN where installed; its own switch)
- [ ] **C** · Supersonic crack by calibre and speed
- [ ] **RT** · Their room rings too: a shooter's own room rings, close shooters only
- [ ] **E** · Echo tuning for big fights: end quiet echoes early, full-auto thinning, give up in stages, thin others while you fire, a lower ceiling when the audio thread falls behind
- [ ] **5r** final pass: the noise at your ears from the probes, indoors and outdoors
- [ ] Ear ringing from blasts by loudness too (user, 13 Nov 2026): grenades add to the same gunfire ring by the pressure that reaches you, like concussion; indoors stacks faster
- [ ] Gunshots on Factory: most sound basically the same across the map (user, 11 Nov 2026: "a small item maybe tweak after 1.0"); give rooms and halls more of their own character
- [ ] "Headset on" from the game's own template, not the earpiece check: headsetmix.csv (raid_20261111_194709) showed the check and the game disagreeing for minutes at a time (headset counted with the game on its default template, and the reverse)

## Maybe, at the end
- [ ] **6b** · EFT 1.0 recordings on full-auto guns (one take per round)
- [ ] **7** · Every gun: a pass over each gun's recordings and loudness

## Any time
- [ ] Explosion sound pass: a listen-through of how blasts sound overall
- [ ] **G** · The ground between you and a distant shooter (grass versus asphalt)
- [ ] Distant buildings blocking far-away fights
- [ ] The room changes the thump: a small hard room pushes more through a wall than a big hall
- [ ] Your own room counts for the shock wave sound too (`ShockWave.FireCore`)

## Known issues
- [ ] Bring Back Concussion puts the mixer back to before the blast when its fade ends; crossing a doorway during those seconds can leave the indoor ambience at the old level until it next changes
- [ ] The G36 never blends indoor/outdoor (raid_20261109_083938); looks like one recording
- [ ] ADAR 2-15: slight static (about 0.3-0.5 s) at the end of tails after quick semi-auto shots. Not from cut voices: in raid_20261113_155943 no tail was cut or faded (tailcuts.csv), yet it was still heard. Left for now (user, 13 Nov 2026). Next check: the same strings with the mod off, to see whether it is in the game's own recording; then your echoes, the room tail and the outdoor tail off one at a time

## Not in Real Sound Simulation (separate mods)
GC and spawn stutters, branch `claude/gc-and-spawn-stutters`. Fix before using its GC mode:
- [ ] Ceiling check can collect every frame (needs a minimum interval)
- [ ] Game switching GC off at raid start can undo Manual mode and break the restore
