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
    [BepInPlugin(Guid, "Real Sound Simulation", "0.7.0")]
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
        internal static ConfigEntry<float> BlastOpeningStrength, ThumpExplosionDb, ThumpOpenDb, ThumpExplosionLengthS, BlastDullStrength, DoorMufflingStrength;
        internal static ConfigEntry<bool> DoorMufflingOn; // 0.6.47 "6.5"
        internal static ConfigEntry<bool> RoomEchoOn; // 0.6.48 "6.3e"
        internal static ConfigEntry<float> RoomEchoDb;
        internal static ConfigEntry<int> RoomEchoCount;
        internal static ConfigEntry<bool> BlastRecordingOn, GlassRouteOn; // 0.6.49 "6.6", "6.7"
        internal static ConfigEntry<bool> BlastCornerOn; // 0.6.50 "6.8"
        internal static ConfigEntry<bool> GunCornerOn, BlastArrivalOn; // 0.6.51 "6.8b", "6.4d"
        internal static ConfigEntry<float> BlastCornerLossDb;
        internal static ConfigEntry<float> BlastOutdoorShare, BlastCloseReachM, GlassDullStrength, GlassLossDb2;
        internal static ConfigEntry<float> BlastOutsidePull; // 0.6.53 "6.6c"
        internal static ConfigEntry<bool> ShockOn; // 0.6.53 "6.9"
        internal static ConfigEntry<float> ShockLevelDb, ShockReachM, ShockDuckDb, ShockCornerDb, ShockEnclosedDb, ShockPushDb; // 0.6.54 "6.9d", "6.9e", "6.9g"
        internal static ConfigEntry<int> ShockRepeats; // 0.6.54 "6.9f"
        internal static ConfigEntry<bool> ConcussionPressureOn; // 0.6.60 "6.9k"
        internal static ConfigEntry<float> ConcussionPressureDb, EarsReachDb; // 0.6.60 "6.9m", "6.9l"
        internal static ConfigEntry<bool> SoftCutOn, FragmentRouteOn; // 0.6.55 "6.9h", "6.7c"
        internal static ConfigEntry<float> BlastTailFadeS, FeltThroughCutDb, FragmentDullStrength, FragmentLossDb; // 0.6.58 "6.9i", "6.9j"
        internal static ConfigEntry<bool> FragmentPassOn; // 0.6.56 "6.7f"
        internal static ConfigEntry<float> FragmentPassDb, FragmentPassReachM;
        internal static ConfigEntry<int> FragmentPassCount;
        internal static ConfigEntry<float> ExplosionEchoDb, ExplosionEchoLengthS, ExplosionEchoRangeM;
        internal static ConfigEntry<int> ExplosionEchoCount;
        internal static ConfigEntry<bool> BotEchoOn, EchoReplaceOn; // 0.6.61 "6.3h", "6.3p"
        internal static ConfigEntry<float> BotEchoDb, BotEchoRangeM, BotEchoGateDb, BotEchoLengthS;
        internal static ConfigEntry<int> BotEchoCount, BotEchoCeiling, OwnEchoShare;
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
            // F12 shows these in the order they're bound. Advanced ones only show with "Advanced settings" ticked.
            // Each description ends with the setting's old code, which the docs and logs still use.
            const string general = "1. General";
            const string gun = "2. Your gun";
            const string others = "3. Other guns";
            const string blasts = "4. Explosions";
            const string rain = "5. Rain and outdoors";
            const string walls = "6. Doors and walls";
            const string perf = "7. Performance";
            const string diag = "8. Diagnostics";
            const bool adv = true;

            // 1. General
            // The big switches go first, so they are easy to find: concussion now, ear ringing and headsets when they are built.
            ConcussionPressureOn = Bind(general, "Concussion by blast pressure", true, "How concussive a blast is comes from the pressure that reaches you: walls protect you, doors and corners help, small closed rooms hit hardest. Needs Bring Back Concussion. Was 6.9k.");
            SoftCutOn = Bind(general, "Soft concussion cut", true, "When concussion cuts your hearing, the world goes at once but the blast's tail fades instead of snapping off, and you still feel its thump. Needs Bring Back Concussion. Was 6.9h.");
            GunSounds = Bind(general, "Gun recordings", GunSoundSet.SPT, "Which recordings guns use. SPT = the game's own. SEB = Sound Engine Backport's EFT 1.0 recordings, once built into the seb_sounds folder with tools/build_seb_sounds.py. Changes at the next trigger pull. Was 0.");
            SebOnFullAuto = Bind(general, "SEB recordings on full-auto guns", false, "Also use SEB's recordings on guns that can fire full auto. Off because the game loops one recording while the trigger is held and SEB's are single shots, so bursts sound wrong. Was 0a.", null, adv);
            RayQualityCfg = Bind(general, "Sound quality", AcousticProbe.RayQuality.Medium, "How many sound paths are traced through the map each second. Higher = steadier, more accurate sound deep inside buildings, for more CPU. Low costs about half of Medium, High twice, Ultra four times. Was 9g.");
            ShotOverlapOn = Bind(general, "Shots ring out under the next", true, "Every gun, yours and everyone else's. The game stops a shot dead when the same gun's next sound starts; with this on it rings out underneath. Off = the game's behaviour. Was 1e.");
            FoleyPresetsOn = Bind(general, "Footsteps and handling like EFT 1.0", true, "Footsteps, gear and weapon handling use EFT 1.0's distance curves, stereo spread and 3D processing, from Sound Engine Backport by red_the_fox. Volumes stay the same. Restart the game to switch. Was 10a.");

            // 2. Your gun
            EchoOn = Bind(gun, "Echoes", true, "Your shots echo off the real surfaces around you (walls, ceilings, building faces, hills), each delayed by its distance and coloured by its material. Was 6.");
            EchoLevelDb = Bind(gun, "Echo volume dB", 0f, "Raise or lower all your echoes. 0 = the physical estimate. Was 6a.", R(-18f, 12f));
            EchoMaxTaps = Bind(gun, "Echoes per shot", 4, "The loudest this many reflections play for each shot. Was 6b.", N(1, 8));
            OwnReverbBoostDb = Bind(gun, "Room reverb boost dB", 0f, "Raise or lower the game's own room reverb on your shots. 0 = the game's level. Was 5.", R(-12f, 12f));
            GunEnvBlendOn = Bind(gun, "Indoor/outdoor blend", true, "Your shot mixes the game's indoor and outdoor recordings by how enclosed the space really is, instead of switching from one to the other. Was 3c.");
            EchoFar = Bind(gun, "Far echoes", true, "Also echo off building faces, cliffs and hills 30 to 250 m away. Was 6d.", null, adv);
            EchoHardRoomDb = Bind(gun, "Hard room echo boost dB", 4f, "In bare concrete stairwells and corridors your echoes are louder and up to 3 more play, by how hard and closed the room is. Was 6e.", R(0f, 12f), adv);
            HardRoomReverbDb = Bind(gun, "Hard room reverb boost dB", 6f, "Extra room reverb on your shots in bare concrete stairwells and corridors, by how hard and closed the room is. 0 = off. Was 5a.", R(0f, 12f), adv);
            EchoDirectivityDb = Bind(gun, "Muzzle directivity dB", 10f, "How much quieter your muzzle blast is behind you than in front, so firing into a room sounds different from firing out of one. Was 6c.", R(0f, 20f), adv);
            EarlyEchoTrimDb = Bind(gun, "Early echo trim dB", 0f, "Echoes arriving within 25 ms of the shot (a wall within about 4 m) blend into it. If your shots sound doubled, lower this. Was 6f.", R(-24f, 0f), adv);
            EchoMeasuredSurfaces = Bind(gun, "Echoes use measured materials", true, "Echoes use measured acoustic values for each material. Off = the older hand-set values. Was 6g.", null, adv);
            OutdoorTailOn = Bind(gun, "Outdoor tail out of openings", true, "Firing out of a window or door from inside, the gun's outdoor tail also plays from that opening a moment later. Needs Echoes on. Was 3m.", null, adv);
            OutdoorTailDb = Bind(gun, "Outdoor tail volume dB", 0f, "Raise or lower the outdoor tail from the opening. Was 3l.", R(-24f, 12f), adv);
            ShotRingFollowsTurn = Bind(gun, "Shot ring follows your turning", 1f, "Where your shot mixes indoor and outdoor, turning after the shot moves them around your head, and whichever is behind you gets duller. 0 = off. Was 3o.", R(0f, 1f), adv);
            OwnShotSource = Bind(gun, "Blend source", OwnShotBlendSource.Enclosure, "What decides the indoor/outdoor mix. Enclosure = the space around you and where you aim, everywhere. Doorway = distance through the nearest doorway, only near doorways. Was 3d.", null, adv);
            OwnShotAimWeight = Bind(gun, "Aim weight", 0.4f, "How much the direction you fire in counts against the space around you. 0 = only the surroundings, 1 = only the aim. Was 3e.", R(0f, 1f), adv);
            MuzzleWeight = Bind(gun, "Muzzle weight", 0.7f, "The room is measured at your head and at the muzzle. This is how much the muzzle counts. Was 3g.", R(0f, 1f), adv);
            MuzzleLobe = Bind(gun, "Muzzle lobe", 1f, "How much the muzzle measure favours the directions in front of it. 0 = all round, 2 = tighter. Was 3k.", R(0f, 2f), adv);
            ThinSurfaceCount = Bind(gun, "Thin surfaces count as walls", 0.5f, "How much glass, thin planks, cardboard and fabric count as walls. 1 = like concrete, 0 = like open air. Was 3f.", R(0f, 1f), adv);
            SmallSpace = Bind(gun, "Small space rule", SmallSpaceRule.Soft, "Small furnished spaces get less of the indoor recording. Current = down to half, Soft = at least 85%, Off = never. Was 3i.", null, adv);
            BlendSteepness = Bind(gun, "Blend steepness", 0.5f, "How quickly the mix leaves an even indoor/outdoor split. 0 = straight, 1 = steep. Was 3j.", R(0f, 1f), adv);
            OpenSkyLowersIndoor = Bind(gun, "Open sky lowers the indoor mix", 1f, "How much open sky around you takes off the room part of your shot. Only with Traced gunshots on. Was 3n.", R(0f, 1f), adv);
            GunV2On = Bind(gun, "Traced gunshots (test)", false, "Your shot's indoor/outdoor mix and the hard room boosts use the full sound tracer instead of the quicker 32-ray room measure. Was 9a.", null, adv);

            // 3. Other guns
            PortalPositioningOn = Bind(others, "Sound from the doorway", true, "A shot from another room comes from the doorway, window or stairwell it reaches you through, at the length of that route, instead of through the wall. Was 6.1.");
            RouteDullOn = Bind(others, "Muffle by route", true, "A shot from another room loses its highs by the way it got to you: a wall on the straight line, each turn and each extra opening. Was 6.1b.");
            BotEchoOn = Bind(others, "Echoes", true, "Other guns echo off walls that both the shooter and you can reach; from another room, off the walls around the opening. Needs Your gun > Echoes on. Was 6.3h.");
            BotEchoDb = Bind(others, "Echo volume dB", 0f, "Raise or lower other guns' echoes. Was 6.3i.", R(-24f, 12f));
            BotEchoCount = Bind(others, "Echoes per shooter", 3, "At most this many for each shot or end of a burst. A burst still firing gets one. Was 6.3j.", N(1, 6));
            PortalStrength = Bind(others, "Doorway strength", 1f, "How far towards the opening the shot is moved. 1 = all the way, 0.5 = halfway. Was 6.1a.", R(0f, 1f), adv);
            RouteDullStrength = Bind(others, "Muffle strength", 1f, "How strongly the route muffles a shot. 0 = not at all, 2 = twice as much. Was 6.1c.", R(0f, 2f), adv);
            RouteDullMinHz = Bind(others, "Most muffled cutoff Hz", 800f, "The most a route can muffle a shot. Lower = more muffled on long routes. Was 6.1d.", R(400f, 4000f), adv);
            GunCornerOn = Bind(others, "Around corners outdoors", true, "Outdoors, a shot from behind a building comes from around its corner, muffled. Was 6.8b.", null, adv);
            BotEchoRangeM = Bind(others, "Echo range m", 120f, "How far away a shooter can be and still echo. Was 6.3l.", R(20f, 250f), adv);
            BotEchoGateDb = Bind(others, "Quietest echo dB", -42f, "An echo quieter than this when it reaches you isn't played. Was 6.3m.", R(-60f, -20f), adv);
            BotEchoLengthS = Bind(others, "Echo length s", 2.5f, "How long each echo lasts. It fades over the second half. Was 6.3o.", R(0.5f, 5f), adv);
            OffMapOn = Bind(others, "Off-map sounds outdoors", true, "Sounds placed off the map's sound grid, like distant battles from ambient gunfire mods, count as outdoors instead of fully blocked. Was 4.", null, adv);
            DistantThroughOpeningsOn = Bind(others, "Distant gunfire through openings", true, "Indoors, distant off-map battles come through the windows and doors that face them. Needs Off-map sounds outdoors. Was 9k.", null, adv);
            GunMixerOn = Bind(others, "Steady muffling", true, "Stops distant gunfire flip-flopping between the muffled and clear sound. Was 1.", null, adv);
            GunMuffleAbove = Bind(others, "Muffle above", 0.60f, "How blocked a gun has to be to switch to the muffled sound (game: 0.55). Was 1a.", R(0.3f, 0.95f), adv);
            GunUnmuffleBelow = Bind(others, "Clear below", 0.45f, "How clear it has to be to switch back (game: 0.55). Was 1b.", R(0.05f, 0.9f), adv);
            GunHoldSeconds = Bind(others, "Hold s", 0.5f, "A change has to last this long before it applies. The first shot after 2 s of quiet is never held. Was 1c.", R(0f, 3f), adv);
            FirstBurstOn = Bind(others, "First burst muffled", true, "A distant shooter's first burst starts muffled if a wall is in the way, instead of starting clear and then being cut down. Was 1d.", null, adv);

            // 4. Explosions
            BlastOpeningOn = Bind(blasts, "Sound from the doorway", true, "An explosion in another room, and its grenade's bounces, come from the doorway, window or stairwell they reach you through. Was 6.4.");
            ThumpExplosionDb = Bind(blasts, "Thump volume dB", 12f, "Extra volume for an explosion's low thump through walls. Was 6.2h.", R(-12f, 24f));
            ExplosionEchoOn = Bind(blasts, "Echoes", true, "Explosions echo off walls that both the blast and you can reach. Was 6.3.");
            ExplosionEchoDb = Bind(blasts, "Echo volume dB", 0f, "Raise or lower explosion echoes. Was 6.3a.", R(-24f, 12f));
            ShockOn = Bind(blasts, "Shock wave", true, "The part of a blast you feel: a pressure hit, a thump that grows up close, and the world dipping for a moment. Was 6.9.");
            ShockLevelDb = Bind(blasts, "Pressure hit volume dB", 0f, "Full strength from 5 m in, weaker further out by real blast pressure. Was 6.9a.", R(-24f, 12f));
            ConcussionPressureDb = Bind(blasts, "Concussion strength dB", 0f, "Makes blasts count as stronger or weaker for concussion. +6 is about like being two-thirds as far away. Was 6.9m.", R(-12f, 12f));
            FragmentPassOn = Bind(blasts, "Fragment pass-bys", true, "Fragments that pass you whizz along their real flight path, and crack when they come very close. Was 6.7f.");
            BlastOpeningStrength = Bind(blasts, "Doorway strength", 1f, "How far towards the opening the blast is moved. 1 = all the way, 0.5 = halfway. Was 6.4a.", R(0f, 1f), adv);
            BlastDullOn = Bind(blasts, "Muffle by route", true, "The blast loses its highs by the way it got to you. Was 6.4b.", null, adv);
            BlastDullStrength = Bind(blasts, "Muffle strength", 0.5f, "How strongly the route muffles a blast. 1 = as much as gunshots, 0 = not at all. Was 6.4c.", R(0f, 2f), adv);
            BlastArrivalOn = Bind(blasts, "Arrives by its route", true, "A blast from a doorway or corner arrives after the extra distance, so the thump through the wall lands first. Was 6.4d.", null, adv);
            BlastCornerOn = Bind(blasts, "Around corners outdoors", true, "Outdoors, a blast behind a building comes from around its corner, muffled and quieter. Was 6.8.", null, adv);
            BlastCornerLossDb = Bind(blasts, "Corner loss dB", 4f, "How much quieter a blast is coming around a corner; twice this with no way round. Was 6.8a.", R(0f, 18f), adv);
            ThumpOpenDb = Bind(blasts, "Open-air thump dB", -6f, "A blast with no wall in the way gets the thump too, this much quieter. Was 6.2i.", R(-24f, 6f), adv);
            ThumpExplosionLengthS = Bind(blasts, "Thump length s", 1.5f, "How long an explosion's thump plays. It fades over the second half. Was 6.2j.", R(0.2f, 5f), adv);
            ExplosionEchoCount = Bind(blasts, "Echoes per explosion", 6, "The loudest this many play. Was 6.3b.", N(1, 12), adv);
            ExplosionEchoLengthS = Bind(blasts, "Echo length s", 1.5f, "How long each echo lasts. Shorter = tighter, longer = bigger. Was 6.3c.", R(0.3f, 3f), adv);
            ExplosionEchoRangeM = Bind(blasts, "Echo range m", 120f, "How far away a blast can be and still echo. Was 6.3d.", R(20f, 250f), adv);
            RoomEchoOn = Bind(blasts, "Room echoes from the doorway", true, "An explosion in another room also sends its room's reflections out through the opening. Was 6.3e.", null, adv);
            RoomEchoDb = Bind(blasts, "Room echo volume dB", 0f, "Raise or lower the room echoes from the opening. Was 6.3f.", R(-24f, 12f), adv);
            RoomEchoCount = Bind(blasts, "Room echoes per explosion", 4, "How many room echoes come from the opening. Was 6.3g.", N(1, 6), adv);
            BlastRecordingOn = Bind(blasts, "Recording by how open the blast is", true, "Blends the game's indoor and outdoor explosion recordings by how open the blast point really is, instead of all one or the other. Was 6.6.", null, adv);
            BlastOutdoorShare = Bind(blasts, "Openness for all outdoor", 0.4f, "How open the blast point has to be for only the outdoor recording to play. Was 6.6a.", R(0.15f, 0.9f), adv);
            BlastCloseReachM = Bind(blasts, "Close recording reach m", 25f, "How far away a blast still plays its sharp close recording (game: 10). Was 6.6b.", R(10f, 60f), adv);
            BlastOutsidePull = Bind(blasts, "Outside pulls to outdoor", 0.75f, "When you're outside, a blast in a garage or doorway sounds at least this outdoor. Was 6.6c.", R(0f, 1f), adv);
            ShockReachM = Bind(blasts, "Shock reach m", 60f, "How far along its way you still feel a blast. Was 6.9b.", R(10f, 100f), adv);
            ShockDuckDb = Bind(blasts, "Dip in other sounds dB", 8f, "How far other sounds dip for a moment when a close blast arrives. 0 = no dip. Was 6.9c.", R(0f, 24f), adv);
            ShockCornerDb = Bind(blasts, "Shock loss per corner dB", 6f, "How much weaker the hit is for each 90-degree turn on its way to you, and half this for each extra opening. Was 6.9d.", R(0f, 18f), adv);
            ShockEnclosedDb = Bind(blasts, "Closed room boost dB", 6f, "A blast in a closed space hits harder as its reflections pile up. Was 6.9e.", R(0f, 12f), adv);
            ShockRepeats = Bind(blasts, "Repeat hits", 3, "Indoors the shock arrives as a train of fading pulses, spaced by the size of the room. Was 6.9f.", N(0, 4), adv);
            ShockPushDb = Bind(blasts, "Slow push dB", 0f, "The long low swell after the hit in a closed space. -24 = off. Was 6.9g.", R(-24f, 12f), adv);
            EarsReachDb = Bind(blasts, "Ears reach dB", 6f, "How much further the hearing cut and ringing reach than full concussion. Was 6.9l.", R(0f, 18f), adv);
            BlastTailFadeS = Bind(blasts, "Tail fade s", 0.8f, "How long the blast's tail and echoes take to fade when concussion cuts your hearing. Was 6.9i.", R(0.1f, 3f), adv);
            FeltThroughCutDb = Bind(blasts, "Felt through the cut dB", 0f, "Volume of the thump and pressure hit you still feel when concussion cuts your hearing. Was 6.9j.", R(-24f, 12f), adv);
            FragmentPassDb = Bind(blasts, "Pass-by volume dB", 0f, "How loud a fragment passing 1 m away is. Was 6.7g.", R(-24f, 12f), adv);
            FragmentPassReachM = Bind(blasts, "Pass-by reach m", 5f, "How close a fragment has to pass to be heard. Was 6.7h.", R(1f, 15f), adv);
            FragmentPassCount = Bind(blasts, "Pass-bys per grenade", 4, "The most that play per grenade, so it doesn't turn to mush. Was 6.7i.", N(1, 12), adv);
            FragmentRouteOn = Bind(blasts, "Fragment hits from other rooms", true, "Fragments hitting things in another room come from the opening, muffled by the route. Was 6.7c.", null, adv);
            FragmentDullStrength = Bind(blasts, "Fragment muffle strength", 1f, "How strongly the route muffles a fragment hit. Was 6.7d.", R(0f, 2f), adv);
            FragmentLossDb = Bind(blasts, "Fragment loss per opening dB", 4f, "How much quieter a fragment hit gets for each opening on its way to you. Was 6.7e.", R(0f, 12f), adv);

            // 5. Rain and outdoors
            ExposureMixOn = Bind(rain, "Outside sound indoors", true, "Outdoor sound follows how open the space around you is and comes from the open directions, so nothing switches at the doorway. Was 8.");
            AmbienceV2On = Bind(rain, "Traced ambience", false, "Rain, wind and the outdoor background are set by the sound tracer: how much reaches you, from where, and how muffled. Needs Outside sound indoors and Doors and walls > Smooth doorways on. Was 9.");
            RoofRainLevel = Bind(rain, "Roof rain volume", 0.3f, "How loud rain on the roof is, before the roof, floors and material are applied. Was 9t.", R(0f, 1f));
            RoofRainByMaterial = Bind(rain, "Roof material matters", 1f, "Rain is louder on a thin metal roof and quieter on a soft one. 0 = every roof the same. Was 9r.", R(0f, 1.5f));
            RainFixOn = Bind(rain, "Fix rain cutting out", true, "Fixes a game bug where rain on the roof fades to silence when you move between some rooms. Was 7.", null, adv);
            RoofRainFromProbe = Bind(rain, "Roof rain traced from the roof", true, "Rain on the roof is traced from the roof itself: full right under it, partly under a mezzanine, down the stairwell from below. Off = a simple count of floors above. Was 9i.", null, adv);
            RoofCountsFloors = Bind(rain, "Roof rain counts floors above", true, "Each floor above takes off an amount by its material, so rain fades gradually the more floors there are. Was 9p.", null, adv);
            RoofFloorLossScale = Bind(rain, "Loss per floor", 1f, "How much each floor above takes off. 1 = the measured value for its material, 0 = none. Was 9q.", R(0f, 1.5f), adv);
            RoofRainShaftLoss = Bind(rain, "Roof rain fade down shafts dB per m", 1f, "How much roof rain fades on its way down a stair shaft, after the first 6 m. 0 = off. Was 9u.", R(0f, 3f), adv);
            RoofCountsLowDetail = Bind(rain, "Count low-detail floors", true, "Also count large low-detail floors, which much of the Capital is built from. Was 9v.", null, adv);
            RoofRainNoRainRooms = Bind(rain, "Respect no-rain rooms", true, "Roof rain fades out in rooms the game gives no rain. Was 9w.", null, adv);
            RainFromOpenings = Bind(rain, "Rain from the window it comes in by", true, "Indoors, rain and wind come from the window or door they get in by. Off = from the direction they reach you. Was 9j.", null, adv);
            RainThroughBuildingDb = Bind(rain, "Rain through the building dB", -32f, "Deep inside, outdoor sound never drops below this, as a low rumble through the structure. -60 = off. Was 9l.", R(-60f, -20f), adv);
            DirectionStrength = Bind(rain, "Direction strength", 1f, "How clearly outdoor sound and roof rain come from the openings indoors. 0 = always all round. Was 9h.", R(0f, 3f), adv);
            BedDirectional = Bind(rain, "Outdoor sounds directional", 1f, "How far the game's own outdoor sounds become positioned sound from the open directions as you go inside. 0 = flat stereo. Was 9b.", R(0f, 1f), adv);
            BedMode = Bind(rain, "Outdoor sounds panning", BedDirection.Mode.UnityPanner, "UnityPanner = left/right with width, duller from behind. MetaHrtf = the game's 3D audio with front/back and up/down. Was 9c.", null, adv);
            RoofBedMode = Bind(rain, "Roof rain panning", BedDirection.Mode.UnityPanner, "The same choice for roof rain, where up and down matter most. Was 9m.", null, adv);
            GlassLossDb = Bind(rain, "Glass loss dB", 22f, "How much quieter outdoor sound is through closed glass. Broken windows let it through. Was 9d.", R(0f, 40f), adv);
            RoomBuildUp = Bind(rain, "Build-up in rooms", 1f, "How much sound coming in builds up by bouncing inside: a bare concrete garage gets loud, a furnished room doesn't. 1 = as measured. Was 9e.", R(0f, 2f), adv);
            DeepBounceDulling = Bind(rain, "Deep bounce dulling", 0.8f, "Each bounce past the third gets duller by this much, so rooms deep inside rumble rather than hiss. 1 = off. Was 9n.", R(0.3f, 1f), adv);
            GuidedRays = Bind(rain, "Guided rays", true, "Aim a third of the traced paths at openings already found, for steadier sound deep inside. Was 9f.", null, adv);
            ProbeMeasuredSurfaces = Bind(rain, "Tracer uses measured materials", true, "The sound tracer uses measured acoustic values for each material. Off = the older hand-set values. Was 9o.", null, adv);
            ProbeChangeDetect = Bind(rain, "React to real changes", true, "The tracer forgets what it measured when the space clearly changes, instead of by how fast you move. Was 9s.", null, adv);
            DirectionCueStrength = Bind(rain, "Direction cue", 0.6f, "How strongly outdoor sound is panned toward the open side, and duller when it's behind you. 0 = off. Was 8a.", R(0f, 1f), adv);
            RoofRainUnderFloors = Bind(rain, "Roof rain under one floor", 0.3f, "Roof rain volume with one floor above you, when it isn't traced from the roof. Was 8b.", R(0f, 1f), adv);
            ThroughGlass = Bind(rain, "Through glass", 0.45f, "How much outdoor sound an intact window lets through compared with an opening. Was 8c.", R(0f, 1f), adv);
            IndoorFadeNearOpenings = Bind(rain, "Indoor sound fade near openings", 0.6f, "How early room tone, indoor wind and roof rain fade as the space opens up. 0 = only when you're almost outside. Was 8d.", R(0f, 1f), adv);
            OpeningShare = Bind(rain, "Sound from openings", 0.7f, "Indoors, this share of the outdoor sound plays from the open directions instead of from everywhere. Was 8e.", R(0f, 1f), adv);
            OpeningHrtf = Bind(rain, "3D processing on opening sounds", true, "Run the sound from openings through the game's 3D audio for front/back and up/down. Off = plain left/right. Was 8f.", null, adv);
            IndoorWindFollowsOpenings = Bind(rain, "Indoor wind follows openings", true, "A room's draught sound follows how much of the outside reaches you, so it fades deep in a stairwell. Was 8g.", null, adv);

            // 6. Doors and walls
            ThumpOn = Bind(walls, "Through-wall thump", true, "A shot in another room or a blast behind a wall also comes through the wall as a low thump from its true direction. Was 6.2.");
            ThumpDb = Bind(walls, "Thump volume dB", 0f, "How loud the thump is before distance and walls. Was 6.2a.", R(-30f, 12f));
            ThumpWallsStop = Bind(walls, "Walls can stop sound", true, "With enough wall or earth in the way, no thump gets through at all. Was 6.2e.");
            DoorMufflingOn = Bind(walls, "Closed doors muffle by material", true, "Thin wood lets the most through, thick metal the least, instead of every closed door counting as fully blocked. Was 6.5.");
            DoorMufflingStrength = Bind(walls, "Closed door strength", 1f, "Lower = closed doors let more through. Never muffles more than the game does. Was 6.5a.", R(0f, 2f));
            GlassRouteOn = Bind(walls, "Breaking glass from other rooms", true, "A window breaking in another room comes from the opening it reaches you through, muffled by the route. Was 6.7.");
            ThumpCutoffHz = Bind(walls, "Gunshot thump cutoff Hz", 50f, "How deep a gunshot's thump through a wall is. Lower = deeper. Depends on your headset. Was 6.2b.", R(20f, 400f), adv);
            ThumpExplosionCutoffHz = Bind(walls, "Explosion thump cutoff Hz", 80f, "The same for explosions. Was 6.2c.", R(20f, 400f), adv);
            ThumpWallLoss = Bind(walls, "Wall loss strength", 0.5f, "How much quieter walls make a thump that gets through. 1 = the full table, 0 = no quieter. Was 6.2d.", R(0f, 1.5f), adv);
            ThumpGunReach = Bind(walls, "Gunshot reach dB", 60f, "How much distance and wall a gunshot's thump gets through. One concrete wall is about 24 to 36 dB, earth 75; suppressed guns get 20 less. Was 6.2f.", R(0f, 120f), adv);
            ThumpExplosionReach = Bind(walls, "Explosion reach dB", 80f, "The same for explosions. Was 6.2g.", R(0f, 120f), adv);
            GlassDullStrength = Bind(walls, "Glass muffle strength", 1f, "How strongly the route muffles breaking glass. Was 6.7a.", R(0f, 2f), adv);
            GlassLossDb2 = Bind(walls, "Glass loss per opening dB", 4f, "How much quieter breaking glass gets for each opening on its way to you. Was 6.7b.", R(0f, 12f), adv);
            DoorwayOn = Bind(walls, "Smooth doorways", true, "The indoor/outdoor mix and ambience blend across a doorway instead of switching. Was 3.", null, adv);
            DoorwayInside = Bind(walls, "Blend inside m", 1.5f, "How far inside the doorway the blend happens. Was 3a.", R(0.25f, 6f), adv);
            DoorwayOutside = Bind(walls, "Blend outside m", 1.0f, "How far outside the doorway the blend happens. Was 3b.", R(0.25f, 6f), adv);
            ObstructionModeCfg = Bind(walls, "Obstruction rule", ObstructionMode.ParallelPaths, "How the game's through-wall and around-corner checks combine. ParallelPaths = blocked only if every path is. Game = the game's average. Was 2.", null, adv);
            ExtraRoutesOn = Bind(walls, "Room routes from EFT 1.0", true, "The routes sound takes between rooms come from Sound Engine Backport's EFT 1.0 data, by red_the_fox, checked per map; a map that differs keeps the game's own. Applies at the next raid. Was 10b.", null, adv);

            // 7. Performance
            OwnEchoShare = Bind(perf, "Echo voices kept for your gun", 48, "Other guns' echoes leave this many echo voices free for yours (at most half of what's free right now). Was 6.3n.", N(16, 96));
            BotEchoCeiling = Bind(perf, "Most voices for other gun echoes", 24, "The most voices other guns' echoes can hold at once. Was 6.3k.", N(0, 64));
            EchoReplaceOn = Bind(perf, "Replace the quietest echo", true, "At the voice limit, a new echo takes over from the other-gun echo you can hear least, with a short fade. Off = the new echo doesn't start. Was 6.3p.", null, adv);

            // 8. Diagnostics
            LogsOn = Bind(diag, "Write logs", true, "Write CSV logs and a summary per raid to BepInEx/RealSoundSimulationLogs. Turning it on starts at the next raid.");
            MarkKey = Bind(diag, "Mark key", new KeyboardShortcut(KeyCode.F10), "Press in raid where something sounds wrong. It marks the spot in marks.csv with what the mod measured there.");
            OverlayOn = Bind(diag, "Overlay", false, "Show the live sound overlay (top left) in raid.", null, adv);
            OverlayKey = Bind(diag, "Overlay key", new KeyboardShortcut(KeyCode.F9), "Show or hide the overlay.", null, adv);
            OverlayRows = Bind(diag, "Overlay rows", 14, "How many of the nearest sounds the overlay lists.", N(4, 40), adv);
            RayViewKey = Bind(diag, "Ray view key", new KeyboardShortcut(KeyCode.F11), "In raid: show or hide the sound tracer's rays. Green reached the sky, cyan after bouncing, yellow through glass, red trapped.", null, adv);
            AcousticProbeOn = Bind(diag, "Sound tracer", true, "The tracer behind rain, ambience and room readings: 128 rays bouncing off surfaces until they reach open sky. Off turns those features off too.", null, adv);
            ProbeOn = Bind(diag, "Enclosure probe", true, "Measures how enclosed you are with 32 short rays, ten times a second. Your shot's indoor/outdoor blend uses it.", null, adv);
            ExposureProbeOn = Bind(diag, "Outdoor exposure probe", true, "Measures how much of the outside reaches you and through what. Outside sound indoors needs it.", null, adv);
            TimingOn = Bind(diag, "Time the audio systems", true, "Measure the time the game's audio systems take each frame.", null, adv);
            AudioThreadTimingOn = Bind(diag, "Time the audio thread", true, "Measure whether the audio thread keeps up (late blocks crackle). Attached at raid start.", null, adv);
            RouteLogOn = Bind(diag, "Log portal routes", false, "Log which opening each shot's route goes through to routes.csv. Moves no sound.", null, adv);
            ContainerLogInterval = Bind(diag, "Container log interval s", 0.5f, "How often every sound's blocked state is logged.", R(0.1f, 5f), adv);
            SourceScanInterval = Bind(diag, "Source scan interval s", 0f, "How often every playing sound in the scene is counted. Causes a visible hitch each time. 0 = off.", R(0f, 60f), adv);
            SpawnBackoffS = Bind(diag, "Back off after a spawn s", 2f, "For this long after bots spawn, diagnostics skip their per-frame work so they don't add to the spawn hitch. 0 = off.", R(0f, 10f), adv);

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
            Safe("6.3h bot echoes", () => BotEchoes.Apply(Harmony)); // 0.6.61
            Safe("6.4 blast from the opening", () => BlastFromOpening.Apply(Harmony)); // 0.6.41
            Safe("6.5 closed doors", () => ClosedDoors.Apply(Harmony)); // 0.6.47
            Safe("6.6 blast recording", () => BlastRecording.Apply(Harmony)); // 0.6.49
            Safe("6.9h soft concussion cut", () => ConcussionProof.Apply(Harmony)); // 0.6.55; the patch itself waits for the first raid
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
            gameObject.AddComponent<BotEchoes>(); // 0.6.61; runs after PortalPositioning
            gameObject.AddComponent<BlastFromOpening>(); // 0.6.41
            gameObject.AddComponent<ClosedDoors>(); // 0.6.47
            gameObject.AddComponent<ShockWave>(); // 0.6.53
            gameObject.AddComponent<FragmentPassBy>(); // 0.6.56
            gameObject.AddComponent<ConcussionProof>(); // 0.6.57
            gameObject.AddComponent<OwnShotTurn>(); // 0.6.30
            Log.LogInfo("Real Sound Simulation loaded");
        }

        private int _order = 1000;

        // Binds one setting with its place in F12. Advanced ones only show with "Advanced settings" ticked.
        private ConfigEntry<T> Bind<T>(string section, string key, T value, string description, AcceptableValueBase range = null, bool advanced = false)
            => Config.Bind(section, key, value, new ConfigDescription(description, range,
                new ConfigurationManagerAttributes { Order = _order--, IsAdvanced = advanced }));

        private static AcceptableValueRange<float> R(float min, float max) => new AcceptableValueRange<float>(min, max);
        private static AcceptableValueRange<int> N(int min, int max) => new AcceptableValueRange<int>(min, max);

        internal static string FixState() =>
            $"0 gun sounds {GunSounds.Value}{(GunSounds.Value == GunSoundSet.SEB ? (SebOnFullAuto.Value ? " (all guns)" : " (semi-only guns)") : "")} | " +
            $"fix1 gun mixer {(GunMixerOn.Value ? $"ON ({GunMuffleAbove.Value:0.00}/{GunUnmuffleBelow.Value:0.00}, hold {GunHoldSeconds.Value:0.0} s)" : "off")}, 1d first burst {(FirstBurstOn.Value ? "ON" : "off")}, 1e shot overlap {(ShotOverlapOn.Value ? "ON" : "off")}, 6.1 bot gunshots from the opening {(PortalPositioningOn.Value ? $"ON ({PortalStrength.Value:0.00})" : "off")}, 6.1b route dulling {(RouteDullOn.Value ? $"ON (x{RouteDullStrength.Value:0.00}, floor {RouteDullMinHz.Value:0} Hz)" : "off")} | " +
            $"6.2 through-wall thump {(ThumpOn.Value ? $"ON ({ThumpCutoffHz.Value:0} Hz, explosions {ThumpExplosionCutoffHz.Value:0} Hz, level {ThumpDb.Value:+0;-0;0} dB, wall loss x{ThumpWallLoss.Value:0.00}, {(ThumpWallsStop.Value ? $"walls stop ON, reach {ThumpGunReach.Value:0}/{ThumpExplosionReach.Value:0}" : "walls stop off")})" : "off")} | " +
            $"6.3h bot echoes {(BotEchoOn.Value ? $"ON ({BotEchoDb.Value:+0;-0;0} dB, {BotEchoCount.Value} per bot, ceiling {BotEchoCeiling.Value}, {BotEchoRangeM.Value:0} m, gate {BotEchoGateDb.Value:0} dB, {BotEchoLengthS.Value:0.0} s)" : "off")}, your gun keeps {OwnEchoShare.Value}, replace quietest {(EchoReplaceOn.Value ? "ON" : "off")} | " +
            $"6.3e room echoes {(RoomEchoOn.Value ? $"ON ({RoomEchoDb.Value:+0;-0;0} dB, {RoomEchoCount.Value})" : "off")} | " +
            $"6.6 blast recording {(BlastRecordingOn.Value ? $"ON (outdoor from {BlastOutdoorShare.Value:0.00}, close to {BlastCloseReachM.Value:0} m, outside pull {BlastOutsidePull.Value:0.00})" : "off")} | " +
            $"6.9 shock wave {(ShockOn.Value ? $"ON ({ShockLevelDb.Value:+0;-0;0} dB, reach {ShockReachM.Value:0} m, corner {ShockCornerDb.Value:0} dB, enclosed +{ShockEnclosedDb.Value:0} dB, {ShockRepeats.Value} repeats, push {ShockPushDb.Value:+0;-0;0} dB, dip {ShockDuckDb.Value:0} dB)" : "off")}, 6.9h soft concussion cut {(SoftCutOn.Value ? $"ON (tail {BlastTailFadeS.Value:0.0} s, felt {FeltThroughCutDb.Value:+0;-0;0} dB)" : "off")}, 6.9k concussion by pressure {(ConcussionPressureOn.Value ? $"ON ({ConcussionPressureDb.Value:+0;-0;0} dB, ears +{EarsReachDb.Value:0} dB)" : "off")} | " +
            $"6.7c fragment hits by route {(FragmentRouteOn.Value ? $"ON (dull x{FragmentDullStrength.Value:0.00}, {FragmentLossDb.Value:0} dB per opening)" : "off")}, 6.7f pass-bys {(FragmentPassOn.Value ? $"ON ({FragmentPassDb.Value:+0;-0;0} dB, {FragmentPassReachM.Value:0.0} m, {FragmentPassCount.Value} per grenade)" : "off")} | " +
            $"6.8 blasts around a corner {(BlastCornerOn.Value ? $"ON ({BlastCornerLossDb.Value:0} dB)" : "off")}, bot gunshots {(GunCornerOn.Value ? "ON" : "off")}, 6.4d arrival by route {(BlastArrivalOn.Value ? "ON" : "off")} | " +
            $"6.7 glass by route {(GlassRouteOn.Value ? $"ON (dull x{GlassDullStrength.Value:0.00}, {GlassLossDb2.Value:0} dB per opening)" : "off")} | " +
            $"6.5 closed doors {(DoorMufflingOn.Value ? $"ON ({DoorMufflingStrength.Value:0.00})" : "off")} | " +
            $"6.4 blast from the opening {(BlastOpeningOn.Value ? $"ON ({BlastOpeningStrength.Value:0.00})" : "off")}, dulled {(BlastDullOn.Value ? $"ON (x{BlastDullStrength.Value:0.00})" : "off")}, explosion thump level {ThumpExplosionDb.Value:+0;-0;0} dB, open-air {ThumpOpenDb.Value:+0;-0;0} dB, length {ThumpExplosionLengthS.Value:0.0} s | " +
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

    // Minimal copy of the attribute the BepInEx Configuration Manager (F12) reads for ordering and the Advanced filter.
    internal sealed class ConfigurationManagerAttributes
    {
        public int? Order;
        public bool? IsAdvanced;
    }
}
