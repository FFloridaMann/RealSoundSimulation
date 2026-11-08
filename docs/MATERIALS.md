# Materials: real acoustic values for the game's surfaces

Research for roadmap step M (8 Nov 2026). **Nothing here is in the game yet.** It compares what the mod assumes with measured or calculated values and proposes what to change.

## What the research found

1. **Thin wood, glass and steel lose bass, not treble.** Our tables dull them (thin wood's echo is cut at 6 kHz). Measured, a 3/8" plywood panel absorbs 0.28 at 125 Hz and 0.11 at 4 kHz, and a window 0.35 and 0.04; Meta's table says the same, and adds steel. Real echoes off them are thin and bright. This is the biggest audible correction, and it needs a high-pass on the echo voices, which only have a low-pass today.
2. **Hard surfaces lose almost nothing per bounce.** Concrete absorbs about 2%, or 0.09 dB. Echoes use 0.5 dB. Probe v2 already uses the right value.
3. **Ground needs a value per frequency.** Soil, mud and grass share one value (−4 dB) today. Grass loses 12% of the energy at the bottom and 87% at 775–3400 Hz, mud about 15–27%, and snow 85% or more above 176 Hz. See "The ground and the gunshot" below: the ground changes tone, not echoes.
4. **Walls behave like a low-pass, which supports your 50 Hz thump.** Below a panel's coincidence dip, transmission loss rises about 6 dB per octave. A 150 mm concrete wall takes off about 46 dB at 125 Hz and 76 dB at 4 kHz, so only the lowest part of a gunshot gets through.
5. **Thin walls block more than the probe assumes.** Thin wood passes 10 dB less today. Real values are about 14–26 dB through the low and mid bands.

## Limits of this data

- **The game's tags don't say how thick anything is.** "Concrete" could be a 100 mm wall or a 400 mm slab. Each transmission row assumes one thickness; doubling it adds about 6 dB.
- **Reflection values** come from Meta's table, checked against published measurements. **Wall rows** are calculated from physics and spot-checked against measured slabs and STC ratings.
- **Real walls leak.** Doors, gaps and paths through the structure usually make field results several dB worse than these lab-style numbers.
- **Scattering:** I found no usable values per surface, so the current scattering values stay.
- **Ground** varies with compaction and moisture, and reflects more at shallow angles than these values suggest.

## Reflection: absorption by frequency

**Main source: Meta XR Audio's own material table**, the spatializer the game already runs. It gives a reflection value per material in four bands; the table below shows absorption (1 − reflection), the share of energy lost per bounce. Where the two overlap, it agrees with the published measurements: concrete 0.01–0.02, unglazed brick 0.03–0.07, plywood 0.28 falling to 0.11, window glass 0.35 falling to 0.04.

| Game tag | Meta material | 0–176 Hz | 176–775 | 775–3.4k | 3.4k+ | Echoes today (dB, low-pass) | Probe today |
|---|---|---|---|---|---|---|---|
| Concrete | Concrete | 0.01 | 0.02 | 0.02 | 0.02 | −0.5, 20 kHz | 0.02 |
| Tile | CeramicTile | 0.01 | 0.01 | 0.02 | 0.02 | −0.5, 20 kHz | 0.02 |
| Stone | Stone | 0.02 | 0.02 | 0.04 | 0.05 | −0.5, 20 kHz | 0.03 |
| Asphalt | ConcreteRough | 0.01 | 0.04 | 0.08 | 0.10 | −0.5, 20 kHz | 0.03 |
| GenericHard | ConcreteRough | 0.01 | 0.04 | 0.08 | 0.10 | −0.5, 20 kHz | 0.04 |
| MetalThick, MetalNoDecal | Steel | 0.21 | 0.16 | 0.07 | 0.02 | −0.5, 20 kHz | 0.05 |
| MetalThin | Steel | 0.21 | 0.16 | 0.07 | 0.02 | −1, 16 kHz | 0.10 |
| GarbageMetal | Steel | 0.21 | 0.16 | 0.07 | 0.02 | −1, 16 kHz | 0.15 |
| Glass | Glass | 0.34 | 0.20 | 0.08 | 0.08 | −1, 20 kHz | 0.04 |
| WoodThick | WoodThick | 0.19 | 0.10 | 0.06 | 0.05 | −1.5, 9 kHz | 0.10 |
| WoodThin | WoodThin | 0.41 | 0.14 | 0.08 | 0.06 | −3, 6 kHz | 0.15 |
| Plastic | GypsumBoard | 0.28 | 0.07 | 0.07 | 0.09 | −2, 10 kHz | 0.08 |
| Rubber, Tyre | Rubber | 0.05 | 0.08 | 0.06 | 0.05 | −8, 2.5 kHz | 0.20 |
| Cardboard, GarbagePaper | Cardboard | 0.41 | 0.56 | 0.75 | 0.79 | −5 to −8, 2.5–5 kHz | 0.30–0.40 |
| Fabric | Curtain | 0.31 | 0.45 | 0.69 | 0.60 | −8, 2.5 kHz | 0.50 |
| GenericSoft | Carpet | 0.01 | 0.09 | 0.42 | 0.65 | −8, 2.5 kHz | 0.50 |
| Water, WaterPuddle | Water | 0.03 | 0.03 | 0.02 | 0.03 | −0.5, 20 kHz | 0.02 |
| Soil | Soil | 0.16 | 0.37 | 0.58 | 0.60 | −4, 4 kHz | 0.30 |
| Mud, Swamp | Mud | 0.16 | 0.27 | 0.21 | 0.15 | −4, 4 kHz | 0.20–0.30 |
| Gravel, Pebbles | Gravel | 0.27 | 0.63 | 0.74 | 0.80 | −3, 6 kHz | 0.35 |
| GrassLow, GrassHigh | Grass | 0.12 | 0.49 | 0.87 | 0.99 | −4, 4 kHz | 0.45–0.60 |
| SoilForest | Foliage | 0.48 | 0.50 | 0.42 | 0.31 | −4, 4 kHz | 0.40 |
| Snow | Snow | 0.47 | 0.85 | 0.95 | 0.95 | −4, 4 kHz | 0.60 |

- **Metal is no longer a guess.** Steel loses bass too (0.21 at the bottom, 0.02 at the top). It's one row for all metal, so thin sheet and thick plate aren't told apart.
- **Plastic has no match.** Gypsum board is the closest in behaviour, a stiff thin panel. Treat it as an estimate.
- **Body and BodyArmor** keep the measured "people" values (0.25–0.50), and **Chainfence and Grate** stay as they are, since they're mostly see-through.
- **Ground differs by source.** My Delany-Bazley calculation from published flow resistivity agrees with Meta on grass and snow. Meta has gravel more absorbent (0.63 against 0.15 at 500 Hz) and soil more absorbent than packed soil calculates. Real ground varies with compaction and moisture, so either is defensible. I'd use Meta's, to stay consistent.

To convert to a loss per bounce: dB = 10 × log10(1 − absorption). So 0.02 is −0.09 dB, 0.15 is −0.7 dB, 0.5 is −3 dB.

## The ground and the gunshot

**What went wrong early wasn't the ground's sound.** Before 0.3.0 the enclosure probe counted the ground as walls, so open ground scored 0.2–0.4 enclosed and every outdoor shot got 15–25% of the indoor recording. It was a measuring bug. Since then echoes ignore floors, on the reasoning that gun recordings already contain the ground bounce.

**What the ground does, calculated** (muzzle 1.4 m up, ear 1.6 m up):

| Who is firing | Delay after the direct sound | What it does |
|---|---|---|
| You | 6.9 ms, about −12.5 dB before any ground loss | Notches about every 146 Hz, up to about ±2 dB |
| Bot at 5 m | 2.4 ms | First notch about 207 Hz |
| Bot at 20 m | 0.65 ms | First notch about 770 Hz |
| Bot at 50 m | 0.26 ms | First notch about 1.9 kHz |
| Bot at 100 m | 0.13 ms | First notch about 3.8 kHz |

A muzzle blast lasts under about 3 ms ([Maher](https://www.montana.edu/rmaher/publications/maher_aes_1015_9471.pdf)), so at these delays the bounce overlaps it. **The ground never makes a separate echo. It changes the tone.** At distance it only affects the top end on hard ground. Notches at those delays are also exactly what the mod's doubled-shot complaints sounded like, which is why a separate ground-bounce voice is the wrong tool.

**What it means for the mod:**
1. **Don't add a ground echo voice.** It would double the bounce already in the recording and comb the shot.
2. **Your own shot could change tone with the ground under you**, but only by about 2 dB at most, and only away from whatever surface the recording was made on. It's real but small. It's a step E item if at all.
3. **The larger effect is ground between you and a distant shooter.** Grass absorbs well from 500 Hz up but reflects like concrete at 125 Hz and below ([field measurements](https://ince.publisher.ingentaconnect.com/contentone/ince/incecp/2021/00000263/00000003/art00051;jsessionid=gpabn5uj6jnh.x-ic-live-03)), and the standard outdoor model (ISO 9613-2) treats asphalt as hard and grass as soft. So a shot across a field should lose mids and treble that a shot across a car park keeps. It costs one filter per gun and no voices, but it needs the ground sampled along the path. New roadmap item G.
4. **Floors indoors** already work: probe v2 bounces off floors with their material, so the new table reaches them automatically.

## Through walls: transmission loss

From the mass law, R = 20 log10(mass per m² × frequency) − 47. It predicts a concrete slab well: a measured 203 mm slab was 55 dB at 500 Hz and 76 dB at 4 kHz. Single-number checks from the National Research Council Canada: 1.3 mm steel STC 30, 16 mm plywood STC 21, 150 mm concrete about STC 55.

| Game tag | Assumed build | kg/m² | 125 Hz | 500 Hz | 1 kHz | 4 kHz | Dip near (Hz) |
|---|---|---|---|---|---|---|---|
| Concrete, Tile | 150 mm | 345 | 46 | 58 | 64 | 76 | 125 |
| Stone | 200 mm | 500 | 49 | 61 | 67 | 79 | 94 |
| MetalThick, MetalNoDecal | 6 mm steel | 47 | 28 | 40 | 46 | 58 | 2100 |
| MetalThin | 1.3 mm steel | 10 | 15 | 27 | 33 | 45 | 9800 |
| WoodThick | 40 mm | 20 | 21 | 33 | 39 | 51 | 540 |
| WoodThin | 16 mm | 9 | 14 | 26 | 32 | 44 | 1400 |
| Glass | 6 mm | 15 | 19 | 31 | 37 | 49 | 2100 |
| Plastic | 5 mm (E) | 6 | 11 | 23 | 29 | 41 | 6000 |
| Cardboard | 1 kg/m² (E) | 1 | 0 | 7 | 13 | 25 | none |
| Fabric, GenericSoft | porous | – | about 0–3 | – | – | – | none |

Around a dip the real loss is lower than these figures, sometimes by 10 dB or more. That matters most for plywood and wood at 500–2000 Hz and for glass and thick steel near 2 kHz.

**How this maps to the game (6.2).** Each row becomes a level (the loss at 125 Hz) and a low-pass. Two cautions:
- Unity's low-pass is steeper than the 6 dB per octave of a real wall, so the slider will need to be judged by ear. Your 50 Hz result is consistent with concrete, but it's one headset.
- The game has no absolute loudness. A real gunshot at about 150–160 dB is still loud after 46 dB of loss, while −46 dB in the game's mix is faint. So 6.2 needs its own level control rather than using these numbers directly.

## Proposed changes, in order

1. **One table.** Today three separate tables disagree (`OwnEchoes.MaterialProps`, `AcousticProbe`'s absorption, `Surfaces`). Replace them with one `Acoustics/Materials.cs` holding the four absorption bands and the wall values.
2. **Echoes follow it, with a high-pass.** Add a high-pass to echo voices so wood and glass sound thin instead of dull. Level comes from the two middle bands, and the outer bands set the filters.
3. **Probe v2 and the rain paths follow it.** Then the retunes listed under step M: rain through the building (9l), exit credits (9k), roof rain and the echo levels. Those need raids.
4. **Roof material (4b phase 3)** uses the same table.
5. **Through-wall values** go into 6.2.

Sources: [Meta XR Audio material table](https://developers.meta.com/horizon/documentation/unity/meta-xr-audio-sdk-wwise-room-acoustics/), [absorption coefficients (Sengpiel)](https://sengpielaudio.com/calculator-RT60Coeff.htm), [concrete and flooring ranges (Acta Acustica table)](https://acta-acustica.edpsciences.org/articles/aacus/full_html/2021/01/aacus200072/T1.html), [metal range (commercial-acoustics)](https://commercial-acoustics.com/guides/sound-absorption-coefficients/), [precast slab test (Norlite)](https://www.norliteagg.com/sites/default/files/imce_images/norlite_sound_transmission.pdf), [STC values and factors (NRC CBD-239)](https://web.mit.edu/parmstr/Public/NRCan/CanBldgDigests/cbd239_e.html), [mass law and critical frequency (INSUL)](https://www.insul.s05.system7.co.nz/media/29391/Seminar-2.pdf), [ground flow resistivity (FHWA)](https://www.fhwa.dot.gov/Environment/noise/resources/ground_and_pavement_effects/gpeffects02.cfm), [ground factor G (Attenborough)](https://www.ioa.org.uk/system/files/publications/K%20ATTENBOROUGH%20GROUND%20EFFECT%20AND%20SURFACE%20TRANSPORT%20NOISE%20PREDICTION.pdf).
