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

## Built, not played yet

- **Your room builds up blasts.** A blast that gets into a small closed room you're in hits harder and longer. `(0.7.1)`
- **Measured room tail.** Your shot's reverb tail from the room the mod measures: its length, its brightness, and how indoor the shot is. `(0.7.4)`
- **Open air pulls to outdoor, gliding.** Outdoors your shot is firmly outdoor, and the mix glides between shots instead of jumping at roof edges. Reviewed by a second model. `(0.7.4–0.7.6)`
- **Thump depth sliders.** Separate depths for blasts through walls, in open air and while concussed. `(0.7.7–0.7.8)`
- **An easier F12 page.** 11 clear sections, settings grouped by name. `(0.7.9)`
- **Headsets protect your hearing.** With any headset on, no ear ringing and most of a blast's hearing cut taken away. Each headset will differ after the revamp. `(0.7.10)`

## Before v1.0, in order

1. **Raid testing** of everything in "Built, not played yet". Shots near buildings outdoors sound good enough to publish as they are.
2. **Fika.** Works in Fika from the first release: other players' shots and blasts take the right route, nothing assumes you're the only player, and a Fika headless host doesn't do audio work it doesn't need. Tested in a raid with a second player. `(CO)`
3. **Echo tuning and optimising.** Fair limits for big fights, which Fika raids have more of, and keeping the audio thread fast. `(E)`
4. **Settings clean-up.** Remove the test switches that are settled, before release, so nobody's settings are reset by an update later. `(S)`
5. **Publish v1.0.** Fill the Forge page's links, check the Forge's rules, make the repo public.

## Updates after v1.0, in order

6. **Weather.** How rain, wet surfaces and wind change what you hear. `(W)`
7. **Headsets revamp and ear ringing.** Each headset protects you and shapes what you hear by how it's really built, optionally simulated, replacing today's flat headset protection. Without ear pro, your ears ring from gunfire by the ammo, the suppressor and the gun's real loudness, using Bring Back Concussion's ringing sounds. Both get their own switch at the top of F12. `(5)`
8. **Footsteps and everyday sounds.** Footsteps, healing, eating, drinking and gear handling get the same treatment as gunfire: from the doorway they come through, muffled by their route and by walls. `(F)`
9. **Bots feel blasts too.** The same blast pressure that concusses or deafens you does the same to bots, through SAIN where it's installed. In Fika it runs only where the bots are controlled. Its own switch, since it changes how fights play. `(B)`
10. **Supersonic crack.** The crack of a passing bullet by its calibre and speed. `(C)`

## Maybe, at the end

- **EFT 1.0 recordings on full-auto guns.** One take per round while the trigger is held. A maybe, because it adds load. `(6b)`
- **Every gun.** A pass over each gun's recordings and loudness. `(7)`

## Any time

- The ground between you and a distant shooter (grass versus concrete). `(G)`
- Distant buildings blocking far-away fights.

## Not part of this mod

Stutters and hitches have their own mods: Raid GC Throttle and the bot loading fix.
