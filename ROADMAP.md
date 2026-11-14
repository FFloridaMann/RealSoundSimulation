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

**Not in 0.9.x: footsteps and everyday sounds.** Footsteps, gear, looting, healing and eating play as recorded; the switch **General - footsteps and handling like EFT 1.0** picks between Sound Engine Backport's backported EFT 1.0 footstep settings (on, the default) and vanilla SPT (off). The simulated version is built (branch `body-sounds`) and comes after 1.0, below.

**0.9.1:** fixes only: the debug overlay (F9, it clashed with Fika) and ray view (F11) are developer tools now, and the bug report key (F10) only works with bug report logs on.

## The road to 1.0

No new features until 1.0, only fixes:

1. **Better raid start loading.** The mod's work while a raid loads in gets smaller and smoother.
2. **Final testing,** one raid per area: your gun, outside sound and rain, other guns, walls and doors, explosions, hearing.
3. **Polishing,** including removing settled test switches, so later updates don't reset anyone's settings.
4. **Bug fixes based on beta reports,** fixed or listed as known issues.
5. **Fika compatibility:** fixing any issues reported in Fika. (Players hearing each other's echoes comes after 1.0.)

## After 1.0

In this order:

1. **Weather.** How rain, wet surfaces and wind change what you hear.
2. **Headsets revamp.** Each headset protects you and shapes what you hear by how it's really built, including how grenades sound through it.
3. **Footsteps and everyday sounds.** Footsteps, gear, healing and eating heard through the doorway they come through, muffled by walls and floors, and from above or below. Already built, held back until it sounds right.
4. **Bots feel blasts too.** The same blast pressure that concusses you concusses bots, through SAIN where it's installed.
5. **Supersonic crack.** The crack of a passing bullet by its calibre and speed.
6. **Room reverb for other guns.** A shot fired in a big hall, stairwell or tunnel brings that room's reverb with it, heard through the doorway like the shot. Your own gun already has this; other guns already have echoes, but not their room's reverb.
7. **Big fights.** Echo tuning so huge firefights stay clear and cheap.

Any time: ear ringing from blasts as well as gunfire, the ground between you and a distant shooter (grass versus concrete), distant buildings blocking far fights, and rooms with more character of their own.

Maybe, at the end: EFT 1.0 recordings on full-auto guns, and a pass over every gun's recordings.

## Not part of this mod

Stutters and hitches have their own mods: Raid GC Throttle and The Bot Loading Freeze Fix.
