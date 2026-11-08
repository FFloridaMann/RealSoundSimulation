# Materials: real acoustic values for the game's surfaces

Research for roadmap step M (8 Nov 2026). **Nothing here is in the game yet.** It compares what the mod assumes with measured or calculated values and proposes what to change.

## What the research found

1. **Thin wood and window glass lose bass, not treble.** Our tables dull them (thin wood's echo is cut at 6 kHz). Measured, a 3/8" plywood panel absorbs 0.28 at 125 Hz and 0.11 at 4 kHz, and a window 0.35 and 0.04. Real echoes off them are thin and bright. This is the biggest audible correction, and it needs a high-pass on the echo voices, which only have a low-pass today.
2. **Hard surfaces lose almost nothing per bounce.** Concrete absorbs about 2%, or 0.09 dB. Echoes use 0.5 dB. Probe v2 already uses the right value.
3. **Ground is the most wrong group.** Soil, mud and grass share one value (−4 dB). Calculated from measured ground properties, packed soil loses about 0.2 dB, grass about 2 dB and snow about 10 dB.
4. **Walls behave like a low-pass, which supports your 50 Hz thump.** Below a panel's coincidence dip, transmission loss rises about 6 dB per octave. A 150 mm concrete wall takes off about 46 dB at 125 Hz and 76 dB at 4 kHz, so only the lowest part of a gunshot gets through.
5. **Thin walls block more than the probe assumes.** Thin wood passes 10 dB less today. Real values are about 14–26 dB through the low and mid bands.

## Limits of this data

- **The game's tags don't say how thick anything is.** "Concrete" could be a 100 mm wall or a 400 mm slab. Each transmission row assumes one thickness; doubling it adds about 6 dB.
- **Quality varies.** Each row is marked: **M** measured (from a published table), **C** calculated from physics, **E** my estimate because I found no data.
- **Real walls leak.** Doors, gaps and paths through the structure usually make field results several dB worse than these lab-style numbers.
- **Scattering:** I found no usable values per surface, so the current scattering values stay.
- **Ground values** are for sound arriving straight on. At shallow angles, ground reflects more.

## Reflection: absorption by frequency

Absorption is the share of energy lost per bounce. Low = 125–250 Hz, mid = 500–1000 Hz, high = 2–4 kHz.

| Game tag | Low | Mid | High | Basis | Echoes today (dB, low-pass) | Probe today (absorption) |
|---|---|---|---|---|---|---|
| Concrete | 0.02 | 0.03 | 0.05 | M: smooth to rough concrete | −0.5, 20 kHz | 0.02 |
| Tile | 0.01 | 0.01 | 0.02 | M: marble/tile | −0.5, 20 kHz | 0.02 |
| Stone | 0.03 | 0.04 | 0.06 | M: unglazed brick | −0.5, 20 kHz | 0.03 |
| GenericHard | 0.03 | 0.04 | 0.05 | E | −0.5, 20 kHz | 0.04 |
| MetalThick, MetalNoDecal | 0.06 | 0.06 | 0.06 | M: range 0.05–0.10 only | −0.5, 20 kHz | 0.05 |
| MetalThin | 0.10 | 0.08 | 0.06 | E, within that range | −1, 16 kHz | 0.10 |
| GarbageMetal | 0.15 | 0.15 | 0.15 | E: unchanged | −1, 16 kHz | 0.15 |
| Glass | 0.30 | 0.15 | 0.05 | M: window | −1, 20 kHz | 0.04 |
| WoodThick | 0.13 | 0.08 | 0.07 | M: wood on joists | −1.5, 9 kHz | 0.10 |
| WoodThin | 0.25 | 0.13 | 0.10 | M: 3/8" plywood | −3, 6 kHz | 0.15 |
| Plastic | 0.08 | 0.08 | 0.08 | E: unchanged | −2, 10 kHz | 0.08 |
| Rubber, Tyre | 0.20 | 0.20 | 0.20 | E: unchanged | −8, 2.5 kHz | 0.20 |
| Cardboard | 0.30 | 0.30 | 0.30 | E: unchanged | −5, 5 kHz | 0.30 |
| Fabric, GenericSoft | 0.15 | 0.50 | 0.65 | M: curtains, carpet | −8, 2.5 kHz | 0.50 |
| GarbagePaper | 0.40 | 0.40 | 0.40 | E: unchanged | −8, 2.5 kHz | 0.40 |
| Body, BodyArmor | 0.30 | 0.44 | 0.50 | M: people | −8, 2.5 kHz | 0.50 |
| Water, WaterPuddle | 0.01 | 0.014 | 0.02 | M: water or ice | −0.5, 20 kHz | 0.02 |
| Asphalt | 0.01 | 0.02 | 0.04 | C | −0.5, 20 kHz | 0.03 |
| Soil, Mud, Swamp | 0.02 | 0.04 | 0.10 | C: hard soil | −4, 4 kHz | 0.20–0.30 |
| Gravel, Pebbles | 0.08 | 0.20 | 0.45 | C: gravel, loose soil | −3, 6 kHz | 0.35 |
| GrassLow, GrassHigh | 0.15 | 0.35 | 0.66 | C: grass | −4, 4 kHz | 0.45–0.60 |
| SoilForest | 0.35 | 0.66 | 0.90 | C: forest floor | −4, 4 kHz | 0.40 |
| Snow | 0.55 | 0.84 | 0.96 | C: snow | −4, 4 kHz | 0.60 |

Calculated ground values use the Delany-Bazley model with published flow resistivities (cgs rayl/cm): snow 20, forest floor 50, grass 200, gravel and loose soil 500, hard soil 5,000, asphalt 20,000. That model has had mixed results on ground, so treat them as approximate. Asphalt and hard soil vary a lot with compaction and moisture.

To convert to a loss per bounce: dB = 10 × log10(1 − absorption). So 0.02 is −0.09 dB, 0.15 is −0.7 dB, 0.5 is −3 dB.

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

1. **One table.** Today three separate tables disagree (`OwnEchoes.MaterialProps`, `AcousticProbe`'s absorption, `Surfaces`). Replace them with one `Acoustics/Materials.cs` holding low, mid and high absorption and the wall values.
2. **Echoes follow it, with a high-pass.** Add a high-pass to echo voices so wood and glass sound thin instead of dull. Level comes from mid, and the low and high values set the filters.
3. **Probe v2 and the rain paths follow it.** Then the retunes listed under step M: rain through the building (9l), exit credits (9k), roof rain and the echo levels. Those need raids.
4. **Roof material (4b phase 3)** uses the same table.
5. **Through-wall values** go into 6.2.

Sources: [absorption coefficients (Sengpiel)](https://sengpielaudio.com/calculator-RT60Coeff.htm), [concrete and flooring ranges (Acta Acustica table)](https://acta-acustica.edpsciences.org/articles/aacus/full_html/2021/01/aacus200072/T1.html), [metal range (commercial-acoustics)](https://commercial-acoustics.com/guides/sound-absorption-coefficients/), [precast slab test (Norlite)](https://www.norliteagg.com/sites/default/files/imce_images/norlite_sound_transmission.pdf), [STC values and factors (NRC CBD-239)](https://web.mit.edu/parmstr/Public/NRCan/CanBldgDigests/cbd239_e.html), [mass law and critical frequency (INSUL)](https://www.insul.s05.system7.co.nz/media/29391/Seminar-2.pdf), [ground flow resistivity (FHWA)](https://www.fhwa.dot.gov/Environment/noise/resources/ground_and_pavement_effects/gpeffects02.cfm).
