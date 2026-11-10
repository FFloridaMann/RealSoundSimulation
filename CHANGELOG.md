# Changelog

What changed, as you'd notice it in game. The full technical log is [docs/dev/CHANGELOG-DETAILED.md](docs/dev/CHANGELOG-DETAILED.md).

## 0.7.0 (not released yet)

- New name: Real Sound Simulation.
- The F12 page is regrouped by what you hear (General, Your gun, Other guns, Explosions, Rain and outdoors, Doors and walls, Performance, Diagnostics) with plain names. Fine-tuning settings are behind "Advanced settings". Each one's description ends with its old code.

## 0.6.61 (9 Nov 2026): other guns echo

- Other people's shots echo off the walls around them, or around the doorway they reach you through.
- Echoes share a voice limit that always keeps your own gun's echoes first; at the limit, the quietest other-gun echo fades out to make room.
- Played in one raid on Factory: nothing of yours was cut. Bigger fights still to test.

## 0.6.37 to 0.6.60 (8 and 9 Nov 2026): walls and explosions

- Sound through walls depends on their material and thickness; enough wall or earth stops it completely.
- Explosions come from the doorway they reach you through, with a thump through the wall that arrives first.
- Explosion echoes, and room echoes from the doorway for blasts in another room.
- A shock wave you feel, stronger in small closed rooms, weaker around corners.
- Concussion by the blast pressure that reaches you; when it cuts your hearing, the blast fades instead of snapping off.
- Fragments whizz past along their real flight path; fragment hits and breaking glass in other rooms come from the opening.
- Closed doors muffle by material.
- Played in about a dozen grenade raids: "sounds pretty good". Fine-tuning by ear is still to do.

## 0.6.25 to 0.6.36 (8 Nov 2026): materials and roof rain

- Every surface uses measured acoustic values for its material.
- Roof rain counts the floors above you and the roof's material, fades down stairwells, and no longer jumps between rooms.
- Footsteps and weapon handling like EFT 1.0, and EFT 1.0's routes between rooms, both from Sound Engine Backport.
- Your shot's ring follows your head when you turn.
- Played: materials "definitely sound better"; rain deep in buildings "mostly sounds better".

## 0.6.20 to 0.6.24 (8 Nov 2026): fights from the doorway

- A shot from another room comes from the doorway, window or stairwell it reaches you through.
- Shots ring out under the same gun's next shot instead of being cut off.
- A distant shooter's first burst no longer starts clear and then gets cut down.
- Played: the doorway version "sounded better"; cut-off shots dropped from 40% to 31%.

## 0.6.0 to 0.6.19 (6 and 7 Nov 2026): the sound tracer

- A tracer bounces rays through the map until they reach open sky, measuring how much outside sound reaches you, from where and how muffled.
- Rain is traced down from the real roof; outside sound comes from the window or door it gets in by.
- Distant off-map battles come through the windows that face them.
- Optional EFT 1.0 gun recordings from Sound Engine Backport.

## 0.3 to 0.5 (6 Nov 2026): your gun and the outside

- Echoes for your own shots, off the real surfaces around you: "much much better".
- Your shot blends the indoor and outdoor recordings by how enclosed you really are.
- Outside sound heard indoors through openings; a fix for rain cutting out between rooms.

## 0.1 to 0.2 (6 Nov 2026): listening first

- Measuring what the game's own audio does, before changing anything. Smooth doorways and steadier muffling of distant gunfire.
