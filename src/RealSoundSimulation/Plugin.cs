using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using RealSoundSimulation.Acoustics;
using RealSoundSimulation.Diagnostics;
using RealSoundSimulation.Fixes;
using HarmonyLib;
using UnityEngine;

namespace RealSoundSimulation
{
    // Real Sound Simulation. Diagnostics (section 1) watch the game's audio systems and log what they decided to
    // BepInEx/RealSoundSimulationLogs. Fixes (section 2) change sound; every one is off by default and switchable live in F12.
    [BepInPlugin(Guid, "Real Sound Simulation", "0.5.7")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "ffloridamann.realsoundsimulation";

        internal static ManualLogSource Log;
        internal static Harmony Harmony;

        internal static ConfigEntry<bool> OverlayOn;
        internal static ConfigEntry<KeyboardShortcut> OverlayKey;
        internal static ConfigEntry<int> OverlayRows;
        internal static ConfigEntry<bool> LogsOn;
        internal static ConfigEntry<float> ContainerLogInterval;
        internal static ConfigEntry<float> SourceScanInterval;
        internal static ConfigEntry<bool> TimingOn;
        internal static ConfigEntry<bool> ProbeOn;
        internal static ConfigEntry<KeyboardShortcut> MarkKey;
        internal static ConfigEntry<bool> ExposureProbeOn;

        internal static ConfigEntry<bool> GunMixerOn;
        internal static ConfigEntry<float> GunMuffleAbove;
        internal static ConfigEntry<float> GunUnmuffleBelow;
        internal static ConfigEntry<float> GunHoldSeconds;
        internal static ConfigEntry<ObstructionMode> ObstructionModeCfg;
        internal static ConfigEntry<bool> DoorwayOn;
        internal static ConfigEntry<float> DoorwayInside;
        internal static ConfigEntry<float> DoorwayOutside;
        internal static ConfigEntry<bool> GunEnvBlendOn;
        internal static ConfigEntry<OwnShotBlendSource> OwnShotSource;
        internal static ConfigEntry<float> OwnShotAimWeight;
        internal static ConfigEntry<float> ThinSurfaceCount;
        internal static ConfigEntry<float> MuzzleWeight;
        internal static ConfigEntry<SmallSpaceRule> SmallSpace;
        internal static ConfigEntry<float> GlassAmbience;
        internal static ConfigEntry<float> HardRoomReverbDb;
        internal static ConfigEntry<float> OwnReverbBoostDb;
        internal static ConfigEntry<bool> OffMapOn;
        internal static ConfigEntry<bool> RainFixOn;
        internal static ConfigEntry<bool> ExposureMixOn;
        internal static ConfigEntry<float> DirectionCueStrength;
        internal static ConfigEntry<float> RoofRainUnderFloors;
        internal static ConfigEntry<float> ThroughGlass;
        internal static ConfigEntry<float> IndoorFadeNearOpenings;
        internal static ConfigEntry<float> OpeningShare;
        internal static ConfigEntry<bool> OpeningHrtf;
        internal static ConfigEntry<bool> EchoOn;
        internal static ConfigEntry<float> EchoLevelDb;
        internal static ConfigEntry<int> EchoMaxTaps;
        internal static ConfigEntry<float> EchoDirectivityDb;
        internal static ConfigEntry<bool> EchoFar;
        internal static ConfigEntry<float> EchoHardRoomDb;

        private void Awake()
        {
            Log = Logger;
            try { Load(); }
            catch (Exception e)
            {
                // A failure here means the plugin is not running at all; make it visible outside BepInEx's log too.
                Log.LogError($"Real Sound Simulation failed to load: {e}");
                DiagLog.Error($"PLUGIN FAILED TO LOAD: {e}");
            }
        }

        private void Load()
        {
            const string diag = "1. Diagnostics";
            OverlayOn = Config.Bind(diag, "Overlay", false, "Show the live occlusion overlay (top left) in raid.");
            OverlayKey = Config.Bind(diag, "Overlay key", new KeyboardShortcut(KeyCode.F9), "Toggle the overlay.");
            OverlayRows = Config.Bind(diag, "Overlay rows", 14, new ConfigDescription("Nearest sound containers to list.", new AcceptableValueRange<int>(4, 40)));
            LogsOn = Config.Bind(diag, "Write logs", true, "Write CSV logs and a summary per raid to BepInEx/RealSoundSimulationLogs.");
            ContainerLogInterval = Config.Bind(diag, "Container log interval s", 0.5f, new ConfigDescription("How often every sound container's occlusion state is logged.", new AcceptableValueRange<float>(0.1f, 5f)));
            SourceScanInterval = Config.Bind(diag, "Source scan interval s", 15f, new ConfigDescription("How often playing sources are counted and new rolloff curves dumped (FindObjectsOfType; keep it slow).", new AcceptableValueRange<float>(2f, 60f)));
            TimingOn = Config.Bind(diag, "Time the audio systems", true, "Measure milliseconds spent in SpatialAudioSystem and BetterAudio each frame.");
            ProbeOn = Config.Bind(diag, "Enclosure probe", true, "Measure how enclosed the space around you is with 32 short rays every 0.1 s. Logged to probe.csv; used by fix 3c in Enclosure mode. Changes no sound by itself.");
            ExposureProbeOn = Config.Bind(diag, "Outdoor exposure probe", true, "Step 4b, measure only: while indoors, 24 rays plus the game's room map find which directions reach the outside and through what (open, glass, other), and what is above you (floors, roof material). Logged to exposure.csv and shown on the overlay. Changes no sound.");
            MarkKey = Config.Bind(diag, "Mark key", new KeyboardShortcut(KeyCode.F10), "Press in raid to mark this spot in marks.csv (position, what the probe measured, the last own shot and the surfaces around you). Use it where something sounds wrong.");

            const string fix = "2. Fixes (all off means vanilla audio)";
            GunMixerOn = Config.Bind(fix, "1 Gun mixer hysteresis", false, new ConfigDescription("Stop distant gunfire flip-flopping between the muffled and normal sound.", null, new ConfigurationManagerAttributes { Order = 100 }));
            GunMuffleAbove = Config.Bind(fix, "1a Muffle above", 0.60f, new ConfigDescription("Occlusion needed to switch a gun to the muffled sound (game: 0.55).", new AcceptableValueRange<float>(0.3f, 0.95f), new ConfigurationManagerAttributes { Order = 99 }));
            GunUnmuffleBelow = Config.Bind(fix, "1b Unmuffle below", 0.45f, new ConfigDescription("Occlusion needed to switch back to the normal sound (game: 0.55).", new AcceptableValueRange<float>(0.05f, 0.9f), new ConfigurationManagerAttributes { Order = 98 }));
            GunHoldSeconds = Config.Bind(fix, "1c Hold s", 0.5f, new ConfigDescription("A change must persist this long before it is applied. The first shot after 2 s of quiet is never delayed.", new AcceptableValueRange<float>(0f, 3f), new ConfigurationManagerAttributes { Order = 97 }));
            ObstructionModeCfg = Config.Bind(fix, "2 Obstruction score", ObstructionMode.Game, new ConfigDescription("How the through-wall, around-corner and echo checks are combined. Game = average (a failed check counts as fully blocked). IgnoreFailedPaths = average of the checks that found a path. ParallelPaths = blocked only if every path is.", null, new ConfigurationManagerAttributes { Order = 90 }));
            DoorwayOn = Config.Bind(fix, "3 Smooth doorways", false, new ConfigDescription("Blend the indoor/outdoor mix and ambience by distance to the doorway instead of switching.", null, new ConfigurationManagerAttributes { Order = 80 }));
            DoorwayInside = Config.Bind(fix, "3a Blend inside m", 1.5f, new ConfigDescription("Distance inside the doorway over which the blend happens.", new AcceptableValueRange<float>(0.25f, 6f), new ConfigurationManagerAttributes { Order = 79 }));
            DoorwayOutside = Config.Bind(fix, "3b Blend outside m", 1.0f, new ConfigDescription("Distance outside the doorway over which the blend happens. Walking past an open door within this distance blends in a little indoor sound.", new AcceptableValueRange<float>(0.25f, 6f), new ConfigurationManagerAttributes { Order = 78 }));
            GunEnvBlendOn = Config.Bind(fix, "3c Blend own gunshots at doorways", false, new ConfigDescription("Near a doorway, play your own gunshots as a mix of the indoor and outdoor recordings instead of switching between them. Uses the doorway value from fix 3 (works with fix 3 off too).", null, new ConfigurationManagerAttributes { Order = 77 }));
            OwnShotSource = Config.Bind(fix, "3d Own gunshot blend source", OwnShotBlendSource.Doorway, new ConfigDescription("What decides the fix 3c indoor/outdoor mix. Doorway = distance through the nearest doorway (only acts near doorways). Enclosure = the enclosure probe plus where you are aiming (acts everywhere: covered spots, firing into or out of rooms).", null, new ConfigurationManagerAttributes { Order = 76 }));
            OwnShotAimWeight = Config.Bind(fix, "3e Aim weight", 0.4f, new ConfigDescription("Enclosure mode: how much the direction you fire in counts against the space around you. 0 = only the surroundings, 1 = only the aim direction.", new AcceptableValueRange<float>(0f, 1f), new ConfigurationManagerAttributes { Order = 75 }));
            ThinSurfaceCount = Config.Bind(fix, "3f Thin and glass surfaces count", 0.5f, new ConfigDescription("Enclosure mode: how much glass, thin wood planks, cardboard and fabric count as walls when deciding how indoor you are. 1 = like concrete, 0 = like open air. Chain fences and grates always count as nearly open.", new AcceptableValueRange<float>(0f, 1f), new ConfigurationManagerAttributes { Order = 74 }));
            MuzzleWeight = Config.Bind(fix, "3g Muzzle weight", 0.7f, new ConfigDescription("Enclosure mode: the room is measured at your head and at the muzzle. This is how much the muzzle counts. On a threshold, the side the muzzle is on then decides most (looking in from outside sounds inside, looking out from inside sounds outside).", new AcceptableValueRange<float>(0f, 1f), new ConfigurationManagerAttributes { Order = 73 }));
            SmallSpace = Config.Bind(fix, "3i Small space rule", SmallSpaceRule.Current, new ConfigDescription("Fix 3c Enclosure mode: small furnished spaces (walls about 2 m away) get less of the indoor recording. Current = down to half (hard bare rooms like stairwells exempt). Soft = at least 85%. Off = room size never reduces it.", null, new ConfigurationManagerAttributes { Order = 71 }));
            GlassAmbience = Config.Bind(fix, "3h Ambience through glass", 0.5f, new ConfigDescription("Fix 3: inside a space walled with glass, raise the outdoor ambience (rain, wind) towards full, still muffled by the indoor filter. 0 = the game's level. At 0.5, all-glass walls let the weather in at the full outdoor level and half-glass walls halfway.", new AcceptableValueRange<float>(0f, 1f), new ConfigurationManagerAttributes { Order = 72 }));
            OwnReverbBoostDb = Config.Bind(fix, "5 Own gun room reverb boost dB", 0f, new ConfigDescription("Experiment. The game plays a hidden reverb-only copy of your own shots through the Meta room acoustics. This raises or lowers that copy's reverb and early-reflection sends. 0 = the game's level.", new AcceptableValueRange<float>(-12f, 12f), new ConfigurationManagerAttributes { Order = 60 }));
            RainFixOn = Config.Bind(fix, "7 Fix rain cutting out between rooms", false, new ConfigDescription("Game bug: moving between rooms that use the same rain-on-roof sound (or up and down in rooms where roof rain follows your height) fades the rain to silence until the next change. This keeps the rain playing. Logged to rain.csv.", null, new ConfigurationManagerAttributes { Order = 52 }));
            ExposureMixOn = Config.Bind(fix, "8 Outdoor sound indoors from openings", false, new ConfigDescription("Needs fix 3 on. Indoors, how much outdoor ambience (wind, rain, background) you hear comes from the open sides, windows and glass around you, not from the room's fixed setting. Near an open side: near full; through glass: quieter and duller; deep inside: the room's level. Also fades the indoor sounds near openings, turns roof rain down under floors, and pans the outdoor sound toward the open side.", null, new ConfigurationManagerAttributes { Order = 51 }));
            DirectionCueStrength = Config.Bind(fix, "8a Direction cue strength", 0.6f, new ConfigDescription("Fix 8: how strongly the outdoor sound is panned toward the open side and dulled when it is behind you. 0 = off.", new AcceptableValueRange<float>(0f, 1f), new ConfigurationManagerAttributes { Order = 50 }));
            RoofRainUnderFloors = Config.Bind(fix, "8b Roof rain under floors", 0.3f, new ConfigDescription("Fix 8: volume of the indoor rain-on-roof sound with ONE floor above you (1 = the game's level). With two or more floors above it is off; with only the roof above, full. Needs fix 7 on (otherwise the game's own rain bug would fade the rain out when the volume changes).", new AcceptableValueRange<float>(0f, 1f), new ConfigurationManagerAttributes { Order = 49 }));
            ThroughGlass = Config.Bind(fix, "8c Through glass", 0.45f, new ConfigDescription("Fix 8: how much outdoor sound an intact glass window or wall lets through, compared with an opening. Broken windows count as open.", new AcceptableValueRange<float>(0f, 1f), new ConfigurationManagerAttributes { Order = 48 }));
            IndoorFadeNearOpenings = Config.Bind(fix, "8d Indoor sound fade near openings", 0.6f, new ConfigDescription("Fix 8: how much the indoor sounds (room tone, indoor wind and rain) fade where the outside is open around you. 0 = never fade.", new AcceptableValueRange<float>(0f, 1f), new ConfigurationManagerAttributes { Order = 47 }));
            OpeningShare = Config.Bind(fix, "8e Sound from openings", 0.7f, new ConfigDescription("Fix 8: indoors, this share of the outdoor sound (wind, rain, background) is played as 3D sound from the directions the outside is open in (6 fixed directions around you), instead of from everywhere. 0 = off (flat sound with the direction cue only).", new AcceptableValueRange<float>(0f, 1f), new ConfigurationManagerAttributes { Order = 46 }));
            OpeningHrtf = Config.Bind(fix, "8f HRTF on opening sounds", false, new ConfigDescription("Fix 8: run the opening sounds through the game's 3D audio plugin (Meta XR) for front/back and up/down placement. Off = plain left/right panning plus a duller sound from behind.", null, new ConfigurationManagerAttributes { Order = 45 }));
            EchoOn = Config.Bind(fix, "6 Own gun echoes", false, new ConfigDescription("Your own shots get short echoes from the real surfaces around you: delayed, quieter, duller copies played from each wall, ceiling, building face or hill they bounce off. Firing into a room gives close loud echoes in front; firing out of one gives weaker ones from behind. Logged to echoes.csv.", null, new ConfigurationManagerAttributes { Order = 58 }));
            EchoLevelDb = Config.Bind(fix, "6a Echo level dB", 0f, new ConfigDescription("Raise or lower all echoes. 0 = the physical estimate.", new AcceptableValueRange<float>(-18f, 12f), new ConfigurationManagerAttributes { Order = 57 }));
            EchoMaxTaps = Config.Bind(fix, "6b Max echoes per shot", 4, new ConfigDescription("The loudest N reflections are played per shot (each is one extra short sound).", new AcceptableValueRange<int>(1, 8), new ConfigurationManagerAttributes { Order = 56 }));
            EchoDirectivityDb = Config.Bind(fix, "6c Muzzle directivity dB", 10f, new ConfigDescription("How much quieter the muzzle blast is behind you than in front. Higher = where you aim matters more (into vs out of a room).", new AcceptableValueRange<float>(0f, 20f), new ConfigurationManagerAttributes { Order = 55 }));
            EchoFar = Config.Bind(fix, "6d Far echoes", true, new ConfigDescription("Also echo off building faces, cliffs and hills 30 to 250 m away (16 long rays, re-scanned when you move 5 m).", null, new ConfigurationManagerAttributes { Order = 54 }));
            HardRoomReverbDb = Config.Bind(fix, "5a Hard room reverb boost dB", 6f, new ConfigDescription("Added to setting 5 in hard, closed rooms (bare concrete stairwells, corridors), scaled by how hard and closed the room is. Raises the game's room reverb on your own shots there. 0 = off.", new AcceptableValueRange<float>(0f, 12f), new ConfigurationManagerAttributes { Order = 59 }));
            EchoHardRoomDb = Config.Bind(fix, "6e Hard room echo boost dB", 4f, new ConfigDescription("Fix 6: in hard, closed rooms (bare concrete stairwells, corridors) all echoes are raised by this much, scaled by how hard and closed the room is, and up to 3 more are played. A real stairwell is loud because the sound keeps bouncing.", new AcceptableValueRange<float>(0f, 12f), new ConfigurationManagerAttributes { Order = 53 }));
            OffMapOn = Config.Bind(fix, "4 Off-map sounds as outdoor", false, new ConfigDescription("Sounds placed outside the baked sound map (e.g. SPTBattleAmbience's distant battles) are treated as outdoors instead of fully blocked.", null, new ConfigurationManagerAttributes { Order = 70 }));

            Harmony = new Harmony(Guid);
            Safe("self-check", PatchSelfCheck.Run);
            Safe("timing patches", () => TimingPatches.Apply(Harmony));
            Safe("shot probe", () => ShotProbe.Apply(Harmony));
            Safe("env probe", () => EnvProbe.Apply(Harmony));
            Safe("fix 1 gun mixer", () => GunMixerFix.Apply(Harmony));
            Safe("fix 2 obstruction score", () => ObstructionScoreFix.Apply(Harmony));
            Safe("fix 3 doorway blend", () => DoorwayBlend.Apply(Harmony));
            Safe("fix 3c own gunshot blend", () => GunEnvironmentBlend.Apply(Harmony));
            Safe("fix 4 off-map sounds", () => OffMapFix.Apply(Harmony));
            Safe("fix 6 own gun echoes", () => OwnEchoes.Apply(Harmony));
            Safe("fix 7 rain dropout", () => RainDropoutFix.Apply(Harmony));
            Safe("fix 8 ambience mix", () => AmbienceMix.Apply(Harmony));
            gameObject.AddComponent<DiagRunner>();
            gameObject.AddComponent<DoorwayBlend>();
            gameObject.AddComponent<EnclosureProbe>();
            gameObject.AddComponent<EchoVoices>();
            gameObject.AddComponent<OutdoorExposure>();
            gameObject.AddComponent<AmbienceMix>();
            Log.LogInfo("Real Sound Simulation loaded");
        }

        internal static string FixState() =>
            $"fix1 gun mixer {(GunMixerOn.Value ? $"ON ({GunMuffleAbove.Value:0.00}/{GunUnmuffleBelow.Value:0.00}, hold {GunHoldSeconds.Value:0.0} s)" : "off")} | " +
            $"fix2 obstruction {ObstructionModeCfg.Value} | " +
            $"fix3 doorways {(DoorwayOn.Value ? $"ON (in {DoorwayInside.Value:0.0} m / out {DoorwayOutside.Value:0.0} m)" : "off")} | " +
            $"fix3c own gunshot blend {(GunEnvBlendOn.Value ? $"ON ({OwnShotSource.Value}, aim {OwnShotAimWeight.Value:0.00}, thin {ThinSurfaceCount.Value:0.00}, muzzle {MuzzleWeight.Value:0.00}, small space {SmallSpace.Value})" : "off")} | " +
            $"5 own reverb boost {OwnReverbBoostDb.Value:+0.0;-0.0;0} dB (+{HardRoomReverbDb.Value:0} in hard rooms) | glass ambience {GlassAmbience.Value:0.00} | probe {(ProbeOn.Value ? "on" : "off")} | " +
            $"fix4 off-map {(OffMapOn.Value ? "ON" : "off")} | fix7 rain dropout {(RainFixOn.Value ? "ON" : "off")} | fix8 openings {(ExposureMixOn.Value ? $"ON (cue {DirectionCueStrength.Value:0.00}, roof rain {RoofRainUnderFloors.Value:0.00}, glass {ThroughGlass.Value:0.00}, indoor fade {IndoorFadeNearOpenings.Value:0.00}, openings {OpeningShare.Value:0.00}{(OpeningHrtf.Value ? " HRTF" : "")})" : "off")} | exposure probe {(ExposureProbeOn.Value ? "on" : "off")} | " +
            $"fix6 echoes {(EchoOn.Value ? $"ON ({EchoLevelDb.Value:+0.0;-0.0;0} dB, max {EchoMaxTaps.Value}, directivity {EchoDirectivityDb.Value:0} dB, far {(EchoFar.Value ? "on" : "off")}, hard rooms +{EchoHardRoomDb.Value:0} dB)" : "off")}";

        internal static void Safe(string what, Action a)
        {
            try { a(); }
            catch (Exception e) { Log.LogError($"{what} failed: {e}"); DiagLog.Error($"{what} failed: {e}"); }
        }
    }

    // Minimal copy of the attribute the BepInEx Configuration Manager (F12) reads for ordering.
    internal sealed class ConfigurationManagerAttributes
    {
        public int? Order;
    }
}
