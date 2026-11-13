# Roadmap

Where Real Sound Simulation stands and what comes next. Everything is played in real raids before it counts as done, and anything that sounds wrong gets fixed before moving on.

The step-by-step checklist is [docs/TRACKER.md](docs/TRACKER.md), and the reasons and designs behind each item are in [docs/dev/ROADMAP-DETAILED.md](docs/dev/ROADMAP-DETAILED.md).

## Out now: 0.9 beta

The first public release (13 Nov 2026). What it does:

- **Your gun:** echoes off the real surfaces around you by material, a reverb tail measured from the room you're in, and a smooth blend between indoors and outdoors.
- **Other guns:** heard through the doorway or opening they really come through, muffled by their route, with their own echoes; your gun's echoes always come first.
- **Walls and doors:** how much gets through depends on the material and thickness, a solid wall can stop a sound, and closed doors muffle by material.
- **Explosions:** from the opening, a thump you feel through walls, echoes, a shock wave that hits harder in small rooms, and fragments whizzing past.
- **Hearing:** concussion decided by the blast pressure that reaches you, a hearing cut that doesn't chop sounds off, headsets that protect you, adrenaline after a close blast, and optional ear ringing from gunfire.
- **Outdoors and rain:** wind, rain and background through the real windows and doors, and rain on the roof traced from the actual roof.

It's been tested solo by one player on one PC, and not yet in Fika.

## The road to 1.0

No new features until 1.0, only fixes:

1. **Your feedback.** Bug reports from the beta, fixed or listed as known issues.
2. **Final tests,** one raid per area: your gun, outside sound and rain, other guns, walls and doors, explosions, hearing.
3. **Polish.** The mod does no diagnostics work at all unless you turn on bug report logs, and the small loading spikes go.
4. **Settings clean-up.** Settled test switches removed, so later updates don't reset anyone's settings.
5. **Fika.** Tested as a host, and players hear each other's echoes.

## After 1.0

In this order:

1. **Weather.** How rain, wet surfaces and wind change what you hear.
2. **Headsets revamp.** Each headset protects you and shapes what you hear by how it's really built, including how grenades sound through it.
3. **Footsteps and everyday sounds.** Footsteps, gear, healing and eating heard through the doorway they come through, muffled by walls and floors, and from above or below. Already built, held back until it sounds right.
4. **Bots feel blasts too.** The same blast pressure that concusses you concusses bots, through SAIN where it's installed.
5. **Supersonic crack.** The crack of a passing bullet by its calibre and speed.
6. **Their room rings too.** A shot from a big hall next door brings the hall's ring with it.
7. **Big fights.** Echo tuning so huge firefights stay clear and cheap.

Any time: ear ringing from blasts as well as gunfire, the ground between you and a distant shooter (grass versus concrete), distant buildings blocking far fights, and rooms with more character of their own.

Maybe, at the end: EFT 1.0 recordings on full-auto guns, and a pass over every gun's recordings.

## Not part of this mod

Stutters and hitches have their own mods: Raid GC Throttle and The Bot Loading Freeze Fix.
