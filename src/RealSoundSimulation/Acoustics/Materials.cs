using EFT.Ballistics;
using UnityEngine;

namespace RealSoundSimulation.Acoustics
{
    // 0.6.25: the one table of what each game surface does to sound (docs/MATERIALS.md, roadmap step M). Values are Meta XR Audio's
    // own presets, the spatializer the game runs, checked against published measurements. Absorption is the share of energy lost per
    // bounce in Meta's four bands (0-176, 176-775, 775-3408, 3408+ Hz); scattering is the 500-1000 Hz average; transmission is dB lost
    // at 125 Hz, 1 kHz and 4 kHz (for 6.2, not used yet).
    // Echoes use it (6g, 0.6.25) and probe v2 uses it (9o, 0.6.27), which feeds rain through buildings, exit credits, roof rain and the
    // gunshot room measures. The transmission column is for 6.2.
    internal static class Materials
    {
        private const float NoLowPass = 20000f, NoTransmit = float.PositiveInfinity;

        public readonly struct Surface
        {
            public readonly float A0, A1, A2, A3, Scatter, T125, T1k, T4k, EchoCutoff;
            public readonly float EchoDb; // one bounce: the two middle bands, where a rifle has its energy

            public Surface(float a0, float a1, float a2, float a3, float scatter, float t125, float t1k, float t4k, float echoCutoff = NoLowPass)
            {
                A0 = a0; A1 = a1; A2 = a2; A3 = a3; Scatter = scatter; T125 = t125; T1k = t1k; T4k = t4k; EchoCutoff = echoCutoff;
                EchoDb = 5f * (Mathf.Log10(1f - a1) + Mathf.Log10(1f - a2)); // the average of the two bands in dB
            }
        }

        private static readonly Surface
            Concrete = new Surface(0.01f, 0.017f, 0.02f, 0.02f, 0.125f, 24f, 28f, 33f),
            ConcreteRough = new Surface(0.011f, 0.036f, 0.078f, 0.10f, 0.175f, 24f, 28f, 33f),
            Tile = new Surface(0.01f, 0.01f, 0.017f, 0.02f, 0.15f, 24f, 28f, 33f),
            Stone = new Surface(0.02f, 0.021f, 0.044f, 0.05f, 0.175f, 24f, 28f, 33f),
            Steel = new Surface(0.207f, 0.16f, 0.074f, 0.02f, 0.10f, 6f, 11f, 23f),
            Glass = new Surface(0.344f, 0.199f, 0.081f, 0.077f, 0.05f, 9f, 16f, 11f),
            WoodThick = new Surface(0.187f, 0.105f, 0.059f, 0.05f, 0.10f, 15f, 16f, 21f),
            WoodThin = new Surface(0.408f, 0.142f, 0.083f, 0.06f, 0.10f, 7f, 10f, 13f),
            Plastic = new Surface(0.279f, 0.072f, 0.066f, 0.09f, 0.125f, 15f, 26f, 25f), // gypsum board: the closest stiff thin panel
            Rubber = new Surface(0.05f, 0.083f, 0.064f, 0.05f, 0.10f, 20f, 17f, 20f),
            Cardboard = new Surface(0.41f, 0.564f, 0.748f, 0.792f, 0.15f, 9f, 9f, 9f, 8000f),
            Curtain = new Surface(0.314f, 0.454f, 0.69f, 0.601f, 0.25f, 4f, 9f, 13f),
            Carpet = new Surface(0.012f, 0.095f, 0.417f, 0.649f, 0.175f, 24f, 28f, 33f, 3000f),
            People = new Surface(0.25f, 0.385f, 0.46f, 0.50f, 0.45f, NoTransmit, NoTransmit, NoTransmit), // measured "people adults"
            Water = new Surface(0.029f, 0.028f, 0.022f, 0.03f, 0.085f, 15f, 17f, 20f),
            Soil = new Surface(0.156f, 0.365f, 0.583f, 0.60f, 0.325f, NoTransmit, NoTransmit, NoTransmit),
            Mud = new Surface(0.156f, 0.273f, 0.205f, 0.15f, 0.325f, NoTransmit, NoTransmit, NoTransmit),
            Gravel = new Surface(0.271f, 0.627f, 0.745f, 0.80f, 0.45f, NoTransmit, NoTransmit, NoTransmit, 8000f),
            Grass = new Surface(0.119f, 0.493f, 0.868f, 0.99f, 0.45f, NoTransmit, NoTransmit, NoTransmit, 2500f),
            Foliage = new Surface(0.482f, 0.496f, 0.421f, 0.31f, 0.45f, 0f, 1f, 5f),
            Snow = new Surface(0.468f, 0.845f, 0.949f, 0.95f, 0.45f, NoTransmit, NoTransmit, NoTransmit, 8000f);

        // False for tags with no row: see-through ones (fences, grates, broken glass) and untagged surfaces.
        public static bool TryGet(MaterialType m, out Surface s)
        {
            switch (m)
            {
                case MaterialType.Concrete: s = Concrete; return true;
                case MaterialType.Tile: s = Tile; return true;
                case MaterialType.Stone: s = Stone; return true;
                case MaterialType.Asphalt: case MaterialType.GenericHard: s = ConcreteRough; return true;
                case MaterialType.MetalThick: case MaterialType.MetalNoDecal: case MaterialType.MetalThin: case MaterialType.GarbageMetal: s = Steel; return true;
                case MaterialType.Glass: s = Glass; return true;
                case MaterialType.WoodThick: s = WoodThick; return true;
                case MaterialType.WoodThin: s = WoodThin; return true;
                case MaterialType.Plastic: s = Plastic; return true;
                case MaterialType.Rubber: case MaterialType.Tyre: s = Rubber; return true;
                case MaterialType.Cardboard: case MaterialType.GarbagePaper: s = Cardboard; return true;
                case MaterialType.Fabric: s = Curtain; return true;
                case MaterialType.GenericSoft: s = Carpet; return true;
                case MaterialType.Body: case MaterialType.BodyArmor: s = People; return true;
                case MaterialType.Water: case MaterialType.WaterPuddle: s = Water; return true;
                case MaterialType.Soil: s = Soil; return true;
                case MaterialType.Mud: case MaterialType.Swamp: s = Mud; return true;
                case MaterialType.Gravel: case MaterialType.Pebbles: s = Gravel; return true;
                case MaterialType.GrassLow: case MaterialType.GrassHigh: s = Grass; return true;
                case MaterialType.SoilForest: s = Foliage; return true;
                case MaterialType.Snow: s = Snow; return true;
                default: s = default; return false;
            }
        }

        // 0.6.31 "9r": how much louder rain is on this roof than on concrete (a volume factor). Raid 41: rain in a metal trailer sounded
        // quiet. Not measured: thin metal drums loudly, soft roofs damp it. Set by ear.
        public static float RoofRainGain(MaterialType m)
        {
            switch (m)
            {
                case MaterialType.MetalThin: case MaterialType.GarbageMetal: return 2.8f;
                case MaterialType.MetalThick: case MaterialType.MetalNoDecal: return 1.6f;
                case MaterialType.WoodThin: case MaterialType.Plastic: return 1.4f;
                case MaterialType.Fabric: case MaterialType.GenericSoft: return 0.7f;
                default: return 1f;
            }
        }

        // What one bounce does to an own-gun echo: level (dB) and low-pass (Hz).
        public static void Echo(MaterialType m, out float db, out float cutoff)
        {
            if (Plugin.EchoMeasuredSurfaces.Value && TryGet(m, out Surface s)) { db = s.EchoDb; cutoff = s.EchoCutoff; return; }
            LegacyEcho(m, out db, out cutoff);
        }

        // The hand-set echo values used before 0.6.25 ("6g" off), and for tags with no row.
        public static void LegacyEcho(MaterialType m, out float db, out float cutoff)
        {
            switch (m)
            {
                case MaterialType.Concrete: case MaterialType.Stone: case MaterialType.Asphalt: case MaterialType.Tile:
                case MaterialType.GenericHard: case MaterialType.MetalThick: case MaterialType.MetalNoDecal:
                case MaterialType.Water: case MaterialType.WaterPuddle:
                    db = -0.5f; cutoff = 20000f; break;
                case MaterialType.Glass:
                    db = -1f; cutoff = 20000f; break;
                case MaterialType.MetalThin: case MaterialType.GarbageMetal:
                    db = -1f; cutoff = 16000f; break;
                case MaterialType.WoodThick:
                    db = -1.5f; cutoff = 9000f; break;
                case MaterialType.Plastic:
                    db = -2f; cutoff = 10000f; break;
                case MaterialType.WoodThin:
                    db = -3f; cutoff = 6000f; break;
                case MaterialType.Gravel: case MaterialType.Pebbles:
                    db = -3f; cutoff = 6000f; break;
                case MaterialType.Soil: case MaterialType.SoilForest: case MaterialType.Mud: case MaterialType.Snow:
                case MaterialType.GrassHigh: case MaterialType.GrassLow: case MaterialType.Swamp:
                    db = -4f; cutoff = 4000f; break;
                case MaterialType.Cardboard: case MaterialType.GlassShattered:
                    db = -5f; cutoff = 5000f; break;
                case MaterialType.Fabric: case MaterialType.GenericSoft: case MaterialType.GarbagePaper:
                case MaterialType.Rubber: case MaterialType.Tyre: case MaterialType.Body: case MaterialType.BodyArmor:
                    db = -8f; cutoff = 2500f; break;
                case MaterialType.Chainfence: case MaterialType.Grate:
                    db = -10f; cutoff = 6000f; break; // mostly see-through
                default:
                    db = -1f; cutoff = 14000f; break; // untagged: treat as a generic hard surface
            }
        }

        // Probe v2, one bounce: absorption (the two middle bands), scattering (share reflected diffusely) and brightness (Hz).
        public static void Probe(MaterialType m, out float a, out float s, out float bright)
        {
            if (Plugin.ProbeMeasuredSurfaces.Value && TryGet(m, out Surface t))
            {
                a = (t.A1 + t.A2) * 0.5f; s = t.Scatter; bright = t.EchoCutoff;
                return;
            }
            LegacyProbe(m, out a, out s, out bright);
        }

        // The values used before 0.6.27 ("9o" off), and for tags with no row (see-through and untagged surfaces).
        private static void LegacyProbe(MaterialType m, out float a, out float s, out float bright)
        {
            switch (m)
            {
                case MaterialType.Concrete: case MaterialType.Tile: a = 0.02f; s = 0.15f; bright = 16000f; return;
                case MaterialType.Stone: case MaterialType.Asphalt: a = 0.03f; s = 0.25f; bright = 15000f; return;
                case MaterialType.GenericHard: a = 0.04f; s = 0.2f; bright = 14000f; return;
                case MaterialType.MetalThick: case MaterialType.MetalNoDecal: a = 0.05f; s = 0.2f; bright = 16000f; return;
                case MaterialType.MetalThin: a = 0.1f; s = 0.3f; bright = 14000f; return;
                case MaterialType.GarbageMetal: a = 0.15f; s = 0.6f; bright = 10000f; return;
                case MaterialType.Glass: case MaterialType.GlassVisor: a = 0.04f; s = 0.05f; bright = 18000f; return;
                case MaterialType.WoodThick: a = 0.1f; s = 0.25f; bright = 9000f; return;
                case MaterialType.WoodThin: a = 0.15f; s = 0.35f; bright = 7000f; return;
                case MaterialType.Plastic: a = 0.08f; s = 0.2f; bright = 12000f; return;
                case MaterialType.Rubber: case MaterialType.Tyre: a = 0.2f; s = 0.3f; bright = 6000f; return;
                case MaterialType.Cardboard: a = 0.3f; s = 0.4f; bright = 5000f; return;
                case MaterialType.Fabric: case MaterialType.GenericSoft: case MaterialType.Body: case MaterialType.BodyArmor:
                    a = 0.5f; s = 0.7f; bright = 2500f; return;
                case MaterialType.GarbagePaper: a = 0.4f; s = 0.7f; bright = 3000f; return;
                case MaterialType.Soil: case MaterialType.Swamp: a = 0.3f; s = 0.5f; bright = 5000f; return;
                case MaterialType.SoilForest: a = 0.4f; s = 0.6f; bright = 4500f; return;
                case MaterialType.Mud: a = 0.2f; s = 0.4f; bright = 6000f; return;
                case MaterialType.Gravel: case MaterialType.Pebbles: a = 0.35f; s = 0.7f; bright = 5000f; return;
                case MaterialType.GrassLow: a = 0.45f; s = 0.7f; bright = 4000f; return;
                case MaterialType.GrassHigh: a = 0.6f; s = 0.8f; bright = 3500f; return;
                case MaterialType.Snow: a = 0.6f; s = 0.6f; bright = 3000f; return;
                case MaterialType.Water: case MaterialType.WaterPuddle: a = 0.02f; s = 0.05f; bright = 18000f; return;
                // 0.6.14: untagged was 0.06 / 0.3 / 10 kHz. Raid 30 tag 2 (Capital_corridor_1F_COLLIDER, no tag): 185 of 192
                // paths trapped, one escape kept 84% after 8 bounces, level -15 dB with 90% build-up. Furnished interiors
                // absorb 0.15-0.3. Tagged materials unchanged until the materials overhaul.
                default: a = 0.15f; s = 0.4f; bright = 8000f; return;
            }
        }
    }
}
