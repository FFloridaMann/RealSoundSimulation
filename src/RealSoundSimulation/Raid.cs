using System;
using Audio.SpatialSystem;
using Comfort.Common;
using EFT;
using RealSoundSimulation.Diagnostics;
using RealSoundSimulation.Fixes;
using UnityEngine;

namespace RealSoundSimulation
{
    // Raid start and end, in one place. One test for "are we in a raid" that every feature uses; every feature's reset at raid
    // start, each in its own try so one failure can't skip the rest; and the per-frame housekeeping that sound features need
    // (not diagnostics). Runs before everything else in the frame. DiagRunner only listens to Started and Ended to open and
    // close the logs. A new feature with state that must not carry over to the next raid adds its reset to Resets.
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

        private static readonly (string Name, Action Run)[] Resets =
        {
            ("ambience blender lookup", BlendSystem.Reset), ("bot shots", BotShots.Reset),("6.2 through-wall thump", ThroughWall.Reset), ("6.3 explosion echoes", ExplosionEchoes.Reset),
            ("6.3h bot echoes", BotEchoes.Reset), ("6.4 blast from the opening", BlastFromOpening.Reset), ("6.6 blast recording", BlastRecording.Reset),
            ("mixer owner", MixerOwner.Reset), ("6.9u game ring", GameRing.Reset), ("6.9 shock wave", ShockWave.Reset),
            ("6.9h concussion", ConcussionProof.Reset), ("5r gunfire hearing", GunfireHearing.Reset), ("A adrenaline", Adrenaline.Reset),
            ("6.7f pass-bys", FragmentPassBy.Reset), ("6.5 closed doors", ClosedDoors.Reset), ("own shot turn", OwnShotTurn.Reset),
            ("route log", RouteLog.Reset), ("6.1 portal positioning", PortalPositioning.Reset), ("1d first burst", FirstBurstOcclusion.Reset),
            ("1e shot overlap", ShotOverlap.Reset), ("shot variation", ShotVariation.Reset), ("room tail", RoomTail.Reset),
            ("0 gun sounds", GunSoundSource.Reset), ("3m outdoor tail", OutdoorTail.Reset), ("1 gun mixer", GunMixerFix.Reset),
            ("2 obstruction", ObstructionScoreFix.Reset), ("4 off-map", OffMapFix.Reset), ("9k distant gunfire", DistantThroughOpenings.Reset),
            ("3c own gunshot blend", GunEnvironmentBlend.Reset), ("6 own echoes", Acoustics.OwnEchoes.Reset),
            ("outdoor exposure", Acoustics.OutdoorExposure.Reset), ("probe v2", Acoustics.AcousticProbe.Reset), ("7 rain", RainDropoutFix.Reset),
            ("8 ambience mix", AmbienceMix.Reset), ("3 doorway report", DoorwayBlend.ClearReport), ("8g room tone report", RoomTone.ClearReport),
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

        private static void OnRaidStart()
        {
            if (Fika.IsHeadless()) { Plugin.TurnOff("this game is a Fika headless host, so no one hears it"); return; }
            StartTime = Time.time;
            ResetsFailed = 0;
            foreach (var r in Resets)
            {
                try { r.Run(); }
                catch (Exception e) { ResetsFailed++; DiagRunner.ReportOnce($"reset at raid start: {r.Name}", e); }
            }
            Started?.Invoke();
        }

        private static void OnRaidEnd() => Ended?.Invoke();
    }
}
