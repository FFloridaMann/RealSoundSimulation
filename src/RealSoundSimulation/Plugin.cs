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
    [BepInPlugin(Guid, "Real Sound Simulation", "0.6.44")]
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
        internal static ConfigEntry<float> SpawnBackoffS;              // 0.6.19
        internal static ConfigEntry<bool> AudioThreadTimingOn; // 0.6.20
        internal static ConfigEntry<bool> RouteLogOn; // 0.6.22
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
        internal static ConfigEntry<bool> RainFromOpenings;
        internal static ConfigEntry<bool> DistantThroughOpeningsOn;

        internal static ConfigEntry<bool> GunMixerOn;
        internal static ConfigEntry<float> GunMuffleAbove;
        internal static ConfigEntry<float> GunUnmuffleBelow;
        internal static ConfigEntry<float> GunHoldSeconds;
        internal static ConfigEntry<bool> FirstBurstOn; // 0.6.21
        internal static ConfigEntry<bool> ShotOverlapOn; // 0.6.23
        internal static ConfigEntry<bool> PortalPositioningOn; // 0.6.24
        internal static ConfigEntry<float> PortalStrength;
        internal static ConfigEntry<bool> RouteDullOn; // 0.6.40 "6.1b"
        internal static ConfigEntry<float> RouteDullStrength, RouteDullMinHz;
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
        internal static ConfigEntry<float> BlendSteepness;     // 0.6.9 "3j"
        internal static ConfigEntry<float> OpenSkyLowersIndoor; // 0.6.26 "3n"
        internal static ConfigEntry<float> ShotRingFollowsTurn; // 0.6.30 "3o"
        internal static ConfigEntry<bool> EchoMeasuredSurfaces; // 0.6.25
        internal static ConfigEntry<float> EarlyEchoTrimDb;    // 0.6.9 "6f"
        internal static ConfigEntry<bool> IndoorWindFollowsOpenings; // 0.6.9 "8g"
        internal static ConfigEntry<float> RainThroughBuildingDb;   // 0.6.9 "9l"
        internal static ConfigEntry<BedDirection.Mode> RoofBedMode; // 0.6.10 "9m"
        internal static ConfigEntry<float> MuzzleLobe;     // 0.6.11 "3k"
        internal static ConfigEntry<bool> OutdoorTailOn;   // 0.6.11 "3m"
        internal static ConfigEntry<float> OutdoorTailDb;  // 0.6.11 "3l"
        internal static ConfigEntry<float> DeepBounceDulling; // 0.6.14 "9n"
        internal static ConfigEntry<bool> ProbeMeasuredSurfaces; // 0.6.27 "9o"
        internal static ConfigEntry<bool> RoofCountsFloors; // 0.6.29 "9p"
        internal static ConfigEntry<float> RoofFloorLossScale; // 0.6.29 "9q"
        internal static ConfigEntry<float> RoofRainByMaterial; // 0.6.31 "9r"
        internal static ConfigEntry<bool> ProbeChangeDetect; // 0.6.32 "9s"
        internal static ConfigEntry<float> RoofRainLevel; // 0.6.33 "9t"
        internal static ConfigEntry<float> RoofRainShaftLoss; // 0.6.34 "9u"
        internal static ConfigEntry<bool> RoofCountsLowDetail; // 0.6.36 "9v"
        internal static ConfigEntry<bool> RoofRainNoRainRooms; // 0.6.36 "9w"
        internal static ConfigEntry<bool> ThumpOn; // 0.6.37 "6.2"
        internal static ConfigEntry<float> ThumpDb, ThumpCutoffHz, ThumpExplosionCutoffHz, ThumpWallLoss;
        internal static ConfigEntry<bool> ThumpWallsStop; // 0.6.38 "6.2e"
        internal static ConfigEntry<float> ThumpGunReach, ThumpExplosionReach; // 0.6.38 "6.2f", "6.2g"
        internal static ConfigEntry<bool> ExplosionEchoOn; // 0.6.39 "6.3"
        internal static ConfigEntry<bool> BlastOpeningOn, BlastDullOn; // 0.6.41 "6.4"
        internal static ConfigEntry<float> BlastOpeningStrength, ThumpExplosionDb, ThumpOpenDb, ThumpExplosionLengthS;
        internal static ConfigEntry<float> ExplosionEchoDb, ExplosionEchoLengthS, ExplosionEchoRangeM;
        internal static ConfigEntry<int> ExplosionEchoCount;
        internal static ConfigEntry<bool> FoleyPresetsOn; // 0.6.35 "10a"
        internal static ConfigEntry<bool> ExtraRoutesOn; // 0.6.35 "10b"

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
            LogsOn = Config.Bind(diag, "Write logs", true, "Write CSV logs and a summary per raid to BepInEx/RealSoundSimulationLogs. Switching it off in raid stops writing log rows at once (the summary is still written at raid end); switching it on starts logging at the next raid.");
            ContainerLogInterval = Config.Bind(diag, "Container log interval s", 0.5f, new ConfigDescription("How often every sound container's occlusion state is logged (at least every 1 s while there are more than 50 containers).", new AcceptableValueRange<float>(0.1f, 5f)));
            SourceScanInterval = Config.Bind(diag, "Source scan interval s", 0f, new ConfigDescription("How often every playing source in the scene is counted and new rolloff curves dumped (FindObjectsOfType: ~56 ms each time, a visible hitch). 0 = off (default since 0.6.15); the summary's playing sources in sound containers (every 0.5 s) replaces it.", new AcceptableValueRange<float>(0f, 60f)));
            TimingOn = Config.Bind(diag, "Time the audio systems", true, "Measure milliseconds spent in SpatialAudioSystem and BetterAudio each frame.");
            SpawnBackoffS = Config.Bind(diag, "Diagnostics back off after a spawn s", 2f, new ConfigDescription("0.6.19: for this long after the alive-bot count changes, the diagnostics skip their per-frame work (container rows, overlay, ambience and probe rows, log saving) so none of it lands on the frames a spawn wave already makes long. Measurements only, with one side effect: the echo headroom cap (fix 6) keeps its last count of playing game sources until the back-off ends. 0 = off.", new AcceptableValueRange<float>(0f, 10f)));
            AudioThreadTimingOn = Config.Bind(diag, "Time the audio thread", true, "0.6.20: a pass-through filter on the listener measures the time between DSP blocks (one block = dspBuffer samples, about 21 ms at 48 kHz). Blocks that come late mean the audio thread could not keep up (crackle). timing.csv, the overlay and the summary's AUDIO THREAD section. Also records Unity's own audio counters if this build offers them (the list goes to audio-counters.txt; a DSP load counter would show how close each block came to its deadline). Changes no sound; attached at raid start.");
            RouteLogOn = Config.Bind(diag, "Log portal routes", false, "0.6.22 (roadmap 6.1, measure only): each time a bot fires (at most 4 times a second per gun), log which doorway or opening the game's best route goes through and how far, to routes.csv. Moves no sound. Live.");
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
            FirstBurstOn = Config.Bind(fix, "1d First burst muffled", true, new ConfigDescription("0.6.21: a quiet bot far away has no occlusion computed yet, so its first burst played un-muffled and was then cut down. With this on, if a wall is in the way, that first burst starts muffled and the game's own calculation takes over. Off = the game's behaviour. Live.", null, new ConfigurationManagerAttributes { Order = 96 }));
            ShotOverlapOn = Config.Bind(fix, "1e Shots ring under the next", true, new ConfigDescription("0.6.23: every gun, yours and bots. The game stops a shot dead when the same gun's next sound starts. With this on it rings out underneath; only a firing loop is ended. A gun has two voices, so a third sound takes back the oldest. If the game runs out of gun voices, the gun closest to finishing gives up its own so a shot never goes silent. Off = the game's behaviour. Live.", null, new ConfigurationManagerAttributes { Order = 95 }));
            PortalPositioningOn = Config.Bind(fix, "6.1 Bot gunshots from the opening", true, new ConfigDescription("0.6.24: a bot's gunshot from another room comes from the doorway, window or stairwell it reaches you through, at the length of that route, instead of through the wall. Uses the route the game already picks. Same room, both outdoors, or no baked route = the true position. Off = the game's behaviour. Live.", null, new ConfigurationManagerAttributes { Order = 94 }));
            PortalStrength = Config.Bind(fix, "6.1a Opening strength", 1f, new ConfigDescription("How far towards the opening the shot is moved: 1 = all the way, 0.5 = halfway between the true direction and the opening.", new AcceptableValueRange<float>(0f, 1f), new ConfigurationManagerAttributes { Order = 93 }));
            RouteDullOn = Config.Bind(fix, "6.1b Dull the shot by its route", true, new ConfigDescription("0.6.40: a bot's gunshot from another room loses its highs by the way it got to you: a wall on the straight line, the turn at the opening, and each extra opening. A clear line through a door stays clear. Works with 6.1 off. Live.", null, new ConfigurationManagerAttributes { Order = 93 }));
            RouteDullStrength = Config.Bind(fix, "6.1c Dulling strength", 1f, new ConfigDescription("How strongly the route dulls the shot. 1 = the starting values, 0 = no dulling, 2 = twice as many octaves. Live.", new AcceptableValueRange<float>(0f, 2f), new ConfigurationManagerAttributes { Order = 93 }));
            RouteDullMinHz = Config.Bind(fix, "6.1d Dullest cutoff Hz", 800f, new ConfigDescription("The dullest the route can make a shot. Lower = more muffled on long routes. Live.", new AcceptableValueRange<float>(400f, 4000f), new ConfigurationManagerAttributes { Order = 93 }));
            ThumpOn = Config.Bind(fix, "6.2 Through-wall thump", true, new ConfigDescription("0.6.37: a bot gunshot in another room, or an explosion behind a wall, also comes through the wall as a low thump from its true direction. The clear sound still comes from the opening. At most 4 at once. Live.", null, new ConfigurationManagerAttributes { Order = 93 }));
            ThumpDb = Config.Bind(fix, "6.2a Thump level dB", 0f, new ConfigDescription("How loud the thump is before distance and wall loss. Live.", new AcceptableValueRange<float>(-30f, 12f), new ConfigurationManagerAttributes { Order = 93 }));
            ThumpCutoffHz = Config.Bind(fix, "6.2b Thump cutoff Hz", 50f, new ConfigDescription("Low-pass for gunshots through walls. Lower = deeper. Depends on your headset. Live.", new AcceptableValueRange<float>(20f, 400f), new ConfigurationManagerAttributes { Order = 93 }));
            ThumpExplosionCutoffHz = Config.Bind(fix, "6.2c Explosion thump cutoff Hz", 80f, new ConfigDescription("Low-pass for explosions through walls. Higher than gunshots because low frequencies pass walls better. Live.", new AcceptableValueRange<float>(20f, 400f), new ConfigurationManagerAttributes { Order = 93 }));
            ThumpWallLoss = Config.Bind(fix, "6.2d Wall loss strength", 0.5f, new ConfigDescription("How much quieter walls make a thump that gets through. 1 = the full table, 0 = no quieter. Whether it gets through at all is 6.2e to 6.2g. Live.", new AcceptableValueRange<float>(0f, 1.5f), new ConfigurationManagerAttributes { Order = 93 }));
            ThumpWallsStop = Config.Bind(fix, "6.2e Walls can stop a sound", true, new ConfigDescription("0.6.38: with enough wall in the way, or earth, a sound plays no thump at all. Off = every blocked sound in range plays. Live.", null, new ConfigurationManagerAttributes { Order = 93 }));
            ThumpGunReach = Config.Bind(fix, "6.2f Gunshot reach dB", 60f, new ConfigDescription("How much distance and wall a gunshot's thump can get through. Distance past 5 m costs 6 dB per doubling; one concrete wall about 24 to 36 dB; earth 75. Suppressed guns get 20 less. Live.", new AcceptableValueRange<float>(0f, 120f), new ConfigurationManagerAttributes { Order = 93 }));
            ThumpExplosionReach = Config.Bind(fix, "6.2g Explosion reach dB", 80f, new ConfigDescription("The same for explosions. Live.", new AcceptableValueRange<float>(0f, 120f), new ConfigurationManagerAttributes { Order = 93 }));
            ThumpExplosionDb = Config.Bind(fix, "6.2h Explosion thump level dB", 12f, new ConfigDescription("0.6.41: extra level for an explosion's thump through walls, on top of 6.2a, so guns stay as they are. Live.", new AcceptableValueRange<float>(-12f, 24f), new ConfigurationManagerAttributes { Order = 93 }));
            ThumpOpenDb = Config.Bind(fix, "6.2i Open-air thump dB", -6f, new ConfigDescription("0.6.42: an explosion with no wall in the way gets the thump too, at 6.2h's level plus this. Lower = less. Live.", new AcceptableValueRange<float>(-24f, 6f), new ConfigurationManagerAttributes { Order = 93 }));
            ThumpExplosionLengthS = Config.Bind(fix, "6.2j Explosion thump length s", 1.5f, new ConfigDescription("0.6.43: how long an explosion's thump plays. It fades out over the second half of this, so a short setting doesn't cut off. It can't run past the end of the recording. Live.", new AcceptableValueRange<float>(0.2f, 5f), new ConfigurationManagerAttributes { Order = 93 }));
            BlastOpeningOn = Config.Bind(fix, "6.4 Blast from the opening", true, new ConfigDescription("0.6.41: an explosion in another room comes from the doorway, window or stairwell it reaches you through, at the length of that route. Since 0.6.42 the grenade's bounces do too. The thump (6.2) stays at the true spot. Same room, both outdoors, or no baked route = the true position. Live.", null, new ConfigurationManagerAttributes { Order = 93 }));
            BlastOpeningStrength = Config.Bind(fix, "6.4a Blast opening strength", 1f, new ConfigDescription("How far towards the opening the blast is moved: 1 = all the way, 0.5 = halfway between the true direction and the opening. Live.", new AcceptableValueRange<float>(0f, 1f), new ConfigurationManagerAttributes { Order = 93 }));
            BlastDullOn = Config.Bind(fix, "6.4b Dull the blast by its route", true, new ConfigDescription("0.6.41: the blast loses its highs by the way it got to you, with the same rule, strength and floor as gunshots (6.1c, 6.1d). Works with 6.4 off. Live.", null, new ConfigurationManagerAttributes { Order = 93 }));
            ExplosionEchoOn = Config.Bind(fix, "6.3 Explosion echoes", true, new ConfigDescription("0.6.39: an explosion echoes off walls that it and you can both reach, from you or a bot. Live.", null, new ConfigurationManagerAttributes { Order = 93 }));
            ExplosionEchoDb = Config.Bind(fix, "6.3a Explosion echo level dB", 0f, new ConfigDescription("How loud the echoes are. Try +12 to hear exactly what they do, then bring it down. Live.", new AcceptableValueRange<float>(-24f, 12f), new ConfigurationManagerAttributes { Order = 93 }));
            ExplosionEchoCount = Config.Bind(fix, "6.3b Echoes per explosion", 6, new ConfigDescription("The loudest this many play. Live.", new AcceptableValueRange<int>(1, 12), new ConfigurationManagerAttributes { Order = 93 }));
            ExplosionEchoLengthS = Config.Bind(fix, "6.3c Explosion echo length s", 1.5f, new ConfigDescription("How long each echo lasts. Shorter = tighter, longer = bigger. Live.", new AcceptableValueRange<float>(0.3f, 3f), new ConfigurationManagerAttributes { Order = 93 }));
            ExplosionEchoRangeM = Config.Bind(fix, "6.3d Explosion echo range m", 120f, new ConfigDescription("How far away a blast can be and still echo. Live.", new AcceptableValueRange<float>(20f, 250f), new ConfigurationManagerAttributes { Order = 93 }));
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
            BedMode = Config.Bind(fix, "9c Directional mode", BedDirection.Mode.UnityPanner, new ConfigDescription("Fix 9, the outdoor sounds (wind, rain, background; roof rain has 9m since 0.6.10): UnityPanner = left/right placement with width, duller from behind (smooth for sure). MetaHrtf = the game's 3D audio plugin (front/back/up/down), if it handles the gradual blend.", null, new ConfigurationManagerAttributes { Order = 41 }));
            GlassLossDb = Config.Bind(fix, "9d Glass loss dB", 22f, new ConfigDescription("Fix 9: how much quieter outdoor sound is through closed glass (a real window: about 20 to 25 dB, and muffled). Broken glass and empty frames let it through.", new AcceptableValueRange<float>(0f, 40f), new ConfigurationManagerAttributes { Order = 40 }));
            RoomBuildUp = Config.Bind(fix, "9e Sound build-up in rooms", 1f, new ConfigDescription("Fix 9: how much sound coming in builds up inside by bouncing (a bare concrete garage gets loud, a furnished room or the back of a building not). Since 0.6.4 measured by tracing paths up to 8 reflections. 1 = the measured value, 0 = only direct and up to 3 bounces.", new AcceptableValueRange<float>(0f, 2f), new ConfigurationManagerAttributes { Order = 39 }));
            GuidedRays = Config.Bind(fix, "9f Guided rays", true, new ConfigDescription("Probe v2: aim a third of the paths at the openings already found (windows, doors), counted so the totals stay right. Steadier sound deep inside, where few paths get out. Off = every path goes in an even or random direction. Also changes gunshot room readings slightly.", null, new ConfigurationManagerAttributes { Order = 38 }));
            RayQualityCfg = Config.Bind(fix, "9g Ray quality", AcousticProbe.RayQuality.Medium, new ConfigDescription("Probe v2: how many paths it traces. More = steadier sound deep inside (noise halves at 4x the paths) and more CPU. Low 320 a second, about half the cost of Medium. Medium 640 (raid 25: about 0.15 ms per frame at 107 fps). High 1280, about 2x. Ultra 2560, about 4x (around 0.6 ms per frame at 107 fps). Changes live.", null, new ConfigurationManagerAttributes { Order = 37 }));
            DirectionStrength = Config.Bind(fix, "9h Direction strength", 1f, new ConfigDescription("Fix 9: how pointed the outdoor sound and roof rain are indoors. 1 = as measured (deep inside mostly all round, leaning towards the openings). Higher = they come more clearly from the openings or the stairwell. 0 = always all round.", new AcceptableValueRange<float>(0f, 3f), new ConfigurationManagerAttributes { Order = 36 }));
            RoofRainFromProbe = Config.Bind(fix, "9i Roof rain from probe", true, new ConfigDescription("Fix 9: rain on the roof is traced from the roof itself: full right under it, partly under a mezzanine, down the stairwell from the floor below, from the direction it reaches you. Off = the floors-above count (8b).", null, new ConfigurationManagerAttributes { Order = 35 }));
            RainFromOpenings = Config.Bind(fix, "9j Rain from openings", true, new ConfigDescription("Fix 9: indoors, the outdoor sound (rain, wind) comes from the window or door it gets in by (where the probe's paths leave the building). Off = from the direction the sound reaches you (often a doorway towards the open part of the building).", null, new ConfigurationManagerAttributes { Order = 34 }));
            DistantThroughOpeningsOn = Config.Bind(fix, "9k Distant gunfire through openings", true, new ConfigDescription("Probe v2: when you're inside, distant sounds off the map (the ambient gunfire mod's battles) come through the windows and doors that face them, at the level that gets in, instead of through the walls from their true direction. Needs fix 4. Off = left to the game and fix 4.", null, new ConfigurationManagerAttributes { Order = 33 }));
            RainThroughBuildingDb = Config.Bind(fix, "9l Rain through the building dB", -32f, new ConfigDescription("Fix 9: deep inside, the outdoor sound never drops below this level, muffled to a low rumble (a building in rain is never silent: the whole structure is being hit). Fades in as you go inside. -60 = off.", new AcceptableValueRange<float>(-60f, -20f), new ConfigurationManagerAttributes { Order = 32 }));
            DeepBounceDulling = Config.Bind(fix, "9n Deep bounce dulling", 0.8f, new ConfigDescription("Probe v2: past the 3rd reflection, each further bounce multiplies the build-up's brightness by this (rooms deep inside rumble rather than hiss). 1 = off (before 0.6.14).", new AcceptableValueRange<float>(0.3f, 1f), new ConfigurationManagerAttributes { Order = 30 }));
            ProbeMeasuredSurfaces = Config.Bind(fix, "9o Measured surfaces", true, new ConfigDescription("0.6.27: probe v2 uses the measured value for each surface (Meta XR's own table; docs/MATERIALS.md) for how much a bounce absorbs, scatters and dulls. Feeds rain through buildings, exit credits, roof rain and the gunshot room measure. Off = the earlier hand-set values. Live.", null, new ConfigurationManagerAttributes { Order = 29 }));
            RoofCountsFloors = Config.Bind(fix, "9p Roof rain counts the floors above", true, new ConfigDescription("0.6.29: roof rain is traced to each ceiling, and every floor above that ceiling takes off an amount by its material, so it fades gradually under more floors and differs between a tin roof and a concrete slab. Off = the earlier top-roof test and the on/off two-floors rule. Live.", null, new ConfigurationManagerAttributes { Order = 28 }));
            RoofFloorLossScale = Config.Bind(fix, "9q Roof rain loss per floor", 1f, new ConfigDescription("9p: how much each floor above takes off. 1 = the measured value for its material (concrete about 28 dB; 25 dB if it has no material tag), 0 = floors do not reduce it, 0.5 = half the dB.", new AcceptableValueRange<float>(0f, 1.5f), new ConfigurationManagerAttributes { Order = 27 }));
            RoofRainByMaterial = Config.Bind(fix, "9r Roof rain by roof material", 1f, new ConfigDescription("0.6.31: rain is louder on a thin metal roof (about +9 dB) and quieter on a soft one. Same recording as the game; this changes its level. 0 = every roof the same, 1 = as set, up to 1.5 for more. Live.", new AcceptableValueRange<float>(0f, 1.5f), new ConfigurationManagerAttributes { Order = 26 }));
            ProbeChangeDetect = Config.Bind(fix, "9s Sound reacts to real changes", true, new ConfigDescription("0.6.32: probe v2 forgets what it measured when the last moments clearly disagree with it, instead of by how fast you are moving. Rooms change at any speed, and standing still stays steady. Off = memory by speed, as before. Live.", null, new ConfigurationManagerAttributes { Order = 25 }));
            RoofRainLevel = Config.Bind(fix, "9t Roof rain level", 0.3f, new ConfigDescription("0.6.34: how loud roof rain plays while it rains, before the roof, floors and material are applied, times the game's own factor for how hard it rains. Replaces the game's value for the room, which ranges from 0 to 1 and made the rain jump between rooms. 0 = the game's value. Live.", new AcceptableValueRange<float>(0f, 1f), new ConfigurationManagerAttributes { Order = 24 }));
            RoofRainShaftLoss = Config.Bind(fix, "9u Roof rain fades down shafts", 1f, new ConfigDescription("0.6.34: dB of roof rain lost per metre on a path that bounces its way to the roof (down a stair shaft), after the first 6 m. A path straight up doesn't fade. 0 = off. Live.", new AcceptableValueRange<float>(0f, 3f), new ConfigurationManagerAttributes { Order = 24 }));
            RoofCountsLowDetail = Config.Bind(fix, "9v Roof rain counts low-detail floors", true, new ConfigDescription("0.6.36: 9p also counts large low-detail floors, which much of the Capital is built from. Off = only the main solid layers, as before, which can read a ceiling with floors above as the top roof. Live.", null, new ConfigurationManagerAttributes { Order = 24 }));
            RoofRainNoRainRooms = Config.Bind(fix, "9w Roof rain respects no-rain rooms", true, new ConfigDescription("0.6.36: when the game marks you indoors and gives the room a rain level of 0, roof rain fades out over 1.5 s. Covers buildings whose upper storeys have no collision for the rays to find. Off = the rays alone decide. Live.", null, new ConfigurationManagerAttributes { Order = 24 }));
            FoleyPresetsOn = Config.Bind(fix, "10a Footsteps and handling like EFT 1.0", true, new ConfigDescription("0.6.35: footsteps, gear and weapon handling use EFT 1.0's distance curves, stereo spread and 3D ear processing, taken from Sound Engine Backport by red_the_fox. Levels stay as they are. Off = SPT's settings. Restart the game to switch.", null, new ConfigurationManagerAttributes { Order = 23 }));
            ExtraRoutesOn = Config.Bind(fix, "10b Sound routes between rooms from EFT 1.0", true, new ConfigDescription("0.6.35: routes sound takes between rooms (through doors, windows, stairs), from Sound Engine Backport by red_the_fox. Each map is checked first, and if it differs the game's own routes are used. Off = the game's routes. Applies at the next raid.", null, new ConfigurationManagerAttributes { Order = 23 }));
            RoofBedMode = Config.Bind(fix, "9m Roof rain directional mode", BedDirection.Mode.UnityPanner, new ConfigDescription("Fix 9: like 9c, for the roof rain only (9c now sets the outdoor sounds: wind, rain, background). UnityPanner keeps the recording's width and steers it; MetaHrtf gives up/down placement. Up versus down only matters for the roof rain, so this is where to try HRTF. Either way the roof rain is muffled by how its paths reach you (down a stair shaft duller than overhead).", null, new ConfigurationManagerAttributes { Order = 31 }));
            MuzzleLobe = Config.Bind(fix, "3k Muzzle lobe", 1f, new ConfigDescription("Fix 3c: the room measured at the muzzle counts the directions in front of it more (a forward lobe around where you aim, ((1 + cos) / 2)^k). Out of a window the muzzle then reads mostly outdoor even though half of its sphere looks back into the room; into a room from a doorway, more indoor. 0 = the whole sphere (before 0.6.11), 1 = default, 2 = tighter.", new AcceptableValueRange<float>(0f, 2f), new ConfigurationManagerAttributes { Order = 69 }));
            OutdoorTailOn = Config.Bind(fix, "3m Outdoor tail from the opening", true, new ConfigDescription("Fix 3c: when your shot is mixed mostly indoor but you fire out of a window or door, the gun's outdoor tail also plays from that opening, a beat later (it adds; nothing of the shot is taken away). Single shots and the tail at the end of a burst. Uses fix 6's voices (needs 6 on). Off = as before.", null, new ConfigurationManagerAttributes { Order = 68 }));
            OutdoorTailDb = Config.Bind(fix, "3l Outdoor tail through opening dB", 0f, new ConfigDescription("Fix 3m: raise or lower the outdoor tail from the opening. 0 = the shot's level x how open the aim is x how indoor the shot is mixed.", new AcceptableValueRange<float>(-24f, 12f), new ConfigurationManagerAttributes { Order = 67 }));
            BlendSteepness = Config.Bind(fix, "3j Blend steepness", 0.5f, new ConfigDescription("Fix 3c: how quickly your gunshot leaves an even indoor/outdoor mix. 0 = straight (0.6.8; many shots near 50/50, two recordings at once can sound doubled). 1 = steep (0.6.9-0.6.13 default; at a garage mouth a small change became a big jump). 0.5 (default since 0.6.14) still makes near-even mixes rare (0.6 -> 0.68).", new AcceptableValueRange<float>(0f, 1f), new ConfigurationManagerAttributes { Order = 70 }));
            OpenSkyLowersIndoor = Config.Bind(fix, "3n Open sky lowers the indoor mix", 1f, new ConfigDescription("0.6.26, fix 3c: how much open sky around you takes off the room part of your gunshot mix. Standing outside with open space behind you and firing into a building now plays closer to an even mix. 1 = by the full share of open sky, 0 = the earlier behaviour. Only matters with probe v2 on gunshots (9a). Live.", new AcceptableValueRange<float>(0f, 1f), new ConfigurationManagerAttributes { Order = 69 }));
            ShotRingFollowsTurn = Config.Bind(fix, "3o Shot ring follows your turning", 1f, new ConfigDescription("0.6.30, fix 3c: at spots where your shot mixes the indoor and outdoor recordings, turning after the shot shifts them: sideways they pan across your ears, and whichever has swung behind you is dulled. Firing without turning changes nothing. 0 = off. Live.", new AcceptableValueRange<float>(0f, 1f), new ConfigurationManagerAttributes { Order = 68 }));
            EarlyEchoTrimDb = Config.Bind(fix, "6f Early echo trim dB", 0f, new ConfigDescription("Fix 6: added to echoes arriving under 25 ms after the shot (a wall within ~4 m). Those fuse with the shot as colour rather than a separate echo; if shots sound doubled, lower this. 0 = the physical level.", new AcceptableValueRange<float>(-24f, 0f), new ConfigurationManagerAttributes { Order = 52 }));
            EchoMeasuredSurfaces = Config.Bind(fix, "6g Measured surfaces", true, new ConfigDescription("0.6.25: fix 6 echoes use measured values for each surface (Meta XR's own material table; docs/MATERIALS.md): wood, glass and metal are no longer dulled, soft ground and fabric lose what they really lose. Off = the earlier hand-set values. Live.", null, new ConfigurationManagerAttributes { Order = 51 }));
            IndoorWindFollowsOpenings = Config.Bind(fix, "8g Indoor wind follows openings", true, new ConfigDescription("Some rooms' room tone is a wind or draught recording (its clip name has 'wind' in it). With this on, that one follows how much of the outside reaches you (like the outdoor sound) instead of how enclosed you are, so it fades deep in a stairwell. Other room tones are unchanged. Every room tone played is logged to roomtone.csv.", null, new ConfigurationManagerAttributes { Order = 44 }));

            Harmony = new Harmony(Guid);
            Safe("self-check", PatchSelfCheck.Run);
            Safe("allocation counter", Perf.InitAlloc); // 0.6.17
            Safe("timing patches", () => TimingPatches.Apply(Harmony));
            Safe("shot probe", () => ShotProbe.Apply(Harmony));
            Safe("env probe", () => EnvProbe.Apply(Harmony));
            Safe("setting 0 gun sounds", () => GunSoundSource.Apply(Harmony));
            Safe("fix 1 gun mixer", () => GunMixerFix.Apply(Harmony));
            Safe("1d first burst", () => FirstBurstOcclusion.Apply(Harmony)); // 0.6.21
            Safe("1e shot overlap", () => ShotOverlap.Apply(Harmony)); // 0.6.23
            Safe("3o shot ring turn", () => OwnShotTurn.Apply(Harmony)); // 0.6.30
            Safe("bot shots log", () => BotShots.Apply(Harmony)); // 0.6.21
            Safe("route log", () => RouteLog.Apply(Harmony)); // 0.6.22
            Safe("6.1 portal positioning", () => PortalPositioning.Apply(Harmony)); // 0.6.24
            Safe("8g room tone", () => RoomTone.Apply(Harmony));
            Safe("3m outdoor tail", () => OutdoorTail.Apply(Harmony));
            Safe("fix 2 obstruction score", () => ObstructionScoreFix.Apply(Harmony));
            Safe("fix 3 doorway blend", () => DoorwayBlend.Apply(Harmony));
            Safe("fix 3c own gunshot blend", () => GunEnvironmentBlend.Apply(Harmony));
            Safe("fix 4 off-map sounds", () => OffMapFix.Apply(Harmony));
            Safe("9k distant gunfire through openings", () => DistantThroughOpenings.Apply(Harmony));
            Safe("fix 6 own gun echoes", () => OwnEchoes.Apply(Harmony));
            Safe("fix 7 rain dropout", () => RainDropoutFix.Apply(Harmony));
            Safe("fix 8 ambience mix", () => AmbienceMix.Apply(Harmony));
            Safe("6.2 through-wall thump", () => ThroughWall.Apply(Harmony)); // 0.6.37
            Safe("6.3 explosion echoes", () => ExplosionEchoes.Apply(Harmony)); // 0.6.39
            Safe("6.4 blast from the opening", () => BlastFromOpening.Apply(Harmony)); // 0.6.41
            Safe("10a footsteps and handling", () => FoleyPresets.Apply(Harmony)); // 0.6.35
            Safe("10b routes", () => ExtraRoutes.Apply(Harmony)); // 0.6.35
            gameObject.AddComponent<DiagRunner>();
            gameObject.AddComponent<DoorwayBlend>();
            gameObject.AddComponent<EnclosureProbe>();
            gameObject.AddComponent<EchoVoices>();
            gameObject.AddComponent<OutdoorExposure>();
            gameObject.AddComponent<AcousticProbe>();
            gameObject.AddComponent<AmbienceMix>();
            gameObject.AddComponent<DistantThroughOpenings>();
            gameObject.AddComponent<PortalPositioning>(); // 0.6.24
            gameObject.AddComponent<BlastFromOpening>(); // 0.6.41
            gameObject.AddComponent<OwnShotTurn>(); // 0.6.30
            Log.LogInfo("Real Sound Simulation loaded");
        }

        internal static string FixState() =>
            $"0 gun sounds {GunSounds.Value}{(GunSounds.Value == GunSoundSet.SEB ? (SebOnFullAuto.Value ? " (all guns)" : " (semi-only guns)") : "")} | " +
            $"fix1 gun mixer {(GunMixerOn.Value ? $"ON ({GunMuffleAbove.Value:0.00}/{GunUnmuffleBelow.Value:0.00}, hold {GunHoldSeconds.Value:0.0} s)" : "off")}, 1d first burst {(FirstBurstOn.Value ? "ON" : "off")}, 1e shot overlap {(ShotOverlapOn.Value ? "ON" : "off")}, 6.1 bot gunshots from the opening {(PortalPositioningOn.Value ? $"ON ({PortalStrength.Value:0.00})" : "off")}, 6.1b route dulling {(RouteDullOn.Value ? $"ON (x{RouteDullStrength.Value:0.00}, floor {RouteDullMinHz.Value:0} Hz)" : "off")} | " +
            $"6.2 through-wall thump {(ThumpOn.Value ? $"ON ({ThumpCutoffHz.Value:0} Hz, explosions {ThumpExplosionCutoffHz.Value:0} Hz, level {ThumpDb.Value:+0;-0;0} dB, wall loss x{ThumpWallLoss.Value:0.00}, {(ThumpWallsStop.Value ? $"walls stop ON, reach {ThumpGunReach.Value:0}/{ThumpExplosionReach.Value:0}" : "walls stop off")})" : "off")} | " +
            $"6.4 blast from the opening {(BlastOpeningOn.Value ? $"ON ({BlastOpeningStrength.Value:0.00})" : "off")}, dulled {(BlastDullOn.Value ? "ON" : "off")}, explosion thump level {ThumpExplosionDb.Value:+0;-0;0} dB, open-air {ThumpOpenDb.Value:+0;-0;0} dB, length {ThumpExplosionLengthS.Value:0.0} s | " +
            $"6.3 explosion echoes {(ExplosionEchoOn.Value ? $"ON ({ExplosionEchoDb.Value:+0;-0;0} dB, {ExplosionEchoCount.Value} per blast, {ExplosionEchoLengthS.Value:0.0} s, {ExplosionEchoRangeM.Value:0} m)" : "off")} | " +
            $"10a footsteps and handling {FoleyPresets.State} | 10b routes {ExtraRoutes.State} | " +
            $"fix2 obstruction {ObstructionModeCfg.Value} | " +
            $"fix3 doorways {(DoorwayOn.Value ? $"ON (in {DoorwayInside.Value:0.0} m / out {DoorwayOutside.Value:0.0} m)" : "off")} | " +
            $"fix3c own gunshot blend {(GunEnvBlendOn.Value ? $"ON ({OwnShotSource.Value}, aim {OwnShotAimWeight.Value:0.00}, thin {ThinSurfaceCount.Value:0.00}, muzzle {MuzzleWeight.Value:0.00}, small space {SmallSpace.Value}, open sky {OpenSkyLowersIndoor.Value:0.00}, ring follows turn {ShotRingFollowsTurn.Value:0.00})" : "off")} | " +
            $"5 own reverb boost {OwnReverbBoostDb.Value:+0.0;-0.0;0} dB (+{HardRoomReverbDb.Value:0} in hard rooms) | probe {(ProbeOn.Value ? "on" : "off")} | " +
            $"fix4 off-map {(OffMapOn.Value ? "ON" : "off")} | fix7 rain dropout {(RainFixOn.Value ? "ON" : "off")} | fix8 openings {(ExposureMixOn.Value ? $"ON (cue {DirectionCueStrength.Value:0.00}, roof rain {RoofRainUnderFloors.Value:0.00}, glass {ThroughGlass.Value:0.00}, indoor fade {IndoorFadeNearOpenings.Value:0.00}, openings {OpeningShare.Value:0.00}{(OpeningHrtf.Value ? " HRTF" : "")})" : "off")} | exposure probe {(ExposureProbeOn.Value ? "on" : "off")} | " +
            $"fix6 echoes {(EchoOn.Value ? $"ON ({EchoLevelDb.Value:+0.0;-0.0;0} dB, max {EchoMaxTaps.Value}, directivity {EchoDirectivityDb.Value:0} dB, far {(EchoFar.Value ? "on" : "off")}, hard rooms +{EchoHardRoomDb.Value:0} dB, surfaces {(EchoMeasuredSurfaces.Value ? "measured" : "hand-set")})" : "off")} | " +
            $"probe v2 {(AcousticProbeOn.Value ? "on" : "off")}: fix9 ambience {(AmbienceV2On.Value ? $"ON (directional {BedDirectional.Value:0.00} {BedMode.Value}, glass {GlassLossDb.Value:0} dB, build-up {RoomBuildUp.Value:0.00}, guided {(GuidedRays.Value ? "on" : "off")}, rays {RayQualityCfg.Value}, direction x{DirectionStrength.Value:0.00}, roof rain {(RoofRainFromProbe.Value ? "probe" : "floors")}, rain from openings {(RainFromOpenings.Value ? "on" : "off")}, surfaces {(ProbeMeasuredSurfaces.Value ? "measured" : "hand-set")}, roof floors {(RoofCountsFloors.Value ? $"counted x{RoofFloorLossScale.Value:0.00}" : "old rule")}, roof material {RoofRainByMaterial.Value:0.00}, reacts to changes {(ProbeChangeDetect.Value ? "on" : "off")}, roof rain level {RoofRainLevel.Value:0.00}, shaft loss {RoofRainShaftLoss.Value:0.0} dB/m, low-detail floors {(RoofCountsLowDetail.Value ? "on" : "off")}, no-rain rooms {(RoofRainNoRainRooms.Value ? "on" : "off")})" : "off")}, 9k distant gunfire through openings {(DistantThroughOpeningsOn.Value ? "ON" : "off")}, fix9a gunshots {(GunV2On.Value ? "ON" : "off")}";

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
