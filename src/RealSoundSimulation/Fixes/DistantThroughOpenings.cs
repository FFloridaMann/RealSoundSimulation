using System;
using System.Collections.Generic;
using Audio.SpatialSystem;
using RealSoundSimulation.Acoustics;
using RealSoundSimulation.Diagnostics;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace RealSoundSimulation.Fixes
{
    // Setting "9k Distant gunfire through openings" (0.6.8; user, raids 12, 23, 26: SPTBattleAmbience's distant battles are
    // heard through solid walls from their true direction). Applies to the sounds fix 4 handles: placed off the baked sound
    // map (an "invalid" room), here also at least 80 m away - in practice the ambient gunfire mod's fake battles.
    //
    // While you're inside (probe v2's enclosed, smoothed), each such sound:
    //   level      x sqrt(T): T = how much sound from that direction gets in (AcousticProbe.Through): the escaping paths
    //              whose last leg heads towards the source (horizontally; cos^2 lobe), energy-weighted, x6 so open air
    //              = 1, plus the building's build-up for the part that bounces in.
    //   direction  moved, at its own distance, to come from where those paths left the building (their exit points: the
    //              window or door), else from their first leg; the build-up part leans the way the building's sound arrives.
    // Both blend in with how enclosed you are, so outside nothing changes. The game's own loss for these sounds is set to 0
    // (fix 4's doorway-based muffling is replaced). Volume: the game's occlusion volume factor is scaled in
    // BetterSource.UpdateSourceVolume (every frame), remembering what the game set so nothing compounds. Positions and
    // factors are put back when the sound ends, when you switch this off, or when the raid ends. Log: distant.csv.
    internal class DistantThroughOpenings : MonoBehaviour
    {
        private const float MinDistance = 80f;

        private class M
        {
            public BetterSource Src;
            public Vector3 Orig, Written, Dir = Vector3.forward;
            public float Raw = 1f, WrittenF = -1f, Gain = 1f, T = 1f, Seen, NextCalc, Dist;
        }

        private static readonly Dictionary<BetterSource, M> Managed = new Dictionary<BetterSource, M>();
        private static readonly List<BetterSource> Gone = new List<BetterSource>();
        private static float _nextRow;
        public static int Moved, MaxAtOnce;

        public static bool On =>
            Plugin.DistantThroughOpeningsOn.Value && Plugin.OffMapOn.Value && Plugin.AcousticProbeOn.Value && AcousticProbe.Valid;

        public static void Apply(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(BetterSource), nameof(BetterSource.UpdateSourceVolume)),
                prefix: new HarmonyMethod(typeof(DistantThroughOpenings), nameof(VolumePrefix)));
        }

        public static void Reset()
        {
            foreach (M m in Managed.Values) Release(m);
            Managed.Clear(); Moved = 0; MaxAtOnce = 0; _nextRow = 0f;
        }

        // From fix 4's postfix, for a container whose room is invalid (off the map): take its far sources.
        public static void Register(AudioSourceContainer c)
        {
            if (!On || c == null || c._sources == null) return;
            Vector3 l = Listener();
            for (int i = 0; i < c._sources.Count; i++)
            {
                BetterSource b = c._sources[i];
                if (b == null) continue;
                if (Managed.TryGetValue(b, out M m)) { m.Seen = Time.time; continue; }
                if ((b.Position - l).sqrMagnitude < MinDistance * MinDistance) continue;
                Managed[b] = new M { Src = b, Orig = b.Position, Written = b.Position, Seen = Time.time, Raw = b.OcclusionVolumeFactor };
                Moved++;
            }
            if (Managed.Count > MaxAtOnce) MaxAtOnce = Managed.Count;
        }

        // fix 4 asks: do we handle this container's level? (then the game's own loss is 0)
        public static bool Handles(AudioSourceContainer c)
        {
            if (Managed.Count == 0 || c == null || c._sources == null) return false;
            for (int i = 0; i < c._sources.Count; i++) if (c._sources[i] != null && Managed.ContainsKey(c._sources[i])) return true;
            return false;
        }

        private static void VolumePrefix(BetterSource __instance)
        {
            if (Managed.Count == 0 || !Managed.TryGetValue(__instance, out M m)) return;
            float cur = __instance.OcclusionVolumeFactor;
            if (Mathf.Abs(cur - m.WrittenF) > 1e-5f) m.Raw = cur; // the game set a new factor
            m.WrittenF = Mathf.Clamp01(m.Raw * m.Gain);
            __instance.OcclusionVolumeFactor = m.WrittenF;
        }

        private static void Release(M m)
        {
            if (m.Src == null) return;
            if ((m.Src.Position - m.Written).sqrMagnitude < 0.01f) m.Src.Position = m.Orig;
            if (Mathf.Abs(m.Src.OcclusionVolumeFactor - m.WrittenF) < 1e-5f) m.Src.OcclusionVolumeFactor = m.Raw;
        }

        private static Vector3 Listener()
        {
            if (!MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated) return Vector3.zero;
            Transform lt = MonoBehaviourSingleton<SpatialAudioSystem>.Instance.ListenerTransform;
            return lt != null ? lt.position : Vector3.zero;
        }

        private void LateUpdate()
        {
            if (Managed.Count == 0) return;
            try { Tick(); }
            catch (Exception e) { DiagRunner.ReportOnce("9k distant gunfire through openings", e); }
        }

        private static void Tick()
        {
            bool on = On;
            Transform lt = MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated ? MonoBehaviourSingleton<SpatialAudioSystem>.Instance.ListenerTransform : null;
            float enc = float.IsNaN(AmbienceMix.V2Level) ? AcousticProbe.Enclosed : AmbienceMix.V2Indoor;
            bool row = DiagLog.IsOpen && Time.time >= _nextRow;
            if (row) _nextRow = Time.time + 0.25f;
            Gone.Clear();
            foreach (KeyValuePair<BetterSource, M> kv in Managed)
            {
                M m = kv.Value;
                if (m.Src == null || lt == null) { Gone.Add(kv.Key); continue; }
                bool playing = m.Src.source1 != null && m.Src.source1.isPlaying;
                if (!on || (!playing && Time.time - m.Seen > 0.5f)) { Release(m); Gone.Add(kv.Key); continue; }
                Vector3 cur = m.Src.Position;
                if ((cur - m.Written).sqrMagnitude > 0.01f) { m.Orig = cur; m.NextCalc = 0f; } // a new sound on this pooled source
                Vector3 rel = m.Orig - lt.position;
                m.Dist = rel.magnitude;
                if (m.Dist < 1f) continue;
                Vector3 trueDir = rel / m.Dist;
                if (Time.time >= m.NextCalc)
                {
                    Vector3 s = new Vector3(trueDir.x, 0f, trueDir.z);
                    s = s.sqrMagnitude > 1e-6f ? s.normalized : Vector3.forward;
                    m.T = AcousticProbe.Through(s, out m.Dir);
                    m.NextCalc = Time.time + 0.1f;
                }
                float w = Mathf.Clamp01(enc);
                Vector3 dir = Vector3.Slerp(trueDir, m.Dir, w).normalized;
                m.Gain = Mathf.Lerp(1f, Mathf.Sqrt(Mathf.Clamp01(m.T)), w);
                m.Written = lt.position + dir * m.Dist;
                m.Src.Position = m.Written;
                if (row)
                    DiagLog.Row("distant.csv", string.Join(",", DiagLog.F(Time.time - DiagRunner.RaidStartTime, "0.00"), m.Src.GetInstanceID().ToString(),
                        DiagLog.F(m.Dist, "0"), DiagLog.F(AcousticProbe.RelAz(lt, trueDir), "0"), DiagLog.F(AcousticProbe.RelAz(lt, dir), "0"),
                        DiagLog.F(m.T), DiagLog.F(m.Gain), DiagLog.F(w), m.Src.source1 != null && m.Src.source1.clip != null ? m.Src.source1.clip.name : ""));
            }
            foreach (BetterSource b in Gone) Managed.Remove(b);
        }

        public const string Header = "t,source,distanceM,trueAz,heardAz,transmission,gain,enclosed,clip";

        public static string Summary() =>
            $"DISTANT GUNFIRE THROUGH OPENINGS (9k {(Plugin.DistantThroughOpeningsOn.Value ? "ON" : "off")}): sounds handled {Moved}, at once max {MaxAtOnce}  (distant.csv)\n";
    }
}
