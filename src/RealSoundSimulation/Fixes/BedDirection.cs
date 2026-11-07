using System.Collections.Generic;
using RealSoundSimulation.Acoustics;
using UnityEngine;

namespace RealSoundSimulation.Fixes
{
    // Probe v2 ambience (0.6.0): the game's OWN ambience sources are made directional - no copies, so nothing can double
    // (raid 21: 12 decorrelated copies made quiet rain flam). Two groups (0.6.7):
    //   Outdoor  wind, outdoor rain, day/night background: from where the outside's sound arrives (AcousticProbe.Arrival)
    //   Roof     the indoor "rain on the roof" player: from where the roof's sound arrives (AcousticProbe.RoofArrival), so
    //            on the floor below a roof it comes down the stairwell (user, raid 26)
    //
    // The review (docs/AMBIENCE.md §13) found the game only ever writes these sources' volume and clip; it never moves
    // them or changes their 2D/3D blend. So, per source:
    //   blend    spatialBlend = how enclosed you are x "9b" (0 in open air: the game's own flat stereo bed; up to fully
    //            positioned deep inside). Changes smoothly, one source, so the 2D and 3D parts can't flam.
    //   place    3 m from you in the direction the sound arrives from (smoothed).
    //   width    Unity panner: spread 180 deg (all round) .. 20 deg (one direction), from the probe's focus x "9h".
    //            Meta HRTF ("9c"): spatialized, volumetric radius 3 m .. 0.3 m instead.
    //   distance no distance loss (min distance 50 m): the probe already decided the level.
    //   behind   Unity panner only: duller from behind (ear shadow), on the source's own low-pass filter (outdoor beds).
    // A source is only moved if that is safe: its object has no children and every audio source on it is in the group.
    // Otherwise it stays flat and the summary says so ("fixed").
    // Everything is put back exactly as it was when the blend returns to 0, v2 is switched off, or the raid ends.
    internal class BedDirection
    {
        public enum Mode { UnityPanner, MetaHrtf }

        public static readonly BedDirection Outdoor = new BedDirection("outdoor beds");
        public static readonly BedDirection Roof = new BedDirection("roof rain");

        private class S
        {
            public AudioSource Src; public AudioLowPassFilter Lp; public bool OwnLp;
            public bool Checked, Movable, Active;
            public Vector3 OrigLocalPos; public float OrigBlend, OrigSpread, OrigMin, OrigMax, OrigDoppler; public AudioRolloffMode OrigRolloff; public bool OrigSpatialize;
        }

        private readonly string _name;
        private readonly List<S> _beds = new List<S>();
        private Vector3 _dir = Vector3.forward;
        public float Blend, Focus;
        public int Movable, Fixed;

        private BedDirection(string name) { _name = name; }

        public void Init(List<AudioSource> srcs, List<AudioLowPassFilter> lps, List<bool> own)
        {
            Restore(); _beds.Clear(); Movable = Fixed = 0;
            for (int i = 0; i < srcs.Count; i++)
                if (srcs[i] != null) _beds.Add(new S { Src = srcs[i], Lp = lps != null ? lps[i] : null, OwnLp = own != null && own[i] });
        }

        public static void RestoreAll() { Outdoor.Restore(); Roof.Restore(); }

        // arrival/focus: from the probe; enclosed: how far to blend from flat to positioned
        public void Tick(float dt, Transform lt, bool on, Vector3 arrival, float focus, float enclosed)
        {
            if (lt == null) return;
            float smooth = Mathf.Max(AmbienceMix.Smooth, AcousticProbe.SmoothS); // 0.6.4: by confidence
            float a = 1f - Mathf.Exp(-dt / smooth);
            float target = on && AcousticProbe.Valid ? Mathf.Clamp01(Plugin.BedDirectional.Value) * enclosed : 0f;
            Blend += (target - Blend) * a;
            // 0.6.7: "9h Direction strength" = how pointed a given focus sounds
            float f = on ? Mathf.Clamp01(focus * Mathf.Max(0f, Plugin.DirectionStrength.Value)) : 0f;
            Focus += (f - Focus) * a;
            // 0.6.1: the direction turns over ~1 s or longer (the level still follows in 0.35 s)
            if (on && AcousticProbe.Valid && arrival.sqrMagnitude > 1e-6f)
                _dir = Vector3.Slerp(_dir, arrival, 1f - Mathf.Exp(-dt / Mathf.Max(1f, smooth))).normalized;
            if (Blend < 0.002f && target <= 0f) { Restore(); return; }

            bool meta = Plugin.BedMode.Value == Mode.MetaHrtf;
            Vector3 fw = lt.forward; fw.y = 0f; if (fw.sqrMagnitude > 1e-4f) fw.Normalize();
            Vector3 flat = new Vector3(_dir.x, 0f, _dir.z);
            float behind = flat.sqrMagnitude > 1e-6f ? Mathf.Max(0f, -Vector3.Dot(flat.normalized, fw)) * Focus * Blend : 0f;
            foreach (S s in _beds)
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

        private void Check(S s)
        {
            s.Checked = true;
            Transform t = s.Src.transform;
            bool ok = t.childCount == 0 && t.GetComponentInChildren<AudioListener>(true) == null;
            if (ok)
                foreach (AudioSource other in t.GetComponents<AudioSource>())
                {
                    bool mine = false;
                    foreach (S b in _beds) if (b.Src == other) { mine = true; break; }
                    if (!mine) { ok = false; break; }
                }
            s.Movable = ok;
            if (ok) Movable++; else Fixed++;
            Plugin.Log.LogInfo($"Probe v2 {_name}: {s.Src.name} ({(s.Src.clip != null ? s.Src.clip.channels + " ch" : "no clip")}, blend {s.Src.spatialBlend:0.00}, spatialize {s.Src.spatialize}, parent {(t.parent != null ? t.parent.name : "-")}): {(ok ? "movable" : "fixed")}");
        }

        private static void Capture(S s)
        {
            AudioSource src = s.Src;
            s.OrigLocalPos = src.transform.localPosition; s.OrigBlend = src.spatialBlend; s.OrigSpread = src.spread;
            s.OrigMin = src.minDistance; s.OrigMax = src.maxDistance; s.OrigRolloff = src.rolloffMode; s.OrigDoppler = src.dopplerLevel;
            s.OrigSpatialize = src.spatialize;
            s.Active = true;
        }

        public void Restore()
        {
            foreach (S s in _beds)
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

        public string Summary() => $"  probe v2 directional {_name}: {Movable} movable, {Fixed} fixed (left flat), mode {Plugin.BedMode.Value}\n";
    }
}
