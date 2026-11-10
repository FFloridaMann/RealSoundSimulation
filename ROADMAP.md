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

## Next, in order

1. **Raid testing and tuning** of everything in the section above.
2. **Weather.** How rain, wet surfaces and wind change what you hear. `(W)`
3. **Headsets and ear ringing.** A headsets revamp: your ear protection shapes what you hear, including how concussion hits you. Without ear pro, your ears ring from gunfire by the ammo, the suppressor and the gun's real loudness, using Bring Back Concussion's ringing sounds. Both get their own switch at the top of F12. `(5)`
4. **Footsteps and everyday sounds.** Footsteps, healing, eating, drinking and gear handling get the same treatment as gunfire: from the doorway they come through, muffled by their route and by walls. `(F)`
5. **Bots feel blasts too.** The same blast pressure that concusses or deafens you does the same to bots, through SAIN where it's installed. Its own switch, since it changes how fights play. `(B)`
6. **EFT 1.0 recordings on full-auto guns.** One take per round while the trigger is held. `(6b)`
7. **Supersonic crack.** The crack of a passing bullet by its calibre and speed. `(C)`
8. **Every gun.** A pass over each gun's recordings and loudness. `(7)`
9. **Final echo tuning and optimising.** Fair limits for big fights, and keeping the audio thread fast. `(E)`
10. **Co-op.** Players hear each other's echoes. `(CO)`

## Any time

- The ground between you and a distant shooter (grass versus concrete). `(G)`
- Distant buildings blocking far-away fights.

## Near the end

- **Settings clean-up.** Remove the test switches that are settled. The F12 page was already regrouped in 0.7.0. `(S)`

## Not part of this mod

Stutters and hitches have their own mods: Raid GC Throttle and the bot loading fix.
