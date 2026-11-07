using System.Collections.Generic;
using System.Reflection;
using Audio.AmbientSubsystem;
using Audio.Extensions;
using Audio.SpatialSystem;
using HarmonyLib;
using UnityEngine;

namespace RealSoundSimulation.Diagnostics
{
    // Measures the indoor/outdoor "pop". Two separate game systems decide whether you are inside:
    //  - EnvironmentManager's IndoorTrigger volumes -> BetterAudio.TransitToEnvironment -> mixer snapshot switch
    //    (0.1 s going in, 0.5 s going out; this swaps reverb/EQ on the whole mix)
    //  - the baked audio rooms -> EnvironmentSoundBlendSystem.OnPlayerRoomChanged -> ambience blenders
    //    (outdoor ambience fades by distance to the doorway while inside, but snaps to full in 0.3 s on stepping out)
    // If the two switch at different places or times, you get two jumps per doorway. transitions.csv logs every
    // switch with the listener position; ambience.csv samples the outdoor-ambience level 10x per second.
    internal static class EnvProbe
    {
        private struct Ev { public float T; public bool Indoor; public Vector3 Pos; }

        private static readonly List<Ev> SnapshotEvents = new List<Ev>();
        private static readonly List<Ev> RoomEvents = new List<Ev>();
        private static bool _haveRoomState, _lastRoomIndoor;
        private static bool _lastSnapshotIndoor;
        private static float _raidStart, _nextSample, _nextProbe;
        private static EnvironmentSoundBlendSystem _blend;
        private static readonly AccessTools.FieldRef<AmbientSoundBlender, float> OutdoorValueRef =
            AccessTools.FieldRefAccess<AmbientSoundBlender, float>("_currentOutdoorFilterValue");

        public static void Apply(Harmony h)
        {
            MethodInfo transit = AccessTools.Method(typeof(BetterAudio), nameof(BetterAudio.TransitToEnvironment), new[] { typeof(EnvironmentType) });
            MethodInfo roomChanged = AccessTools.Method(typeof(EnvironmentSoundBlendSystem), nameof(EnvironmentSoundBlendSystem.OnPlayerRoomChanged),
                new[] { typeof(ISpatialAudioRoom), typeof(EAudioRoomTypeMask), typeof(int) });
            if (transit != null) h.Patch(transit, postfix: new HarmonyMethod(typeof(EnvProbe), nameof(TransitPostfix)));
            if (roomChanged != null) h.Patch(roomChanged, postfix: new HarmonyMethod(typeof(EnvProbe), nameof(RoomPostfix)));
        }

        public static void Reset(float raidStart)
        {
            SnapshotEvents.Clear(); RoomEvents.Clear();
            _haveRoomState = false; _lastSnapshotIndoor = false;
            _raidStart = raidStart; _nextSample = 0f; _nextProbe = 0f;
            _blend = null;
        }

        private static Vector3 ListenerPos()
        {
            if (!MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated) return Vector3.zero;
            Transform t = MonoBehaviourSingleton<SpatialAudioSystem>.Instance.ListenerTransform;
            return t != null ? t.position : Vector3.zero;
        }

        private static void TransitPostfix(EnvironmentType environment)
        {
            if (!DiagLog.IsOpen) return;
            try
            {
                bool indoor = environment != EnvironmentType.Outdoor;
                _lastSnapshotIndoor = indoor;
                var e = new Ev { T = Time.time - _raidStart, Indoor = indoor, Pos = ListenerPos() };
                SnapshotEvents.Add(e);
                Write("snapshot", e, indoor ? "0.1" : "0.5", "");
            }
            catch (System.Exception ex) { DiagRunner.ReportOnce("env probe (snapshot)", ex); }
        }

        private static void RoomPostfix(ISpatialAudioRoom room, EAudioRoomTypeMask roomType)
        {
            if (!DiagLog.IsOpen) return;
            try
            {
                bool indoor = !roomType.IsOutdoor();
                if (_haveRoomState && indoor == _lastRoomIndoor) return; // indoor->indoor room changes are not doorway crossings
                _haveRoomState = true; _lastRoomIndoor = indoor;
                var e = new Ev { T = Time.time - _raidStart, Indoor = indoor, Pos = ListenerPos() };
                RoomEvents.Add(e);
                Write("room", e, indoor ? "blend by doorway distance" : "0.3", ShotProbe.RoomName(room));
            }
            catch (System.Exception ex) { DiagRunner.ReportOnce("env probe (room)", ex); }
        }

        private static void Write(string system, Ev e, string fade, string room)
        {
            DiagLog.Row("transitions.csv", string.Join(",", DiagLog.F(e.T, "0.00"), system, e.Indoor ? "indoor" : "outdoor",
                DiagLog.F(e.Pos.x, "0.00"), DiagLog.F(e.Pos.y, "0.00"), DiagLog.F(e.Pos.z, "0.00"), fade, DiagLog.Csv(room)));
        }

        // Called from DiagRunner.Tick. 10 samples per second of the outdoor-ambience level and both indoor flags.
        public static void Tick(float now)
        {
            if (!DiagLog.IsOpen || now < _nextSample) return;
            _nextSample = now + 0.1f;
            if (_blend == null) _blend = Object.FindObjectOfType<EnvironmentSoundBlendSystem>();
            float outdoor = float.NaN;
            if (_blend != null && _blend._ambientSoundBlenders != null && _blend._ambientSoundBlenders.Length > 0 && _blend._ambientSoundBlenders[0] != null)
                outdoor = OutdoorValueRef(_blend._ambientSoundBlenders[0]);
            Vector3 p = ListenerPos();
            DiagLog.Row("ambience.csv", string.Join(",", DiagLog.F(now - _raidStart, "0.00"),
                DiagLog.F(p.x, "0.00"), DiagLog.F(p.y, "0.00"), DiagLog.F(p.z, "0.00"),
                _lastSnapshotIndoor ? "1" : "0", _haveRoomState ? (_lastRoomIndoor ? "1" : "0") : "", DiagLog.F(outdoor),
                DiagLog.F(Fixes.DoorwayBlend.TargetOutdoorness), DiagLog.F(Fixes.DoorwayBlend.Outdoorness), Fixes.DoorwayBlend.PortalsConsidered.ToString(),
                Fixes.DoorwayBlend.DebugPortalId.ToString(), DiagLog.F(Fixes.DoorwayBlend.DebugS, "0.00"), DiagLog.F(Fixes.DoorwayBlend.DebugLat, "0.00"),
                DiagLog.F(Fixes.DoorwayBlend.GlassLeak), DiagLog.F(Fixes.AmbienceMix.ExposureApplied), DiagLog.F(Fixes.AmbienceMix.IndoorFade()),
                DiagLog.F(Fixes.AmbienceMix.BedPan, "0.00"), DiagLog.F(Fixes.AmbienceMix.BedCutoff, "0"), DiagLog.F(Fixes.AmbienceMix.RoofRainFactor),
                DiagLog.F(Fixes.OpeningSounds.Share), Fixes.OpeningSounds.Voices.ToString(),
                DiagLog.F(Acoustics.OutdoorExposure.RelAz(Fixes.OpeningSounds.FirstDir()), "0"), DiagLog.F(Fixes.OpeningSounds.FirstGain())));
            if (now >= _nextProbe && Acoustics.EnclosureProbe.Valid)
            {
                _nextProbe = now + 0.5f;
                Vector3 q = Acoustics.EnclosureProbe.LastOrigin;
                DiagLog.Row("probe.csv", string.Join(",", DiagLog.F(now - _raidStart, "0.00"), DiagLog.F(q.x, "0.00"), DiagLog.F(q.y, "0.00"), DiagLog.F(q.z, "0.00"),
                    _haveRoomState ? (_lastRoomIndoor ? "1" : "0") : "", DiagLog.F(Fixes.DoorwayBlend.TargetOutdoorness),
                    DiagLog.F(Acoustics.EnclosureProbe.Enclosure), DiagLog.F(Acoustics.EnclosureProbe.Horizontal),
                    DiagLog.F(Acoustics.EnclosureProbe.Overhead), DiagLog.F(Acoustics.EnclosureProbe.MeanHit, "0.0"),
                    DiagLog.F(Acoustics.EnclosureProbe.HorizClosure), DiagLog.F(Acoustics.EnclosureProbe.OverClosure),
                    DiagLog.F(Acoustics.EnclosureProbe.Room), DiagLog.F(Acoustics.EnclosureProbe.SizeFactor),
                    DiagLog.F(Acoustics.EnclosureProbe.ThinShare), DiagLog.F(Acoustics.EnclosureProbe.ZenithClosure)));
            }
        }

        // For each snapshot switch, the nearest room switch in the same direction: how far apart in time and space.
        public static string Summary()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("INDOOR/OUTDOOR TRANSITIONS (the doorway pop)");
            sb.AppendLine($"  mixer snapshot switches (IndoorTrigger volumes): {SnapshotEvents.Count}   audio-room switches (baked rooms): {RoomEvents.Count}");
            var gapsT = new List<float>(); var gapsD = new List<float>(); int unmatched = 0;
            foreach (Ev s in SnapshotEvents)
            {
                float best = float.MaxValue; Ev match = default;
                foreach (Ev r in RoomEvents)
                    if (r.Indoor == s.Indoor && Mathf.Abs(r.T - s.T) < best) { best = Mathf.Abs(r.T - s.T); match = r; }
                if (best > 10f) { unmatched++; continue; }
                gapsT.Add(best); gapsD.Add(Vector3.Distance(match.Pos, s.Pos));
            }
            if (gapsT.Count > 0)
            {
                gapsT.Sort(); gapsD.Sort();
                sb.AppendLine($"  paired {gapsT.Count}: time apart median {gapsT[gapsT.Count / 2]:0.00} s, max {gapsT[gapsT.Count - 1]:0.00} s;  distance apart median {gapsD[gapsD.Count / 2]:0.0} m, max {gapsD[gapsD.Count - 1]:0.0} m");
            }
            sb.AppendLine($"  snapshot switches with no room switch within 10 s: {unmatched}   <- the two systems disagree about where 'inside' is");
            sb.AppendLine("  details: transitions.csv (every switch + position), ambience.csv (outdoor ambience level, 10/s)");
            return sb.ToString();
        }
    }
}
