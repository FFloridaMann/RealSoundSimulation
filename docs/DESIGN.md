# Current design: how indoor and outdoor sound works

**State: 0.5.8** (6 Nov 2026), with the default settings (which are the tested setup). This doc describes what the plugin does **now**. It is not a history: why things changed, and what was tried first, is in [CHANGELOG.md](CHANGELOG.md), [FIXES.md](FIXES.md) and [AMBIENCE.md](AMBIENCE.md). When the code changes, this doc changes in the same commit.

---

## 1. The idea in one picture

The game decides "inside or outside" mostly from hand-placed data: baked audio rooms, trigger volumes and doorways. That data switches at boundaries, is sometimes wrong (lobbies marked outdoor, glass towers marked sealed), and has no idea which way the outside is. The plugin **measures the real geometry around you** and uses those measurements to drive the game's own sound systems.

```
 MEASURE (rays against the map)          DECIDE                                YOU HEAR
 ───────────────────────────────         ──────────────────────────────        ─────────────────────────────
 Outdoor exposure probe (48 dirs) ──┬──> outdoor value (0..1) ───────────────> outdoor ambience level + muffling
   how much outside reaches you     ├──> enclosed (0..1) ────────────────────> opening sounds, direction cue
   and through what                 ├──> indoor layers (0..1) ───────────────> room tone, indoor wind, roof rain
                                    └──> 6 sectors (where the outside is) ───> 3D outdoor sound from the openings
 Above you (5 up-rays) ─────────────────> floors above ───────────────────────> roof rain level
 Doorways (game data) ──────────────────> outdoorness at a doorway ──────────> indoor/outdoor mix snapshot (reverb)
 Enclosure probe (32 rays, sphere) ─────> room, size, hard room ─────────────> your gunshot: indoor/outdoor recording
   + muzzle and aim cone                                                         mix, room reverb boost
 Surfaces + materials ──────────────────> reflections ────────────────────────> echoes of your own shots
```

Everything is switchable in F12 (section "2. Fixes"); with every fix off you get the game's own audio.

---

## 2. Measurements

### 2.1 What the game already knows (used as input)
- **Baked audio rooms.** Each point of the map belongs to an indoor or outdoor room. Each indoor room has a designed outdoor-ambience level (`OutdoorAmbientVolume`, the "room floor"), and its own rain-on-roof clip and room tone.
- **Doorways (portals).** Openings between indoor rooms and the outside, with open/closed state.
- **Trigger volumes.** Switch the whole mix between the Indoor and Outdoor snapshots.

### 2.2 Outdoor exposure probe — "how much of the outside reaches you, and through what"
`Acoustics/OutdoorExposure.cs`. Runs everywhere, indoors and out (setting `Outdoor exposure probe`, on).
- **48 directions:** 24 around you (15° apart) × level and 15° up.
- **Continuous:** about 288 rays per second, round robin, so each direction is re-measured about 6 times a second. Each re-measure is nudged randomly by up to ±7.5°, so over time every angle is covered and the value changes one direction at a time (no jumps).
- **Per direction:** a ray to the first sound-blocking wall (max 40 m). Points along it (1.5, 3, 5, 8, 12, 18, 25, 35 m, then 3 halvings) are checked: is this point **outside**? Outside means the game's room map says outdoor **and** there is open sky above, or a roof that is not your own ceiling ("covered outside", half weight: canopies, overhangs). Interior patches that the room map wrongly calls outdoor (Capital lobby) share your ceiling, so they don't count.
- **What's in between:** a second ray up to that point, on the layers sound passes through, classifies the path:

  | Path | Counts as | Examples |
  |---|---|---|
  | Open | 1 | doorway, open side, nothing in the way |
  | Broken | 1 | shot-out window (only frame shards left) |
  | Bars | 0.8 | window bars, grilles, fences |
  | Glass | `8c` (0.45) | intact window or glass wall |
  | Other | 0.5 | railings, props |
  | Blocked | 0 | never reaches outside |

- **Nearness:** each direction counts 1 / (1 + distance to the outside / 15 m) × its path weight.
- **Outdoor value** = 1 − e^(−4 × open) + `8c` × (1 − e^(−4 × glass)), never below the room floor indoors. Open air ≈ 1; a room with a broken window wall ≈ 0.3–0.6; a deep interior ≈ the room floor.
- **Open side:** the weighted direction of the outside, and how one-sided it is (0 all round, 1 one side only).
- **6 sectors:** fixed 60° world sectors, each summing its directions' weight, direction and glass share. The opening sounds play from these.

### 2.3 Above you
Every 0.25 s, 5 rays straight up (median, so one beam doesn't flip it): the first ceiling and its material, **floors above** (surfaces above that ceiling; −1 = open sky) and the roof material.

### 2.4 Enclosure probe — "what kind of space am I in" (for gunshots)
`Acoustics/EnclosureProbe.cs`. 32 rays over a sphere every 0.1 s, out to 40 m. Ground and floors are ignored (they're in every recording already).
- **Room** = wall closure × roof closure. The roof only counts if there is one right above your head (zenith test), so a canopy alone or walls alone are not a room.
- **Surface closure:** solid = 1; glass, thin wood, cardboard, fabric = `3f` (0.5); fences, grates, shattered glass = 0.1. Windows' blocking colliders are registered as glass.
- **Size factor:** 0.5 when walls average 2 m away, 1 from 6 m.
- **Hard room:** how closed × share of bare concrete, stone, tile, thick metal (stairwells, corridors).
- **At the muzzle** (when you fire): the same measure from the muzzle, and a 9-ray **aim cone** along your shot.

### 2.5 Surfaces
`Acoustics/Surfaces.cs`: the game's bullet material tags (concrete, metal, wood, glass, soil, fabric…), cached per collider. Used for closure (2.4) and for echo loss and brightness (5.3).

---

## 3. Ambience: what you hear when nobody is shooting

### 3.1 The game's layers
- **Outdoor beds** (flat 2D stereo loops): wind, outdoor rain, day/night background. Their volume and muffling come from one **outdoor value** (0..1) through the game's own curves (volume, low-pass/high-pass, reverb sends).
- **Indoor layers:** room tone, indoor wind, **roof rain** (the indoor rain player, rain hitting the roof above you).

### 3.2 What the plugin sets (fix 8, with fix 3)
All of these follow **one measure** (the outdoor value, 2.2) with **one smoothing time** (0.35 s), indoors and outdoors, so nothing switches at a doorway and nothing moves at a different speed from the rest.

| What | Rule | Setting |
|---|---|---|
| Outdoor level and muffling | the outdoor value; indoors at least the room floor | `8c` for glass |
| **Enclosed** (0..1) | 1 while the outdoor value ≤ 0.75, fading to 0 in open air | — |
| **Opening sounds** | `8e` × enclosed of the outdoor beds plays as 3D sound from the 6 sectors (power split by each sector's share, so total loudness is unchanged); the flat bed keeps the rest. Each sector's copy starts at a random point in the loop (no doubling). Glass sectors duller (to 2.5 kHz). 4 m out, 120° wide | `8e` 0.7, `8f` HRTF on |
| **Direction cue** on the remaining flat bed | panned toward the open side and dulled (to 5 kHz) when it's behind you, × `8a` × how one-sided × enclosed | `8a` 0.6 |
| **Indoor layers** | 1 when enclosed, 0 in open air; `8d` sets how early openings fade them | `8d` 0.6 |
| **Roof rain** | full with only the roof above; × `8b` with one floor above; off with two or more. A new level must hold 0.5 s. Written directly to the rain player's volume | `8b` 0.3 |
| **Rain not cutting out** | the game's rain player picks "no clip" when the next room uses the same clip, fading the rain to silence; fixed | `7` |

**Example — walking from the street into a glass-fronted hallway in rain:**
outside, the outdoor value ≈ 1: beds as the game plays them, no indoor layers. Through the doorway the value drops smoothly as fewer directions reach outside; the beds get quieter and duller; "enclosed" rises, so part of the rain moves to 3D sources in the directions of the doorway and windows; roof rain and room tone fade in. Deep inside, under several floors, roof rain is off and the outside is heard only through the openings, from their direction.

### 3.3 The mix snapshot at doorways (fix 3)
Separately, the game switches the whole mix (mostly reverb) between its Indoor and Outdoor snapshots at trigger volumes. Fix 3 replaces the switch with a blend by **outdoorness at the nearest doorway**: 0 at 1.5 m inside, 0.5 on the doorway plane, 1 at 1.0 m outside (`3a`, `3b`); the same doorway is kept through a crossing. With fix 8 off, fix 3 also drives the ambience by that doorway value (the older, simpler behaviour).

---

## 4. Other people's sounds
- **Gun mixer (fix 1):** a remote gun switches to its muffled channel above occlusion 0.60 and back below 0.45, held 0.5 s, so a gun at the line doesn't flicker.
- **Obstruction score (fix 2, ParallelPaths):** a sound is blocked only if every path (through, around, reflected) is; a path search that fails no longer counts as "blocked".
- **Off-map sounds (fix 4):** sounds outside the baked map (SPTBattleAmbience's distant battles) count as outdoor instead of fully blocked; indoors they fade by the doorway value.
- **Not yet:** remote sounds still come through walls rather than from the doorway, and too much gets through hard walls (step 6); bot gunshots change when *you* cross a threshold; nothing past ~250 m is obstructed; bot shots get no echoes.

---

## 5. Your own gunshots

### 5.1 Indoor/outdoor recording mix (fix 3c, Enclosure mode)
Every gun has indoor and outdoor recordings. The game plays one, picked by trigger volumes. The plugin plays **both, mixed** (the gun player's free second slot), with the indoor share from the enclosure probe:
1. **Room** (2.4), mapped 0.15 → open, 0.65 → fully indoor.
2. **Aim:** the aim cone's closure, blended in by `3e` (0.4) × roof, so firing into a room leans indoor and firing out leans outdoor.
3. **Size:** small furnished spaces get less of the indoor recording (its long room tail suits halls, not closets): `3i` Soft keeps at least 85%. Hard rooms (bare stairwells) are exempt.
4. **Head and muzzle:** each value is head × 0.3 + muzzle × 0.7 (`3g`). The muzzle's side of a threshold decides most.
Applies to single shots, full-auto (picked at the start of a burst) and the burst tail (picked fresh).

### 5.2 Room reverb (fix 5)
The game plays a hidden reverb-only copy of your shot through Meta XR's room acoustics. Its reverb sends get `5` (0 dB) + `5a` (+6 dB) × hard room, so stairwells and corridors ring more.

### 5.3 Echoes (fix 6)
Up to 4 echoes per shot (`6b`; up to 3 more in hard rooms), each a delayed, quieter, duller copy played from the surface it came from:
- **Sources:** mirror reflections off walls and ceilings, scattered returns from corners and clutter, what you fire at (aim cone, including glass and bars), and far faces 30–250 m away (16 level rays, re-scanned when you move 5 m).
- **Level:** distance (−20·log10(path / 2 m)), muzzle directivity (`6c`, 10 dB quieter straight behind), surface loss, `6a` trim, + `6e` (4 dB) × hard room.
- **Colour:** the lowest of air absorption for the path, the surface's brightness, and head shadow from behind.
- **Timing:** path ÷ 343 m/s after the shot; full-auto loops and tails get echoes too.
- Played on the game's gunshot mixer channel, so volume and the in-game headset treat them like the shot.

---

## 6. Known limits (now)
Things this design does not do yet. Each has a place in [ROADMAP.md](ROADMAP.md).
- **Roof holes:** a bombed-out patch of roof isn't found; "floors above" only looks straight up (AMBIENCE.md §9).
- **Roof material:** roof rain sounds the same under concrete, metal or glass (step 4b phase 3).
- **Only two elevations** (0° and 15°) for exposure; openings well above or below you count less than they should.
- **Walls let too much through** for other people's sounds; no material transmission yet (step 6).
- **Material behaviour is rough** (fixed tables), and rain masking of echoes isn't modelled (weather research).
- **Indoor and outdoor recordings are mixed by straight volume**, so exactly halfway can be up to ~3 dB quieter.
- **No HRTF on echoes** (left/right plus a duller back only).

---

## 7. Cost
Main thread, from the raid summary's COST section (raid 20, 0.5.6): about 15.6 ms per second of game time in total (~1.6% of one core). Biggest regular parts: exposure probe (continuous, ~290 rays/s), enclosure probe (10/s), per-shot echo search. The diagnostics' source scan costs ~51 ms once every 15 s (kept by choice, logging only). One-off at raid start: window registry ~60 ms, ambience setup ~140 ms.

---

## 8. Settings → what they change

| Setting | Part | Default |
|---|---|---|
| `1`, `1a`–`1c` | remote gun muffle switching (§4) | on, 0.60 / 0.45 / 0.5 s |
| `2 Obstruction score` | remote sound blocking (§4) | ParallelPaths |
| `3`, `3a`, `3b` | mix snapshot at doorways (§3.3); needed by fix 8 | on, 1.5 m / 1.0 m |
| `3c`, `3d`, `3e`, `3f`, `3g`, `3i` | your gunshot recording mix (§5.1) | on, Enclosure, 0.4, 0.5, 0.7, Soft |
| `4` | off-map sounds (§4) | on |
| `5`, `5a` | your gunshot room reverb (§5.2) | 0 dB, +6 dB |
| `6`, `6a`–`6e` | your gunshot echoes (§5.3) | on, 0 dB, 4, 10 dB, far on, +4 dB |
| `7` | rain not cutting out (§3.2) | on |
| `8`, `8a`–`8f` | ambience from the exposure probe (§3.2) | on, 0.6, 0.3, 0.45, 0.6, 0.7, HRTF on |
| Diagnostics: `Enclosure probe`, `Outdoor exposure probe` | the measurements (§2); fixes 3c and 8 need them | on |
