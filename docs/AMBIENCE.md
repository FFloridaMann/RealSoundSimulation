# How the game's ambient audio works (and what step 4b could change)

Written for roadmap step 4b (interior/exterior mixing). It covers what the game does (from its code), what our fix 3 already changes, what raid 10 showed, and the options for 4b. **Nothing in this file is built yet**; it is the basis for deciding how to build it.

## 1. The layers

Everything you hear as "the world around you" (not guns, steps or voices) comes from a handful of looping layers plus some one-shots. Each layer is played twice: an **outdoor version** and an **indoor version**, routed to different mixer channels.

| Layer | What it plays | Outdoor version | Indoor version |
|---|---|---|---|
| **Day/night bed** (`DayTimeAmbientBlender`) | The general outdoor background: birds, distant city, insects; day and night clips crossfaded by sun height | Yes | (outdoor only; heard indoors through the outdoor channel) |
| **Wind** (`WindAmbientBlender`) | Wind loop chosen by wind speed from the weather | Yes | Yes (an indoor wind sound) |
| **Rain/snow** (`PrecipitationAmbientBlender`) | Rain loop by intensity (none/light/medium/high) and season | The open-air rain | **The room's own clip**: each baked room can carry its own "rain on this roof" sound and volume. This is the "rain on the roof right above me" sound |
| **Room tone** (`RoomAmbientSoundPlayer`) | A quiet hum/air sound per room | – | Yes, from the room's settings |
| **Weather one-shots** (`WeatherRandomAmbientSoundPlayer`) | Thunder, gusts and other random one-shots, by rain and wind | Yes | Changes with your room type |
| **Positional ambience** (`SoundPlayerRandomPointComponent`, room observers) | Sounds placed at points in the map, some only active when you're in certain rooms | 3D in the world | – |

## 2. What decides how much of each you hear

Two mixer "knobs" per layer-group, driven by one number between 0 and 1 called the **outdoor value** (1 = fully outside):

- **Outdoor channel** (`AmbientSoundBlender.SetOutdoorMixerParams(value)`). The value is turned, by curves set in the game's assets, into the channel's **volume**, a **low-pass** filter (muffling), a **high-pass**, and its **reverb and echo sends**. So a lower value is quieter *and* more muffled at the same time. "Rain heard through a wall" and "rain heard through an open window" are the same knob at different settings.
- **Indoor channel** (`SetIndoorMixerParams(value)`): just the volume of the indoor layers (indoor wind, the room's roof rain, room tone).

### How the game sets the outdoor value

From the baked audio rooms (`EnvironmentSoundBlendSystem`):

- **Outside:** 1.
- **Inside**, two parts:
  - **The room's floor level.** Each room has a baked `OutdoorAmbientVolume`, the minimum outdoor value anywhere in that room. Raid 10: the glass building's room has it at **0**.
  - **Near an opening.** Every 0.2 m you move, a background job finds the best outdoor *portal* (door, window, gap) of the building and its distance from you. The value rises from the room's floor level to 1 as you approach it, over a distance set by that portal's baked "depth". Height difference makes the distance count for more.

  So indoors, the outside is only heard **near the best single opening**, and otherwise at the room's baked level.
- **The indoor layers** are faded in when you enter any indoor room and out when you leave.

### Each room's baked settings (`RoomAmbientData`)

| Setting | Meaning |
|---|---|
| `OutdoorAmbientVolume` | the outdoor value floor in this room (0–1) |
| `OutdoorAmbientHighCutFreq` | intended muffling for this room |
| precipitation clip + `PrecipitationVolume` | which "rain on the roof" sound this room plays, and how loud |
| `UseVolumeRange` + `PrecipitationVolumeRange` | optionally, roof rain gets louder from floor to ceiling as you go up |
| room tone clip + volume, reverb preset | the room's own hum and reverb |

These are set by hand per room by the map makers. Nothing in them is computed from the actual walls, windows or glass.

## 3. What our fix 3 already changes

Fix 3 (smooth doorways) takes over the outdoor value while it's on. It skips the game's own blend and sets:

- **Outdoor value** = the room's floor level, rising to 1 across the **nearest open doorway or window**, measured through its plane, over 1.5 m inside to 1 m outside.
- **Indoor layers** faded the opposite way.
- The mixer snapshot (indoor/outdoor reverb and EQ) is blended across the doorway the same way.

That fixed the *switch* at doorways, which is why thresholds now sound good. But inside a room it uses a **shorter** fall-off than the game's portal depth. Deep in a room, both end up at the room's baked floor level, which is 0 in the glass building.

## 4. What raid 10 showed, and one game bug

1. **Glass building, ground floor (marks 5–9):**
   - The room's floor level is 0, so the outdoor channel was at 0.001: no street rain, no wind.
   - Its windows are mostly permanently broken. Our rays correctly see open air in two directions, but nothing uses that for ambience.
   - The room's own roof-rain clip plays at full, so it sounds like rain on a roof right above you.
2. **"It cuts in and out": a bug in the game's rain player.** When you move into a room whose roof-rain clip is the *same* clip as the last room's, the code that picks the clip says "nothing new". It then sets **no clip** and crossfades the rain down to silence. The next room change brings it back. In rooms where roof rain follows your height, every small volume change does the same. So rain flips on and off as you move between rooms or up and down.
3. **Facade glass** that isn't a breakable window isn't on the collision layers our rays use, so it looks like air to us. Broken windows also look like air, which is correct.

## 5. Options for step 4b

### A. How much of the outside reaches you indoors ("exposure")

| Option | How | For | Against |
|---|---|---|---|
| **A1. Use the game's portals better** | Instead of the single best portal, add up *all* open portals of the room (doors, windows) by size and distance | Uses the map makers' openings; cheap | Only knows openings that were baked as portals; broken-out windows and big glass fronts may not be portals; no glass/open distinction |
| **A2. Rays + room lookup** (my first proposal) | About 24 level rays from your head. For each, find where it first reaches an *outdoor* room, using the game's own room lookup at a few points along the ray. Nearer openings count more. A second ray checks what's in between: glass → muffled, nothing → open | Works from the real geometry: broken windows, glass fronts, holes in walls; distinguishes glass from open; can give a direction | New measure, needs tuning; costs ~24 rays + ~70 room lookups 4×/s (cheap, but not free) |
| **A3. Both** | Take the larger of A1 and A2 | Most robust | Most to tune |

### B. How exposure becomes sound

- **B1.** Exposure drives the existing outdoor value (volume + muffling together, via the game's own curves). Through glass gets a lower value, so it's quieter and more muffled; open gets close to full. Smoothed over 1–2 s.
- **B2.** As B1, but also drive the **indoor layers** down as exposure rises. A room open on two sides should have less "indoor" character.

### C. Roof rain

- **C1. Fix the dropout bug** (always pick the clip, even if it's the same one). Small and safe.
- **C2. Roof rain only under a real roof.** Check whether there's more building above your ceiling. On lower floors of a tall building, turn the roof-rain layer down (rain is then heard through windows, via exposure, not on the roof).
- **C3.** Optionally let exposure blend some *outdoor* rain in instead (rain on the street below through the open side).

### D. Direction (later, harder)

Today the beds are 2D: they come from everywhere. Making the outside come *from* the open side or the windows means placing or panning those sources. That's possible, but a bigger change, and best done after A–C.

### Questions to decide together

1. Exposure: A1, A2 or A3?
2. Should glass let the outside in (quieter, muffled), or count as a closed wall?
3. Roof rain: C1 + C2? And should the open side bring in street rain (C3)?
4. When a room is very open, should the indoor layers (room tone, indoor wind) fade down (B2)?
5. Direction (D): now, or later?

## 6. Decisions (6 Nov, with the user)

1. Exposure: **A2**, rays plus the game's room lookup.
2. Glass: **quieter and muffled, unless broken** (a broken window is open air).
3. Roof rain: **all three**: fix the dropout bug (C1), turn roof rain down under floors (C2), and let open sides bring in street rain (C3).
4. Indoor layers: **depends on where you are**. A room deep inside a building keeps its full indoor character; a room next to windows or an open side loses some of it. Exposure already weights near openings much more than far ones, so the indoor layers fade with exposure, not with the room as a whole.
5. Direction: **yes, from the open sides and windows**, with each opening's sound shaped by what it comes through (open, glass). Roof rain is also shaped by the roof's material: metal rings and is bright, concrete is dull, glass is bright and light.

## 7. Build plan (phased, each phase ends with a test raid)

| Phase | Version | What | Changes sound? |
|---|---|---|---|
| 1 | 0.4.0 (**built**) | **Measure only, plus the rain bug fix.** Exposure (A2): per direction, how far to the outside and whether through glass, open or blocked. Floors above you and the roof's material. All logged (`exposure.csv`, F10 marks) and shown on the overlay. Rain dropout fix (C1) as its own switch | Only C1 |
| 2 | 0.4.2 (**built**, fix 8) | **Mixing.** Exposure drives the outdoor value: volume and muffling together, glass quieter and duller, smoothed 1–2 s. Indoor layers fade with exposure. Roof rain down under floors | Yes |
| 3 | 0.4.2 | **Roof material.** Filter and level on the indoor roof-rain sound from the roof's material (metal, concrete, glass, wood) | Yes |
| 4 | 0.5.0 (**built**) | **Direction.** A few 3D "opening emitters" placed at the strongest open and glass openings, playing the outdoor rain, wind and background, each filtered by what it comes through. The 2D outdoor bed is lowered by the same amount | Yes |

Why measure first: the exposure numbers have to match what you see in game (glass building, deep interior room, top floor, under a metal roof) before anything is mixed from them. Otherwise phase 2 would be tuned against a wrong measure.

### Direction in phase 2: "half the room is open behind me" (6 Nov)

**The question.** If half the room is open and you face the closed side, it should sound open *behind* you.

**The limit.** The game's outdoor beds (wind, rain, day/night background) are flat 2D stereo loops. They play the same whichever way you face. Phase 2 on its own would only make the outside *louder*: open all around you, not behind you. Real front/back placement needs 3D sources at the openings (phase 4).

**Agreed approach: a cheap directional cue in phase 2, true 3D in phase 4.**
- The exposure measure already knows which directions are open and how near. Their weighted sum is an **"open side" direction**, plus how one-sided it is (open on one side only, or all round).
- **Left/right:** the outdoor beds are panned toward the open side, by how one-sided it is. Fully open on your left means mostly left; open all round means centred.
- **Behind:** when the open side is behind you, the outdoor beds get a gentle extra high-cut. Sound from behind is duller because the ear shadows it, which is the same cue the echoes use. Turn around and it brightens.
- Indoors only. Outdoors the beds stay as the game plays them.
- **Phase 4** then replaces this with real 3D emitters at the openings, and HRTF where possible.


**What happens to the phase 2 cue in phase 4.** It's replaced, not thrown away:
- The "open side" calculation from phase 2 is what phase 4 uses to decide where to place the 3D emitters and how loud each one is.
- When the emitters are on, they carry most of the outdoor sound from the real openings, and the flat 2D bed is lowered by the same amount. The pan/dulling then only shapes the small remainder of the 2D bed, or is switched off.
- It stays as a fallback, its own switch: with emitters off, or when voices run short, you still get the phase 2 cue.

## 8. If 0.5.2's opening sounds still don't sound right: review first (agreed 6 Nov)

**Raid 17 result:** the remaining problems (rain loud "inside" the Capital building, the left bias, a sudden drop) happened with 3D off too. They came from the "where is outside" measure, which trusted the game's room map; that map calls interior patches outdoor. So 0.5.3 fixes the measure (open sky required). The review below is still the next step if the *3D placement itself* sounds wrong.

Before another change, stop and review how Tarkov and 3D audio actually work. In particular:
1. **How the game spatialises.** Its sources go through the Meta XR Audio plugin (HRTF, directivity, reverb sends per source). Our opening sounds use plain Unity panning unless 8f is on: left/right only, with no real front/back or up/down, and stereo loops get spread across both ears. That alone may make them sound "in one place" or "on top".
2. **Whether the game already has positional ambience** we should use instead of copying the 2D beds: `AmbientSplineEmitterController` (sounds along splines), `SoundPlayerRandomPointComponent` / `SoundPoint`, and the per-room observers.
3. **Letting Meta's acoustics do the work.** The map has a Meta acoustic map (1 map, 35 geometry objects). Rain sources placed *outside* the openings, running through the Meta spatializer with acoustics on, might be occluded and diffracted through the real windows by Meta itself. That would replace our hand-made direction and level.
4. **Clip format.** Whether the rain and wind clips are mono or stereo, and how Unity's 3D spread treats stereo clips.

Output: a short write-up with options, discussed before any code (as for step 4b).

## 9. Holes in the roof (user question, 6 Nov; planned)

**Today (0.5.3):**
- The exposure rays only look around you (level and +15°), and points under a roof hole are still indoor on the game's room map. So a hole adds no outside sound unless you stand right under it.
- Standing right under it, the straight-up check sees open sky, so roof rain plays at full: rain on a roof that isn't there.
- (Gunshots are fine: fix 3c's roof check sees the open roof and leans toward the outdoor recording.)

**Plan: sky rays.**
- **Rays:** about 8–10 upward rays (around 40°, 65° and straight up, at several azimuths). A ray that reaches open sky without hitting anything is an opening above, and no room map is needed to prove it.
- **Distance to the hole:** ceiling height (from the neighbouring up-rays) ÷ sin(elevation). A near hole counts much more than one across the room.
- **Overhead 3D source:** a seventh opening source, overhead, plays the outdoor beds from the hole's direction. Further holes are duller.
- **Roof rain:** with open sky directly above you, the roof-rain layer is replaced by outdoor rain (you're standing in the rain coming through the hole).

## 10. More rays? (user question, 6 Nov)

**Measured cost so far.** Outdoor exposure: 0.26 ms average and 2.6 ms worst case per update, 4 per second. That's about 1 ms per second of game time, around 0.1% of the frame time at 160 fps. 0.5.3 logs the cost of every part (summary COST section) so this can be decided from numbers.

**More rays help only up to a point.** At 15° spacing, rays are 1.3 m apart 5 m out, so narrow openings further away can be missed, and readings flip as you move. Doubling the rays doubles cost and spikes, and only halves the gap.

**Better (proposed, after 0.5.3 is checked):**
- **Rotate the pattern** a few degrees every update and smooth the results over ~1 s. Over time this covers all angles, like many more rays, at the same cost.
- **Spread each update's rays over several frames,** which removes the 2.6 ms single-frame spike.

## 11. Why the 3D rain copies start at different points (user question, 6 Nov)

**The question:** shouldn't all the sources play the same rain, just at different loudness by distance, obstruction and so on?

**Yes, and they do.** Every source plays the same rain recording, through the same mixer channel, with its loudness set by how open its direction is and its filter by glass. The only thing that differs is *where in the loop* each copy is playing.

**Why that matters:**
- Real rain is countless separate drops. The sound arriving through the left window and through the right window is the same *kind* of sound, but never the identical waveform.
- If several sources play the *identical* waveform in step, the ears merge them into one sound inside your head. Where the copies are a few milliseconds apart, some frequencies cancel and others double up (comb filtering). That gives the hollow, flanging, "two at once" sound from raid 18, and the "on top of me" feeling.
- Starting each copy at its own point in the loop keeps the same rain character, loudness and filtering. But at any moment each source has different "drops", so they add up like real, separate rain from each direction. The brain then places it by which directions are louder, which is the intended effect.
- The rain loop has no beginning or end you can hear, so starting at 7.3 s instead of 2.1 s sounds the same on its own. It only changes how the copies combine.

## 12. Raid 21: why the probe falls short, and a proposal for probe v2 (6 Nov; not built)

**What raid 21 showed (0.5.8, medium rain).**
- **Doubled quiet rain.** Deep inside (Nakatani, exposure 0.05) 12 copies of the outdoor loops were playing at once: 6 sectors × each bed. Each copy starts at a different point in the loop (0.5.4, to stop phasing), so each distinct drop is heard several times a fraction of a second apart. Loud rain hides it; quiet, muffled rain is sparse enough that the repeats stand out ("like two gunshots at once").
- **Garage reads as deep underground.** One open side about 9 m away, one concrete slab above. Only 3 of 48 directions counted as outside (2 more hit a parked car), so the value was 0.14.
  - The walls and the roof are **one collider** (`Road_descent_cover`). The "your own ceiling" rule (made for the Capital lobby) therefore rejected every roofed point the rays reached.
  - The probe only counts **straight lines** to the outside. A bare concrete room with one open side fills up with the sound coming in: it reflects off the walls and builds up, so it is loud everywhere inside, from the opening's direction. There is no physics for that.
- **The probe's limits so far:** only 2 elevations (0° and 15°), so roof holes are missed; it depends on the game's room map, which is wrong in places; two separate probes (enclosure for guns, exposure for ambience) disagree about the same space.

**Proposal: one acoustic probe (v2).**
1. **Directions over the whole sphere** (about 128, evenly spread), rotated randomly every frame, measured continuously. This covers the upper hemisphere too, so roof holes and skylights are found.
2. **"Outside" = sky.** A path reaches the outside where it escapes to open sky. The room map becomes a hint, not the judge, and there's no ceiling-collider rule.
3. **Bounces (1–3).** At each hit the ray reflects, and its energy is reduced by the surface's absorption (bare concrete ~2–5%, fabric ~50%). The energy that escapes to the sky after bounces is the **reflected** share. Sabine's rule for a room: level ∝ open area ÷ total absorption. So a hard room with one opening is loud inside, and a furnished one isn't.
4. **Through glass:** a path can pass intact glass at a loss (`8c`) and get duller, as now.
5. **Outputs, from one measure:**
   - outdoor level (direct + reflected);
   - where it arrives from (the first leg of each path, so sound that comes in by a wall arrives from that wall);
   - enclosure, room size, mean free path, absorption and a reverb-time estimate (for the gunshot blend, fix 5 and echoes);
   - what is above you.
6. **Cost:** batched raycasts (Unity `RaycastCommand`, run on worker threads), so a few hundred rays a second costs less on the main thread than the current ~600.

**Proposal: opening sounds v2 (no duplicate loops).** Instead of 6 copies per bed, at most **one 3D copy** per bed, crossfaded against the flat bed, never several loud at once. Its direction is the probe's arrival direction, and its width follows how spread out the openings are. Better still, if the game's own bed source can be made 3D directly (Unity's 2D↔3D blend on one source), that needs no copy at all. Checking whether the game's spatializer allows this is part of the agreed review.

**Order:** the test route first (so v2 can be judged), then the review of how the game's 3D audio works, then v2 behind a switch.

