using System.Collections.Generic;
using RealSoundSimulation.Acoustics;
using UnityEngine;

namespace RealSoundSimulation.Fixes
{
    // Fix 8 phase 4 (roadmap step 4b, 0.5.0): the outside heard indoors comes FROM the openings. Design: docs/AMBIENCE.md.
    //
    // The game's outdoor beds (wind, outdoor rain, day/night background) are flat 2D loops. While you're indoors:
    //   - 0.5.2: 6 fixed world sectors (60 deg each). OutdoorExposure sums each sector's exposed directions (weight,
    //     weighted direction, glass share). Each sector has an emitter: a 3D copy of every bed that's playing, 4 m out in
    //     the sector's weighted direction (it only moves within its sector), routed to the bed's own mixer group (so the
    //     game's outdoor level and muffling - set by fix 8 - still apply). 0.5.0-0.5.1 used up to 3 "openings" that were
    //     re-assigned each update and swept across the listener (raid 16).
    //   - "8e" of the bed's sound moves to the emitters (the 2D bed keeps 1 - 8e), split between sectors by power
    //     (gain = sqrt(weight / total)). Total loudness stays the same; only where it comes from changes.
    //   - Each emitter is filtered by what it comes through: glass openings duller (down to 2.5 kHz at all-glass), and,
    //     unless HRTF is on (8f), duller when the opening is behind you (ear shadow - the same cue as the echoes).
    //   - Spread follows the opening's width (a wide open side sounds wide).
    // Outdoors, with fix 8 off, or 8e = 0: emitters fade out and the beds get their full volume back.
    //
    // Bed volume: the game writes the beds' volume itself (crossfades, day/night balance). Each frame (LateUpdate, after
    // the game's coroutines) the volume the game last wrote is captured and written back x (1 - share).
    internal static class OpeningSounds
    {
        // 0.5.2: one source group per fixed world sector (6 x 60 deg), never re-pointed across the listener.
        private const int SlotN = OutdoorExposure.SectorN;

        private class Slot
        {
            public GameObject Go; public AudioLowPassFilter Lp; public AudioSource[] Src;
            public Vector3 Dir = Vector3.forward, TargetDir = Vector3.forward;
            public float Gain, TargetGain, Dist = 4f, Glass, Width = 60f;
        }

        private static Slot[] _slots;
        private static List<AudioSource> _beds;
        private static float[] _gameVol, _written;
        private static bool _tracking;

        public static float Share;        // applied share of the beds moved to the emitters (smoothed)
        public static int Voices;         // emitter sources playing now
        public static int MaxVoices;

        public static void Init(List<AudioSource> beds)
        {
            Destroy();
            _beds = beds;
            _gameVol = new float[beds.Count]; _written = new float[beds.Count];
            _slots = new Slot[SlotN];
            for (int s = 0; s < SlotN; s++)
            {
                // start each sector pointing at its own middle
                var go = new GameObject("RealSoundSimulation opening " + s);
                Object.DontDestroyOnLoad(go);
                var src = new AudioSource[beds.Count];
                var flat = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 1f));
                for (int b = 0; b < beds.Count; b++)
                {
                    AudioSource a = go.AddComponent<AudioSource>();
                    a.playOnAwake = false; a.spatialBlend = 1f; a.dopplerLevel = 0f; a.reverbZoneMix = 0f; a.bypassReverbZones = true;
                    a.priority = 100; a.rolloffMode = AudioRolloffMode.Custom; a.maxDistance = 100000f;
                    a.SetCustomCurve(AudioSourceCurveType.CustomRolloff, flat);
                    src[b] = a;
                }
                // the filter after the sources: Unity refuses a filter on an object with no audio source (0.5.0 crash, raid 15)
                var lp = go.AddComponent<AudioLowPassFilter>(); lp.lowpassResonanceQ = 1f; lp.cutoffFrequency = 22000f;
                _slots[s] = new Slot { Go = go, Lp = lp, Src = src, Dir = OutdoorExposure.SectorHome(s), TargetDir = OutdoorExposure.SectorHome(s) };
            }
            _tracking = false; Share = 0f; Voices = 0;
        }

        public static void Destroy()
        {
            Restore();
            if (_slots != null) foreach (Slot s in _slots) if (s?.Go != null) Object.Destroy(s.Go);
            _slots = null; _beds = null;
        }

        // Give the beds their full volume back and stop the emitters.
        public static void Restore()
        {
            if (_beds != null && _tracking)
                for (int b = 0; b < _beds.Count; b++)
                    if (_beds[b] != null && Mathf.Abs(_beds[b].volume - _written[b]) < 1e-4f) _beds[b].volume = _gameVol[b];
            _tracking = false;
            if (_slots != null) foreach (Slot s in _slots) { if (s == null) continue; s.Gain = 0f; foreach (AudioSource a in s.Src) if (a != null && a.isPlaying) a.Stop(); }
            Share = 0f; Voices = 0;
        }

        public static void Tick(float dt, bool active, Transform lt, float enclosed)
        {
            if (_slots == null || _beds == null || lt == null) return;
            foreach (Slot s in _slots) if (s == null || s.Lp == null) return;
            // 0.5.6: the share follows how enclosed you are (full deep inside, fading out toward open air), at the same speed
            // as the outdoor level - no switch at the room boundary.
            float targetShare = active && OutdoorExposure.OpeningN > 0 ? Mathf.Clamp01(Plugin.OpeningShare.Value) * Mathf.Clamp01(enclosed) : 0f;
            Share += (targetShare - Share) * (1f - Mathf.Exp(-dt / AmbienceMix.Smooth));
            if (targetShare <= 0f && Share < 0.002f) { if (_tracking || Voices > 0) Restore(); return; }

            // each sector's share of the outside (power split), direction within its sector, glass share
            float total = 0f;
            for (int k = 0; k < SlotN; k++) total += OutdoorExposure.SectorWeight[k];
            for (int k = 0; k < SlotN; k++)
            {
                Slot sl = _slots[k];
                float w = OutdoorExposure.SectorWeight[k];
                sl.TargetGain = total > 0f && w > 0f ? Mathf.Sqrt(w / total) : 0f;
                sl.TargetDir = w > 0f ? OutdoorExposure.SectorDir[k] : OutdoorExposure.SectorHome(k);
                if (w > 0f) sl.Glass = OutdoorExposure.SectorGlass[k];
                sl.Dist = 4f; sl.Width = 120f; // 0.5.2: wide and overlapping, so rain sounds like an area, not a point (raid 16)
            }

            // beds: capture what the game wrote, keep (1 - share) on the flat 2D bed
            if (!_tracking) { for (int b = 0; b < _beds.Count; b++) { _gameVol[b] = _beds[b] != null ? _beds[b].volume : 0f; _written[b] = -1f; } _tracking = true; }
            for (int b = 0; b < _beds.Count; b++)
            {
                AudioSource bed = _beds[b];
                if (bed == null) continue;
                if (Mathf.Abs(bed.volume - _written[b]) > 1e-4f) _gameVol[b] = bed.volume;
                _written[b] = _gameVol[b] * (1f - Share);
                bed.volume = _written[b];
            }

            Vector3 o = lt.position, fwd = lt.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude > 1e-4f) fwd.Normalize();
            bool hrtf = Plugin.OpeningHrtf.Value;
            float ag = 1f - Mathf.Exp(-dt / AmbienceMix.Smooth), ad = 1f - Mathf.Exp(-dt / AmbienceMix.Smooth); // 0.5.6: one speed for everything
            Voices = 0;
            foreach (Slot sl in _slots)
            {
                sl.Gain += (sl.TargetGain - sl.Gain) * ag;
                sl.Dir = Vector3.Slerp(sl.Dir, sl.TargetDir, ad); // stays within its 60 deg sector
                if (sl.Dir.sqrMagnitude < 1e-4f) sl.Dir = sl.TargetDir;
                sl.Go.transform.position = o + sl.Dir * Mathf.Clamp(sl.Dist, 2f, 8f);
                Vector3 dh = new Vector3(sl.Dir.x, 0f, sl.Dir.z);
                float behind = dh.sqrMagnitude > 1e-4f ? Mathf.Max(0f, -Vector3.Dot(dh.normalized, fwd)) : 0f;
                float cutoff = Mathf.Lerp(22000f, 2500f, Mathf.Clamp01(sl.Glass)) * (hrtf ? 1f : Mathf.Lerp(1f, 0.55f, behind));
                sl.Lp.cutoffFrequency = Mathf.Clamp(cutoff, 400f, 22000f);
                sl.Lp.enabled = cutoff < 21500f;
                for (int b = 0; b < _beds.Count; b++)
                {
                    AudioSource bed = _beds[b], a = sl.Src[b];
                    float vol = _gameVol[b] * Share * sl.Gain;
                    bool want = bed != null && bed.isPlaying && bed.clip != null && vol > 0.0005f;
                    if (!want) { if (a.isPlaying) a.Stop(); continue; }
                    if (a.clip != bed.clip || !a.isPlaying)
                    {
                        a.clip = bed.clip; a.loop = bed.loop;
                        a.outputAudioMixerGroup = bed.outputAudioMixerGroup;
                        a.Play();
                        // 0.5.4: each copy starts at its own random point in the loop. Copies started in step with the bed
                        // summed into a hollow, doubled, flanging sound (raid 18: "like 2 gunshots at once").
                        a.time = Random.Range(0f, bed.clip.length * 0.95f);
                    }
                    a.spatialize = hrtf;
                    a.spread = Mathf.Clamp(sl.Width, 20f, 160f);
                    a.pitch = bed.pitch;
                    a.volume = Mathf.Clamp01(vol);
                    Voices++;
                }
            }
            if (Voices > MaxVoices) MaxVoices = Voices;
        }

        public static float FirstGain() => _slots != null && _slots[0] != null ? _slots[0].Gain : 0f;
        public static Vector3 FirstDir() => _slots != null && _slots[0] != null ? _slots[0].Dir : Vector3.zero;
    }
}
