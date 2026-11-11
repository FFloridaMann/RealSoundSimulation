# Real Sound Simulation

A simulated audio overhaul for SPT. The game picks a recording and a volume; this mod works out how each sound would actually travel through the map to reach you, and plays what would arrive.

## Why I made it

I couldn't stand walking into a room, or back out of one, and having the indoor and outdoor audio cut straight over the moment I crossed the doorway. It broke the immersion every time. And I couldn't stand hearing a fight and not being able to tell where it was really coming from.

I shoot regularly, so this is built from what real gunfire and blasts sound like, backed by many hours of research into how sound and blast waves travel through rooms, doorways and walls, and across open ground: off hills, cliffs and buildings, and around their corners. Next on the roadmap are what the ground itself does to a distant shot, forests and weather.

## How it works

Thousands of times a second it traces rays through the map's real geometry: walls, floors, ceilings, doorways, windows and open sky.

Every material in the game is matched to how that material behaves acoustically in the real world, from published measurements and acoustics research: how much sound it absorbs at each pitch, how much it reflects, and how much gets through it at what thickness. Concrete, stone, metal, wood, glass, tile, fabric, soil, gravel and the rest each sound like themselves: a bare concrete stairwell rings, sound dies in soft furnishings, and a thin metal wall lets far more through than a concrete one.

From that it works out, for each sound that matters, which paths reach you, how long they take at the speed of sound, which way they arrive from, and how much each wall, corner and doorway takes off.

## What you'll notice in a raid

- **Inside and outside, without the pop.** Outdoor sound fades in and out as the space around you really opens and closes, through the windows and doors it can get through. No more outdoor audio snapping on and off as you step through a doorway.
- **Your gun.** Echoes come back off the surfaces actually around you, delayed by their real distance. A concrete stairwell rings hard, a room full of soft furnishings soaks more of it up, an open field barely answers. Fire out of a window and you hear the shot leave the building.
- **Fights nearby.** A firefight in the next room reaches you through the doorway it would really come through. The longer and more twisted its route, the duller it gets. Behind a wall, how much gets through depends on what the wall is made of and how thick it is, and a solid wall can leave only a low thump, or nothing.
- **Grenades.** The blast comes from the opening it travels through. A blast behind a wall you still feel, as a deep thump through the wall that arrives before the sound around the doorway. A grenade that goes off in a small closed room hits harder than one in a big hall, and fragments whizz past along their real flight path.
- **Rain and weather.** Rain is traced down from the real roof. You hear how many floors are above you and what the roof is made of, and it fades as you go deeper inside or down a stairwell.
- **Everything else.** Closed doors muffle by material, glass breaking in another room comes from the right place, and footsteps and handling sound like EFT 1.0.

## Concussed or just deafened: decided by the blast that reaches you

This mod decides whether a grenade concusses you, only deafens you, or leaves you alone. It doesn't go by straight-line distance. It works out the blast pressure that actually reaches you, by whichever way is stronger:

- **Through the openings.** A blast that comes through a doorway or around corners loses pressure with every turn, and a closed door in the way takes a lot more.
- **Straight through the wall.** The wall takes pressure off by its material and thickness. A thin wall right beside a grenade still hits you hard; it takes enough wall, and no way round, to stop a blast completely.

Then the room the grenade goes off in: a small closed room traps the pressure and hits harder, and for longer, than a big hall or open air.

That pressure decides what happens:

- **Enough to concuss you:** full concussion. How strong it is follows the pressure; how long it lasts follows how small and shut in the blast's room is, and gets shorter the further round it had to come.
- **Less, but still close:** you're deafened. Your hearing cuts out and your ears ring, but your vision stays clear.
- **Weaker still, or stopped by walls:** nothing.

When your hearing cuts, the world goes quiet at once, but the blast itself fades out naturally instead of snapping off, and you still feel its thump.

Both parts have their own switch at the top of F12 (**Concussion - by blast pressure** and **Concussion - soft hearing cut**), and how strong blasts count can be set too. The concussion effect itself, the ear ringing and their sounds come from [Bring Back Concussion](https://forge.sp-tarkov.com/mod/2550/bring-back-concussion) by harmony, which is needed for this part.

## Performance

A lot of the work went into making this cheap. The tracing runs on spare CPU cores alongside the game, answers are reused instead of traced again, and nothing is allocated on the audio thread.

Measured on my PC (Ryzen 7 9800X3D, 8 cores and 16 threads, RTX 5070 Ti) in a 20-minute Factory raid with up to 8 bots, Sound quality on High:

- **FPS cost: about 2%.** 122 FPS with the mod, about 125 without (worked out from the mod's own timing).
- **CPU:** about 2% of one core, around 9,500 rays a second, and the game never had to wait for it.
- **Hitches:** in a raid checked frame by frame with a profiler, the mod caused none during play. Its single biggest cost was about 12 ms, once.

It hasn't been measured on lower-end CPUs yet. If you have fewer cores:

- Set **Sound quality** (F12, 01 GENERAL) to Medium or Low.
- Lower **Echoes - per shooter** (03 OTHER GUNS) and **Most voices for other gun echoes** (10 PERFORMANCE).
- Turn off **Echoes - far (buildings and hills)** (02 YOUR GUN, under Advanced settings).

If it still sounds off or costs too much on your PC, [Sound Engine Backport](https://sp-mod.com/mod/3314/sound-engine-backport) by red_the_fox with [True Acoustics](LINK-HERE) by Kaan is a lighter way to get better audio, and they sound great. Use them instead of this mod, not alongside it.

**My other performance mods.** I recommend these alongside it for a smoother SPT experience:

- **[The Bot Loading Freeze Fix](LINK-HERE):** about two minutes into every raid, the game builds a batch of spare bot profiles all at once and the whole game freezes for up to about 1.3 seconds. This spreads that background loading over a second or so instead, a few milliseconds a frame, so the freeze is gone. Bots load and play exactly as before; nothing changes about which bots you get or what they carry.
- **Raid GC Throttle** (coming soon): fewer garbage-collection hitches in raid.

## Coming next

- **Ear ringing from gunfire.** Without ear pro, firing indoors will make your ears ring, by the ammo, whether you're suppressed, and the gun's real loudness. It uses Bring Back Concussion's ringing sounds. It will have its own switch at the top of F12.
- **Headsets revamp.** Your ear protection will shape what you hear, including how concussion and ringing hit you. Its own switch at the top of F12.
- **Footsteps and everyday sounds.** Footsteps, healing, eating, drinking and gear handling will travel through the map like gunfire does: from the doorway they come through, muffled by their route and by walls.
- **Bots feel blasts too.** The same blast pressure that concusses or deafens you will do the same to bots, through SAIN where it's installed. Its own switch at the top of F12.
- **The room you're in.** Today only the blast's room counts; a blast getting into a small room you're standing in should build up too.
- **Fika** support.

## Good to know

- **Gun sounds come from SPT's own recordings.** This mod simulates how they travel; it doesn't replace them. If a gun sounds bad here, it sounds bad in vanilla too.
- **Grenades: Real Grenade Simulation (coming soon).** My next mod gives each grenade its real number of fragments (vanilla throws 30, mostly overhead) and the natural randomness of a real explosion; this mod makes you hear them whizz past and hit things in other rooms. Vanilla grenades work too, with fewer fragments to hear.
- **Works with weapon mods.** It changes how sound travels, not the guns, so modded weapons get the same treatment. I play it with a large set of weapon packs.
- **Rain is still being tuned.** Everything here has had extensive testing and tuning in raids, and rain has been the hardest to get right. Many surfaces in the game's maps carry no material, some upper floors and roofs have no collision for the rays to find, and buildings are put together from pieces that don't always match the rooms you see. Rain sounds right in most places, but some buildings still have odd spots.
- Work in progress: built and tested by one player, so expect rough edges.

## Found a bug?

1. In F12 → Real Sound Simulation → **01 GENERAL**, turn on **Bug report - logs**.
2. Play one raid. Wherever something sounds wrong, press **F10** on the spot. You can change the key right below the switch (**Bug report - mark key**).
3. After the raid, open your SPT folder (the one with `EscapeFromTarkov.exe`), then the **RealSoundSimulation Logs** folder.
4. Send me the newest **Bug report** zip, and say roughly when and where it happened.

Expect slightly lower FPS during that raid. When it ends, stay on the raid results screen for a few seconds before closing the game, so the zip can finish. Then turn Bug report logs off again; it writes several MB per raid.

I'm a solo developer with a full-time job, so please be patient with requests and bug fixes. Every report gets read.

## Credits

- **Concussion and ear ringing:** the concussion effect, the ear ringing and their sounds come from [Bring Back Concussion](https://forge.sp-tarkov.com/mod/2550/bring-back-concussion) V1.2.1 by harmony. This mod decides who gets concussed or deafened, and how the sound fades.
- **Improved paths between rooms and EFT 1.0 audio:** the routes sound takes between rooms, the footstep and handling settings, and the optional EFT 1.0 gun recordings come from [Sound Engine Backport](https://sp-mod.com/mod/3314/sound-engine-backport) by red_the_fox, used with his permission. Thank you.

## How it was made

This mod has been in the works for a while. It grew out of smaller audio mods I made first, each one teaching me what the next needed.

**Development: 70 hours**, 84 versions and 90 test raids.

## Enjoying it?

If this mod makes your raids sound better, consider [buying me a coffee](LINK-HERE). It helps keep the updates coming: ear ringing, the headsets revamp and Fika support are next.
