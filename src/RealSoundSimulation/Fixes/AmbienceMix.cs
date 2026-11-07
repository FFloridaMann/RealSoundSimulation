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
    // Fix 8: outdoor sound indoors from openings (roadmap step 4b). Design and history: docs/AMBIENCE.md.
    // Needs fix 3 on (fix 3 is what takes the ambience over from the game) and the outdoor exposure probe.
    // Current behaviour (0.5.8). Everything below follows ONE measure (OutdoorExposure.Value, indoors and outdoors) with
    // ONE smoothing time (Smooth, 0.35 s), so nothing switches at the room boundary:
    //   Level      The game's outdoor value (outdoor beds' volume and muffling, through the game's own curves) is the
    //              exposure value, smoothed (ExposureApplied); indoors never below the room's baked floor. Open sides count
    //              fully, intact glass by "8c", bars 80%.
    //   Indoor     The indoor layers (room tone, indoor wind/rain) follow IndoorLayers(): 1 when enclosed, 0 in open air;
    //              "8d" sets how early openings fade them. Applied by DoorwayBlend.
    //   Roof rain  The indoor rain player's sources are scaled directly: full with only the roof above you, x "8b" with
    //              one floor above, 0 with two or more (a new level must hold 0.5 s).
    //   Openings   OpeningSounds: "8e" x Enclosed() of the outdoor beds plays as 3D sound from 6 fixed world sectors.
    //   Direction  The remaining flat beds are panned toward the open side and dulled when it is behind you, x "8a" x
    //              how one-sided the opening is x Enclosed().
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
            BedDirection.Restore();
            OpeningSounds.Destroy();
            Took.Clear(); V2Active = false; V2Level = float.NaN;
            Beds.Clear(); IndoorRain.Clear();
            RestoreRain(); RainSrc.Clear();
            _found = false; _nextFind = 0f; _expSm = -1f; RoofRainApplied = 1f;
            ExposureApplied = float.NaN; BedPan = 0f; BedCutoff = 22000f; RoofRainFactor = 1f;
        }

        // ---------------- probe v2 (fix 9, 0.6.0) ----------------
        // With "9 Ambience from probe v2" on: the outdoor beds' volume and muffling come straight from the acoustic probe
        // (dB and Hz, smoothed like everything else), written to the game's outdoor mixer parameters directly instead of
        // through its outdoor-value curve (which also raised a high-pass up to 700 Hz indoors - unphysical: walls and glass
        // let the lows through). Indoor layers follow probe v2's Enclosed. The beds become directional (BedDirection); the
        // 0.5.x opening-sound copies and direction cue are off.
        public static bool V2Active;
        public static float V2Level = float.NaN, V2Cutoff = 22000f, V2Indoor = 1f;
        private static float _coverSm = 1f;
        private static readonly HashSet<AmbientSoundBlender> Took = new HashSet<AmbientSoundBlender>();

        private static void V2Smooth(float dt)
        {
            float lv = Mathf.Max(AcousticProbe.Level, 1e-4f), cut = Mathf.Max(AcousticProbe.CutoffHz, 100f);
            if (float.IsNaN(V2Level)) { V2Level = lv; V2Cutoff = cut; V2Indoor = AcousticProbe.Enclosed; _coverSm = AcousticProbe.Coverage; return; }
            // 0.6.4: averaged longer when few probe paths get out (AcousticProbe.SmoothS 0.35..2.5 s)
            float a = 1f - Mathf.Exp(-dt / Mathf.Max(Smooth, AcousticProbe.SmoothS));
            float db = 10f * Mathf.Log10(V2Level), tdb = 10f * Mathf.Log10(lv);
            V2Level = Mathf.Pow(10f, (db + (tdb - db) * a) / 10f);
            float lc = Mathf.Log(V2Cutoff), tc = Mathf.Log(cut);
            V2Cutoff = Mathf.Exp(lc + (tc - lc) * a);
            V2Indoor += (AcousticProbe.Enclosed - V2Indoor) * a;
            _coverSm += (AcousticProbe.Coverage - _coverSm) * a;
        }

        // Write the outdoor level (dB) and muffling (Hz) to a blender's outdoor mixer parameters, the same parameters the
        // game's SetOutdoorMixerParams writes, without its curve. The first time, the game's own setter runs once so any
        // fade it had running is stopped.
        public static void ApplyOutdoorDirect(AmbientSoundBlender b, UnityEngine.Audio.AudioMixer mixer, float volDb, float cutoff)
        {
            if (b == null || mixer == null || b._outdoorMixerParams == null) return;
            if (Took.Add(b)) b.SetOutdoorMixerParams(1f, 0f, true);
            SetP(b, mixer, Audio.AmbientSubsystem.Data.EMixerParameterType.Volume, volDb);
            SetP(b, mixer, Audio.AmbientSubsystem.Data.EMixerParameterType.LowPassFreq, Mathf.Clamp(cutoff, b._minLowpassFreq, b._maxLowpassFreq));
            SetP(b, mixer, Audio.AmbientSubsystem.Data.EMixerParameterType.HighPassFreq, b._minHighPassFreq);
            SetP(b, mixer, Audio.AmbientSubsystem.Data.EMixerParameterType.ReverbSend, Mathf.Min(volDb, b._maxOutdoorReverbSendDb));
            SetP(b, mixer, Audio.AmbientSubsystem.Data.EMixerParameterType.DelaySend, Mathf.Min(volDb, b._maxOutdoorDelaySendDb));
        }

        private static void SetP(AmbientSoundBlender b, UnityEngine.Audio.AudioMixer mixer, Audio.AmbientSubsystem.Data.EMixerParameterType type, float v)
        {
            if (!b._outdoorMixerParams.TryGetParameters(type, out Audio.Data.AudioMixerParamsContainer c) || c == null) return;
            for (int i = 0; i < c.Count; i++) mixer.SetFloat(c[i], v);
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
                    BedDirection.Restore();
                    RestoreRain();
                    RoofRainFactor = 1f;
                    V2Active = false; Took.Clear(); V2Level = float.NaN;
                    return;
                }
                if (!_found && Time.time >= _nextFind) Find();
                float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
                bool v2 = Plugin.AmbienceV2On.Value && Plugin.AcousticProbeOn.Value && AcousticProbe.Valid;
                if (v2) V2Smooth(dt);
                else if (V2Active) { Took.Clear(); V2Level = float.NaN; }
                V2Active = v2;
                Transform lt2 = MonoBehaviourSingleton<Audio.SpatialSystem.SpatialAudioSystem>.Instantiated ? MonoBehaviourSingleton<Audio.SpatialSystem.SpatialAudioSystem>.Instance.ListenerTransform : null;

                // roof rain: full with only the roof above, 8b with one floor above, none with two or more. A new level must
                // hold 0.5 s (no flicker under beams), then it's applied smoothly and directly to the rain player's volume.
                int fl = OutdoorExposure.FloorsAbove;
                float want = fl <= 0 ? 1f : fl == 1 ? Mathf.Clamp01(Plugin.RoofRainUnderFloors.Value) : 0f;
                if (v2) want *= Mathf.Clamp01(_coverSm); // 0.6.0: a hole in the roof above you lets less rain hit "your" roof
                if (Mathf.Abs(want - _roofCandidate) > 0.01f) { _roofCandidate = want; _roofCandidateSince = Time.time; }
                if (Time.time - _roofCandidateSince >= 0.5f) RoofRainFactor = _roofCandidate;
                RoofRain(dt);

                if (v2)
                {
                    // probe v2: the beds themselves become directional; no copies, no pan/filter cue
                    OpeningSounds.Restore();
                    if (BedPan != 0f) foreach (Bed b in Beds) if (b.Src != null) b.Src.panStereo = 0f;
                    BedPan = 0f; BedCutoff = 22000f;
                    BedDirection.Tick(dt, lt2, true);
                    return;
                }
                BedDirection.Tick(dt, lt2, false);

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
            var lps = new List<AudioLowPassFilter>(); var own = new List<bool>();
            foreach (Bed b in Beds) { lps.Add(b.Lp); own.Add(b.OwnFilter); }
            BedDirection.Init(srcs, lps, own);
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
            $"AMBIENCE MIX (fix 8): outdoor beds found {BedsFound} sources, indoor rain players {IndoorRainFound}, opening sound voices max {OpeningSounds.MaxVoices}\n" +
            BedDirection.Summary();
    }
}
