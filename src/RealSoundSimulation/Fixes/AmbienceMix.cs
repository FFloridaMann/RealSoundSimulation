using System;
using System.Collections.Generic;
using Audio.AmbientSubsystem;
using RealSoundSimulation.Acoustics;
using RealSoundSimulation.Diagnostics;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace RealSoundSimulation.Fixes
{
    // Fix 8: outdoor sound indoors from openings (roadmap step 4b, phase 2, 0.4.2). Design: docs/AMBIENCE.md.
    // Needs fix 3 on (fix 3 is what takes the ambience over from the game).
    //
    //   Level     Indoors, the game's outdoor value (outdoor beds' volume + muffling, via the game's own curves) is the
    //             exposure value from OutdoorExposure, smoothed over ~1 s, never below the room's baked floor. Open sides
    //             count fully, glass by "8c Through glass", bars 80%. Replaces fix 3's doorway fade for the level (the
    //             doorway blend still drives the indoor/outdoor reverb snapshot) and fix 3h's glass guess.
    //   Indoor    The indoor layers (room tone, indoor wind/rain) fade by "8d" x exposure: a spot next to an open side
    //             loses some indoor character, a deep interior keeps it all.
    //   Roof rain The indoor rain player's volume: full with only the roof above you, x "8b" with one floor above, 0 with
    //             two or more (0.5.0; raid 14: roof rain heard down the Nakatani exit stairs under 4 floors).
    //   Openings  (0.5.0, phase 4) OpeningSounds: 3D copies of the outdoor beds placed at the openings; see that file.
    //   Direction (cue until phase 4) The outdoor beds (wind, outdoor rain, day/night background) are 2D stereo loops. While
    //             indoors they're panned toward the open side (left/right, by how one-sided it is) and dulled when the open
    //             side is behind you (ear shadow), x "8a Direction cue strength". Outdoors they're left as the game plays them.
    // 0.5.6 (raid 19): one continuous measure, one speed. Exposure is measured outdoors too (no step at the room boundary),
    // and the outdoor level, the opening sounds' share, the indoor layers and the roof rain all follow it with the same
    // smoothing (Smooth, ~0.35 s). Before, the level lagged ~2 s (1 s smoothing, 4 updates/s) while the opening sounds
    // dropped in 0.6 s and the indoor layers switched instantly - "like the game is playing different audio loops".
    // Roof rain is now set directly on the game's indoor rain player (no crossfade requests; 0.5.4 crashed asking an
    // uninitialised player to crossfade and then only changed on the game's own room-change crossfades).
    internal class AmbienceMix : MonoBehaviour
    {
        public const float Smooth = 0.35f;
        public static float ExposureApplied = float.NaN, BedPan, BedCutoff = 22000f, RoofRainFactor = 1f;
        private static float _expSm = -1f;

        private struct Bed { public AudioSource Src; public AudioLowPassFilter Lp; public bool OwnFilter; }
        private static readonly List<Bed> Beds = new List<Bed>();
        private static readonly List<PrecipitationAmbientBlender> IndoorRain = new List<PrecipitationAmbientBlender>();
        private static bool _found;
        private static float _nextFind, _roofCandidate = 1f, _roofCandidateSince;

        public static bool On => Plugin.ExposureMixOn.Value && Plugin.DoorwayOn.Value;

        public static void Apply(Harmony h)
        {
            // 0.5.6: no patches; roof rain is applied to the rain player's sources directly (see RoofRain).
        }

        // the indoor rain player's sources: volume the game last wrote, and what we wrote back
        private static readonly List<AudioSource> RainSrc = new List<AudioSource>();
        private static float[] _rainGame = new float[0], _rainWritten = new float[0];
        private static bool _rainTracking;
        public static float RoofRainApplied = 1f;

        public static void Reset()
        {
            RestoreBeds();
            OpeningSounds.Destroy();
            Beds.Clear(); IndoorRain.Clear();
            RestoreRain(); RainSrc.Clear();
            _found = false; _nextFind = 0f; _expSm = -1f; RoofRainApplied = 1f;
            ExposureApplied = float.NaN; BedPan = 0f; BedCutoff = 22000f; RoofRainFactor = 1f;
        }

        // Called by DoorwayBlend each frame while fix 3 drives the ambience: the outdoor value to use, or NaN = leave fix 3's.
        public static float OutdoorValue(float dt, float roomFloor)
        {
            if (!On || !OutdoorExposure.Valid) { _expSm = -1f; ExposureApplied = float.NaN; return float.NaN; }
            float target = OutdoorExposure.ListenerIndoor ? Mathf.Max(OutdoorExposure.Value, roomFloor) : OutdoorExposure.Value;
            _expSm = _expSm < 0f ? target : _expSm + (target - _expSm) * (1f - Mathf.Exp(-dt / Smooth));
            ExposureApplied = _expSm;
            return _expSm;
        }

        // 0.5.7: indoor-layer level from enclosure: 1 when enclosed, fading to 0 by open air; 8d scales how much openings
        // around you reduce it (0 = indoor layers stay full until you are fully outside).
        public static float IndoorLayers()
        {
            if (float.IsNaN(ExposureApplied)) return 1f;
            float fade = Mathf.Clamp01(Plugin.IndoorFadeNearOpenings.Value);
            float open = Mathf.InverseLerp(0.15f, 0.95f, ExposureApplied);
            float level = 1f - Mathf.SmoothStep(0f, 1f, open);
            // with 8d < 1 the indoor layers keep more level until close to open air
            return Mathf.Clamp01(Mathf.Lerp(1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.85f, 0.97f, ExposureApplied)), level, fade));
        }

        // Indoor-layer volume multiplier.
        public static float IndoorFade()
        {
            if (!On || float.IsNaN(ExposureApplied)) return 1f;
            return 1f - Mathf.Clamp01(Plugin.IndoorFadeNearOpenings.Value) * ExposureApplied;
        }

        // How enclosed you are, 0 (open air) .. 1 (inside): scales the opening sounds and the direction cue, so they fade in
        // as you go inside instead of switching at the room boundary.
        public static float Enclosed()
        {
            if (float.IsNaN(ExposureApplied)) return 0f;
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - ExposureApplied) / 0.25f));
        }

        private static void RoofRain(float dt)
        {
            if (RainSrc.Count == 0) return;
            if (_rainGame.Length != RainSrc.Count) { _rainGame = new float[RainSrc.Count]; _rainWritten = new float[RainSrc.Count]; _rainTracking = false; }
            RoofRainApplied += (RoofRainFactor - RoofRainApplied) * (1f - Mathf.Exp(-dt / Smooth));
            if (!_rainTracking) { for (int i = 0; i < RainSrc.Count; i++) { _rainGame[i] = RainSrc[i] != null ? RainSrc[i].volume : 0f; _rainWritten[i] = -1f; } _rainTracking = true; }
            for (int i = 0; i < RainSrc.Count; i++)
            {
                AudioSource s = RainSrc[i];
                if (s == null) continue;
                if (Mathf.Abs(s.volume - _rainWritten[i]) > 1e-4f) _rainGame[i] = s.volume; // the game changed it (crossfade)
                _rainWritten[i] = _rainGame[i] * RoofRainApplied;
                s.volume = _rainWritten[i];
            }
        }

        private static void RestoreRain()
        {
            if (!_rainTracking) return;
            for (int i = 0; i < RainSrc.Count && i < _rainGame.Length; i++)
                if (RainSrc[i] != null && Mathf.Abs(RainSrc[i].volume - _rainWritten[i]) < 1e-4f) RainSrc[i].volume = _rainGame[i];
            _rainTracking = false; RoofRainApplied = 1f;
        }

        // LateUpdate: after the game's ambience coroutines have written the beds' volumes this frame.
        private void LateUpdate()
        {
            Perf.Begin();
            try { LateUpdateCore(); }
            finally { Perf.End(Perf.Part.AmbienceMix); }
        }

        private void LateUpdateCore()
        {
            bool inRaid = SpatialAudioSystem_InRaid();
            if (!inRaid) { if (_found) Reset(); return; }
            try
            {
                RainDropoutFix.Tick();
                if (!On)
                {
                    // off: hand the beds back once, then touch nothing
                    if (_found && (BedPan != 0f || BedCutoff < 22000f)) { RestoreBeds(); BedPan = 0f; BedCutoff = 22000f; }
                    OpeningSounds.Restore();
                    RestoreRain();
                    RoofRainFactor = 1f;
                    return;
                }
                if (!_found && Time.time >= _nextFind) Find();
                float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);

                // roof rain: full with only the roof above, 8b with one floor above, none with two or more. A new level must
                // hold 0.5 s (no flicker under beams), then it's applied smoothly and directly to the rain player's volume.
                int fl = OutdoorExposure.FloorsAbove;
                float want = fl <= 0 ? 1f : fl == 1 ? Mathf.Clamp01(Plugin.RoofRainUnderFloors.Value) : 0f;
                if (Mathf.Abs(want - _roofCandidate) > 0.01f) { _roofCandidate = want; _roofCandidateSince = Time.time; }
                if (Time.time - _roofCandidateSince >= 0.5f) RoofRainFactor = _roofCandidate;
                RoofRain(dt);

                // direction cue on the outdoor beds
                float pan = 0f, cutoff = 22000f;
                if (OutdoorExposure.Valid && MonoBehaviourSingleton<Audio.SpatialSystem.SpatialAudioSystem>.Instantiated)
                {
                    Transform lt = MonoBehaviourSingleton<Audio.SpatialSystem.SpatialAudioSystem>.Instance.ListenerTransform;
                    Vector3 side = OutdoorExposure.OpenSide;
                    if (lt != null && side.sqrMagnitude > 1e-4f)
                    {
                        float k = Mathf.Clamp01(Plugin.DirectionCueStrength.Value) * Mathf.Clamp01(OutdoorExposure.OneSided) * Enclosed();
                        Vector3 f = lt.forward; f.y = 0f; Vector3 r = lt.right; r.y = 0f;
                        side.y = 0f; side.Normalize();
                        if (f.sqrMagnitude > 1e-4f) { f.Normalize(); r.Normalize(); }
                        pan = Mathf.Clamp(Vector3.Dot(side, r) * k, -0.9f, 0.9f);
                        float behind = Mathf.Max(0f, -Vector3.Dot(side, f)) * k;
                        cutoff = Mathf.Lerp(22000f, 5000f, behind);
                    }
                }
                float a = 1f - Mathf.Exp(-dt / Smooth);
                BedPan += (pan - BedPan) * a;
                BedCutoff += (cutoff - BedCutoff) * a;
                // phase 4: 3D sound at the openings (indoors only)
                Transform lt2 = MonoBehaviourSingleton<Audio.SpatialSystem.SpatialAudioSystem>.Instantiated ? MonoBehaviourSingleton<Audio.SpatialSystem.SpatialAudioSystem>.Instance.ListenerTransform : null;
                OpeningSounds.Tick(dt, OutdoorExposure.Valid, lt2, Enclosed());

                foreach (Bed b in Beds)
                {
                    if (b.Src == null) continue;
                    if (b.Src.spatialBlend < 0.5f) b.Src.panStereo = BedPan;
                    if (b.OwnFilter && b.Lp != null)
                    {
                        b.Lp.enabled = BedCutoff < 21500f;
                        b.Lp.cutoffFrequency = BedCutoff;
                    }
                }
            }
            catch (Exception e) { DiagRunner.ReportOnce("fix 8 ambience mix", e); }
        }

        private static bool SpatialAudioSystem_InRaid() =>
            Audio.SpatialSystem.SpatialAudioSystem.Initialized && MonoBehaviourSingleton<Audio.SpatialSystem.SpatialAudioSystem>.Instantiated && GamePlayerOwner.MyPlayer != null;

        private static void Find()
        {
            _nextFind = Time.time + 5f;
            var seen = new HashSet<AudioSource>();
            foreach (WindAmbientBlender w in FindObjectsOfType<WindAmbientBlender>()) { AddBed(w._windSource, seen); AddBed(w._windMixSource, seen); }
            foreach (DayTimeAmbientBlender d in FindObjectsOfType<DayTimeAmbientBlender>()) { AddBed(d._outdoorAmbientDaySource, seen); AddBed(d._outdoorAmbientNightSource, seen); }
            foreach (PrecipitationAmbientBlender p in FindObjectsOfType<PrecipitationAmbientBlender>())
            {
                if (p._environment == EnvironmentType.Outdoor) { AddBed(p._precipitationSource, seen); AddBed(p._precipitationMixSource, seen); }
                else
                {
                    IndoorRain.Add(p);
                    if (p._precipitationSource != null) RainSrc.Add(p._precipitationSource);
                    if (p._precipitationMixSource != null) RainSrc.Add(p._precipitationMixSource);
                }
            }
            _found = Beds.Count > 0 || IndoorRain.Count > 0;
            BedsFound = Beds.Count; IndoorRainFound = IndoorRain.Count;
            var srcs = new List<AudioSource>();
            foreach (Bed b in Beds) srcs.Add(b.Src);
            if (srcs.Count > 0) OpeningSounds.Init(srcs);
        }

        public static int BedsFound, IndoorRainFound;

        private static void AddBed(AudioSource s, HashSet<AudioSource> seen)
        {
            if (s == null || !seen.Add(s)) return;
            AudioLowPassFilter lp = s.GetComponent<AudioLowPassFilter>();
            bool own = false;
            if (lp == null) { lp = s.gameObject.AddComponent<AudioLowPassFilter>(); lp.lowpassResonanceQ = 1f; lp.cutoffFrequency = 22000f; lp.enabled = false; own = true; }
            Beds.Add(new Bed { Src = s, Lp = lp, OwnFilter = own });
        }

        private static void RestoreBeds()
        {
            foreach (Bed b in Beds)
            {
                if (b.Src != null && b.Src.spatialBlend < 0.5f) b.Src.panStereo = 0f;
                if (b.OwnFilter && b.Lp != null) b.Lp.enabled = false;
            }
        }

        public static string Summary() =>
            $"AMBIENCE MIX (fix 8): outdoor beds found {BedsFound} sources, indoor rain players {IndoorRainFound}, opening sound voices max {OpeningSounds.MaxVoices}\n";
    }
}
