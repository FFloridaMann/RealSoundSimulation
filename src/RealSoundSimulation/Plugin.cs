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
    // BepInEx/RealSoundSimulationLogs. Fixes (section 2) change sound; every one is switchable live in F12 (defaults = the tested setup).
    [BepInPlugin(Guid, "Real Sound Simulation", "0.6.7")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "ffloridamann.realsoundsimulation";

        internal static ManualLogSource Log;
        internal static Harmony Harmony;

        internal static ConfigEntry<GunSoundSet> GunSounds;
        internal static ConfigEntry<bool> SebOnFullAuto;

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
        internal static ConfigEntry<bool> AcousticProbeOn;
        internal static ConfigEntry<KeyboardShortcut> RayViewKey;
        internal static ConfigEntry<bool> AmbienceV2On;
        internal static ConfigEntry<bool> GunV2On;
        internal static ConfigEntry<float> BedDirectional;
        internal static ConfigEntry<BedDirection.Mode> BedMode;
        internal static ConfigEntry<float> GlassLossDb;
        internal static ConfigEntry<float> RoomBuildUp;
        internal static ConfigEntry<bool> GuidedRays;
        internal static ConfigEntry<AcousticProbe.RayQuality> RayQualityCfg;
        internal static ConfigEntry<float> DirectionStrength;
        internal static ConfigEntry<bool> RoofRainFromProbe;

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
            const string guns = "0. Gun sounds";
            GunSounds = Config.Bind(guns, "0 Gun sounds", GunSoundSet.SPT, new ConfigDescription("Which recordings guns use. SPT = the game's own. SEB = Sound Engine Backport's EFT 1.0 recordings (6 to 48 single-shot takes per sound where SPT has one loop), built into the plugin's seb_sounds folder by tools/build_seb_sounds.py. Switches at the next trigger pull, never mid-burst. Logged to gunsounds.csv.", null, new ConfigurationManagerAttributes { Order = 100 }));
            SebOnFullAuto = Config.Bind(guns, "0a SEB on full-auto guns", false, new ConfigDescription("Also use SEB's recordings on guns that can fire full-auto. Off by default: the game loops one recording while the trigger is held, and SEB's are single shots, so a burst plays one shot repeating every 3 s until playback is patched.", null, new ConfigurationManagerAttributes { Order = 99 }));

            const string diag = "1. Diagnostics";
            OverlayOn = Config.Bind(diag, "Overlay", false, "Show the live occlusion overlay (top left) in raid.");
            OverlayKey = Config.Bind(diag, "Overlay key", new KeyboardShortcut(KeyCode.F9), "Toggle the overlay.");
            OverlayRows = Config.Bind(diag, "Overlay rows", 14, new ConfigDescription("Nearest sound containers to list.", new AcceptableValueRange<int>(4, 40)));
            LogsOn = Config.Bind(diag, "Write logs", true, "Write CSV logs and a summary per raid to BepInEx/RealSoundSimulationLogs.");
            ContainerLogInterval = Config.Bind(diag, "Container log interval s", 0.5f, new ConfigDescription("How often every sound container's occlusion state is logged.", new AcceptableValueRange<float>(0.1f, 5f)));
            SourceScanInterval = Config.Bind(diag, "Source scan interval s", 15f, new ConfigDescription("How often playing sources are counted and new rolloff curves dumped (FindObjectsOfType; keep it slow).", new AcceptableValueRange<float>(2f, 60f)));
            TimingOn = Config.Bind(diag, "Time the audio systems", true, "Measure milliseconds spent in SpatialAudioSystem and BetterAudio each frame.");
            ProbeOn = Config.Bind(diag, "Enclosure probe", true, "Measure how enclosed the space around you is with 32 short rays every 0.1 s. Logged to probe.csv; used by fix 3c in Enclosure mode. Changes no sound by itself.");
            ExposureProbeOn = Config.Bind(diag, "Outdoor exposure probe", true, "Measures how much of the outside reaches you and through what (open, glass, bars, other): 48 directions all round (level and 15 deg up), re-measured continuously at about 290 rays per second, plus what is above you (floors, roof). Runs indoors and outdoors. Fix 8 needs it on. Logged to exposure.csv and shown on the overlay.");
            MarkKey = Config.Bind(diag, "Mark key", new KeyboardShortcut(KeyCode.F10), "Press in raid to mark this spot in marks.csv (position, what the probe measured, the last own shot and the surfaces around you). Use it where something sounds wrong.");
            AcousticProbeOn = Config.Bind(diag, "Acoustic probe v2", true, "Probe v2 (0.6.0): 128 rays over the whole sphere that bounce off surfaces like sound does (losing energy by material, passing glass and thin planks at a loss) until they reach open sky. Measures how much outdoor sound reaches you, from where and how muffled, and the room for your gunshots. Feeds fixes 9 and 9a; logged to probe2.csv and paths.csv (F10).");
            RayViewKey = Config.Bind(diag, "Probe ray view key", new KeyboardShortcut(KeyCode.F11), "In raid: show or hide the probe v2 rays in the world. Green reached the sky, cyan after bouncing, yellow through glass, red trapped, magenta a long interior.");

            const string fix = "2. Fixes (all off means vanilla audio)";
            GunMixerOn = Config.Bind(fix, "1 Gun mixer hysteresis", true, new ConfigDescription("Stop distant gunfire flip-flopping between the muffled and normal sound.", null, new ConfigurationManagerAttributes { Order = 100 }));
            GunMuffleAbove = Config.Bind(fix, "1a Muffle above", 0.60f, new ConfigDescription("Occlusion needed to switch a gun to the muffled sound (game: 0.55).", new AcceptableValueRange<float>(0.3f, 0.95f), new ConfigurationManagerAttributes { Order = 99 }));
            GunUnmuffleBelow = Config.Bind(fix, "1b Unmuffle below", 0.45f, new ConfigDescription("Occlusion needed to switch back to the normal sound (game: 0.55).", new AcceptableValueRange<float>(0.05f, 0.9f), new ConfigurationManagerAttributes { Order = 98 }));
            GunHoldSeconds = Config.Bind(fix, "1c Hold s", 0.5f, new ConfigDescription("A change must persist this long before it is applied. The first shot after 2 s of quiet is never delayed.", new AcceptableValueRange<float>(0f, 3f), new ConfigurationManagerAttributes { Order = 97 }));
            ObstructionModeCfg = Config.Bind(fix, "2 Obstruction score", ObstructionMode.ParallelPaths, new ConfigDescription("How the through-wall, around-corner and echo checks are combined. Game = average (a failed check counts as fully blocked). IgnoreFailedPaths = average of the checks that found a path. ParallelPaths = blocked only if every path is.", null, new ConfigurationManagerAttributes { Order = 90 }));
            DoorwayOn = Config.Bind(fix, "3 Smooth doorways", true, new ConfigDescription("Blend the indoor/outdoor mix and ambience by distance to the doorway instead of switching.", null, new ConfigurationManagerAttributes { Order = 80 }));
            DoorwayInside = Config.Bind(fix, "3a Blend inside m", 1.5f, new ConfigDescription("Distance inside the doorway over which the blend happens.", new AcceptableValueRange<float>(0.25f, 6f), new ConfigurationManagerAttributes { Order = 79 }));
            DoorwayOutside = Config.Bind(fix, "3b Blend outside m", 1.0f, new ConfigDescription("Distance outside the doorway over which the blend happens. Walking past an open door within this distance blends in a little indoor sound.", new AcceptableValueRange<float>(0.25f, 6f), new ConfigurationManagerAttributes { Order = 78 }));
            GunEnvBlendOn = Config.Bind(fix, "3c Blend own gunshots at doorways", true, new ConfigDescription("Play your own gunshots as a mix of the indoor and outdoor recordings instead of switching between them. With 3d = Enclosure (default) it acts everywhere: covered spots, small rooms, firing into or out of a room. With 3d = Doorway it acts only near doorways, using the doorway value from fix 3.", null, new ConfigurationManagerAttributes { Order = 77 }));
            OwnShotSource = Config.Bind(fix, "3d Own gunshot blend source", OwnShotBlendSource.Enclosure, new ConfigDescription("What decides the fix 3c indoor/outdoor mix. Doorway = distance through the nearest doorway (only acts near doorways). Enclosure = the enclosure probe plus where you are aiming (acts everywhere: covered spots, firing into or out of rooms).", null, new ConfigurationManagerAttributes { Order = 76 }));
            OwnShotAimWeight = Config.Bind(fix, "3e Aim weight", 0.4f, new ConfigDescription("Enclosure mode: how much the direction you fire in counts against the space around you. 0 = only the surroundings, 1 = only the aim direction.", new AcceptableValueRange<float>(0f, 1f), new ConfigurationManagerAttributes { Order = 75 }));
            ThinSurfaceCount = Config.Bind(fix, "3f Thin and glass surfaces count", 0.5f, new ConfigDescription("Enclosure mode: how much glass, thin wood planks, cardboard and fabric count as walls when deciding how indoor you are. 1 = like concrete, 0 = like open air. Chain fences and grates always count as nearly open.", new AcceptableValueRange<float>(0f, 1f), new ConfigurationManagerAttributes { Order = 74 }));
            MuzzleWeight = Config.Bind(fix, "3g Muzzle weight", 0.7f, new ConfigDescription("Enclosure mode: the room is measured at your head and at the muzzle. This is how much the muzzle counts. On a threshold, the side the muzzle is on then decides most (looking in from outside sounds inside, looking out from inside sounds outside).", new AcceptableValueRange<float>(0f, 1f), new ConfigurationManagerAttributes { Order = 73 }));
            SmallSpace = Config.Bind(fix, "3i Small space rule", SmallSpaceRule.Soft, new ConfigDescription("Fix 3c Enclosure mode: small furnished spaces (walls about 2 m away) get less of the indoor recording. Current = down to half (hard bare rooms like stairwells exempt). Soft = at least 85%. Off = room size never reduces it.", null, new ConfigurationManagerAttributes { Order = 71 }));
            OwnReverbBoostDb = Config.Bind(fix, "5 Own gun room reverb boost dB", 0f, new ConfigDescription("Experiment. The game plays a hidden reverb-only copy of your own shots through the Meta room acoustics. This raises or lowers that copy's reverb and early-reflection sends. 0 = the game's level.", new AcceptableValueRange<float>(-12f, 12f), new ConfigurationManagerAttributes { Order = 60 }));
            RainFixOn = Config.Bind(fix, "7 Fix rain cutting out between rooms", true, new ConfigDescription("Game bug: moving between rooms that use the same rain-on-roof sound (or up and down in rooms where roof rain follows your height) fades the rain to silence until the next change. This keeps the rain playing. Logged to rain.csv.", null, new ConfigurationManagerAttributes { Order = 52 }));
            ExposureMixOn = Config.Bind(fix, "8 Outdoor sound indoors from openings", true, new ConfigDescription("Needs fix 3 and the outdoor exposure probe on. The outdoor ambience (wind, rain, background) follows how open the space around you is: open sides count fully, intact glass by 8c, deep inside the room keeps its own level. Part of it plays as 3D sound from the open directions (8e); the rest is panned toward the open side (8a). The indoor sounds (room tone, indoor wind, roof rain) follow how enclosed you are (8d), and roof rain turns down under floors (8b). One measure and one speed indoors and out, so nothing switches at the doorway.", null, new ConfigurationManagerAttributes { Order = 51 }));
            DirectionCueStrength = Config.Bind(fix, "8a Direction cue strength", 0.6f, new ConfigDescription("Fix 8: how strongly the outdoor sound is panned toward the open side and dulled when it is behind you. 0 = off.", new AcceptableValueRange<float>(0f, 1f), new ConfigurationManagerAttributes { Order = 50 }));
            RoofRainUnderFloors = Config.Bind(fix, "8b Roof rain under floors", 0.3f, new ConfigDescription("Fix 8: volume of the indoor rain-on-roof sound with ONE floor above you (1 = the game's level). With two or more floors above it is off; with only the roof above, full.", new AcceptableValueRange<float>(0f, 1f), new ConfigurationManagerAttributes { Order = 49 }));
            ThroughGlass = Config.Bind(fix, "8c Through glass", 0.45f, new ConfigDescription("Fix 8: how much outdoor sound an intact glass window or wall lets through, compared with an opening. Broken windows count as open.", new AcceptableValueRange<float>(0f, 1f), new ConfigurationManagerAttributes { Order = 48 }));
            IndoorFadeNearOpenings = Config.Bind(fix, "8d Indoor sound fade near openings", 0.6f, new ConfigDescription("Fix 8: how early the indoor sounds (room tone, indoor wind, roof rain) fade out as the space around you opens up. 1 = they fade steadily from enclosed to open air; 0 = they stay full until you are almost fully outside.", new AcceptableValueRange<float>(0f, 1f), new ConfigurationManagerAttributes { Order = 47 }));
            OpeningShare = Config.Bind(fix, "8e Sound from openings", 0.7f, new ConfigDescription("Fix 8: indoors, this share of the outdoor sound (wind, rain, background) is played as 3D sound from the directions the outside is open in (6 fixed directions around you), instead of from everywhere. 0 = off (flat sound with the direction cue only).", new AcceptableValueRange<float>(0f, 1f), new ConfigurationManagerAttributes { Order = 46 }));
            OpeningHrtf = Config.Bind(fix, "8f HRTF on opening sounds", true, new ConfigDescription("Fix 8: run the opening sounds through the game's 3D audio plugin (Meta XR) for front/back and up/down placement. Off = plain left/right panning plus a duller sound from behind.", null, new ConfigurationManagerAttributes { Order = 45 }));
            EchoOn = Config.Bind(fix, "6 Own gun echoes", true, new ConfigDescription("Your own shots get short echoes from the real surfaces around you: delayed, quieter, duller copies played from each wall, ceiling, building face or hill they bounce off. Firing into a room gives close loud echoes in front; firing out of one gives weaker ones from behind. Logged to echoes.csv.", null, new ConfigurationManagerAttributes { Order = 58 }));
            EchoLevelDb = Config.Bind(fix, "6a Echo level dB", 0f, new ConfigDescription("Raise or lower all echoes. 0 = the physical estimate.", new AcceptableValueRange<float>(-18f, 12f), new ConfigurationManagerAttributes { Order = 57 }));
            EchoMaxTaps = Config.Bind(fix, "6b Max echoes per shot", 4, new ConfigDescription("The loudest N reflections are played per shot (each is one extra short sound).", new AcceptableValueRange<int>(1, 8), new ConfigurationManagerAttributes { Order = 56 }));
            EchoDirectivityDb = Config.Bind(fix, "6c Muzzle directivity dB", 10f, new ConfigDescription("How much quieter the muzzle blast is behind you than in front. Higher = where you aim matters more (into vs out of a room).", new AcceptableValueRange<float>(0f, 20f), new ConfigurationManagerAttributes { Order = 55 }));
            EchoFar = Config.Bind(fix, "6d Far echoes", true, new ConfigDescription("Also echo off building faces, cliffs and hills 30 to 250 m away (16 long rays, re-scanned when you move 5 m).", null, new ConfigurationManagerAttributes { Order = 54 }));
            HardRoomReverbDb = Config.Bind(fix, "5a Hard room reverb boost dB", 6f, new ConfigDescription("Added to setting 5 in hard, closed rooms (bare concrete stairwells, corridors), scaled by how hard and closed the room is. Raises the game's room reverb on your own shots there. 0 = off.", new AcceptableValueRange<float>(0f, 12f), new ConfigurationManagerAttributes { Order = 59 }));
            EchoHardRoomDb = Config.Bind(fix, "6e Hard room echo boost dB", 4f, new ConfigDescription("Fix 6: in hard, closed rooms (bare concrete stairwells, corridors) all echoes are raised by this much, scaled by how hard and closed the room is, and up to 3 more are played. A real stairwell is loud because the sound keeps bouncing.", new AcceptableValueRange<float>(0f, 12f), new ConfigurationManagerAttributes { Order = 53 }));
            OffMapOn = Config.Bind(fix, "4 Off-map sounds as outdoor", true, new ConfigDescription("Sounds placed outside the baked sound map (e.g. SPTBattleAmbience's distant battles) are treated as outdoors instead of fully blocked.", null, new ConfigurationManagerAttributes { Order = 70 }));
            AmbienceV2On = Config.Bind(fix, "9 Ambience from probe v2", false, new ConfigDescription("Test: the outdoor ambience (rain, wind, background) is set from the acoustic probe v2 instead of the exposure probe. Volume and muffling are set directly from the physics (no game curve), and the game's own rain and wind sources become directional indoors (no copies). Needs fixes 3 and 8 on.", null, new ConfigurationManagerAttributes { Order = 44 }));
            GunV2On = Config.Bind(fix, "9a Gunshots from probe v2", false, new ConfigDescription("Test: your own gunshots' indoor/outdoor mix (3c Enclosure mode), the hard-room reverb boost (5a) and the hard-room echo boost (6e) use the acoustic probe v2's room measure instead of the 32-ray enclosure probe.", null, new ConfigurationManagerAttributes { Order = 43 }));
            BedDirectional = Config.Bind(fix, "9b Game sounds directional", 1f, new ConfigDescription("Fix 9: how far the game's own outdoor sounds become positioned sound coming from the open directions as you go inside. 0 = they stay flat stereo.", new AcceptableValueRange<float>(0f, 1f), new ConfigurationManagerAttributes { Order = 42 }));
            BedMode = Config.Bind(fix, "9c Directional mode", BedDirection.Mode.UnityPanner, new ConfigDescription("Fix 9: UnityPanner = left/right placement with width, duller from behind (smooth for sure). MetaHrtf = the game's 3D audio plugin (front/back/up/down), if it handles the gradual blend.", null, new ConfigurationManagerAttributes { Order = 41 }));
            GlassLossDb = Config.Bind(fix, "9d Glass loss dB", 22f, new ConfigDescription("Fix 9: how much quieter outdoor sound is through closed glass (a real window: about 20 to 25 dB, and muffled). Broken glass and empty frames let it through.", new AcceptableValueRange<float>(0f, 40f), new ConfigurationManagerAttributes { Order = 40 }));
            RoomBuildUp = Config.Bind(fix, "9e Sound build-up in rooms", 1f, new ConfigDescription("Fix 9: how much sound coming in builds up inside by bouncing (a bare concrete garage gets loud, a furnished room or the back of a building not). Since 0.6.4 measured by tracing paths up to 8 reflections. 1 = the measured value, 0 = only direct and up to 3 bounces.", new AcceptableValueRange<float>(0f, 2f), new ConfigurationManagerAttributes { Order = 39 }));
            GuidedRays = Config.Bind(fix, "9f Guided rays", true, new ConfigDescription("Probe v2: aim a third of the paths at the openings already found (windows, doors), counted so the totals stay right. Steadier sound deep inside, where few paths get out. Off = every path goes in an even or random direction. Also changes gunshot room readings slightly.", null, new ConfigurationManagerAttributes { Order = 38 }));
            RayQualityCfg = Config.Bind(fix, "9g Ray quality", AcousticProbe.RayQuality.Medium, new ConfigDescription("Probe v2: how many paths it traces. More = steadier sound deep inside (noise halves at 4x the paths) and more CPU. Low 320 a second, about half the cost of Medium. Medium 640 (raid 25: about 0.15 ms per frame at 107 fps). High 1280, about 2x. Ultra 2560, about 4x (around 0.6 ms per frame at 107 fps). Changes live.", null, new ConfigurationManagerAttributes { Order = 37 }));
            DirectionStrength = Config.Bind(fix, "9h Direction strength", 1f, new ConfigDescription("Fix 9: how pointed the outdoor sound and roof rain are indoors. 1 = as measured (deep inside mostly all round, leaning towards the openings). Higher = they come more clearly from the openings or the stairwell. 0 = always all round.", new AcceptableValueRange<float>(0f, 3f), new ConfigurationManagerAttributes { Order = 36 }));
            RoofRainFromProbe = Config.Bind(fix, "9i Roof rain from probe", true, new ConfigDescription("Fix 9: rain on the roof is traced from the roof itself: full right under it, partly under a mezzanine, down the stairwell from the floor below, from the direction it reaches you. Off = the floors-above count (8b).", null, new ConfigurationManagerAttributes { Order = 35 }));

            Harmony = new Harmony(Guid);
            Safe("self-check", PatchSelfCheck.Run);
            Safe("timing patches", () => TimingPatches.Apply(Harmony));
            Safe("shot probe", () => ShotProbe.Apply(Harmony));
            Safe("env probe", () => EnvProbe.Apply(Harmony));
            Safe("setting 0 gun sounds", () => GunSoundSource.Apply(Harmony));
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
            gameObject.AddComponent<AcousticProbe>();
            gameObject.AddComponent<AmbienceMix>();
            Log.LogInfo("Real Sound Simulation loaded");
        }

        internal static string FixState() =>
            $"0 gun sounds {GunSounds.Value}{(GunSounds.Value == GunSoundSet.SEB ? (SebOnFullAuto.Value ? " (all guns)" : " (semi-only guns)") : "")} | " +
            $"fix1 gun mixer {(GunMixerOn.Value ? $"ON ({GunMuffleAbove.Value:0.00}/{GunUnmuffleBelow.Value:0.00}, hold {GunHoldSeconds.Value:0.0} s)" : "off")} | " +
            $"fix2 obstruction {ObstructionModeCfg.Value} | " +
            $"fix3 doorways {(DoorwayOn.Value ? $"ON (in {DoorwayInside.Value:0.0} m / out {DoorwayOutside.Value:0.0} m)" : "off")} | " +
            $"fix3c own gunshot blend {(GunEnvBlendOn.Value ? $"ON ({OwnShotSource.Value}, aim {OwnShotAimWeight.Value:0.00}, thin {ThinSurfaceCount.Value:0.00}, muzzle {MuzzleWeight.Value:0.00}, small space {SmallSpace.Value})" : "off")} | " +
            $"5 own reverb boost {OwnReverbBoostDb.Value:+0.0;-0.0;0} dB (+{HardRoomReverbDb.Value:0} in hard rooms) | probe {(ProbeOn.Value ? "on" : "off")} | " +
            $"fix4 off-map {(OffMapOn.Value ? "ON" : "off")} | fix7 rain dropout {(RainFixOn.Value ? "ON" : "off")} | fix8 openings {(ExposureMixOn.Value ? $"ON (cue {DirectionCueStrength.Value:0.00}, roof rain {RoofRainUnderFloors.Value:0.00}, glass {ThroughGlass.Value:0.00}, indoor fade {IndoorFadeNearOpenings.Value:0.00}, openings {OpeningShare.Value:0.00}{(OpeningHrtf.Value ? " HRTF" : "")})" : "off")} | exposure probe {(ExposureProbeOn.Value ? "on" : "off")} | " +
            $"fix6 echoes {(EchoOn.Value ? $"ON ({EchoLevelDb.Value:+0.0;-0.0;0} dB, max {EchoMaxTaps.Value}, directivity {EchoDirectivityDb.Value:0} dB, far {(EchoFar.Value ? "on" : "off")}, hard rooms +{EchoHardRoomDb.Value:0} dB)" : "off")} | " +
            $"probe v2 {(AcousticProbeOn.Value ? "on" : "off")}: fix9 ambience {(AmbienceV2On.Value ? $"ON (directional {BedDirectional.Value:0.00} {BedMode.Value}, glass {GlassLossDb.Value:0} dB, build-up {RoomBuildUp.Value:0.00}, guided {(GuidedRays.Value ? "on" : "off")}, rays {RayQualityCfg.Value}, direction x{DirectionStrength.Value:0.00}, roof rain {(RoofRainFromProbe.Value ? "probe" : "floors")})" : "off")}, fix9a gunshots {(GunV2On.Value ? "ON" : "off")}";

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
