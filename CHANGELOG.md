# Changelog

What changed, as you'd notice it in game. The full technical log is [docs/dev/CHANGELOG-DETAILED.md](docs/dev/CHANGELOG-DETAILED.md).

## 0.7.6 (not released yet): steadier shots at roof edges

- Your shot's indoor/outdoor mix now glides from one shot to the next whatever caused the change (a roof edge, turning to fire out of a window, a small wobble in the measurement), instead of only when the sky opened up. Shots a second or more apart still take the new mix almost at once, and the first shot after a few quiet seconds is always fresh.
- Open air pulls to outdoor a little more firmly again: the last version had softened it so far that the shot it was made for, just outside a building, was left half indoor.
- The measured room tail now uses at most three tails at once (the oldest gives way in fast fire), and its voices are counted so echoes never push the game's own sounds out.
- Automatic fire keeps its normal pitch: a varied pitch could put a burst's last shot slightly out of step. Volume still varies, and single shots vary as before.
- Not played yet.

## 0.7.5 (not released yet): smoother open-air pull

- Stepping out from under a roof edge, your shot now glides from indoor to outdoor over about half a second instead of jumping between two shots, and moving along a roof edge no longer flips it back and forth. Not played yet.

## 0.7.4 (not released yet): your shot in the real room

- **Measured room tail:** your shot's reverb tail now comes from the room the mod measures: as long as the room really rings, duller in soft rooms than hard ones, and as loud as the shot is indoor. The game's own reverb is turned down under it. F12, Your gun: on/off, volume, length and how far the game's reverb is turned down.
- **Open air pulls to outdoor:** with open sky above you and over the muzzle, your shot is mixed firmly outdoor, even right after you step out of a building. In the last raid this changed 13 of 56 shots, all outdoors (one went from 53% indoor to 26%).
- Not played yet.

## 0.7.3 (not released yet): no two shots alike

- Every gun, yours and everyone else's: each shot gets a small random pitch and volume change before the simulation, so its echoes and tail vary with it. F12, General: **Shot-to-shot variation**, with **Variation pitch %** (2) and **Variation volume dB** (1) to test by ear. Not played yet.

## 0.7.2 (not released yet): better bug reports

- The bug report zip now also lists your installed mods (BepInEx plugins and server mods, by name) and includes BepInEx's own log, so problems with other mods can be found.

## 0.7.1 (not released yet): your room builds up blasts

- A blast that gets into a small closed room you're in, through a door or window, now hits a bit harder and lasts a bit longer, because it bounces around your room. Big or open rooms add nothing. It's less than a blast going off in your own room.
- Switch it in F12, General: "Your room builds up blasts". Not played yet.

## 0.7.0 (not released yet)

- New name: Real Sound Simulation.
- The F12 page is regrouped by what you hear (General, Your gun, Other guns, Explosions, Rain and outdoors, Doors and walls, Performance, Diagnostics) with plain names. Fine-tuning settings are behind "Advanced settings". Each one's description ends with its old code. The concussion switches are at the top.
- **Bug report logs** (General, off by default): one raid's logs packed into a zip in the RealSoundSimulation Logs folder in your SPT folder, ready to send.

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
