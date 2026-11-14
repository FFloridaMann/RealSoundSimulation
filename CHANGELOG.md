# Changelog

What changed, as you'd notice it in game. The full technical log is [docs/dev/CHANGELOG-DETAILED.md](docs/dev/CHANGELOG-DETAILED.md).

## 0.9.2 beta

- **No more stutter on Icebreaker** (Manimal's custom map). The mod kept searching the whole map for one of the game's sound parts that Icebreaker doesn't have: a hitch every 2 seconds for everyone, and about 60 ms every tenth of a second with **Bug report - logs** on. Now it looks once per raid. In testing, Icebreaker went from 42 to 79 FPS with logs on, and the mod's share of the CPU went from 40% of one core to 2%, the same as on other maps. Any other custom map without that part is fixed the same way.
- On those maps, outdoor ambience doesn't fade smoothly at doorways; the map's own sound handles it (see Known issues).

## 0.9.1 beta

- **No more hotkey clashes.** The debug overlay (F9) and the ray view (F11) are developer tools and are gone from the public build; F9 clashed with Fika. The bug report mark key (F10) only does anything while **Bug report - logs** is on.
- Footsteps and everyday sounds are still not simulated in this version; they come after 1.0 (see the roadmap). **General - footsteps and handling like EFT 1.0** only switches between Sound Engine Backport's backported EFT 1.0 footstep settings (on) and vanilla SPT (off).

## 0.9.0 beta: first public release

- **A simpler F12 page.** 31 settings: one switch per feature at the top, then a few volumes. Everything else keeps its tested value.
- **Diagnostics only when you ask.** The timing measurements run only with **Bug report - logs** on, so normal raids don't pay for them.
- **Rain and outdoor sound** has one clear switch: **Outdoors - rain and outdoor sound indoors**. Off gives you the game's own rain and outdoor sound.
- **Sound Engine Backport's routes between rooms are in the download**, with red_the_fox's permission, so you don't need his mod for them.
- **New plugin ID for the Forge** (`com.ffloridamann.realsoundsimulation`). Your settings carry over once.
- Footsteps and body sounds from 0.7.15-0.7.22 are held back for after 1.0; EFT 1.0 gun recordings are in the developer build only.

## 0.7.22 (not released yet): above or below

With **Footsteps and body sounds - simulated** on, sounds from another floor now say which way they are, not just that they're muffled:

- **Knock from above, boom from below** (08, both on): each footstep from a floor above adds a short, sharp knock; from below, a soft, slower, deeper boom.
- **Muffling by floors** (08, on): footsteps from above come through clearer than from below, and two floors more muffled than one. Before, nearly everyone far off was muffled the same.
- **Floor tone** (08, on): sound through a floor gets the slab's ring, knocky from above, a hollow boom from below. Through a wall on your own floor it stays plain.
- **Height layer** (08, now on): a short, faint high copy of each sound from another floor, nearly straight above or below you in their real direction, so a headset can place it. Not realistic; turn it off to compare.
- **Steeper above and below** is now off: sounds keep their real direction, and the way round by the stairs comes from the opening.
- **Height test key** (08, F8): in raid, take a step and press it: three footsteps one floor above, three one floor below, 20 m to your right as in a raid; then the same plain to compare.
- Voices, gear and meds/food get the muffling, floor tone and height layer too; the knock and boom are footsteps only.
- **Footsteps and body sounds: one person, one place.** Each person's footsteps, gear, voice and meds now always come from where they really are, as in the plain game. Walls, floors and stairs only change how muffled they sound. Before, a person's steps could come through the floor while their gear came from the stairwell, so one person seemed to be in two places. **Body sounds - both ways at once** is now off.
- **Adrenaline after close blasts** (05, on): a blast close enough to shake you sets your heart racing, then it settles smoothly within about 20 s, and after a big one your character is out of breath in their own voice. The heartbeat is made by the mod (no recordings) and isn't cut by concussion.
- **Concussion: the blast stays heard** (05, on): when a blast cuts your hearing, the blast, its echoes and the world now stay heard, muffled and about 15-25 dB down, instead of going nearly silent after the first split second. Also fixed: with a headset on, the hearing cut made dry gun sounds louder instead of quieter.
- **Explosions: a crack before the cut** (05, on): a blast close enough to cut your hearing is now heard sharp for a split second (0.1 s, shorter and quieter with a headset) before the cut starts. Before, something cut gun sounds about 50 dB at once and only the dull "duuum" was left.
- **Shock wave: sharp start** (05, on): the pressure hit starts with a short, bright crack, barely muffled round a doorway or corner, then the boom.

## 0.7.21 (not released yet): your echoes switch only switches your echoes

- Turning off **Your gun > Echoes - on** also silenced the through-wall thump, explosion echoes, the shock wave, fragment pass-bys and the second way of body sounds, because they share the same voices. Now it stops only your own gun's echoes.

## 0.7.20 (not released yet): the way round keeps its height

- Someone upstairs or downstairs heard round through the stairwell (their voice, gear, meds) now sounds from the stairwell's direction but at their height, instead of level with you.
- Their footsteps keep the thud through the floor and, when the stairs to them are close, also come quieter from the stairwell, at their height.

## 0.7.19 (not released yet): footsteps above and below

- With **Footsteps and body sounds - simulated** on, footsteps on another floor always come through the floor or ceiling from where the person is, never from the stairwell. In 0.7.17 they often came from the stairwell, so the same person seemed above you one moment and across the room the next.
- Above you: a ceiling thud, brighter on a light floor and deeper on concrete. Below you: quieter and duller. Voices and gear from below are duller too.
- New switch **Body sounds - steeper above and below** (08, on): sounds through a floor are placed at least 45 degrees up or down, so a headset can tell up from down. Off = their real height.

## 0.7.18 (not released yet): other guns' shots really ring out now

- 0.7.12's fix for bots' and other players' shots being cut off by their next shot hardly ever ran: it kept 6 of the game's spare gun voices back and the game rarely had more. It now always runs, with at most 8 shots ringing on their own at once.

## 0.7.17 (not released yet): body sounds through walls, and both ways at once

- With **Footsteps and body sounds - simulated** on, other people's footsteps, voices, gear, looting, meds and food now also come through walls, doors and windows, not only floors: thin wood, plasterboard and glass let them through from where the person really is, dull; thick concrete sends them round through the doorway.
- Both ways at once: when a sound reaches you through a wall and round through a doorway at levels close enough to hear both (within 10 dB), both play, each from its own place with its own muffling. Switch **Body sounds - both ways at once** (08) to compare.
- EFT 1.0's extra footstep layer outdoors is treated like the rest of the footsteps.

## 0.7.15 (not released yet): footsteps and body sounds, as a switch

- New switch **Footsteps and body sounds - simulated** (01 GENERAL), off by default.
- With it on, other people's footsteps, gear, looting, voices, meds, food and reloads reach you the way their gunshots do. Someone above or below you is heard through the floor from where they really are: footsteps as a dull thud, talking and zippers very muffled. If there's a stairwell or open way round, their voice and gear sounds come from it. Someone in the next room on your floor is heard from the doorway.
- Your own sounds don't change. Range and floor muffling are under Advanced settings in 08.

## 0.7.14 (not released yet): ear ringing by ammo, small rooms sound indoors

- Ear ringing comes sooner and lasts: a faint ring after a few shots without ear pro, a strong one after a lot. Your ears recover a couple of seconds after you stop, over about 20 s.
- By ammo: supersonic without a suppressor rings quickest, subsonic a little slower, a suppressor slower still, subsonic with a suppressor not at all. (Before, subsonic shots never counted, so pistol fire didn't ring.)
- Your shots in a small closed room play the room's sound fully. Before, small rooms mixed in some of the outdoor recording and could sound like they were outside; the measured room tail now decides how long a small room rings.

## 0.7.13 (not released yet): your ears ring from gunfire

- Without a headset, every loud shot near you adds up: yours, and anyone's close by. A long string of fire slowly brings in a ring and dulls the world a little; both fade once you stop.
- How much a shot counts: its calibre, the muzzle device (a suppressor adds nothing), the barrel and the ammo; subsonic shots don't count. A small closed room counts far more than open ground, and a shot behind a wall counts less.
- A headset takes off half and stops the ring. A helmet with a built-in headset now counts as a headset, here and for blast hearing protection.
- New switch **Ear ringing - from gunfire without a headset** (01 GENERAL); the ring volume, strength, recovery and dulling are under Advanced settings in 05.

## 0.7.12 (not released yet): other guns' shots ring out

- When a bot or another player fires again while their last shot is still ringing, that ring now finishes instead of being cut off. It was most noticeable on automatic guns fired in single shots or short bursts in the distance.
- The ring keeps where it came from and how muffled it was. It uses a spare voice from the game's own pool, and only while the pool has some to spare, so no gun goes silent.
- New switch **Tails - ring out on a spare voice** (03 OTHER GUNS), on by default; off = cut as before.

## 0.7.11 (not released yet): ready for Fika

- A Fika headless host, the machine that runs the bots with no one listening, no longer runs this mod, so it doesn't take frame time from the bots.
- With Fika installed, each copy of the game writes its bug report logs to its own folder.
- Nothing changes in how anything sounds. A read of Fika's code found it already works alongside this mod: other players' guns are treated like any other gun, and each grenade goes off once for each player. A two-player test is still to come.

## 0.7.10 (not released yet): headsets protect your hearing

- With any headset on, a blast no longer takes your hearing away: the closest blast dulls the world by about 20 dB for a moment instead of going silent, and lighter blasts don't cut it at all. Ear ringing already never played with a headset.
- New slider **Concussion - headset protection %** (05 EXPLOSIONS: SHOCK AND CONCUSSION), 75% by default; 0 means headsets don't help.
- The concussion itself, its blur and the thump you feel are unchanged. Every headset protects the same for now; the headsets revamp will make them differ.

## 0.7.9 (not released yet): an easier F12 page

- 11 sections with headers that stand out (01 GENERAL to 11 DIAGNOSTICS); Explosions is split into sound, shock and concussion, and fragments and glass.
- Settings that belong together share a name and stay together, for example everything about the explosion thump (its volumes and all three depth sliders) is **Thump - ...** in 04 EXPLOSIONS: SOUND, and a group's Advanced settings sit right under its main ones.
- Your values carry over; nothing is removed. Each description still ends with its old code.

## 0.7.8 (not released yet): open-air thump depth

- New slider "Open-air thump cutoff Hz" (Explosions, Advanced): how deep the thump is for a grenade with no wall in the way, outdoors or in your room. Walled grenades keep "Explosion thump cutoff Hz". Default 80 Hz, the same as before. Not played yet.

## 0.7.7 (not released yet): concussed thump depth

- New slider "Felt thump cutoff Hz" (Explosions, Advanced): how deep the explosion thump is while concussion cuts your hearing, separate from the normal thump's. Default 80 Hz, the same as before. Not played yet.

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
