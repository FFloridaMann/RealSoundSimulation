# Roadmap

Where the mod stands and what comes next. Every step is played in real raids before it counts as done, and anything that sounds wrong gets fixed or tuned before moving on.

The detailed checklist is [docs/TRACKER.md](docs/TRACKER.md). The small codes in brackets match it.

## Done and tested in raids

- **Echoes for your own gun.** Off the real surfaces around you, delayed by distance, coloured by material. `(4)`
- **Smooth doorways.** Your shots and the ambience blend from inside to outside instead of switching. `(1, 3)`
- **Outside sound heard indoors.** Wind, rain and background come in through the real windows and doors. `(4b)`
- **Rain on the roof.** Traced from the actual roof, quieter under more floors, louder on tin, fading down stairwells. `(4b, M)`
- **Real materials.** Concrete, metal, wood, glass and the rest use measured acoustic values. `(M)`
- **Fights from the doorway.** A shot from another room comes through the opening it would really use, muffled by its route. `(6.1)`
- **Shots ring out.** A shot is no longer cut off by the same gun's next one. `(1e)`

## Built and played, still being tuned

- **Sound through walls.** How much gets through depends on the wall's material and thickness, and a solid wall can stop it completely. `(6.2)`
- **Explosions.** From the doorway, a thump you feel through walls, echoes, a shock wave that hits harder in small rooms, fragments whizzing past, and concussion by the pressure that reaches you. `(6.2–6.9)`
- **Closed doors and breaking glass.** Doors muffle by material; glass breaking in another room comes from the right place. `(6.5, 6.7)`
- **Other guns echo.** With a voice limit that always keeps your own gun's echoes first. One raid so far; needs bigger fights. `(6.3)`
- **Shot-to-shot variation.** Every shot, yours and others', varies slightly in pitch and volume before the simulation, so no two shots or their echoes sound alike. `(0.7.3)`
- **Bug reports.** One switch, one zip per raid in an obvious folder, with the player's mod list and BepInEx log. `(0.7.0–0.7.2)`

## Built, final test before v1.0

- **Your room builds up blasts.** A blast that gets into a small closed room you're in hits harder and longer. `(0.7.1)`
- **Measured room tail.** Your shot's reverb tail from the room the mod measures: its length, its brightness, and how indoor the shot is. `(0.7.4)`
- **Open air pulls to outdoor, gliding.** Outdoors your shot is firmly outdoor, and the mix glides between shots instead of jumping at roof edges. Reviewed by a second model. `(0.7.4–0.7.6)`
- **Thump depth sliders.** Separate depths for blasts through walls, in open air and while concussed. `(0.7.7–0.7.8)`
- **An easier F12 page.** 11 clear sections, settings grouped by name. `(0.7.9)`
- **Other guns' shots ring out.** A bot's or another player's shot rings out even when they fire again straight away. `(0.7.12)`
- **Ears ring from gunfire without a headset.** Shots near you add up by calibre, muzzle device, ammo and room size; a ring fades in and the world dulls a little. Ported from the author's earlier audio mod. `(0.7.13)`
- **Footsteps and body sounds, as a switch.** Other people's footsteps, gear, looting, voices, meds and food come through the floor, walls and doors, and from the doorway or stairwell. Above or below you sound different: a knock from above, a boom from below, muffling by floors and a height layer, with an F8 test. Off by default. `(0.7.15-0.7.22)`
- **Headsets protect your hearing.** With any headset on, no ear ringing and most of a blast's hearing cut taken away. Each headset will differ after the revamp. `(0.7.10)`
- **A crack before the cut.** A close blast is heard sharp for a split second before your hearing goes, and the pressure hit starts with a crack. `(0.7.22)`

## 0.9 beta: the first public release

Out on 13 Nov 2026 so players on other PCs and maps can report what sounds wrong. Version 1.0 follows their feedback.

## Before v1.0, in order

Feature work is frozen until v1.0: only fixes for what sounds wrong. Each area gets one final test raid instead of a test per change (the list is in the tracker).

1. **Final tests, area by area.** Your own gun; outside sound and rain; other guns; through walls and doors; explosions; hearing; footsteps (decide: on by default, or off and finished after 1.0).
2. **Release build.** Logging off by default with one diagnostics switch, and the load-in spikes gone.
3. **Settings clean-up.** Remove the test switches that are settled, so nobody's settings are reset by an update later; defaults are the tested setup. `(S)`
4. **Fika.** Commit the co-op echoes and play one raid as a Fika host. A two-player raid when a second player is available. `(CO)`
5. **Publish v1.0.** The Forge fixes (plugin ID, licence, install steps, credits) were done for the 0.9 beta. Bring Back Concussion's author has given permission.

## Updates after v1.0, in order

6. **Adrenaline after close calls.** A heartbeat that races after a close blast or heavy fire and settles back, built by the mod itself (no recordings), plus your character's own hard breathing from the game. Gas mask breathing from your character's own breaths through a mask filter. First update after 1.0. `(A)`
7. **Weather.** How rain, wet surfaces and wind change what you hear. `(W)`
8. **Headsets revamp.** Each headset protects you and shapes what you hear by how it's really built, optionally simulated, replacing today's flat headset protection. Its own switch at the top of F12. `(5)`
9. **Footsteps and everyday sounds: tuning.** Built as a switch in 0.7.15 (see above); tuned by ear after raids. `(F)`
10. **Bots feel blasts too.** The same blast pressure that concusses or deafens you does the same to bots, through SAIN where it's installed. In Fika it runs only where the bots are controlled. Its own switch, since it changes how fights play. `(B)`
11. **Supersonic crack.** The crack of a passing bullet by its calibre and speed. `(C)`
12. **Their room rings too.** A shot from someone in another room brings that room's ring with it: a long ring from a big hall next door, a short one from a small room. Today only your own room is measured. For close shooters only, to keep the cost down. `(RT)`

## Maybe, at the end

- **EFT 1.0 recordings on full-auto guns.** One take per round while the trigger is held. A maybe, because it adds load. `(6b)`
- **Every gun.** A pass over each gun's recordings and loudness. `(7)`

## Any time

- **Explosion sound pass.** A listen-through of how blasts sound overall, since they may be divisive. `(user, 10 Nov 2026)`

- The ground between you and a distant shooter (grass versus concrete). `(G)`
- Distant buildings blocking far-away fights.

## Not part of this mod

Stutters and hitches have their own mods: Raid GC Throttle and the bot loading fix.
