# Changelog

Newest first. Each version: what changed, and whether it has been run in game.

## 0.7.2 — 10 Nov 2026 (bug report: the player's setup)
**Run in game:** not yet.
- The bug report zip adds `mods.txt` (game version, every loaded BepInEx plugin with name, version, id and folder name, and the server mod folder names, read from `SPT_Runtime/user/mods`, `SPT/user/mods` or `user/mods`; names only, never full paths) and a copy of `BepInEx/LogOutput.log`, read with sharing because BepInEx keeps it open.
- `Plugin.Version` constant; the description moved into the repo as `FORGE-PAGE.md` and the README points to it.

## 0.7.1 — 10 Nov 2026 (6.9n: your room builds up blasts)
**Run in game:** not yet. The 3 dB top is hand-set from small-scale tests.
- **New `6.9n Your room builds up blasts`** (on, live, in General next to the other concussion switches) and `6.9o Your room build-up` (3 dB, 0 to 6, advanced, Explosions). User: "a blast getting into a small room you're standing in should build up too". Until now only the blast's room counted in the concussion dose.
- Your room is measured with the same 48 rays as the blast's (6.6's MeasureAt, from ear height, cached per frame). A blast that reaches you through the air from elsewhere adds up to `6.9o` dB in a very small closed room, nothing in a big one (14 m mean wall distance) or an open one, and up to 0.3 more duration. Not added through a wall, or in the same room as the blast (no wall on the line and closer than twice your room's mean wall distance plus 1 m), where the blast's room already counts.
- concdose.csv gets blastRoomDb, yourRoomDb, yourRoomM and yourRoom (added, same room, open or big, through wall, off). The CONCUSSION SOUND CUT summary line counts them.
- Version 0.7.1. F12 now has 158 settings (39 by default, 119 advanced).

## 0.7.0 — 10 Nov 2026 (new name; F12 regrouped)
**Run in game:** not yet.
- Renamed to Real Sound Simulation: plugin ID `ffloridamann.realsoundsimulation`, DLLs `RealSoundSimulation.dll` and `RealSoundSimulation.Dsp.dll`, folder `BepInEx/plugins/FFloridaMann-RealSoundSimulation`, logs in `RealSoundSimulation Logs`. Settings are carried over from the old config file once, at install.
- F12 regrouped into eight sections by what you hear, with plain names. 38 settings show by default, 118 behind "Advanced settings". Each description ends with the old code (for example "Was 6.3i"), which these docs still use.
- README, roadmap and changelog rewritten in plain language; the detailed docs moved to `docs/dev`.
- Concussion by blast pressure and Soft concussion cut are the first two settings in General.
- `Write logs` is now **Bug report logs** in General, off by default. Logs go to `RealSoundSimulation Logs` in the SPT folder (was a folder under `BepInEx`). At raid end the raid folder, selfcheck.txt, patches.txt and errors.txt are zipped on a worker thread as `Bug report <date>_<time>.zip`.
- "Respect no-rain rooms" (9w) keeps working with logging off: the game's indoor/outdoor room state is recorded before the logging check.

## 0.6.61 — 9 Nov 2026 (6.3h: bot gunshot echoes and voice priority)
**Run in game:** not yet. It compiles; every number is a starting guess.
- **New `6.3h Bot gunshot echoes`** (on, live). A bot's shot echoes off walls that it and you can both reach. When a wall is in the way and a route is known, the echoes start from the opening nearest you. The search uses the same 25-ray fan as explosion echoes, on the worker threads, and is reused for 1.5 s, so a burst costs one search.
- **One set per shooter at most every 0.25 s.** A shot or the end of a burst gets the full set. A full-auto burst running past 0.2 s gets its loudest echo, looping, until it stops. `6.3i` level (0 dB), `6.3j` echoes per bot (3), `6.3l` range (120 m), `6.3m` audibility (−42 dB), `6.3o` length (2.5 s).
- **Echo voice priority.** User: "prioritizes the players1 guns shots and echos first", "without allowign the cracks to show due to voice limits". Only bot echoes can ever lose a voice. `6.3k` is their ceiling (24). `6.3n` keeps voices free for your gun (48, at most half of what's available right now).
- **`6.3p Replace the quietest echo`** (on, live). At the limit a new echo takes over from the bot echo you can hear least, with a 40 ms fade. Your gun, grenades and everything else can take any bot voice; a bot echo only takes one that ranks below it. A later echo of a shooter's set goes first, so each shooter's first echo outranks another's third, then the quietest by how far through its length it is. Eight spare voices carry the fades, so the new echo starts at once.
- **A shooter keeps the echo count it started its burst with,** chosen from the bot voices free at that moment, so echoes don't flicker from round to round.
- No bots firing means no change to anything else: your own echoes, grenades and the rest follow the same limit as before.
- New botechoes.csv and voices.csv; new BOT ECHOES and ECHO VOICE PRIORITY summary lines.
- Version 0.6.61. F12 now has 156 settings.

## 0.6.60 — 9 Nov 2026 (6.9k: concussion by blast pressure)
**Run in game:** not yet. Levels are hand-set; the research gives the shape, not thresholds for a grenade.
- **New `6.9k Concussion by blast pressure`** (on, live), `6.9l Ears reach` (+6 dB, 0 to 18) and `6.9m Concussion strength` (0 dB, −12 to +12). User: "shouldnt it be more concussive indoors", "a wall may protect but the blast through a door delivers concussive effects ... room indoors but very big? very small? lots of windows no windows". Bring Back Concussion used straight-line distance only: in raid_20261109_140020 a blast 3.3 m away through a wall was the strongest of the raid, and one 6.1 m away in the same room barely registered.
- A blast's dose comes from the way it reached you (route, corners, shut doors, walls) plus up to 5 dB for a very small enclosed room and 0.5 dB for a very big one. Its duration comes from how small and shut in the room is, with broken windows counting as open, and less for a detour. Inside 2.5 m it never falls below −2 dB.
- That dose becomes the open-air distance with the same pressure, and that mod's explosion patch is called with the blast moved there, so its radius and closeness still decide.
- **Two levels:** ears (sound cut and ringing) reach `6.9l` further than the full concussion. Concussion length and strength, and the sound cut's hold, scale with duration and closeness.
- The concussion waits for the blast sound, which the game plays a frame later. If that doesn't come within 0.25 s, or for flashbangs and smoke, that mod's distance rule is used.
- New concdose.csv; the CONCUSSION SOUND CUT summary line counts them.
- Version 0.6.60. F12 now has 147 settings.

## 0.6.59 — 9 Nov 2026 (explosion echoes fade out)
**Run in game:** not yet.
- **Bug fix: explosion echoes (`6.3`) ended dead at `6.3c`.** User: "the end tail on concussion grenade explosions is still cutting out sharply". They were the one blast layer with no fade. raid_20261109_140020: echoes up to +1 dB, while the concussion cut only took Guns down 7 to 13 dB on four of its five blasts. They now fade over the second half of `6.3c`, like the thump and room echoes. That this was the sound heard is likely, not proven.
- Version 0.6.59. F12 still has 144 settings.

## 0.6.58 — 9 Nov 2026 (6.9h: soft concussion cut)
**Run in game:** not yet. Starting values, set by ear.
- **`6.9h Soft concussion cut`** (on, live), **`6.9i Blast tail fade s`** (0.8, 0.1 to 3) and new **`6.9j Felt blast through concussion dB`** (0, −24 to +12), replacing 0.6.57's delay. User: "better if the hearing cuts out with concussion when the effect is applied. but we find a way to keep the feeling of the blast wave alive and no sharp cut offs for tails". Bring Back Concussion's cut is taken over and copied (same depth, cut time, hold, fade-back and tinnitus check). World sounds still go at its speed, but the Guns channel fades over `6.9i`, so the bang, echoes and tails melt away. Inside the part of its radius where it cuts, the thump, pressure hit, repeat hits and slow push go to the Master group and survive.
- Bots' gunfire is on Guns too, so it fades over the same time. If that mod's code isn't found, its own cut runs.
- Adapted from Bring Back Concussion by harmony (MIT): `THIRD_PARTY_NOTICES.md`.
- New concussion.csv; the CONCUSSION SOUND CUT summary line counts soft cuts.
- Version 0.6.58. F12 now has 144 settings.

## 0.6.57 — 9 Nov 2026 (long routes checked against the world; concussion cut delayed)
**Run in game:** not yet.
- **Bug fix: a long baked route no longer moves a sound on its own say-so.** raid_20261109_131528: a window at the car dealer, 32 m away, took a 75 m route and was heard towards the Capital; the blast there did the same. User: "i threw a grenade at the cardealer adn heard glass break in the capital". A window with nothing solid between you and it plays from the window. A route longer than 1.5 x the straight line + 6 m is checked against the corner search: blasts, glass and fragments use the shorter of the two; with no way found they keep the true direction, dulled and quieter as if around two corners. Bounces keep their true spot and are dulled. `reason` can be `clear to the window`.
- **`6.9h` reworked: Bring Back Concussion's cut is delayed instead of avoided.** `6.9h Delay concussion sound cut` (on) and `6.9i Concussion cut delay s` (0.8, 0 to 3), replacing 0.6.55's Master routing, which lost the Guns bus's gain: a blast at 6.9 m was heard as "very quiet". That cause is likely, not proven. Everything stays on its normal channel, and that mod's cut starts after the delay. It patches that mod's cut by name, and if the mod changes the cut just happens at its normal time. The game's own ringing isn't delayed.
- New blastlevel.csv: the level each blast's recording really played at, once the game's occlusion is applied.
- Fragment pass-bys under volume 0.05 no longer play or use up a place.
- Version 0.6.57. F12 still has 143 settings.

## 0.6.56 — 9 Nov 2026 (6.7f: fragment pass-bys)
**Run in game:** not yet. Sound shapes and levels are by ear; the voice and CPU cost are estimates until a raid.
- **New `6.7f Fragment pass-bys`** (on, live; `Fixes/FragmentPassBy.cs`), `6.7g` level (0 dB, −24 to +12), `6.7h` reach (5 m, 1 to 15) and `6.7i` per grenade (4, 1 to 12). User: "what if 1 fragment comes out the door at me", "grenade fragments do make alot of noise though and cracks". Each fragment's real flight is followed. A path within reach with nothing solid between plays a generated whiz, high to low bass and pitched per fragment, and inside 1.5 m a short crack. Works for your own grenades and for bots behind cover, which the game's whoosh skips. The game's own whoosh is left alone.
- Inside the concussion radius they go past that cut (`6.9h`). They obey the echo voice cap.
- Fragment speeds in the game's data are 90 to 100 m/s, so a pass lands later than a real one would. A search for real fragment speeds was refused by the search tool, so nothing here rests on them. Soldiers' accounts were used for the character of the sound.
- New passby.csv; FRAGMENT PASS-BYS summary line.
- Version 0.6.56. F12 now has 143 settings.

## 0.6.55 — 9 Nov 2026 (6.9h: the blast survives concussion; 6.7c: fragment hits by route)
**Run in game:** not yet. Starting values, set by ear.
- **New `6.9h Blast survives concussion`** (on, live; `Fixes/ConcussionProof.cs`) and `6.9i` level (0 dB, −24 to +6). User: "when concussions are triggered the echo and grenade background sounds bad it cuts out". Bring Back Concussion, for a blast inside its radius, takes every mixer bus including Guns down by up to 80 dB within 0.05 to 0.3 s of the grenade going off, then fades back over its `Explosion Sound Fade In Time`. Explosions and the mod's thumps, hits and echoes are on Guns, so they were chopped. Inside that mod's radius + 1 m they now go to the Master group, which the cut doesn't touch, as the game's own tinnitus ring does. The dip (`6.9c`) is left to that mod there, and is ended before it reads the volumes it later restores.
- **New `6.7c Fragment hits by route`** (on, live), `6.7d` dulling (1, 0 to 2) and `6.7e` loss per opening (4 dB, 0 to 12, 16 at most). A grenade fragment hitting something in another room comes from the opening, dulled and quieter. Fragments are told by speed: 90 to 100 m/s in the game's item data. The route is looked up once per room for 0.5 s.
- New fragments.csv; BLAST THROUGH CONCUSSION and FRAGMENT HITS BY ROUTE summary lines.
- Version 0.6.55. F12 now has 139 settings.

## 0.6.54 — 9 Nov 2026 (6.9: the shock wave goes through the building)
**Run in game:** not yet. Starting values, set by ear; the research behind them is mostly small-scale and was read from abstracts.
- **The pressure hit now takes the way the wave would.** raid_20261109_121839: every blast in shock.csv had a wall on the straight line, so a hit that only took the straight line averaged volume 0.06, even at 6 m in the same room. User: not getting it "at mid distances and indoors around corners and rooms". It uses the stronger of the way through the openings (or around a corner) and the straight line through the walls; your own room or a clear line counts as open. It comes from the way it arrives and arrives by the length of that way.
- **New `6.9d` corner loss** (6 dB per 90 degrees, half for each extra opening, 0 to 18), plus a shut door's closure x 20 dB. The front softens from 3 kHz, halving per bend; only a real path through a wall is cut to 200 Hz.
- **New `6.9e` enclosed boost** (up to +6 dB, 0 to 12), by how shut in the blast point is. **New `6.9f` repeat hits** (3, 0 to 4), spaced by the size of the room, because indoors a shock arrives as a train of shrinking pulses. **New `6.9g` slow push** (0 dB, −24 = off): a long low swell after the hit, strongest in a small or shut room.
- **`6.9b` reach is now 60 m** (renamed so the new default applies), and it's measured along the way.
- **The dip now goes on top of whatever else sets those mixer controls**, every frame, instead of letting go. 8 of the 12 controls were let go last raid. Its depth follows the route too. It only restores a control nobody else has moved.
- shock.csv has new columns: `way`, `pathM`, `bends`, `closure`, `enclosed`, `roomM`, `repeats`. The SHOCK WAVE summary line counts hits by way.
- Version 0.6.54. F12 now has 134 settings.

## 0.6.53 — 9 Nov 2026 (6.6c: outside pulls to outdoor; 6.9: shock wave)
**Run in game:** not yet. Starting values, set by ear.
- **New `6.6c Outside pulls to outdoor`** (0.75, 0 to 1, live). raid_20261109_114154: all 6 recording changes turned the game's Outdoor into Indoor while the player was outside. User, beside a garage: "it still doesnt sound like its outdoor mix is right when its comes out the front of the entrance". blastrec.csv has `listenerEnclosure` and `outdoorFloor`.
- **New `6.9 Shock wave`** (on, live; `Fixes/ShockWave.cs`), `6.9a` level (0 dB, −24 to +12), `6.9b` reach (40 m, 10 to 100), `6.9c` dip (8 dB, 0 to 24). User: explosions "just lack the sense of a shock wave that the player can feel". A generated pressure hit with its level from Kinney-Graham blast pressure; the thump gets a second in-step voice up to +6 dB; other sounds dip for about 0.38 s. Works with `6.2` off, apart from the thump part.
- New shock.csv; SHOCK WAVE summary line.
- Version 0.6.53. F12 now has 130 settings.

## 0.6.52 — 9 Nov 2026 (6.5 actually reads doors)
**Run in game:** not yet.
- **Bug fix: `6.5` never worked on a real door.** `GameWorld.FindDoor` crashes in SPT because its `World` component is null, so from 0.6.47 every shut door kept the game's full closure. Doors are now read from the scene once the raid has them.
- The same crash stopped `6.4` for any blast, bounce or glass whose route ran through a shut door: no move to the opening, no dulling, no room echoes, no row in blast.csv. raid_20261109_114154, a grenade in a room with one wood door, shut during the fuse: route cost went from 0.03 to 0.77 and the blast had no row. A failure reading a door now keeps the game's value and leaves `6.4` running.
- The CLOSED DOORS summary line also says how many doors are in the scene. Not found after the first tries shows as `door not found`.
- Version 0.6.52. F12 still has 125 settings.

## 0.6.51 — 9 Nov 2026 (6.8: search from both ends, bot gunshots, arrival times)
**Run in game:** not yet. Starting values.
- **The corner search now works from both ends** (`Acoustics/CornerSearch.cs`), on the worker threads: fans from you and from the source, plus the openings probe v2 remembers, and it finds one or two corners. About 290 rays when the blast is close, finished in the same frame; about 960 over the next 2 frames when the sound has at least 50 ms still to travel.
- **The 4-per-second limit is gone.** A request within 3 m at the source and 1.5 m at you reuses an answer up to 3 s old. Up to 8 searches at once.
- **New `6.8b Bot gunshots around a corner`** (on, live): the same search for bots when the room map has you both outdoors. Position and tone only.
- **New `6.4d Blast arrives by its route`** (on, live): a blast moved to an opening or corner now arrives after the extra distance it travels; before, it used the straight-line delay. The through-wall thump keeps the straight line, so it lands first.
- **The thump gets a 1.5 m head start** (about 4 ms). User: "blasts waves move faster than sound". Worked out with the Kinney-Graham blast formula for 60 g to 180 g charges: the front is 40 to 135% faster than sound at 1 m, 3 to 7% at 5 m and under 1% at 28 m, which adds up to 1.1 to 2 m (3 to 6 ms) by 28 m. Not audible by itself.
- blast.csv: `openings` is the number of corners; `reason` can be `search busy`. New CORNER SEARCH summary line.
- Version 0.6.51. F12 now has 125 settings.

## 0.6.50 — 9 Nov 2026 (6.8: blasts around a corner)
**Run in game:** not yet. Starting values.
- **New `6.8 Blasts around a corner`** (on, live) and `6.8a Corner loss dB` (4, 0 to 18). The routes behind `6.4` only exist between rooms, so where the room map has you and a blast both outdoors, a blast behind a building played clear from its true direction: 4 of 12 blasts in raid_20261109_105838. User, 9 Nov 2026: it should sound different "and from the correct position based on if you are standing at the front or behind the entrance". When the straight line is blocked, rays find the shortest way around one corner and the blast comes from there, dulled and quieter. With no way round it stays put, a step duller and twice as much quieter.
- Cost: at most 144 rays per blocked blast, at most 4 searches a second, nothing otherwise.
- blast.csv: `reason` can be `around a corner` or `no way round`; new last column `lossDb`. The BLAST FROM THE OPENING summary line counts them.
- Limits: one corner only. Grenade bounces and glass aren't included.
- Version 0.6.50. F12 now has 123 settings.

## 0.6.49 — 9 Nov 2026 (6.6, 6.7: blast recording by how open it is; glass by route)
**Run in game:** not yet. All values are starting values.
- **New `6.6 Blast recording by how open it is`** (on, live; `Fixes/BlastRecording.cs`) and `6.6a Open share for all outdoor` (0.4, 0.15 to 0.9). User, 9 Nov 2026: a grenade at the mouth of a garage "sounded fully indoors but the garage opens to the front to full open air". The game plays its indoor or outdoor recording, all or nothing, by its own indoor boxes. Now 48 rays from the blast measure how open it is and the two recordings blend, the weaker on a second voice that follows the same distance, delay, occlusion, opening and dulling. That second voice adds no second thump or echoes.
- **New `6.6b Close recording reach m`** (25, 10 to 60, live; 10 = the game). The game fades to its distant recording between 10 and 30 m, so a blast 27 m away was about 85% distant. User: "grenades are still not violent enough".
- **New `6.7 Glass from other rooms by route`** (on, live), `6.7a` dulling (1, 0 to 2) and `6.7b` loss per opening (4 dB, 0 to 12, 16 at most). User: glass broke "a few walls and turns" away and "sounded a bit loud". It now comes from the opening, dulled and quieter by the route. A window of your own room is untouched.
- New blastrec.csv and glass.csv; BLAST RECORDING and GLASS BY ROUTE summary lines.
- Not proven: that the game chose its indoor recording in raid_20261109_105838. Nothing logged it; blastrec.csv `gameEnv` does now. The open-air thump (`6.2i`) is unchanged.
- Version 0.6.49. F12 now has 121 settings.

## 0.6.48 — 9 Nov 2026 (6.3e: room echoes from the opening)
**Run in game:** not yet. Levels are worked out by hand, not measured.
- **New `6.3e Room echoes from the opening`** (on, live), `6.3f` level (0 dB, −24 to +12) and `6.3g` count (4, 1 to 6). raid_20261109_102037: 8 of 11 explosions had no echo, because one bounce needs a wall both the blast and you can reach. An explosion in another room now also sends reflections out through the opening it reaches you by: path = route + 6, 12, 20, 32 m; level +3 − 20 log10(path / 2) − 2 dB per echo; each duller than the last. They fade out over the second half of `6.3c`.
- echoes.csv has rows with `kind` = `Room`. The BLAST FROM THE OPENING summary line counts them.
- Limits: only where a route is found, so not the hideout or both outdoors. Nothing comes from your own room's walls.
- Version 0.6.48. F12 now has 115 settings.

## 0.6.47 — 9 Nov 2026 (6.5: closed doors muffle by material)
**Run in game:** not yet. Values are hand-set.
- **New `6.5 Closed door muffling by material`** (on, live; `Fixes/ClosedDoors.cs`) and `6.5a Closed door strength` (1, 0 to 2). User, 9 Nov 2026, Capital: "doors I dont think closed should muffle so much especially not wood doors". The game's route cost adds a shut door's closure level straight on and caps the total at 1, so one door at 1.0 could use up the whole score. Closure is now held to: cardboard and fabric 0.2, thin wood 0.3, plastic and glass 0.4, thick wood 0.5, thin metal 0.6, thick metal 0.8, unknown 0.7.
- Only the route data changes, and only ever downward. Every sound through a door is affected, not only grenades. `6.4` picks its route with these values too.
- The summary has a CLOSED DOORS line with the doors read by material.
- Version 0.6.47. F12 now has 112 settings.

## 0.6.46 — 9 Nov 2026 (6.4c: blasts dulled less than gunshots)
**Run in game:** not yet.
- **New `6.4c Blast dulling strength`** (0.5, 0 to 2, live). raid_20261109_100114 (Ground Zero): the gunshot rule took four blasts 10 m away, behind two walls, to about 1.5 kHz; user: not "loud enough or concussive enough especially indoors". At 0.5 those go to about 5 kHz. Grenade bounces still use `6.1c`.
- The same raid: every one of those blasts was beyond Bring Back Concussion's `Explosion Effect Radius` (7 m), so none concussed. That's the other mod's setting and wasn't changed.
- Version 0.6.46. F12 now has 110 settings.

## 0.6.45 — 9 Nov 2026 (grenade thump and echoes in the hideout)
**Run in game:** not yet.
- **An explosion's thump (`6.2`) and echoes (`6.3`) now work in the hideout.** User, 9 Nov 2026: "can we make it work in the hide out?" The game only creates its spatial audio system when it loads a raid, so these fall back to the game's listener. If the sound probe hasn't run yet, walls are found on the HighPolyCollider and Terrain layers (LowPolyCollider if wall-sized); not checked against the hideout's own layers.
- `6.4` (from the opening) and bounces can't work there: no rooms or routes exist. Gun thumps need rooms too. The hideout still writes no logs.
- Version 0.6.45.

## 0.6.44 — 9 Nov 2026 (6.2j: the thump fades out)
**Run in game:** not yet.
- **An explosion's thump now fades out over the second half of `6.2j`**, so a short setting doesn't cut off. User, 9 Nov 2026. At the default 1.5 s it holds for 0.75 s and then fades, so it's a little shorter-sounding than the old hard stop at 1.5 s. Gun thumps are unchanged.
- Version 0.6.44.

## 0.6.43 — 9 Nov 2026 (6.2j: explosion thump length)
**Run in game:** not yet.
- **New `6.2j Explosion thump length s`** (1.5, 0.2 to 5, live). 1.5 s was the fixed length. It can't run past the end of the recording, and it stops without a fade.
- Version 0.6.43. F12 now has 109 settings.

## 0.6.42 — 9 Nov 2026 (6.4: grenade bounces from the opening; thump in the open)
**Run in game:** not yet. Levels are starting values.
- **Grenade bounces now follow `6.4`, `6.4a` and `6.4b`.** User, 9 Nov 2026: "grenades bouncing on the ground noise still coming from true direction". Each grenade's own voice is re-placed each frame at the opening, with the route re-read every 0.1 s while the grenade moves or the voice plays. No thud for bounces.
- **Explosions get the thump with no wall in the way.** User: "grenades should also have more thump to them (in general". Same formula with no wall loss, plus new **`6.2i Open-air thump dB`** (−6, −24 to +6, live): full level up to 20 m, −6 dB at 40 m, −12 dB at 80 m, up to 100 m.
- The summary's BLAST FROM THE OPENING line counts bounces moved. In thump.csv, explosions with `walls` 0 now play.
- Limit: bounces only. A bot's pin and throw sounds stay where they are.
- The hideout wrote no logs in the session it was tried, so the mod's in-raid check didn't pass there; test grenades in a raid.
- Version 0.6.42. F12 now has 108 settings.

## 0.6.41 — 9 Nov 2026 (6.4: explosions from the opening, with a felt thud)
**Run in game:** not yet. The 15 m shut-door cost and the levels are starting values. Test with grenades in another room, `6.4` on and off.
- **New `6.4 Blast from the opening`** (on, live; `Fixes/BlastFromOpening.cs`) and `6.4a Blast opening strength` (1). User, 9 Nov 2026, Ground Zero: "i sort of wish grenades sounded more from the entrance and (felt (thud from teh source)". An explosion in another room plays from the direction of the opening nearest you, at the length of the route. Picks the shortest of the game's baked routes between the two rooms; a shut door counts as 15 m. The game's occlusion stays at the true spot through its own position offset.
- **New `6.4b Dull the blast by its route`** (on, live). The same rule as `6.1b`, using `6.1c` and `6.1d`.
- **New `6.2h Explosion thump level dB`** (+12, −12 to +24, live). Guns stay as they were. On raid_20261109_083938's two grenades the thump goes from −21 and −25 dB to −9 and −13 dB.
- blast.csv has a row per blast, and there's a new BLAST FROM THE OPENING line in the summary.
- Limits: the game's grenade group only, so not rockets. Same room, both outdoors or no baked route stay at the true spot. The sound stays where it was placed while it rings. The time of flight still uses the straight distance.
- Version 0.6.41. F12 now has 107 settings.

## 0.6.40 — 9 Nov 2026 (6.1b: bot gunshots dulled by their route)
**Run in game:** two raids, 9 Nov (raid_20261109_083033 Factory, raid_20261109_083938 Ground Zero). The code ran without errors: typical cutoff 1.6 kHz and 2.7 kHz. Not yet confirmed by ear that the filter works through the Meta spatializer. Numbers are starting values. Factory FPS was 40 with 13 bots, but the mod's measured cost was 1.9% of a core and audio timing was unchanged.
- **New `6.1b Dull the shot by its route`** (on, live; `Fixes/PortalPositioning.cs`). User, 9 Nov 2026: "the sound should be muffled (less distinct) regardless". raid_20261109_001706: a bot 39 m away with 3–5 walls on the straight line read occlusion 0.17 because one opening joins the rooms, and the game only muffles above 0.60, so 2 of 96 bot shots under 60 m were muffled.
- Rule: 1 step for a wall on the straight line + the turn at the nearest opening / 90° (at most 135°, less within 1 m of the opening) + 0.5 per extra opening. Cutoff = 16 kHz × 2^(−1.5 × steps × `6.1c`), floor `6.1d`. Smoothed over 150 ms, set at once on the first shot. Tone only.
- Sound bending round an edge does lose its highs first, so the direction is real. Real rooms are gentler than a filter and hard walls bounce some highs along, so the amounts are estimates.
- Sliders, all live: `6.1c` strength (1, 0 to 2), `6.1d` dullest cutoff (800 Hz, 400 to 4000). Works with `6.1` off. Steps aside when the game has put the shot on its muffled channel.
- routes.csv has `dullHz`, and there's a new ROUTE DULLING line in the summary.
- Limits: bot gunshots only, and only with a route. The number of openings still depends on the route file (`10b`).
- Version 0.6.40. F12 now has 103 settings.

## 0.6.39 — 9 Nov 2026 (6.3, first half: explosion echoes)
**Run in game:** not yet. Levels are starting values on the gun echo rule. Test in a no-AI raid: grenades in a concrete room and outdoors near a building, `6.3` on and off.
- **New `6.3 Explosion echoes`** (on, live; `Fixes/ExplosionEchoes.cs`). An explosion from you or a bot echoes off walls that it and you can both reach. 25 rays from the blast up to 150 m; ground ignored. Mirror bounce if the true bounce point checks out, otherwise a weaker scattered echo. Paths under 3 m longer than the direct sound are dropped. Under 80 rays per blast.
- Level: the gun echo rule (−20 log10(path / 2 m), surface loss, scattered −3 dB and × share) + 6 dB for an explosion. Air and surface dull it the same way.
- Played through fix 6's voices, under its headroom cap, on the gunshot mixer, so they should duck with Bring Back Concussion. Not checked in a raid.
- Sliders, all live: `6.3a` level (0 dB, −24 to +12), `6.3b` echoes per blast (6, 1 to 12), `6.3c` length (1.5 s, 0.3 to 3), `6.3d` range (120 m, 20 to 250).
- echoes.csv has rows with sample `explosion`, and a new EXPLOSION ECHOES line in the summary.
- Limit: one bounce, so a blast in another room with no wall you share gets no echoes.
- Version 0.6.39. F12 now has 100 settings.

## 0.6.38 — 9 Nov 2026 (6.2: walls can stop a sound)
**Run in game:** two raids, 9 Nov. raid_20261109_001706 (bots): 45 gun thumps played, 71 stopped by walls, but the game's own nearly clear gunshot (median volume 0.78) covered them; "hard to tell what was what". raid_20261109_002546 (no bots): the explosion hook works, 5 of 7 grenades played a thump, 1 stopped by walls, 1 no wall. Reach, earth and levels are still starting values.
- **New `6.2e Walls can stop a sound`** (on, live). A sound has a reach: gunshot 60 dB (`6.2f`), explosion 80 (`6.2g`), suppressed gun 20 less. Distance past 5 m (6 dB per doubling) and the full wall loss use it up. With none left there's no thump; the last 6 dB fade out. User, 9 Nov 2026: "not every gunshot and explosion should get through". They chose these numbers over a stricter set (40 / 65).
- At these numbers one concrete wall carries to full range, however thick. Two 0.3 m concrete floors stop a gunshot past 9 m and a grenade past 88 m. Three stop both. Earth stops every gunshot and a grenade past 9 m.
- `6.2d` (still 0.5) now only sets how much quieter a wall makes a thump that gets through.
- **Earth is 75 dB flat.** Before, soil, mud, gravel, grass and snow fell back to the untagged 20 dB, terrain had no tag so it read as untagged too, and forest ground read as foliage, 0 dB. Terrain's thickness can't be measured, so earth ignores thickness.
- **Thick metal is 15 dB for the thump,** not the table's 6, so a bunker door isn't as thin as a sheet.
- **Fences, grates and broken glass aren't walls.** They cost 20 dB before.
- **Thickness fix:** when one mesh holds two walls, the first took the far side of the last, up to the 1.5 m limit. Now each wall uses its nearest far side.
- **Layered walls** (hits under 0.6 m apart) count as the heaviest material among them, not the first.
- thump.csv: new `spentDb` and `reachDb`, reason `stopped by walls`, `earth` in `materials`. The summary counts stopped gun and explosion thumps.
- Known risk: where terrain runs under a building, a shot in the basement heard from the ground floor counts as earth. thump.csv will show it.
- Version 0.6.38. F12 now has 95 settings.

## 0.6.37 — 8 Nov 2026 (step 6.2: gunshots and explosions through walls)
**Run in game:** not yet, and the levels are starting values with no raid behind them. Needs bots and a fight on the other side of a wall.
- **New `6.2 Through-wall thump`** (on, live; `Fixes/ThroughWall.cs`). A bot gunshot within 60 m in another room, or an explosion within 100 m behind a wall, also plays as a low thump from its true direction. The clear sound still comes from the opening (`6.1`). Walls are measured with rays from you to the source and back: material, and thickness where a collider is hit from both sides. Loss per wall = the material's 125 Hz loss + 6 dB per doubling above 0.2 m, up to +12. At most 4 at once, ahead of echoes in the voice pool; full-auto is a 0.3 s slice at most every 0.25 s per shooter.
- Settings: `6.2a` level (0 dB), `6.2b` cutoff (50 Hz), `6.2c` explosion cutoff (80 Hz), `6.2d` wall loss strength (0.5). All live.
- Where there's no route the game's own muffled voice still plays under the thump, so a shot can't go silent. Removing it would be a small follow-up.
- **Voice budget:** echo reserve 32 (was 16), and the mod's opening sounds count toward the cap.
- Explosions are caught by sound-bank type (`Grenades`) on `BetterAudio.PlayAtPoint` and `PlayAtPointDistant`; not checked in a raid.
- New thump.csv (each decision: walls, materials, thickness, loss, level); a THROUGH-WALL THUMP line in the summary.
- Version 0.6.37.

## 0.6.36 — 8 Nov 2026 (roof rain counts low-detail floors and respects no-rain rooms)
**Run in game:** not yet. From the raid at raid_20261108_231751, tag 1 (Capital meeting room, floors above): roof rain held at 0.18, which one counted floor can't give, so about one upward path in five read a ceiling with nothing above it.
- **New `9v Roof rain counts low-detail floors`** (on, live): much of the Capital's floors are low-detail colliders on layers the main mask leaves out, so `9p` looking up from a ceiling couldn't see them. They now count when both sides are at least 4 m, so props above you don't. Unconfirmed until a raid; storeys with no collider at all still can't be seen.
- **New `9w Roof rain respects no-rain rooms`** (on, live): when the game marks you indoors and the room's rain level is 0, roof rain fades out over 1.5 s. Tag 2 (Capital, roof rain 0.90 with no floors found above) is probably storeys with no collision, which no ray can see. A trailer the game thinks is outdoors is not affected. Soft fades are possible at some room edges.
- ownshots.csv ends with `gun` (the gun's sound bank name); rain2.csv ends with `noRainGate`.
- probe2.csv ends with `floorsOldNow` (the count on the main layers alone) beside `floorsNow`. Summary: how many spots gained a floor and how many of those had read as top roof. The overlay shows both counts.
- Version 0.6.36.

## 0.6.35 — 8 Nov 2026 (footsteps and handling like EFT 1.0; routes between rooms from SEB)
**Run in game:** not yet. From a raid on Sound Engine Backport and True Acoustics (8 Nov): no blending between inside and outside and shots not as good, but footsteps and gun inspections were better, and its path work is worth having. Taken with credit: README, THIRD_PARTY_NOTICES.md.
- **New `10a Footsteps and handling like EFT 1.0`** (on, needs a game restart): the Character and Weaponary sound groups use SEB's EFT 1.0 distance curves, stereo spread, occlusion radius and 3D ear processing, kept on at any distance. Weapon handling also fades 3D processing by distance, angle and height. Not SEB's mixer, and levels stay SPT's, so this may not be all of what was heard. No new recordings. Whether the inspection sounds are in the Weaponary group is unconfirmed.
- **New `10b Sound routes between rooms from EFT 1.0`** (on, next raid): loads SEB's route file for maps that have one, after checking every room and portal is where the file expects, within 5 cm; otherwise the game's routes. The files (508 MB) are in `plugins/FFloridaMann-RealSoundSimulation/seb_routes/AudioBakeData`, copied from SEB's `acoustics/AudioBakeData`, and not in git.
- Summary "Fixes at" lines show what SPT had for those groups and which routes loaded.
- Version 0.6.35.

## 0.6.34 — 8 Nov 2026 (roof rain holds its level and fades down shafts; 9s without direction)
**Run in game:** not yet. From raid 43 (raid_20261108_221708, Ground Zero).
- **`9t` is now one level** (renamed "9t Roof rain level", 0.3, live): while it rains, the game's value for the room no longer counts. "At least 0.3" still jumped, because the game's value is 1 in some rooms and 0 in others (tag 4: 0.74 and 0.225 with the roof unchanged). The level is multiplied by the game's factor for how hard it rains. 0 = the game's value.
- **New `9u Roof rain fades down shafts`** (1 dB per metre, live): a path that bounces its way to the real roof loses level with distance after the first 6 m. Before, it lost only absorption (2% a bounce on concrete), so roof rain came down a concrete stair shaft nearly in full (tag 6, the Nakatani stairwell). A path straight up doesn't fade. Whether this is the whole stairwell problem is untested.
- **`9s` no longer ends the memory on direction.** Raid 43: 56 of 57 resets were direction, 43 standing still.
- **Few escaping paths can't raise the sound quickly** (tags 2 and 3: 2 paths were worth 13 dB). Under 8 paths a rise keeps the long average; falling stays quick.
- **Overlay:** with `9p` on, "above" shows the floors the sound uses, the surface overhead, the roof rain and how much it faded. The old 5-ray count and "roof" (whatever the last ray hit) read wrong at tags 4 and 5.
- probe2.csv ends with `floorsNow, roofLossDb`. Summary no longer counts direction resets.
- Version 0.6.34.

## 0.6.33 — 8 Nov 2026 (the memory check, less jumpy; roof rain has its own level)
**Run in game:** not yet. From raid 42 (raid_20261108_214847): `9s` fired 7 times a minute standing still and 25 moving (mostly direction), missed quiet changes, and removing the speed rule left the memory at 3 s on the move (the stairwell). The trailer's rain was silent because the game plays its indoor rain at volume 0 there.
- **`9s` retuned.** Moving caps the memory again (0.5 s at speed), and after you stop it grows back over about 1.5 s instead of jumping to 3 s. A change must also be at least 1.6× as a ratio and pass a stricter test; direction needs at least 20 paths and to differ by 90 degrees or more. While moving, listeners follow over 0.35 s.
- **New `9t Roof rain minimum level`** (0.3, live): while it rains, roof rain plays at least this loud before roof, floors and material are applied. Fixes the trailer, and lifts rooms where the game's baked level is about 0.05. 0 = only the game's value.
- Version 0.6.33.

## 0.6.32 — 8 Nov 2026 (the probe forgets when the space really changes)
**Run in game:** not yet. From raid 41 tag 4 ("slow to play"; "at times you hear audio that doesn't make sense for the space") and a Fable design review.
- **New `9s Sound reacts to real changes`** (on, live). Probe v2's memory depended on speed: 3 s below 0.5 m/s, 0.5 s above 1.5 m/s. So stopping stretched out where you had just been, and creeping through a door was the worst case. Now a 0.4 s estimate runs beside the 3 s memory. When they differ by more than chance explains (roof rain, how much sound gets out, or the direction it arrives from), the memory is replaced by the fast estimate. At most every 0.25 s. Off = memory by speed.
- For 1 s after a change, the sounds that follow the probe smooth over 0.35 s. Otherwise few escaping paths still means a long average, so deep rooms don't flutter (a change from Fable's suggestion to drop that rule).
- probe2.csv ends with `fastRoof, changes`; `memoryS` shows the time since the last change, up to 3 s. Summary counts changes by kind, and those while standing still, which should be few.
- Not built: tracing some rays from ahead of you (would help the lag at walking speed).
- Version 0.6.32.

## 0.6.31 — 8 Nov 2026 (roof rain level by roof material)
**Run in game:** not yet. From raid 41 (raid_20261108_210319): rain in a metal trailer was quiet and didn't sound like metal; `9p` and `9q` couldn't help because there are no floors above.
- **New `9r Roof rain by roof material`** (1, live): roof rain's volume follows the most common surface above you: thin metal ×2.8 (about +9 dB), thick metal ×1.6, thin wood and plastic ×1.4, fabric ×0.7, others ×1. Values set by ear, not measured. It's the same game recording, so it sounds louder on metal, not like patter. Own roof-rain sounds are logged for step E.
- Version 0.6.31.

## 0.6.30 — 8 Nov 2026 (your shot's ring follows your turning)
**Run in game:** not yet. From raid 40 tag 1: at a half-open spot, fire, then turn around, and the indoor side seems to be where you now face and the outdoor side behind you. Your gun is a stereo recording locked to your head.
- **New `3o Shot ring follows your turning`** (1, live; `Fixes/OwnShotTurn.cs`). When a shot has both recordings in it, the open side is remembered, and until the sound ends the change in your facing shifts them: sideways they pan across your ears (up to 60%), and whichever has swung behind you is low-passed down to 3.5 kHz. Firing without turning changes nothing. Scaled by how clear the open side is. No rays, no voices. Your own gun only.
- Limit: a stereo recording can't truly be placed behind you, so front and back is a cue rather than a position.
- Version 0.6.30.

## 0.6.29 — 8 Nov 2026 (roof rain counts the floors above)
**Run in game:** not yet. From raid 40: all upward rays at the roof-rain tags stayed trapped, but the look-down top-roof test still said "top roof" under floors, so roof rain read 0.9–1.0 and only the on/off 5-ray rule kept it out.
- **New `9p Roof rain counts the floors above`** (on, live) and **`9q Roof rain loss per floor`** (1). From each ceiling a path reaches, the probe looks up and counts the surfaces above it, from either side; surfaces under 1 m apart are one slab. Top roof = full; each floor above takes off its material's transmission loss (concrete about 28 dB, untagged 25 dB). A path keeps its best result, so rain coming down a stair shaft from the real roof still counts. Replaces the two-floors rule while on; 0.6.28's hold and fade only apply with it off.
- Summary: how many spots had 0, 1, 2 or 3+ floors above, and how often the old test wrongly said top roof.
- Version 0.6.29.

## 0.6.28 — 8 Nov 2026 (roof rain no longer snaps on and off)
**Run in game:** not yet. From raid 40 (raid_20261108_202707, Ground Zero, 0.6.27): "speratic roof rain sounds" in the Capital and on the escalators, with louder outdoor audio. Rain deep in the building otherwise "mostly sounds better".
- **The "two or more floors above = no roof rain" rule** (0.6.9) uses 5 upward rays. The count flickered and flipped roof rain between nothing and full 85 times in the raid, even standing still (roof level steady at 0.9–1.0 the whole time). It must now hold 2 s (was 0.5 s), and the change fades over 1.5 s. Roof rain with one floor or less above you is unchanged.
- Version 0.6.28.

## 0.6.27 — 8 Nov 2026 (probe v2 uses the measured material table)
**Run in game:** not yet. Roadmap step M.
- **New `9o Measured surfaces`** (on, live): probe v2 takes absorption, scattering and brightness per surface from `Acoustics/Materials.cs`. It feeds rain through buildings, exit credits (9k), roof rain and the gunshot room measures. Off = the earlier values.
- **Biggest changes in absorption per bounce:** steel 0.05 to 0.12, glass 0.04 to 0.14, rubber 0.20 to 0.07, cardboard 0.30 to 0.66, carpet 0.50 to 0.26, gravel 0.35 to 0.69, snow 0.60 to 0.90. Concrete, the bulk of most interiors, barely moves (0.02). Wood and metal are no longer dulled per bounce. Untagged surfaces keep 0.15.
- **Not retuned yet:** `9l` rain floor, `9k` exit credits, the roof rain reference (0.85) and its ×0.7 dulling, and the echo levels were tuned on the old values. They need raids. Metal rooms now read as less hard (more absorbent), which can lower the `5a` hard-room reverb boost there.
- **Unchanged:** what passes through a surface (glass, thin wood). That belongs to 6.2.
- Version 0.6.27.

## 0.6.26 — 8 Nov 2026 (open sky lowers the indoor gunshot mix)
**Run in game:** not yet. From raid 39 (raid_20261108_195106, Ground Zero, 0.6.25; `6g` measured surfaces: "def sounds better"). Mark 7, outside the extract with wide open space behind, firing past a pillar into the glass room: "too much indoor mix on the shot, not enough of the open space behind me". About 42% of the sky was open, but the room measure (0.45–0.59) took no account of it, and the shot played 82–89% indoor.
- **New `3n Open sky lowers the indoor mix`** (1, live): in fix 3c's Enclosure mode with probe v2, the room part of the weight is multiplied by (1 − open sky × 3n). Simulated on raid 39's 97 shots: those three go to about 64%, 49% and 40%; the 52 shots with almost no open sky don't change; only shots with 20% or more open sky do.
- Version 0.6.26.

## 0.6.25 — 8 Nov 2026 (echoes use measured surfaces)
**Run in game:** not yet. Roadmap step M, from [MATERIALS.md](MATERIALS.md).
- **New `Acoustics/Materials.cs`:** one table of what each game surface does to sound, from Meta XR's own presets: absorption in four bands, scattering, transmission. Probe v2's values and the old echo values moved into it unchanged.
- **New `6g Measured surfaces`** (on, live). Fix 6 echoes take their level from the table's two middle bands. Wood, glass and metal are no longer dulled; a low-pass remains only where it's real (carpet, grass, and mildly cardboard, gravel, snow). Examples per bounce: concrete −0.5 to −0.1 dB, thin wood −3 to −0.5 dB, rubber −8 to −0.3 dB, grass −4 to −5.9 dB, snow −4 to −10.5 dB. Off = the earlier hand-set values.
- **Not changed yet:** probe v2 (rain, exit credits, roof rain) keeps its values until its retune, which needs raids. Scattering and transmission are in the table but unused.
- Version 0.6.25.

## 0.6.24 — 8 Nov 2026 (6.1: bot gunshots come from the opening)
**Run in game:** not yet. ROADMAP 6.1. Raid 36's routes.csv: 156 of 171 bot shots had a route, half the openings over 33 degrees from the true direction, 40% over 45, the worst 150; the best opening changed only 7 times.
- **New `6.1 Bot gunshots from the opening`** (on, live) and **`6.1a Opening strength`** (1) (`Fixes/PortalPositioning.cs`). A bot's gun voices are placed along the direction of the opening nearest you, at the real length of the route (solutions doc 2.1). The game already picks the route; only its cost was used.
- **Stabilised:** a route changes only if another is better by more than 0.05; blended in by the propagation effect (full at 0.3), so a shot just around a door frame doesn't jump; direction and distance smoothed over 150 ms; faded out within 1 m of the opening. Routes re-read every 0.1 s per firing gun.
- No route (same room, both outdoors, none baked, such as raid 36's shots from a floor below) keeps the true position; those wait for 6.2's through-wall path. Gunshots only for now.
- `Acoustics/PortalRoutes.cs`: one route reader shared by routes.csv and the move. routes.csv ends with `movedDeg`. Summary: PORTAL POSITIONING. COST has its own line.
- Version 0.6.24.

## 0.6.23 — 8 Nov 2026 (shots ring under the next one)
**Run in game:** not yet. From raid 36 (raid_20261108_165557, Factory): 89 of 224 bot sounds were cut by the same gun's next sound, on average 1.85 s lost; 0 muted, 0 without voices. Same mechanism as the STM-9 on the FRT sounding like one looping shot. The old Gun Audio mod's "Let shots overlap" fixed it and left with that mod on 5 Nov.
- **New `1e Shots ring under the next`** (on, live; `Fixes/ShotOverlap.cs`), every gun. `SuperBetterAudioQueue.Update` with one change: only a firing loop is ended when the next sound starts. Gun Audio's random pitch and volume are not included. A gun still has two voices, so a third sound takes back the oldest. A non-auto gun's voices are kept long enough to cover the sound's flight time.
- **No silent shots** (user idea): if the game has no free gun voices, the gun closest to finishing (not firing, never yours) gives up its two, so a fading tail is cut instead of a shot not playing. Only runs when the pool is empty. Summary: BOT GUNSHOTS.
- botshots.csv: with `1e` on, `cutPrevS` means a third sound took back the oldest voice.
- Self-check: 9 new targets. Version 0.6.23.

## 0.6.22 — 8 Nov 2026 (portal positioning, step one: log the routes)
**Run in game:** not yet. Changes no sound. ROADMAP 6.1's first build, measure only (user: "build 6.1"). The sound itself moves in a later build, once a raid has shown the routes are sensible.
- **New `Log portal routes`** (F12 section 1, off, live) and **routes.csv** (`Diagnostics/RouteLog.cs`). The game already works out the best route for every bot in another room and only uses its cost. Each time a bot fires (at most 4 times a second per gun) this reads that finished result: the opening nearest you, the route's path length, the cost of the best and second-best routes, how many portals it goes through, and how far the opening is from the true direction. A shot with no route says why (`outdoor`, `sameRoom`, `outOfRange`, `noRoute`, `notComputed`, `busy`). Summary: PORTAL ROUTES (average angle between opening and true direction, share over 45 degrees, path against the straight line, how often the best opening changed). Picks the best route by the game's own rule.
- Self-check: 7 new targets. Version 0.6.22.

## 0.6.21 — 8 Nov 2026 (bot gunshots: what cuts them off; first burst starts muffled)
**Run in game:** not yet. From raid 34 (raid_20261108_154730, Factory, 0.6.20): "the ai fight around mark 1 sounded like a lot of shots were getting cut off". The logs could not say why, so this build measures it, and fixes the one cause they did show. Raid 34 also answered 0.6.20's questions: the game exposes no audio-load counter (only 2 audio memory counters, both 0), and probe v2's ray batch never makes a frame wait (average 0.001 ms, 0% of frames), so 6.0b needs no fix.
- **New botshots.csv** (`Diagnostics/BotShots.cs`): one row per sound a bot's gun queued or lost. From the game's code a remote shot can be lost or shortened three ways, each counted: **cut** (a gun has two voices that alternate, and the next sound ends the older one at its exact start with no fade, so a semi-auto gun's next shot chops the last one's decay; `cutPrevS`, `cutPrevShare`), **muted** (`FireBullet` plays nothing if the voice's occlusion volume is already 0), **noqueue** (no free voices for the gun). Summary: BOT GUNSHOTS. Clock time in every row, to match FrameDiag.
- **New `1d First burst muffled`** (on; `Fixes/FirstBurstOcclusion.cs`). A quiet bot beyond its own hearing range (~80 m, set by its footsteps) has no occlusion computed, so the value stays 0. Firing raises the range to 600 m and the calculation starts, but it takes several frames and is smoothed, so its first burst played un-muffled on the open gun mixer and was then cut down (raid 34, container 9: 0, 0.68, 0.997). Fix 1's "fresh shot" rule read that 0 too, and its hold kept the open mixer for up to another 0.5 s. Now, when a gun's voices are registered and that shooter's occlusion was never computed: one line-of-sight test; if blocked, the smoothed value starts at "blocked" (propagation if the shooter is in another room, obstruction if in yours) and the game's own result takes over. Clear line of sight: no change. Later bursts keep the last computed value, so this only touches a bot's first burst.
- **shots.csv `t` is now raid seconds** like every other log (it was the game's clock, so raid 34's shots seemed to start at 380 s); `clock` added at the end.
- Self-check: 6 new targets. Version 0.6.21.

## 0.6.20 — 8 Nov 2026 (measure before step 6: audio thread, Unity's audio counters, ray batch wait, shared patches)
**Run in game:** not yet. Changes no sound. ROADMAP 6.0a and 6.0b: the measurements step 6 is sized from. Part comes from the `claude/selfcheck-foreign-patches-block-timing` branch (built there as "0.6.19"; renumbered because main's 0.6.19 came first).
- **From the branch: patches.txt** at raid start (in the raid folder, and the latest copy next to selfcheck.txt): every plugin BepInEx loaded (GUID, name, version); whether Sound Engine Backport's client is present; the mixer as it is now (Master's name, the snapshots in order, the gunshot mixer groups, the spatializer); and every method that another mod patches among the methods this plugin patches (SHARED) or only plans to patch (FOREIGN, with the self-check fix number), with each patch's kind, priority, owner and method. Done at raid start, not at launch, because other plugins install their patches after our Awake. Summary: SHARED PATCHES line. `Diagnostics/SharedPatches.cs`; `PatchSelfCheck.MethodTargets()`.
- **From the branch: `Time the audio thread`** (on): a pass-through filter on the AudioListener's object for the raid (`Diagnostics/AudioThreadProbe.cs`, allocation-free, touches no samples) times the gap between DSP blocks. A block (dspBuffer samples, ~21 ms at 48 kHz) that comes late means the whole mix took longer than a block or the thread was held off the CPU: the condition behind crackle. timing.csv gains `audioBlocks, audioBlockMs, audioGapAvgMs, audioGapMaxMs, audioLate15 (gaps > 1.5x block), audioLate2x, audioStalls (> 100 ms: alt-tab, loading)`; the overlay shows the window; the summary's AUDIO THREAD section gives totals and a verdict (healthy / marginal / LATE). Re-attached if the listener object changes mid-raid.
- **New: Unity's own audio counters** (`Diagnostics/AudioCounters.cs`, under the same setting). The gap timing only sees blocks that came late, not how close the others came to their deadline. Unity's audio profiler has a DSP load and voice counts, but a release game only exposes some counters and which ones is not documented, so nothing is guessed: at raid start every counter the runtime offers is listed in **audio-counters.txt** (raid folder + latest copy next to selfcheck.txt; all audio ones, plus a count per category), and every audio counter (up to 24) is recorded: sampled each frame (one read each, no allocation, also during the spawn back-off), window avg/max in **audiocounters.csv** every 5 s, raid avg/max in the summary's UNITY AUDIO COUNTERS section. A counter named DSP or CPU in percent is read as the share of the audio deadline used: frames over 50% and 80% (its 0–1 or 0–100 scale chosen from the raid max and printed). If the game offers no audio counters, the file says so and only the late-block count remains. If it offers voice counts, they also give the voice peaks 6.3 needs without the 56 ms source scan.
- **New: probe v2's ray batch wait** (`Diagnostics/BatchWait.cs`). How long the main thread blocks in LateUpdate waiting for the worker threads to finish the ray batch, and the per-shot muzzle measure's waits (64 rays × 3 legs, completed at once). timing.csv ends with `batchWaitAvgMs, batchWaitMaxMs, batchWaitedFrames`; new **slowframes.csv**: one row per frame over 25 ms or with a wait of 1 ms or more (`t, clock, frameMs, batchWaitMs, muzzleWaitMs, rays, aliveBots`; each row describes the previous frame, and `clock` matches FrameDiag's frames.log), written during the spawn back-off too; summary: PROBE V2 BATCH WAIT; overlay line. Test: a heavy Streets raid, sort slowframes.csv by frameMs. If the wait is near zero on the bad frames, neither 6.0b fix is needed.
- Version 0.6.20.

## 0.6.19 — 8 Nov 2026 (diagnostics: no garbage from container rows, back off during spawn waves)
**Run in game:** not yet. Changes no sound (one side effect below). Aimed at raid 33's frame spikes: 869 and 940 ms frames with no gunfire nearby, the heap growing 75–100 MB each, and 100–150 ms frames in windows with 6–9 new sound containers. The spawn work itself is the game's and SAIN's; this removes what the plugin's own logging adds to those frames. Taken from the `claude/gc-and-spawn-stutters` branch (built there as part of "0.6.20"); that branch's GC timing experiment stays there, because GC and spawn stutters are a separate project and will likely be separate mods (user, 8 Nov).
- **containers.csv rows without garbage.** Up to ~300 rows/s in a big fight, each built from ~25 strings. New `DiagLog.Begin/AppendI/AppendF/AppendB/AppendS/EndRow`: digits appended by hand (Mono's StringBuilder.Append(int) allocates), one reused builder and char buffer. Same columns; the 0–1 scores now have three fixed decimals (`0.500` instead of `0.5`).
- **`Diagnostics back off after a spawn s`** (F12 section 1, default 2, 0 = off): for this long after the alive-bot count changes, the diagnostics skip their per-frame work (container rows, overlay, ambience and probe rows, timing row, source scan, log saving). spawns.csv is still written. Summary counts the frames skipped. Side effect: the echo headroom cap keeps its last count of playing game sources until the back-off ends (raid 32: the cap never engaged).
- Version 0.6.19. (The unmerged `claude/selfcheck-foreign-patches-block-timing` branch also calls itself 0.6.19; it takes the next number if it is merged.)

## 0.6.18 — 7 Nov 2026 (diagnostics: alive bots and spawn waves)
**Run in game:** not yet. Purpose: line FrameDiag's slow frames up with spawn waves. Raid 33: the two biggest frames (869 ms at 134.6 s, 940 ms at 190.7 s) had no gunfire within 5 s but grew the heap 75–100 MB, and several 100–150 ms frames sat in windows with 6–9 new sound containers.
- **timing.csv** ends with `aliveBots` (everyone alive in GameWorld.AllAlivePlayersList except you).
- **New spawns.csv:** a row whenever that count changes: `t` (raid seconds), `clock` (wall clock HH:mm:ss.fff, the same clock as FrameDiag's frames.log), `aliveBots`, `delta` (+ spawned / − died). Checked every frame.
- **Summary:** alive bots max and how many spawned during the raid.

## 0.6.17 — 7 Nov 2026 (allocation counter that works under Unity's Mono)
**Run in game:** not yet. Raid 32 (raid_20261107_160950, 0.6.16): plugin 12.1 ms/s, the exposure sweep skipped every frame, probe v2 8.7 ms/s, echo voices stolen 0, the headroom cap never engaged; FrameDiag blames Real Sound Simulation only inside the raid's loading freeze (first 2 s).
- **COST's `alloc KB/s` read 0.0 everywhere:** under Unity's Mono `GC.GetAllocatedBytesForCurrentThread` returns 0 without failing. Now at startup each counter is tried with a deliberate 64 KB allocation, and the first that sees it is used: the per-thread counter, else `GC.GetTotalMemory(false)` deltas, else `Profiler.GetMonoUsedSizeLong` deltas. The two heap counters are whole-heap and approximate (other threads count; a part during which a collection ran is dropped). The COST total line names the counter and what the test read; the BepInEx log too.
- Not done (optional in the request): spreading the raid-start setup over several frames. Its spikes all fall inside the game's own loading freeze.

## 0.6.16 — 7 Nov 2026 (exposure sweep skipped under fix 9, echo headroom cap, probe batch within the frame)
**Run in game:** not yet. From the 0.6.15 review (user: "Yes to the skip").
- **Exposure probe:** while fix 9 runs, its 48-direction sweep is skipped (6–7 ms per second and the odd 50 ms frame, read only by exposure.csv and the marks columns). The 5 up-rays still run every 0.25 s for fix 9's roof rain cut under two floors (now gated on their own flag). The sweep resumes by itself when fix 9 is off. Summary counts the skipped frames. Longer term the floors count can come from probe v2's roof test and the exposure probe can go.
- **Echo headroom cap:** echo voices busy at once ≤ clamp(Unity's real voices − game sources playing in sound containers (0.5 s count) − 16, 16, 96). At the cap a new echo is dropped rather than stealing; a shot's echoes are offered loudest first, so the quietest drop. The outdoor tail goes through it too (logged "headroom cap"). Summary: dropped by the cap, and the cap at raid end. Quiet raids never reach it; in a 157-source fight it holds echoes to ~23.
- **Probe v2's batch is finished in LateUpdate of the same frame** (0.6.15 finished it in the next frame's Update, so it spanned FixedUpdate, where physics moves colliders - not documented as safe for raycast jobs). Still overlaps the rays with the frame's Update work.

## 0.6.15 — 7 Nov 2026 (raid 31 performance)
**Run in game:** not yet. From the raid 31 performance review (FFloridaMann-FrameDiag/frames.log and summary.txt). Not done: gating the exposure probe off while fix 9 runs (user: leave it on), and the echo "headroom cap" (not defined; to discuss). The flashlight flicker was another mod (removed); NvgFlash's thermal update (114 ms per hit) is its own project.
- **`Write logs` is live:** switched off in raid, no more log rows are built or written; settings.txt and summary.txt are still written. Switched on, logging starts at the next raid.
- **Source scan off by default:** `Source scan interval s` 0 = off, default 0 (range 0–60). FrameDiag put DiagRunner.Update behind 83 frames of ~56 ms: this FindObjectsOfType scan, the plugin's only hitch. The playing-sources count at every container sample (0.6.14) stays. **Your config file was changed to 0** (it held 15; backup in `G:\SPT\_backups`).
- **containers.csv** at most every 1 s while there are more than 50 containers (raid 31: 167 × 2/s).
- **Allocations measured:** COST has an `alloc KB/s` column per part and a total (bytes allocated on the main thread during each part, `GC.GetAllocatedBytesForCurrentThread`; "n/a" if the runtime lacks it). Raid 31's heap grew 2.0 → 3.1 GB with 67 GCs; this shows our share.
- **Echo voices priority 220** (was 40): when Unity runs short of real voices, game sounds keep theirs and ours go virtual first.
- **Probe v2's ray batch finishes on the next frame:** scheduled and left running on the worker threads; completed and processed at the start of the next frame, instead of the main thread waiting for it. The muzzle measure, the raid reset and switching the probe off finish or drop it first (they share its buffers). The probe's results are one frame later than before.

## 0.6.14 — 7 Nov 2026 (raid 30 fixes + the queued raid 29 items)
**Run in game:** not yet. From the build request after raid 30 (raid_20261107_135517, 0.6.13): tag 1 ambience good; gunfire inside → outside better (the tail played at the window with the mix at 0.81–0.94 indoor, so 3m's gate stays 0.5). Problems: tag 2 rain too loud deep inside, the garage-mouth gunshot mix flipping abruptly, stairwell rain slow to start and stop, the echo pool saturated.
- **Probe v2: untagged surfaces absorb more.** Material None (e.g. `Capital_corridor_1F_COLLIDER`) 0.06 → 0.15 absorption, scattering 0.3 → 0.4, brightness 10 → 8 kHz. Tag 2: 185 of 192 paths trapped, the one escape kept 84% after 8 bounces, level −15 dB with 90% build-up. Tagged materials unchanged (materials overhaul).
- **New `9n Deep bounce dulling`** (0.8; 1 = off): past the 3rd reflection each further bounce multiplies the build-up's brightness by this (tag 2 read 10–14 kHz deep inside).
- **Movement-gated memory:** the probe's running sums (level, build-up direction, roof) remember 3 s while you move under 0.5 m/s and 0.5 s from 1.5 m/s. The openings memory is unchanged. Stairwell: escapes went to 0 but the level took 3 s to fall. probe2.csv `memoryS`, `speedMs`.
- **Zero escapes = least confident:** relErr 1, so the longest smoothing (2.5 s); raid 30 still showed 0.35 s with 0 escapes.
- **Roof rain brightness** from the same upward paths as its level (raid 29 tag 3: 4.4 kHz right under the roof).
- **Few escapes → all round:** the outdoor sound's focus × escapes / (escapes + 5); the `9l` floor's share is always all round (raid 29 tag 7: front-left with 0 escapes).
- **`3j` default 1 → 0.5** (garage mouth: a measured change of 0.13 became 0.50). **Your config file was changed to 0.5 too** (it held the old default 1).
- **3m tail distance loss:** × 2 / max(d, 2) with d = you to the opening (fix 6's voices don't fade with distance). Gate stays 0.5. Summary counts tails from the 3 m fallback (raid 30: 25 of 74).
- **Echo pool 96** (was 48; raid 30: 48/48, 57 cuts, no bots).
- **One-round loops:** a full-auto loop's echoes are held back until the loop has run 0.2 s. One round (next sample sooner): dropped, the tail's echoes cover it. Longer: started 0.2 s late from the matching point in the loop recording. CloseLoops also frees at once any loop voice closed within 0.2 s of its start. Summary counts both.
- **Summary:** playing sources in the sound containers, counted at every container sample (0.5 s), beside the 15 s full scan and our echo voices; the indoor-layer mixer volumes and room tones heard are kept until the next raid starts (they were cleared before the summary was written).

## 0.6.13 — 7 Nov 2026 (9k: pooled sources let go when reused for a near or on-map sound)
**Run in game:** not yet. From the 0.6.12 review (hook verified: all four `PlayAtPoint` overloads return a `BetterSource` with a `position` parameter; `PickSingleClip(0)` = outdoor).
- **Pooled-source reuse.** A source 9k manages stays managed until it has been silent for 0.5 s. If the pool hands it to a near or on-map sound in that window, 0.6.12 kept it: `PlayPostfix` returned early, and the per-frame step saw the moved source and adopted the new sound as distant, moving it. Now both places run the same test (80 m+ and off the room map): `PlayPostfix` lets go of a managed source whose new sound fails it, and the per-frame step does the same when a managed source turns up somewhere else. A reused source that passes re-reads the game's factor (the game resets occlusion per sound). Summary: "N pooled sources let go for a near or on-map sound".
- Outdoor tail (3m) still unchanged until it has been raided; the 0.6.12 queue stands.

## 0.6.12 — 7 Nov 2026 (9k: distant sounds caught as they start)
**Run in game:** not yet. User: "fix 3 now" (the review's third item); the outdoor tail (3m) is left as 0.6.11 until it has been raided.
- **9k onset leak fixed.** Raid 29: 33 distant.csv rows indoors with 9k's gain under 0.3 but the sound playing above 0.3 - each distant shot began loud, from its true direction, because 9k only took a sound once fix 4's occlusion pass reached its container a few frames later, and the game then eased the volume down from full. Now a postfix on both `BetterAudio.PlayAtPoint` overloads takes a sound started 80 m+ away at a point the room map doesn't know (off the map, the same test as fix 4) in the frame it starts: position and gain are set at once and the volume the game just set is scaled by the gain. On-map sounds (a real grenade 90 m away) are not touched. Summary: "N caught as they started".
- **Queued for after the raid (user):** outdoor tail distance loss (−20·log10(max(d, 2) / 2)) and its gate 0.5 → 0.25 (only after the raid shows 3m's behaviour); roof rain brightness from upward paths; at tag 7 the outdoor direction fading to all-round when no paths escape, and the 9l floor all-round; the summary lines cleared before they're written.

## 0.6.11 — 7 Nov 2026 (inside shot, outside tail: forward-lobe muzzle measure and the outdoor tail from the opening)
**Run in game:** not yet. From a build request for raid 29 tag 1 (barrel out of a window, the whole shot sounded indoor; user: "inside shot, outside tail"). ownshots.csv at 122–130 s: muzzle 0.49 m from the head, v2 muzzle room 0.72–0.98, aim closure 0.11–0.35, indoor weight 0.61–0.66, after 3j 0.80–0.85.
- **New `3k Muzzle lobe`** (1; 0 = the sphere as before): the muzzle's room measure (probe v2's 64 directions, and the 32-ray one when 9a is off) weights each direction by ((1 + cos to the aim) / 2)^k, room, roof and size alike. Out of a window the muzzle reads mostly outdoor although half its sphere looks back into the room; firing into a room from a doorway reads more indoor. The head measure stays a sphere. ownshots.csv adds `muzzleRoomSphere` and `v2MuzzleRoomSphere`; the summary has MUZZLE LOBE (mean sphere minus lobe).
- **New `3m Outdoor tail from the opening`** (on) and **`3l` trim** (0 dB): when 3c mixes the shot mostly indoor (> 0.5 after 3j) and the aim cone is open, the gun's outdoor Tail recording also plays from the opening the shot goes out of: the aim cone's first ray (forward first) that leaves cover before its wall; through glass at the pane (9d loss, 1.5 kHz), bars at the grate (−1 dB, 12 kHz), else the exit point; 3 m along the aim if none is found. Delay = distance / 343 m/s; level = shot × open share × indoor weight × 3l. Additive: 3c's two body slots are unchanged. On fix 6's voices (needs 6 on), one per trigger pull, not counted in 6b.
- **Differs from the request:** it also plays on the tail at the end of a burst, not only on "shot" samples. A gun that can fire full-auto plays every trigger pull (single shots too) as loop + tail, never as a "shot"; tag 1 was an AK-74, so a shot-only tail would never have played there. Never on the loop.
- **ownshots.csv** rows are written when the shot call finishes (same rows as before), so they can carry `tailPlayed, tailLevelDb, tailDelayMs, tailAz, tailThrough` (or why it didn't play). Summary: OUTDOOR TAIL FROM THE OPENING (played, mean level and delay, reasons not played).
- **Cost:** the opening search runs at most 5 times a second (up to ~90 short casts).

## 0.6.10 — 7 Nov 2026 (roof rain: its own directional mode, muffled by its paths)
**Run in game:** not yet. User: "Yes, build it" (after the panner review: keep the panner for the beds; try HRTF only where up/down matters).
- **New `9m Roof rain directional mode`** (UnityPanner): the roof rain's own mode; `9c` now sets only the outdoor sounds (its key is unchanged, so your value stays). Try `9m` = MetaHrtf with `9c` on the panner.
- **Roof rain muffled by its paths:** the probe records how bright each roof path is when it reaches the roof (the dullest surface it passed or reflected off, x0.7 per reflection) and the roof rain gets that as a low-pass, on a filter the plugin adds only where the game has none. Straight up stays 22 kHz; down a stair shaft after three bounces ~5 kHz. Until now roof rain had no muffling at all, only level, so rain down a shaft sounded like rain overhead, just quieter. The x0.7 per bounce is a placeholder for the materials overhaul.
- **Logs:** probe2.csv `roofCutoffHz`; rain2.csv `lowpassHz`; the summary shows each group's mode.
- **Before the next raid (user's side):** headset surround / "Superhuman Hearing" and Windows spatial sound (Sonic, Atmos) off, so only the plugin spatialises.

## 0.6.9 — 7 Nov 2026 (raid 28 fixes: doubled gunshots, roof rain, stairwell wind and gunfire, rain floor)
**Run in game:** not yet. The exposure probe stays on (user). Two reviews of raid 28's logs agreed on the causes; the user said "build" for all but turning the exposure probe off.
- **Echo voices (fix 6):** pool 48 (was 24); when full, the voice that ends soonest is reused, not the one that started first. Raid 28: every sample was a full-auto loop plus a tail, the pool was full, and 274 echoes were cut off mid-sound (256 by tail echoes) - the likeliest cause of the doubled/stuttering shot.
- **New `6f Early echo trim dB`** (0 = no change): added to echoes under 25 ms. 414 of 1,216 echoes arrived that early, some at −4.5 dB 9 ms after the shot. Judge by ear after the voice fix.
- **New `3j Blend steepness`** (1): fix 3c leaves an even indoor/outdoor mix faster (0.6 → 0.79, 0.4 → 0.21); same crossover. 132 of 202 samples were between 0.25 and 0.75. ownshots.csv gains `weightUsed`.
- **Roof rain (9i):** the roof level is now per upward path (first leg > 30° up). Under a roof right overhead in a room with big openings it read 0.42 ("half a roof") because most paths escaped sideways first; the roof radiates the same either way. probe2.csv keeps the old measure as `roofLevelAll`.
- **Roof rain under two floors:** 0 when the exposure probe's up-rays count two or more floors above (held 0.5 s), whatever the roof test says (tag 4: 0.68 under two floors). One floor above stays the probe's call (raid 26's mezzanine edge).
- **Roof rain and the indoor fade:** the plugin reads which mixer volumes the game's indoor-layer fade moves; if the rain's is one, roof rain is compensated so openings around you don't turn the roof down (tags 2–3: 0.65 × 0.53–0.68). Summary: "indoor-layer mixer volumes"; rain2.csv `fadeComp`.
- **Rain crossfades (fix 7):** a crossfade to the same clip is a volume ramp instead (tag 2: six room changes in 3 s each crossfaded the clip into a second copy of itself, dipping to 0.07). Correction to the raid 28 review: fix 7 didn't cause these, the game did. Summary counts them.
- **New `8g Indoor wind follows openings`** (on): room tones whose clip name contains "wind" follow how much of the outside reaches you (full above −10 dB, down to −40 dB) instead of how enclosed you are (last tag: wind four floors down a stairwell). New roomtone.csv logs every room tone and its clip, and the summary lists them, so the "wind" match can be checked.
- **New `9l Rain through the building dB`** (−32; −60 = off): deep inside, the outdoor sound never drops below this, muffled to ~350 Hz, faded in by how enclosed you are (tags 1 and 4: "not enough of the interior"; a building in rain is never silent).
- **9k:** every escaping path counts - side openings by how they face the battle (as before), openings to the sky above at 0.35 from any direction, side openings facing away at 0.05 (last tag: the only way out was up the stair shaft, so it found nothing and the direction jumped). distant.csv gains `heardEl, outVolume, occlFactor, mixer, spatialBlend, spatialize, reverbZoneMix, playing`, to find what carried the gunfire heard at −45 dB.
- **probe2.csv `levelDb`** floors at −60 dB like the applied level (was −40).
- **Self-check:** 8 new targets.
- **Materials overhaul (user, 7 Nov):** sound propagation will change with realistic materials. The new constants (roof reference, 9k's 0.35/0.05, the rain floor) are geometry or settings, not per material, and should be retuned after it.

## 0.6.8 — 7 Nov 2026 (exit points, rain from openings, distant gunfire through openings; Meta leak fixed)
**Run in game:** not yet (installed 11:51). User: "build .6.8 and give me a toggle for Exit points and the ambient gunfire mod through openings".
- **Exit points:** for escaping paths while you're inside (~200 a second), where the escaping leg leaves cover (roof tests along it, refined): the window or door it went out of.
- **`9j Rain from openings`** (on): indoors, the outdoor sound's direction points at the exit points instead of where the sound first reaches you.
- **`9k Distant gunfire through openings`** (on, needs fix 4): when you're inside, the ambient gunfire mod's distant battles (off-map, ≥ 80 m) are moved to come through the openings that face them, at the level that gets in (√ of the probe's transmission from that direction), blended with how enclosed you are. Replaces fix 4's doorway muffling for them. distant.csv.
- **Meta HRTF leak fixed:** in `9c` MetaHrtf mode, Meta's own reflections and reverb send are off on the moved sources (raid 27 tag 1: rain heard where the level was 0).
- **Level floor −60 dB** (was −40).
- **rain2.csv:** the game's indoor rain player every 0.25 s, to find the car dealer roof rain on/off (raid 27 tags 2–5).
- 4 new self-check targets (112).

## 0.6.7 — 7 Nov 2026 (direction from where the sound arrives; roof rain traced from the roof)
**Run in game:** raid 27 (11:25, rain). Meta HRTF leaks rain where the level is 0 (its own reverb send); car dealer roof rain on/off by tag with our factor steady; stairwell faint rain from the build-up. Fix 9 only.
- **Raid 26 (0.6.6):** "overall an improvement", garage good, deep rooms much quieter. Still: rain a little "through the walls" deep inside (wind too), "maybe it was better when sounds from openings slid to 100%"; car dealer roof rain stopping or getting quieter suddenly; overlay "focus near" jumping 0–0.5.
- **Build-up direction from where the sound arrives:** the first leg of every escaping path (deep ones ×2), energy-weighted over ~3 s, instead of where it left the building (0.6.5: mostly floor spots 40–75° below you, from 3–8 escapes, so it wandered).
- **New `9h Direction strength`** (1 = as measured, up to 3): how pointed the outdoor sound and roof rain are indoors.
- **Roof rain traced from the roof (`9i`, on):** the top roof's underside is a sound source; paths that reach it give the level (full right under it, partly under a mezzanine, down the stairwell from the floor below) and the direction. The game's indoor rain player is made directional like the outdoor beds. User: "if there is roof rain on the floor above you would hear that coming down the staircase". Replaces the one-ray floor count that flipped 30% → 100% → 30% at the car dealer's mezzanine edge.
- Overlay: the focus actually applied (was the raw near-part value, which jumped) and the roof rain level. Logs: probe2.csv `focusExit/exitAz/exitEl/exitCount` renamed `focusBuildUp/buildUpAz/buildUpEl/buildUpCount`, new `roofLevel, roofAz, roofEl, roofFocus, roofCount`.
- `9c` stays UnityPanner (user, raid 26: MetaHrtf sounds worse in the Nakatani hallway).

## 0.6.6 — 7 Nov 2026 (probe v2 ray quality setting)
**Run in game:** raid 26 (10:52, rain, `9g` High). "Overall an improvement" → 0.6.7. At the default (Medium) it is exactly 0.6.5.
- **New `9g Ray quality`** (user idea): Low / Medium / High / Ultra = 320 / 640 / 1280 / 2560 paths a second, averaged over 1 / 2 / 4 / 8 sweeps, so the window stays ~0.6 s and more paths really mean less noise (Ultra: half the noise of Medium at ~4× the cost, about 0.6 ms per frame at 107 fps). Changes live. DESIGN.md §7.
- Room for up to 1024 paths in flight (was 256) and a per-frame start cap that grows with the rate, so Ultra isn't throttled at 60 fps.
- Summary and settings line show the quality; COST line renamed "acoustic probe v2 (batched, guided, rays by 9g)".

## 0.6.5 — 7 Nov 2026 (probe v2: leak through floors fixed, direction by what you hear)
**Run in game:** not yet (installed 10:46 as part of 0.6.6). Affects fix 9 (ambience) and, slightly, 9a.
- **Raid 25 (0.6.4):** the measured build-up was 3–5× lower than the old formula (−18 dB at mark 1, was −14). But deep in the Capital the rain came from both sides and from behind. No path really got out there: the only "escapes" fell through a hole in the ground floor to the map's catch plane 48 m below and came back up through the building (raycasts don't hit the back of one-sided floors). With focus ≈ 1, one or two of them set the direction alone.
- **Leak fix:** every cast hits back faces too, and a back face reflects like a front face. The far side of a pane or object a path has just passed through is skipped (no second loss). Every escape gets the roof test, not only level and downward legs.
- **Direction by what you hear:** the direct/bounced part counts by its share of the level and only firmly when several paths agree; the build-up gets its own direction, towards where escaping paths left the building over ~3 s, never fully focused. Deep inside: wide, leaning towards the openings.
- Logs: probe2.csv `focusEarly, focusExit, exitAz, exitEl, exitCount`; paths.csv `exitAz, exitEl`; summary counts back faces hit, far sides skipped, upward escapes rejected. Overlay: focus near / exits.
- Expect: deep rooms quieter than 0.6.4 (part of their build-up came from the fake escapes), the rain direction steadier and wider.

## 0.6.4 — 7 Nov 2026 (probe v2: measured build-up, guided rays, smoothing by confidence)
**Run in game:** raid 25 (09:42, rain, AI on). Build-up 3–5× lower; direction wrong deep inside (fake escapes through the floor) → 0.6.5. Affects fix 9 (ambience) and, slightly, 9a (gunshot room readings).
- **Raid 24 (0.6.2):** sound from inside "improved"; outdoor ambience still too loud far into the building. At mark 1 (Capital) the room build-up was 88% of the level. Lowering `9e` helped, but what was left came from 2–5 paths and jumped in and out.
- **Build-up measured, not assumed.** Half the paths keep bouncing after their 3rd reflection, up to 8, to measure where the remaining energy goes in this building (in energy: an escape through closed glass now counts at −22 dB, not like an open door). Only the energy still bouncing after 8 uses the room formula. Smoothed over ~3 s. 0.6.3's value is logged as `tailOld` for comparison.
- **Guided rays (`9f`, on):** 64 of every 192 paths aim at the openings already found (windows, doors), each counted as p_uniform / p_mixture so totals stay right. Openings are remembered (fade over ~2 s unless found again) and re-checked every sweep. The user's "weighted rays" idea, and "fixed rays" done as rays fixed on real openings.
- **Smoothing by confidence:** fix 9 averages the level for 0.35 s when many paths get out, up to 2.5 s when few do; the direction over at least 1 s.
- Ray view: 192 paths, guided ones fainter, colour = where the path finally ended. probe2.csv and paths.csv: new columns (DIAGNOSTICS.md). Overlay: paths out, openings, error, smoothing time.
- Cost: more rays deep inside (~4,000/s expected, was ~2,200). Next build: a ray quality setting.

## 0.6.3 — 7 Nov 2026 (setting 0: gun sounds from SPT or SEB)
**Run in game:** not yet. With the default (`0 Gun sounds` = SPT) nothing sounds different from 0.6.2.
- **New setting 0, "Gun sounds"**, at the top of F12 (section "0. Gun sounds"): SPT (default) or SEB, Sound Engine Backport's EFT 1.0 recordings, without SEB's engine. Switches at the next trigger pull, never mid-burst. FIXES.md "Setting 0".
- **Why:** compared on 7 Nov, SEB's gun banks hold the same recordings as SPT's plus 6 to 48 single-shot takes per sound (AK-74 295 single-shot clips against SPT's 9, M4A1 306 against 27). The SPT recordings they share are the same audio. An AKM single shot measured within 0.5 dB per band of SPT's loop-start + tail; the gain is variety, not quality.
- **`0a SEB on full-auto guns`** (off): the game loops one Body recording while the trigger is held on any gun that can fire full-auto. SEB's are single shots, so those guns stay SPT until playback is patched.
- **New tool `tools/build_seb_sounds.py`:** copies SEB's 39 gun bank bundles and the 28 they reference with new internal names (Unity can't load two bundles with the same internal name), into `BepInEx/plugins/FFloridaMann-RealSoundSimulation/seb_sounds/` with `index.txt`. Not in git.
- **Logs:** new `gunsounds.csv`; a GUN SOUNDS line in the summary; setting 0 in settings.txt; 11 new self-check targets.
- SEB's server mod is no longer installed (moved to `G:\SPT\_removed_mods`), so the game loads SPT's own banks and the switch decides.

## 0.6.2 — 6 Nov 2026 (bug fix: rays leaked through solid low-detail shapes)
**Run in game:** not yet (installed 22:16).
- **Solid objects on low-detail layers now block completely.** 0.6.0 gave them a small leak, done by chance: 5% of rays passed **at full strength**. The Capital has a whole-floor low-detail collider (`Capital_Indoor_1F_COLLIDER`, no material tag) in front of its real walls. In raid 23 at mark 1, 10 of the 11 paths that "reached outside" had gone through it, through the back and side walls and once down through the floor to the ground 48 m below. That put the rain behind you and made the level jump whenever a lucky path got through. Sound through walls belongs to step 6 (by material and thickness), not to this probe.

## 0.6.1 — 6 Nov 2026 (probe v2: steadier, direction from the earliest sound)
**Run in game:** raid 23 (22:08, rain). "Sounds better." Still: at mark 1 (Capital) the rain came from the back wall; distant gunfire (SPTBattleAmbience) through the building walls; in and out in the hallway as fewer paths reach outside. Cause of the first: the bug fixed in 0.6.2.
Raid 22 (0.6.0, 9 and 9a on): "really good", garage "big improvement". Still: in the back of the Capital building rain came through the concrete back and side walls, some ambient sounds were placed oddly, and the audio changed in and out a little. Logs: at the Capital mark only 6 of 128 paths reached outside; 85% of the level was room build-up, spread all round (focus 0.07). The direction jumped a median 36° per 0.125 s (often 140°) and the level 0.2–2 dB. Focus was under 0.3 for 94% of the raid. v2 cost 21.8 ms/s.
- **Direction from the earliest sound** (the precedence effect: you place a sound where its first arrival comes from). Escaping paths weigh by e^(−path length / 20 m) for direction and focus. The build-up no longer widens the sound, so it stays on the openings' side instead of coming through the back walls.
- **Direction turns over ~1 s**; the level still follows in 0.35 s.
- **Less noise:** the last two sweeps (256 paths) are aggregated, and 640 paths/s instead of 512.
- **Build-up leak rate from late bounces** (legs after 2+ reflections): the energy still bouncing is deep in the space, so back rooms come out quieter; a single hard room (the garage) the same.
- **Cost:** each collider's kind is worked out once; hit labels are only built for F10 dumps (they allocated on every hit).

## 0.6.0 — 6 Nov 2026 (probe v2)
**Run in game:** not yet (installed 21:40, game closed). The new switches 9 and 9a are **off**: with them off, everything sounds as in 0.5.8.
- **New: acoustic probe v2** (diagnostics, on). 128 rays over the whole sphere that bounce like sound until they reach open sky, losing energy by material and passing glass, thin planks and bars at a loss; the rest of a room's ringing is completed with Sabine's rule. Batched on worker threads. Design: AMBIENCE.md §12–13, DESIGN.md §2.6. Raid 21 asked for it: the garage read as underground and quiet rain flammed.
- **New fix 9, "Ambience from probe v2"** (off, test): the outdoor beds' volume (dB) and muffling (Hz) are written directly, bypassing the game's curve (which also cut the lows indoors); the game's own rain and wind sources become directional (no copies, so no flamming); indoor layers and roof rain follow the probe. Settings 9b–9e.
- **New fix 9a, "Gunshots from probe v2"** (off, test): the gunshot indoor/outdoor mix and the 5a/6e hard-room boosts use probe v2's room measure (head and muzzle).
- **New: probe ray view** (F11) draws every probe v2 path in the world.
- **Logs:** new `probe2.csv` and `paths.csv`; extra columns at the end of `marks.csv` and `ownshots.csv`; an overlay line; ACOUSTIC PROBE V2 in the summary.
- User decisions (6 Nov): physical glass (−22 dB, `9d`); set volume and muffling directly; build both probe uses at once with a switch each; make the game's own sounds directional rather than copying them.

## 0.5.8 — 6 Nov 2026 (clean-up)
**Run in game:** raid 21 (21:10, medium rain): "interior transitions sound better". Still: quiet rain deep inside sounds doubled, and the garage reads as deep underground (see DIAGNOSTICS raid 21, ROADMAP probe v2). With the settings you already had, nothing should sound different. Part of the consolidation (ROADMAP.md).
- **Defaults are now the tested setup.** Every raid since raid 10 ran with: every fix on, `2 Obstruction score` ParallelPaths, `3d` Enclosure, `3i` Soft, `8f` HRTF on. The code defaults were "everything off", so a reset or a fresh install would have given an untested setup. Your config file keeps its own values; the section name is unchanged, so nothing was reset. Every fix is still switchable, and all off is still the game's own audio.
- **Removed fix 3h "Ambience through glass".** Fix 8 replaced it in 0.4.2; with fix 8 on it did nothing. Its line stays in the config file, unused (BepInEx keeps unknown entries).
- **Removed dead code:** `AmbienceMix.IndoorFade()` (replaced by `IndoorLayers()` in 0.5.7) and the 0.5.0 opening grouping in the exposure probe (the opening sounds use the 6 fixed sectors since 0.5.2; the grouping was only logged).
- **Setting descriptions corrected:** `Outdoor exposure probe` (runs everywhere, 48 directions, drives fix 8), `3c` (acts everywhere in Enclosure mode), `8` (what it does now), `8b` (no longer needs fix 7), `8d` (how early the indoor sounds fade). No setting was renamed.
- **Code comments** for the exposure probe, fix 8 and the opening sounds now describe what they do now. The history stays in this changelog and AMBIENCE.md.
- **Logs:** `ambience.csv` drops `glassLeak`, and `indoorFade` becomes `indoorLayers`. `exposure.csv` and `marks.csv`: `previewOutdoorValue` becomes `outdoorValue`. `exposure.csv`: `openings, op1Az, op1DistM, op1Weight, op1Glass` become `openSectors`.

## 0.5.7 — 6 Nov 2026
**Run in game:** not yet (installed 19:23). Included in 0.5.8.
- **Exposure is measured continuously.** About 2 directions are re-measured every frame, cycling through all 48 (about 290 rays per second, the same as before). Each re-measure is nudged by a random amount, up to half the ray spacing, and the totals are rebuilt every frame.
  - Raid 20: measuring all 48 at once 8 times a second made the value jump around while walking (0.23 → 0.42 → 0.17 → 0.23 → 0.13 entering the Nakatani hallway), because rays found and lost the windows together. The game's steep volume curve made each jump an audible cut.
  - Now one direction changes at a time, the nudges cover every angle over time, and the cost no longer spikes (up to 12 ms before).
- **Indoor layers follow enclosure** (fix 8). The indoor channel (roof rain, room tone, indoor wind) now follows how enclosed you are, not the game's room map.
  - Raid 20: the garage entrance is an *outdoor* room under a 3.6 m concrete ceiling. The probe read it as very enclosed (13–16% open), which turned the outdoor rain down, but the indoor channel was off. The result was dry and quiet.
  - Now covered spots get roof rain and indoor character, fading out toward open air. 8d sets how early openings around you fade them.

## 0.5.6 — 6 Nov 2026
**Run in game:** raid 20 (19:09, medium rain). "Generally sounds a lot better at thresholds." Still: the outdoor rain cut in and out at hard points entering the Nakatani hallway, and the garage entrance had no roof rain (both addressed in 0.5.7). Includes 0.5.5. COST: source scan 51.5 ms every 15 s (a real hitch); exposure 0.77 ms avg / 12 ms max; total 15.6 ms/s.
- **One continuous measure, one speed** (raid 19: transitions laggy and "like the game is playing different audio loops").
  - Exposure is now measured outdoors too, so the outdoor value no longer jumps at the room boundary (it went 0.55 → 1 in one step at the Capital entrance). It updates 8 times a second (was 4).
  - The outdoor level, the opening sounds' share, the direction cue, the indoor-layer fade and the roof rain all follow it with the same ~0.35 s smoothing. Before, the level took about 2 s to catch up (1 s smoothing at 4 updates/s), the opening sounds dropped out in 0.6 s, and the indoor layers switched instantly.
  - The opening sounds and the direction cue scale with how enclosed you are: full deep inside, fading to nothing in open air. They no longer switch at the room boundary.
- **Roof rain** is set directly on the game's indoor rain player (volume follow, 0.5 s hold), with no crossfade requests.
  - 0.5.4 asked an uninitialised rain player to crossfade at raid start. That crashed and kept retrying, so roof rain only changed on the game's own room-change crossfades and depended on which way you walked (raid 19, tag 3).
  - It no longer needs fix 7.

## 0.5.5 — 6 Nov 2026
**Run in game:** never on its own (built while the game ran; shipped inside 0.5.6).
- **The logging is in the COST section too**, to check whether it causes hitches (user suspicion). New rows:
  - diagnostics per frame: container sampling, overlay, timing;
  - the source scan every 15 s (`FindObjectsOfType`; the likeliest single spike);
  - the ambience/probe rows;
  - saving the log files every 2 s (about 15 files, on the main thread).

  Nested work is counted once. Nothing else changed.

## 0.5.4 — 6 Nov 2026
**Run in game:** raid 19 (18:47, rain). Roof rain crashed at raid start (an uninitialised rain player) and then changed only on room changes, depending on which way you walked. Transitions lagged about 2 s, with layers moving at different speeds. The doubled rain was gone ("less echoing in the rain"). Under a single concrete carport there was no roof rain: the game treats it as outdoors, so its indoor rain player is silent there.
- **New setting 3i Small space rule** (fix 3c): Current (default, the 0.3.1 rule down to 50%), Soft (at least 85%), Off. Raid 18: the Capital's small interior rooms (walls 2–3 m, room 0.72–0.93) got only 65–76% indoor recording because of this rule. Gunshot behaviour is otherwise unchanged; the default keeps the current setup.
- **Opening sounds no longer double up.** Each 3D copy starts at its own random point in the rain/wind loop. Copies started in step with the game's bed summed into a hollow, flanging "doubled" sound (raid 18: "like 2 gunshots at once").
- **Distance counted once.** An opening's weight is now 1 / (1 + distance / 15 m), not 1 / (1 + (distance / 5 m)²). How many rays an opening covers already accounts for distance. Raid 18: a wall of shot-out windows 13 m away counted 13%; now about 50%.
- **Covered outside counts again.** A roofed point the room map calls outdoor counts as outside at half weight, *unless* its roof is the same ceiling you're standing under (the room map's interior patches, raid 17). Raid 18: 0.5.3's strict open-sky rule also rejected the real outside beyond windows, under the building's canopy and overhangs, so shot-out windows changed nothing. `rays.csv` now shows `covered-outside` / `roofed-rejected` and both roof names (`roofOverOutside`, `yourCeiling`).

## 0.5.3 — 6 Nov 2026
**Run in game:** raid 18 (18:20, rain). No errors. Tag 1 (Capital): no more rain from inside, but the rain sounded doubled and distorted (fixed in 0.5.4). Shot-out windows (tags 2–4) and the Nakatani extract windows (tags 6–7) changed almost nothing, because of the strict sky rule and the double distance weighting (fixed in 0.5.4). Stairwell (tag 8): all blocked, correct. COST: 6.3 ms/s total (0.6% of a core); one-off raid-start spikes of 52 ms (window scan) and 138 ms (setup).
- **"Outside" means the game's room map says outdoor *and* there's open sky above** (nothing sound-blocking within 40 m straight up). It applies to the level, the 3D sources and the bisection.
  - Raid 17, inside the Capital building (the user's start area): the room map calls large interior patches outdoor. The probe found "outside" 0.6 m away in about 20 directions, with escalators and indoor walls beyond.
  - 0.5.2's covered check only weakened the 3D sources, not the outdoor level, so rain played loud indoors even with 3D off (8e = 0).
  - It also caused the left-ear bias (tag 1) and the jump from 0.89 to 0.02 when stepping out of the patch (tag 2).
  - Now those points simply aren't outside, and each direction keeps looking until it reaches real sky (past the glass).

- **Cost logging.** The summary gains a COST section: for each part (enclosure sweep, muzzle room, aim cone, echo search, outdoor exposure, ambience mix with opening sounds), its calls per second, average and maximum ms per call, ms per second of game time, and raycasts and room lookups per second. Work done inside another part is counted once. The F9 overlay shows the total ms per second.

## 0.5.2 — 6 Nov 2026
**Run in game:** raid 17 (18:04, rain). No errors; opening voices up to 24. Tag 4, elsewhere: "all round this building sounds okay". In the Capital building, rain was too loud and sounded "inside the building", also with 3D off. The cause was the room map's outdoor patches indoors (fixed in 0.5.3), not the 3D audio.
- **Opening sounds come from 6 fixed directions** (every 60° in world space), like a ring of speakers. Each one's volume follows how open its direction is.
  - Raid 16: the up-to-3 "openings" were re-sorted every quarter-second and their sources swung to new directions, passing through the listener. The user heard rain "directly on top of me, moving", "like the direction is decided once".
  - Sources are now 120° wide and overlap, so the rain sounds like an area, not a point ("comes from one location, not ambiently"). Volume changes between directions are smoothed over about 1 s.
- **"Window" objects:** a "window" collider with no glass tag counts as glass only if a glass surface is actually drawn there (glass material on its own, its parent's or its children's renderers). Otherwise it's an open window. This keeps the Nakatani tower's empty frames open (raid 15), but a glass wall built the same way stays glass (user question: a long glass wall inside a concrete building).
- **"Outside" must have sky above.** Where a direction reaches an outdoor room, a ray goes straight up from just past that point. A roof within 30 m means a covered area (lobby, vestibule, canopy), and that direction counts 35%. Raid 16: rain came "from inside the building" through an entrance area the game's room map calls outdoor. `rays.csv` marks these directions as "covered".

## 0.5.1 — 6 Nov 2026
**Run in game:** raid 16 (17:52, rain). No errors; opening sounds played (up to 12 voices). Hallway "sounds much better". Rain sometimes sounded on top of the listener and moving, sometimes came from inside a building, and came from one point rather than all around (all fixed in 0.5.2). Rain crossfades: none needed holding, no dropouts.
- **Opening sounds crash fixed.** In 0.5.0 the setup added a filter to an object before any audio source, which Unity refuses. Setup failed, no opening sound played (raid 15: "voices max 0"), and fix 8's per-frame update failed from then on, freezing the direction cue.
- **Roof rain no longer flips.**
  - Floors above are counted on five upward rays (straight up and four tilted 20°), and the middle answer is used.
  - A new roof-rain level must hold for 2 s before it's applied.

  Raid 15: one vertical ray kept catching beams, ducts and stairs, so the level jumped between 0, 30% and 100% every half-second ("cutting in and out").
- **Rain crossfades are never interrupted (fix 7).** A crossfade requested while one is running now waits until it finishes. Before, the game, and fix 8's roof rain, restarted the incoming rain loop at a random point mid-fade, which is a jump or click (raid 15, Nakatani hall). The summary counts these as "crossfades held".
- **Exposure:**
  - Where a direction crosses outside is now found to within about 1 m (three halvings between room-map samples). Before, a window 6 m away could read as "outside at 8–18 m" and barely count.
  - A "window" object that isn't tagged as glass counts as an **open** window. Raid 15: the Nakatani tower's window shape (`Nakatani_tower_windows_01_COLLIDER`), where bullets pass through, was counted as glass. The user hears these as open windows.

## 0.5.0 — 6 Nov 2026 (roadmap step 4b, phase 4 + fixes from the rainy raid)
**Run in game:** raid 15 (17:35, rain). The opening sounds crashed at setup, so they never played (fixed in 0.5.1). Roof rain was "better", but it flipped and clicked (fixed in 0.5.1). The Nakatani hall windows read as glass, and too far away (fixed in 0.5.1).
- **Phase 4: sound from the openings** (part of fix 8, setting **8e Sound from openings**, 0.7; 0 = off).
  - Indoors, the open directions are grouped into up to 3 openings. Each gets a 3D copy of the outdoor beds (wind, outdoor rain, day/night background), placed in its direction and routed through the same mixer channel, so fix 8's level and muffling still apply.
  - 70% of the outdoor sound moves to the openings, split between them by strength; the flat bed keeps 30%. Total loudness is unchanged.
  - Glass openings are duller. Without HRTF, openings behind you are also duller. Wide openings sound wide.
  - New **8f HRTF on opening sounds** (off) runs them through the game's 3D audio plugin.
- **Exposure:** 24 directions around you (15° apart, was 12 at 30°). Raid 14: the Nakatani exit hallway's open windows were barely found.
- **F10 now also dumps every exposure direction** to `rays.csv`: wall distance and collider, where it reached outside, the path type, what's in between, and the last room sampled. This is for diagnosing spots like that hallway.
- **Roof rain** (fix 8, with fix 7):
  - full with only the roof above you;
  - **8b** (30%) with one floor above;
  - **none with two or more** (was 30% for any).

  Raid 14 and the user: no roof rain inside tall buildings, or down the Nakatani exit stairs.
- Logs: `ambience.csv` gains openingShare, openingVoices, opening1Az and opening1Gain; `exposure.csv` gains openings and the strongest opening's direction, distance, weight and glass share.

## 0.4.2 — 6 Nov 2026 (roadmap step 4b, phase 2)
**Run in game:** raids 13 and 14 (raid 14 in medium rain: the Nakatani exit hallway's windows were barely found, and roof rain was heard down the underground stairs; both addressed in 0.5.0). Raid 13 (16:28, Ground Zero, no AI). Loaded (self-check 16:25), no errors. Fix 8 A/B tested: "works pretty well". Over 4,584 samples the applied outdoor level jumped by more than 0.15 per 0.1 s only once; the pan cue was active in 626 samples and the behind-dulling in 346. Six outdoor bed sources were found, plus 1 indoor rain player. 274 echo candidates off glass facades and bars. No rain yet, so fix 7 and the roof-rain cut are still untested.
- **New fix 8, "Outdoor sound indoors from openings"** (off by default; needs fix 3 on). Indoors, the exposure measure now sets the outdoor ambience level (volume and muffling), smoothed over about 1 s and never below the room's baked level:
  - open sides and broken windows count fully;
  - intact glass counts by **8c Through glass** (0.45);
  - window bars count 80%.

  It replaces fix 3's doorway fade for the level, which removes the sideways 6% ↔ 95% swings at wide entrances (raid 12). The doorway blend still drives the indoor/outdoor reverb. It also replaces fix 3h.
- **Indoor sounds fade near openings** (8d, 0.6 × exposure). Deep interior rooms keep their full indoor character.
- **Roof rain under floors** (8b, 0.3 when there are floors above you). Only with fix 7 on, because changing the rain volume makes the game re-pick its clip, which without fix 7 can fade the rain to silence.
- **Direction cue** (8a, 0.6). Indoors, the outdoor beds (wind, outdoor rain, day/night background) are panned toward the open side, by how one-sided it is, and dulled when the open side is behind you. It's a cue until phase 4's 3D emitters, and stays as their fallback.
- **Exposure probe:** shards in broken frames now count as glass when their collider is a mesh (raid 12: they are, shaped like the shard). New `openSideAz` and `oneSided` columns.
- **Echoes (fix 6) off glass facades and window bars.** The aim cone also checks the glass and bar layers the sound-blocking rays pass through. Intact glass reflects as glass; bars are a weak, dull reflector.
- Logs: `ambience.csv` gains exposureApplied, indoorFade, bedPan, bedCutoffHz and roofRainFactor. The summary gains an AMBIENCE MIX section.
- Self-check: 97 targets, all verified.

## 0.4.1 — 6 Nov 2026
**Run in game:** raid 12 (16:02, Ground Zero, no AI). Loaded (self-check 15:57), no errors. The tower flicker is gone. Remaining: just inside wide entrances, stepping sideways swings outdoor ambience 6% ↔ 95% (fix 3's sideways fade), which the phase 2 exposure value would hold steady (~0.6–0.7). Gun clips are compressed in memory, so recording loudness can't be read (n/a). Shard colliders are real shard meshes, so the size rule was wrong and they should count as glass.
- **Fix 3h no longer flickers.** Raid 11: going in and out of the Nakatani tower, the outdoor ambience cut on and off deep inside. Fix 3h raised it by the glass share the 32-ray probe saw, and one or two rays hitting a window or not flipped it between 0 and 0.12 every second. It's now smoothed over about 2 s. Phase 2 replaces it with the exposure measure.
- **Stairwells louder (fix 6).** In hard, closed rooms all echoes are raised by the new setting **6e Hard room echo boost dB** (+4) × how hard and closed the room is, and up to 3 extra echoes are played (was 2). Raid 11: stairwell gunshots were "quieter than a stairwell should be".
- **Recording loudness** is measured: each gun's indoor against outdoor recordings (RMS and peak, first 0.4 s), shown in the summary under OWN GUN ECHOES. It checks whether the indoor set is quieter, which would explain quieter stairwells.
- **Exposure probe:**
  - Window bars and grilles count as mostly open (80%), not glass.
  - A shard left in a broken frame counts as glass only if its collider is shard-sized (≤ 0.6 m). Larger, the window counts as broken, i.e. open.
  - The summary lists each thing between you and the outside with its collider type and size, to check both rules.
  - New columns `barsRays` and `brokenRays` in `exposure.csv`.

## 0.4.0 — 6 Nov 2026 (roadmap step 4b, phase 1)
**Run in game:** raid 11 (15:39, Ground Zero, no AI). Loaded (self-check 15:37), no errors. Exposure probe 0.16 ms per update; facade glass found by name. Bars and broken-frame shards misread as glass; outdoor ambience flickered in the tower (fix 3h); stairwell gunshots quieter. No rain, so fix 7 is untested. All addressed in 0.4.1 except fix 7.
- **New: outdoor exposure probe** (Diagnostics, on, **measures only, changes no sound**). Indoors, every 0.25 s:
  - 24 directions are checked against the game's own baked room map: which reach the outside, how far away, and through what (open, glass, or other).
  - It also records what is above you: the ceiling and its material, floors above, and the roof material.

  Logged to `exposure.csv`, added to F10 marks, and shown on the F9 overlay. It also shows the value phase 2 would set for outdoor ambience. See [AMBIENCE.md](AMBIENCE.md).
- **New fix 7, "Fix rain cutting out between rooms"** (off by default). Fixes a game bug: moving between rooms with the same rain-on-roof clip, or up and down in rooms where roof rain follows your height, faded the rain to silence. Every rain clip pick is logged to `rain.csv`, with whether the game would have silenced it.
- Summary gains OUTDOOR EXPOSURE (cost per update, what stood between you and the outside) and RAIN sections.
- Self-check: 89 targets, all verified.

## 0.3.3 — 6 Nov 2026
**Run in game:** raid 10 (15:07, Ground Zero, no AI). Loaded (self-check 15:03), no errors. Threshold (muzzle) and stairwell/office: "good". Stairwell: "maybe more reverb" (the boost reached about +4–5 dB of the +6). Glass ambience: barely acted, because big facade glass doesn't block the probe rays (glass share ~0.06). Inside, the game's own room setting has outdoor ambience at 0, so only its indoor "rain on the roof" layer plays. This led to roadmap step 4b.
- **Fix 3c: the room is measured at the muzzle too.** Raid 9: on a glass office's threshold, head outside and muzzle inside (looking in) sounded outdoor; head inside and muzzle outside (looking out) sounded indoor, because everything was measured at the head. The room, roof and size readings are now a mix of head and muzzle. New setting **3g Muzzle weight** (0.7).
- **Hard closed rooms keep their full indoor sound.** Raid 9, the Nakatani basement stairs: walls 0.81–0.94, roof 1, all concrete and thick metal, but the small-space rule cut them to about 55% indoor. The size cut now fades out as the room gets hard (bare concrete, stone, tile, thick metal) and closed.
- **More stairwell character.** Hard closed rooms get up to 2 extra echoes (fix 6). They also get a boost to the game's room reverb on your own shots: new setting **5a Hard room reverb boost dB** (+6), scaled by how hard and closed the room is.
- **Rain and wind through glass** (fix 3). Inside a space walled with glass, the outdoor ambience level is raised toward full, still muffled by the indoor filter. New setting **3h Ambience through glass** (0.5). Raid 9: glass buildings let too little weather in.
- Logs: `ownshots.csv` gains muzzle readings, the room value used, hardShare, hardRoom and glassShare. Its reverbBoostDb is now the boost actually applied. `ambience.csv` gains glassLeak.

## 0.3.2 — 6 Nov 2026
**Run in game:** raid 9 (14:51, Ground Zero, no AI). Loaded (self-check 14:48), no errors. "A lot better." Stepping out of small spaces now drops the indoor sound. Still heard: head/muzzle on opposite sides of a threshold; stairwell not indoor enough; glass buildings let in too little rain and wind (all in 0.3.3).
- **Fix 3c, Enclosure mode: "is there a roof right above you?"** Five extra rays per sweep go straight up and 20° off vertical. The roof only counts as far as they agree (roof closure = the smaller of the two). Raid 8: about 1 m outside a small space's door, the angled upward rays still caught its roof edge and overhang (0.58–0.84), so shots taken visibly outside got about 40% of the indoor recording. Now the indoor share fades out over about 1 m past the roofline.
- Logs: `zenithClosure` added to `probe.csv`, `ownshots.csv` and `marks.csv`.

## 0.3.1 — 6 Nov 2026
**Run in game:** raid 8 (14:34, Ground Zero, no AI). Loaded (self-check 14:30), no errors. "Sounds pretty good." Bench now 0.12 indoor; 1,221 windows registered as glass (31 glass echoes, against 8 in raid 7). 7 marks: small spaces still sounded indoor when stepping just outside (fixed in 0.3.2).
- **Fix 3c, Enclosure mode: new room measure** (from raid 7: a covered bench and a small glass office sounded too indoor). The indoor share now needs walls *and* a roof (wall closure × roof closure), so a roof alone isn't a room. Tiny spaces get less of the indoor recording (size factor 0.5 at walls ~2 m away, 1 from ~6 m), because the game's indoor recordings carry a long room tail; the echoes supply a small space's sound. Aim direction only counts under a roof. Details in FIXES.md.
- **Windows are recognised as glass.** A window blocks rays with a separate collider that has no material tag, so windows read as the building's concrete. All `WindowBreaker` colliders are now registered as Glass at raid start, for the probe and the echoes; broken windows let rays through.
- **New setting 3f "Thin and glass surfaces count"** (0.5): how much glass, thin wood planks, cardboard and fabric count as walls. Chain fences and grates always count as nearly open.
- **New Diagnostics setting "Mark key"** (F10): marks the spot you're standing on in `marks.csv`, with what the probe measures there and the surfaces around you. Use it where something sounds wrong.
- Logs: `probe.csv` and `ownshots.csv` gain the new measures (wallClosure, roofClosure, room, sizeFactor, thinShare; aimClosure). The summary shows how many windows were registered.
- Self-check: 80 targets, all verified.

## 0.3.0 — 6 Nov 2026 (roadmap step 4)
**Run in game:** raid 7 (14:12, Ground Zero, no AI). Loaded (self-check 14:09), no errors. "Sounds much much better", "overall huge improvement". Echoes turned on mid-raid: 984 played, 4.0 per sample, surface tags found on almost every hit (Concrete 500, MetalThin 217, MetalThick 143, WoodThin 60, …), 24 of 24 voices busy at peak, 17 stolen. Two spots too indoor (fixed in 0.3.1).
- **New fix 6, "Own gun echoes"** (off by default): your own shots get up to 4 short echoes from the real surfaces around you (walls and ceilings by mirror reflection, what you fire at, and building faces or hills up to 250 m). Each echo is delayed by its path at 343 m/s, quieter by distance, the muzzle's directivity and the surface material, and duller by air absorption, surface and (from behind) head shadow. Hooked on the gun's own sound queue, so single shots, full-auto loops and burst tails all get echoes with the same timing. Settings 6, 6a–6d. Logged to `echoes.csv` and a new OWN GUN ECHOES section in `summary.txt`.
- **Enclosure probe fix (from raid 6):** floor-like surfaces (normal more than about 45° upward) no longer count as enclosure, in the 32-ray sweep or the aim cone. Before, the ground just below level read as walls, so open ground scored 0.2–0.4 and every own shot outdoors in Enclosure mode got 15–25% of the indoor recording. Expect lower `enclosure`/`horizontal` outdoors and a lower `aimEnclosure` when firing into open air.
- Self-check: 79 targets, all verified against the decompiled game. Setting names checked.

## 0.2.2 — 6 Nov 2026
**Run in game:** raid 6 (13:51, Ground Zero, no AI). Loaded (self-check 13:46), no errors. Sticky doorway: no doorway swaps all raid. Enclosure mode heard: "seems to work", but outdoor shots sounded a little indoors (the ground bug, fixed in 0.3.0). Meta XR acoustics confirmed running (1 map, 35 geometry). Fix 5 not tried (left at 0).
- **Fix 3:** sticky doorway. The doorway used last frame stays in the running through a crossing (another must be 1 m closer to take over). `ambience.csv` logs which doorway was used and your distance through and beside it.
- **New: enclosure probe** (Diagnostics, on by default, changes no sound): 32 rays every 0.1 s, logged to `probe.csv`.
- **Fix 3c:** new **Enclosure** mode (setting 3d) that blends your own gunshot's indoor/outdoor recordings from the probe plus a 9-ray cone along the shot (setting 3e Aim weight). Doorway mode stays the default. Every own shot and tail is logged to `ownshots.csv`.
- **New fix 5 (experiment):** own-gun room reverb boost, which raises or lowers the sends of the game's hidden Meta reverb copy of your shots.
- **Correction:** the earlier claim that your own shots get no room acoustics was wrong. The game already plays a reverb-only copy through Meta XR (see FIXES.md, fix 5).
- Summary gains a META XR ACOUSTICS section. Self-check: 75 targets, all verified.
- Added [ROADMAP.md](ROADMAP-DETAILED.md).

## 0.2.1 — 6 Nov 2026
**Run in game:** raid 5 (11:28). Loaded (self-check 11:24), no errors, the ambience startup error is gone.
- **Fix 3 (smooth doorways):** outdoorness is now measured through the doorway's plane, with a sideways fade, instead of by distance from its centre. That removes the jump at the room boundary (0.24 → 1.0 in raid 4).
- **Fix 3:** waits for the game's ambience blenders to be set up (about 30 s into a raid) instead of throwing an error every frame until then.
- **New fix 3c, "Blend own gunshots at doorways"** (off by default): near a doorway, your own shot and burst tail play as a mix of the indoor and outdoor recordings.
- Self-check grew to 70 targets, all verified against the decompiled game. Setting names checked for characters BepInEx rejects.

## 0.2.0 — 6 Nov 2026
**Run in game:** loads after the setting-name fix below; fixes not yet heard.
- Added four fixes, all off by default and switchable live in F12 (see [FIXES.md](FIXES.md)):
  1. Gun mixer hysteresis
  2. Obstruction score (Game / IgnoreFailedPaths / ParallelPaths)
  3. Smooth doorways
  4. Off-map sounds as outdoor
- Self-check grew to 62 targets, all verified against the decompiled game.
- `settings.txt` and `summary.txt` record which fixes were on and how often each acted. `ambience.csv` gained `doorwayTarget`, `doorwayApplied` and `doorwayPortals`.
- **Bug, fixed in the same version:** the first build named its settings section `2. Fixes (all off = the game's own audio)`. BepInEx rejects `=` and `'` in setting names, so the plugin failed to load. It's renamed to `2. Fixes (all off means vanilla audio)`, and a load failure is now also written to `errors.txt`.

## 0.1.1 — 6 Nov 2026
**Run in game:** raids 2 and 3.
- Added the doorway logging: `transitions.csv`, `ambience.csv`, and a transitions section in the summary.
- Added an `occluder` column to `containers.csv`.
- Plugin errors are now also written to `RealSoundSimulation Logs/errors.txt`, because BepInEx's `LogOutput.log` wasn't being written.

## 0.1.0 — 6 Nov 2026
**Run in game:** raid 1.
- First build, diagnostics only: self-check, timing, shot probe, occlusion samples, source scan, rolloff dump, F9 overlay, raid summary.
- Headset DSP library and offline bench (not hooked into the game).
