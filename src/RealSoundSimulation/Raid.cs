using System;
using Audio.SpatialSystem;
using Comfort.Common;
using EFT;
using EFT.Utilities;
using HarmonyLib;
using RealSoundSimulation.Acoustics;
using RealSoundSimulation.Diagnostics;
using RealSoundSimulation.Fixes;
using UnityEngine;

namespace RealSoundSimulation
{
    // Raid start and end, in one place. One test for "are we in a raid" that every feature uses; every feature's reset at raid
    // start, each in its own try so one failure can't skip the rest; and the per-frame housekeeping that sound features need
    // (not diagnostics). Runs before everything else in the frame. DiagRunner only listens to Started and Ended to open and
    // close the logs. A new feature with state that must not carry over to the next raid adds its reset to Resets.
    //
    // Raid start loading (0.9.3): one-time work that doesn't need you spawned runs on the loading screen instead (Prepare),
    // when the game says the location has loaded, about 20 s before you spawn: the mod's own voices are built, the map's
    // windows and doors read and the ambience blender searched for. Resets with an IfPrepared action do only that at spawn, but only if Prepare ran for this same game world; if it
    // didn't (it was missed, or Fika loads the raid another way) everything runs at spawn as before. StartupTiming logs it.
    [DefaultExecutionOrder(-1000)]
    internal sealed class Raid : MonoBehaviour
    {
        // The raid's sound system is up, you exist, and the world isn't the hideout's (the main menu is a HideoutGameWorld too).
        internal static bool IsOn =>
            SpatialAudioSystem.Initialized && MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated && GamePlayerOwner.MyPlayer != null
            && !(Singleton<GameWorld>.Instantiated && Singleton<GameWorld>.Instance is HideoutGameWorld);

        internal static float StartTime;
        internal static event Action Started, Ended;
        internal static int ResetsFailed;

        private bool _on;
        private static GameWorld _preparedWorld;

        private static readonly Action Nothing = () => { };

        // Each reset; a few have a second action that replaces it at spawn when Prepare already did the work for this raid on
        // the loading screen (see OnRaidStart).
        private static readonly (string Name, Action Run, Action IfPrepared)[] Resets =
        {
            ("6 surfaces and windows", Surfaces.Reset, Nothing), ("6.5 closed doors", ClosedDoors.Reset, ClosedDoors.RestartTries),
            ("ambience blender lookup", BlendSystem.Reset, null), ("Icebreaker ambience", NoBlenderAmbience.Reset, null), ("Icebreaker beds", IcebreakerBeds.Reset, null),("8h room tone blend", RoomToneBlend.Reset, null),
            ("bot shots", BotShots.Reset, null), ("6.2 through-wall thump", ThroughWall.Reset, null), ("6.3 explosion echoes", ExplosionEchoes.Reset, null),
            ("6.3h bot echoes", BotEchoes.Reset, null), ("6.4 blast from the opening", BlastFromOpening.Reset, null), ("6.6 blast recording", BlastRecording.Reset, null),
            ("mixer owner", MixerOwner.Reset, null), ("6.9u game ring", GameRing.Reset, null), ("6.9 shock wave", ShockWave.Reset, null),
            ("6.9h concussion", ConcussionProof.Reset, null), ("5r gunfire hearing", GunfireHearing.Reset, null), ("A adrenaline", Adrenaline.Reset, null),
            ("6.7f pass-bys", FragmentPassBy.Reset, null), ("own shot turn", OwnShotTurn.Reset, null),
            ("route log", RouteLog.Reset, null), ("6.1 portal positioning", PortalPositioning.Reset, null), ("1d first burst", FirstBurstOcclusion.Reset, null),
            ("1e shot overlap", ShotOverlap.Reset, null), ("shot variation", ShotVariation.Reset, null), ("room tail", RoomTail.Reset, null),
            ("0 gun sounds", GunSoundSource.Reset, null), ("3m outdoor tail", OutdoorTail.Reset, null), ("1 gun mixer", GunMixerFix.Reset, null),
            ("2 obstruction", ObstructionScoreFix.Reset, null), ("4 off-map", OffMapFix.Reset, null), ("9k distant gunfire", DistantThroughOpenings.Reset, null),
            ("3c own gunshot blend", GunEnvironmentBlend.Reset, null), ("6 own echoes", Acoustics.OwnEchoes.Reset, null),
            ("outdoor exposure", Acoustics.OutdoorExposure.Reset, null), ("probe v2", Acoustics.AcousticProbe.Reset, null), ("7 rain", RainDropoutFix.Reset, null),
            ("8 ambience mix", AmbienceMix.Reset, null), ("3 doorway report", DoorwayBlend.ClearReport, null), ("8g room tone report", RoomTone.ClearReport, null),
        };

        private void Update()
        {
            bool on = IsOn;
            if (on && !_on) Plugin.Safe("raid start", OnRaidStart);
            if (!on && _on) Plugin.Safe("raid end", OnRaidEnd);
            _on = on;
            if (!on) return;
            try { RoomTail.Tick(); } // lets the tail voices go after quiet seconds
            catch (Exception e) { DiagRunner.ReportOnce("room tail (tick)", e); }
            try { ShotOverlap.Tick(); } // gives rung-out tail voices back (1f)
            catch (Exception e) { DiagRunner.ReportOnce("1f tail hand-off (tick)", e); }
        }

        private void OnApplicationQuit()
        {
            if (_on) Plugin.Safe("raid end (quit)", OnRaidEnd);
            _on = false;
        }

        internal static void Apply(Harmony h) =>
            h.Patch(AccessTools.Method(typeof(ClientMetricsEvents), nameof(ClientMetricsEvents.SetLocationLoaded)),
                postfix: new HarmonyMethod(typeof(Raid), nameof(LocationLoadedPostfix)));

        // The game calls this once per raid (and on reconnect), right after it has created the raid's game world and loaded its
        // scenes; never for the hideout or the menu. The loading screen is still up.
        private static void LocationLoadedPostfix() => Plugin.Safe("raid start loading", Prepare);

        private static void Prepare()
        {
            _preparedWorld = null;
            StartupTiming.Clear();
            BlendSystem.ForgetLoadingSearch(); RoomTone.ForgetToneLoops(); RoomTone.ForgetTonePlayers();
            if (Fika.IsHeadless() || !Singleton<GameWorld>.Instantiated || Singleton<GameWorld>.Instance is HideoutGameWorld) return;
            // The mod's own voices. They are kept between raids, so after the first raid these cost nothing.
            Step("echo voices", EchoVoices.Prebuild);
            Step("room tail voices", RoomTail.Prebuild);
            Step("ring voices", () => { GameRing.Prebuild(); GunfireHearing.Prebuild(); });
            Step("adrenaline sounds", Adrenaline.Prebuild);
            // The map's windows (glass) for echoes and the outdoor probes: one scene search plus each window's colliders.
            // The map's doors (6.5 closed doors) and the ambience blender (fix 3): one whole-scene search each.
            bool windows = false, doors = false;
            Step("windows", () => { Surfaces.Reset(); Surfaces.RegisterWindowsNow(); windows = true; });
            Step("doors", () => { ClosedDoors.PrepareDoors(); doors = true; });
            Step("ambience blender scene search", BlendSystem.SearchOnLoading);
            Step("indoor tone loops", RoomTone.FindToneLoops);
            if (windows && doors) _preparedWorld = Singleton<GameWorld>.Instance;
        }

        private static void Step(string name, Action a)
        {
            try { StartupTiming.Run("loading", name, a); }
            catch (Exception e) { DiagRunner.ReportOnce($"raid start loading: {name}", e); }
        }

        private static void OnRaidStart()
        {
            if (Fika.IsHeadless()) { Plugin.TurnOff("this game is a Fika headless host, so no one hears it"); return; }
            bool prepared = _preparedWorld != null && Singleton<GameWorld>.Instantiated && ReferenceEquals(Singleton<GameWorld>.Instance, _preparedWorld);
            _preparedWorld = null;
            if (!prepared) { StartupTiming.Clear(); BlendSystem.ForgetLoadingSearch(); RoomTone.ForgetToneLoops(); }
            StartupTiming.Spawned();
            StartTime = Time.time;
            ResetsFailed = 0;
            foreach (var r in Resets)
            {
                long t0 = StartupTiming.Now();
                try { (prepared && r.IfPrepared != null ? r.IfPrepared : r.Run)(); } // the loading screen did the rest
                catch (Exception e) { ResetsFailed++; DiagRunner.ReportOnce($"reset at raid start: {r.Name}", e); }
                StartupTiming.Add("spawn", r.Name, StartupTiming.Now() - t0);
            }
            StartupTiming.Run("spawn", "diagnostics start", () => Started?.Invoke());
            Plugin.Log.LogInfo(StartupTiming.Line() + (prepared ? "" : " (the loading-screen step didn't run for this raid)"));
        }

        private static void OnRaidEnd()
        {
            Plugin.Log.LogInfo(StartupTiming.Line());
            Ended?.Invoke();
        }
    }
}
