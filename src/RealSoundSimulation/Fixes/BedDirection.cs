using System.Collections.Generic;
using RealSoundSimulation.Acoustics;
using UnityEngine;

namespace RealSoundSimulation.Fixes
{
    // Probe v2 ambience (0.6.0): the game's OWN outdoor beds (wind, outdoor rain, day/night background) are made
    // directional - no copies, so nothing can double (raid 21: 12 decorrelated copies made quiet rain flam).
    //
    // The review (docs/AMBIENCE.md §13) found the game only ever writes these sources' volume and clip; it never moves
    // them or changes their 2D/3D blend. So, per source:
    //   blend    spatialBlend = how enclosed you are x "9b" (0 in open air: the game's own flat stereo bed; up to fully
    //            positioned deep inside). Changes smoothly, one source, so the 2D and 3D parts can't flam.
    //   place    3 m from you in the direction the outside arrives from (probe v2 Arrival, smoothed).
    //   width    Unity panner: spread 180 deg (all round) .. 20 deg (one direction), from the probe's focus.
    //            Meta HRTF ("9c"): spatialized, volumetric radius 3 m .. 0.3 m instead.
    //   distance no distance loss (min distance 50 m): the probe already decided the level.
    //   behind   Unity panner only: duller from behind (ear shadow), on the bed's low-pass filter.
    // A source is only moved if that is safe: its object has no children and every audio source on it is one of the
    // beds. Otherwise it stays flat and the summary says so ("fixed").
    // Everything is put back exactly as it was when the blend returns to 0, v2 is switched off, or the raid ends.
    internal static class BedDirection
    {
        public enum Mode { UnityPanner, MetaHrtf }

        private class S
        {
            public AudioSource Src; public AudioLowPassFilter Lp; public bool OwnLp;
            public bool Checked, Movable, Active;
            public Vector3 OrigLocalPos; public float OrigBlend, OrigSpread, OrigMin, OrigMax, OrigDoppler; public AudioRolloffMode OrigRolloff; public bool OrigSpatialize;
        }

        private static readonly List<S> Beds = new List<S>();
        private static Vector3 _dir = Vector3.forward;
        public static float Blend, Focus;
        public static int Movable, Fixed;

        public static void Init(List<AudioSource> srcs, List<AudioLowPassFilter> lps, List<bool> own)
        {
            Restore(); Beds.Clear(); Movable = Fixed = 0;
            for (int i = 0; i < srcs.Count; i++) if (srcs[i] != null) Beds.Add(new S { Src = srcs[i], Lp = lps[i], OwnLp = own[i] });
        }

        public static void Tick(float dt, Transform lt, bool on)
        {
            if (lt == null) return;
            float a = 1f - Mathf.Exp(-dt / AmbienceMix.Smooth);
            float target = on && AcousticProbe.Valid ? Mathf.Clamp01(Plugin.BedDirectional.Value) * AcousticProbe.Enclosed : 0f;
            Blend += (target - Blend) * a;
            Focus += ((on ? AcousticProbe.Focus : 0f) - Focus) * a;
            // 0.6.1: the direction turns over ~1 s (the level still follows in 0.35 s): it swung a median 36 deg per 0.125 s
            if (on && AcousticProbe.Valid) _dir = Vector3.Slerp(_dir, AcousticProbe.Arrival, 1f - Mathf.Exp(-dt / 1f)).normalized;
            if (Blend < 0.002f && target <= 0f) { Restore(); return; }

            bool meta = Plugin.BedMode.Value == Mode.MetaHrtf;
            Vector3 f = lt.forward; f.y = 0f; if (f.sqrMagnitude > 1e-4f) f.Normalize();
            float behind = Mathf.Max(0f, -Vector3.Dot(new Vector3(_dir.x, 0f, _dir.z).normalized, f)) * Focus * Blend;
            foreach (S s in Beds)
            {
                if (s.Src == null) continue;
                if (!s.Checked) Check(s);
                if (!s.Movable) continue;
                if (!s.Active) Capture(s);
                AudioSource src = s.Src;
                src.transform.position = lt.position + _dir * 3f;
                src.spatialBlend = Blend;
                src.dopplerLevel = 0f;
                src.rolloffMode = AudioRolloffMode.Logarithmic; src.minDistance = 50f; src.maxDistance = 500f;
                if (meta)
                {
                    if (!src.spatialize) src.spatialize = true;
                    src.SetSpatializerFloat(4, Mathf.Lerp(3f, 0.3f, Focus)); // Meta: volumetric radius (m)
                    src.spread = 0f;
                }
                else
                {
                    if (src.spatialize) src.spatialize = false;
                    src.spread = Mathf.Lerp(180f, 20f, Focus);
                }
                if (s.OwnLp && s.Lp != null)
                {
                    float cut = meta ? 22000f : Mathf.Lerp(22000f, 6000f, behind);
                    s.Lp.enabled = cut < 21500f;
                    s.Lp.cutoffFrequency = cut;
                }
            }
        }

        private static void Check(S s)
        {
            s.Checked = true;
            Transform t = s.Src.transform;
            bool ok = t.childCount == 0 && t.GetComponentInChildren<AudioListener>(true) == null;
            if (ok)
                foreach (AudioSource other in t.GetComponents<AudioSource>())
                {
                    bool isBed = false;
                    foreach (S b in Beds) if (b.Src == other) { isBed = true; break; }
                    if (!isBed) { ok = false; break; }
                }
            s.Movable = ok;
            if (ok) Movable++; else Fixed++;
            Plugin.Log.LogInfo($"Probe v2 bed {s.Src.name} ({(s.Src.clip != null ? s.Src.clip.channels + " ch" : "no clip")}, blend {s.Src.spatialBlend:0.00}, spatialize {s.Src.spatialize}, parent {(t.parent != null ? t.parent.name : "-")}): {(ok ? "movable" : "fixed")}");
        }

        private static void Capture(S s)
        {
            AudioSource src = s.Src;
            s.OrigLocalPos = src.transform.localPosition; s.OrigBlend = src.spatialBlend; s.OrigSpread = src.spread;
            s.OrigMin = src.minDistance; s.OrigMax = src.maxDistance; s.OrigRolloff = src.rolloffMode; s.OrigDoppler = src.dopplerLevel;
            s.OrigSpatialize = src.spatialize;
            s.Active = true;
        }

        public static void Restore()
        {
            foreach (S s in Beds)
            {
                if (!s.Active || s.Src == null) { s.Active = false; continue; }
                AudioSource src = s.Src;
                src.transform.localPosition = s.OrigLocalPos; src.spatialBlend = s.OrigBlend; src.spread = s.OrigSpread;
                src.minDistance = s.OrigMin; src.maxDistance = s.OrigMax; src.rolloffMode = s.OrigRolloff; src.dopplerLevel = s.OrigDoppler;
                if (src.spatialize != s.OrigSpatialize) src.spatialize = s.OrigSpatialize;
                if (s.OwnLp && s.Lp != null) s.Lp.enabled = false;
                s.Active = false;
            }
            Blend = 0f;
        }

        public static string Summary() => $"  probe v2 directional beds: {Movable} movable, {Fixed} fixed (left flat), mode {Plugin.BedMode.Value}\n";
    }
}
