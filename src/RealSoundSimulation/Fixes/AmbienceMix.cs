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
        private static bool _under2, _under2Candidate; private static float _under2Since; // 0.6.9: two floors above
        private const float Under2HoldS = 2f, Under2FadeS = 1.5f; // 0.6.28
        private static float _under2Gate = 1f, _roofMatGain = 1f, _noRainGate = 1f; // 0.6.31: 9r; 0.6.36: 9w

        public static bool On => Plugin.ExposureMixOn.Value && Plugin.DoorwayOn.Value;

        public static void Apply(Harmony h)
        {
            // 0.5.6: no patches; roof rain is applied to the rain player's sources directly (see RoofRain).
        }

        // the indoor rain player's sources: volume the game last wrote, and what we wrote back
        private static readonly List<AudioSource> RainSrc = new List<AudioSource>();
        private static float[] _rainGame = new float[0], _rainWritten = new float[0];
        private static bool _rainTracking;
        public static float RoofRainApplied = 1f, RoofRainComp = 1f;

        public static void Reset()
        {
            RestoreBeds();
            BedDirection.RestoreAll();
            OpeningSounds.Destroy();
            Took.Clear(); V2Active = false; V2Level = float.NaN;
            Beds.Clear(); IndoorRain.Clear();
            RestoreRain(); DestroyMirrors(); RainSrc.Clear();
            _found = false; _nextFind = 0f; _sceneSearched = false; _expSm = -1f; RoofRainApplied = 1f;
            _under2 = _under2Candidate = false; _under2Since = 0f; _under2Gate = 1f; _roofMatGain = 1f; _noRainGate = 1f; DoorwayBlend.ResetIndoorParams();
            RoomTone.Reset();
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
            float lv = Mathf.Max(AcousticProbe.Level, 1e-6f), cut = Mathf.Max(AcousticProbe.CutoffHz, 100f); // 0.6.8: down to -60 dB (was -40)
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

        // 0.6.9 "9l": deep inside, the outdoor sound never drops below a muffled floor - the building itself being hit by the
        // rain and wind, a diffuse low rumble (raid 28 tags 1 and 4: -40 dB and below, "not enough of the interior").
        // Added as energy, faded in by how enclosed you are; the muffling is the energy-weighted mix of the two.
        // Geometry-free: retune after the materials overhaul (walls of different materials pass different amounts).
        public const float FloorCutoffHz = 350f;
        // 0.6.14: the floor's share of what the outdoor beds play; that share comes from all round (raid 29 tag 7)
        public static float FloorShare(float level)
        {
            float db = Plugin.RainThroughBuildingDb.Value;
            if (db <= -59.5f || float.IsNaN(level)) return 0f;
            float fl = Mathf.Pow(10f, db / 10f) * Mathf.Clamp01(V2Indoor);
            return fl > 0f ? fl / (Mathf.Max(level, 0f) + fl) : 0f;
        }
        public static void ApplyFloor(ref float level, ref float cutoff)
        {
            float db = Plugin.RainThroughBuildingDb.Value;
            if (db <= -59.5f) return;
            float fl = Mathf.Pow(10f, db / 10f) * Mathf.Clamp01(V2Indoor);
            if (fl <= 0f) return;
            float tot = level + fl;
            cutoff = Mathf.Exp((level * Mathf.Log(Mathf.Max(cutoff, 100f)) + fl * Mathf.Log(FloorCutoffHz)) / tot);
            level = tot;
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
            // 0.9.3: no high-pass. The maps' blenders store min 1000 Hz / max 10 Hz (the game goes from 1000 Hz deep inside to
            // 10 Hz outside), so "_minHighPassFreq" cut everything below 1 kHz from outdoor wind, rain and ambience for the
            // whole raid, outdoors too (Streets raid_20261114_120507: AmbientOutHighpass 10 -> 1000 Hz at 20 s, never back).
            // Walls let the low end through, so the lower of the two (no cut) is used; the muffling is the low-pass above.
            SetP(b, mixer, Audio.AmbientSubsystem.Data.EMixerParameterType.HighPassFreq, Mathf.Min(b._minHighPassFreq, b._maxHighPassFreq));
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

        private static readonly AccessTools.FieldRef<PrecipitationAmbientBlender, float> RainMultRef =
            AccessTools.FieldRefAccess<PrecipitationAmbientBlender, float>("_globalsVolumeMult"); // 0.6.34: the game's factor for how hard it rains

        private static void RoofRain(float dt)
        {
            if (RainSrc.Count == 0) return;
            if (_rainGame.Length != RainSrc.Count) { _rainGame = new float[RainSrc.Count]; _rainWritten = new float[RainSrc.Count]; _rainTracking = false; }
            // 0.9.3: in over 0.5 s, out over 1.5 s (was 0.35 s both ways): a brief dip in the roof reading no longer pulls the rain down
            RoofRainApplied += (RoofRainFactor - RoofRainApplied) * (1f - Mathf.Exp(-dt / (RoofRainFactor > RoofRainApplied ? RoofAttackS : RoofReleaseS)));
            if (!_rainTracking) { for (int i = 0; i < RainSrc.Count; i++) { _rainGame[i] = RainSrc[i] != null ? RainSrc[i].volume : 0f; _rainWritten[i] = -1f; } _rainTracking = true; }
            // 0.6.9: if the indoor-layer fade (openings around you) also turns the rain player's mixer down, undo it here:
            // roof rain follows the roof only (raid 28 tags 2-3: 0.65 from the probe x 0.53-0.68 from the fade)
            bool one = Plugin.RoofRainOnePlayer.Value && Plugin.RoofRainOutdoorSpots.Value;
            RoofRainComp = !one && DoorwayBlend.IndoorParamsIncludeRain ? 1f / Mathf.Max(DoorwayBlend.IndoorApplied, 0.1f) : 1f;
            if (_rainWanted.Length != RainSrc.Count) _rainWanted = new float[RainSrc.Count];
            // 0.6.33 "9t": the game's indoor rain volume is baked per room (0.05 in much of the Capital, 0 in a trailer it thinks is outdoors).
            // 0.6.34: and 1 in others, so "at least 0.3" still jumped between rooms (raid 43 tag 4: 0.74 and 0.225 with the roof unchanged).
            // While it rains and the probe traces the roof, the room's value no longer counts: one level, times the game's own factor for how
            // hard it rains. The game's volumes only say how the level is shared between the two sources while they crossfade.
            bool ownLevel = Plugin.AmbienceV2On.Value && Plugin.RoofRainFromProbe.Value && AcousticProbe.Valid && RainController.IntensityType != RainController.ERainIntensity.None;
            float sum = 0f, level = 0f; int playing = 0;
            if (ownLevel)
            {
                for (int i = 0; i < RainSrc.Count; i++)
                {
                    AudioSource s = RainSrc[i];
                    if (s == null) continue;
                    if (Mathf.Abs(s.volume - _rainWritten[i]) > 1e-4f) _rainGame[i] = s.volume;
                    if (s.isPlaying && s.clip != null) { sum += _rainGame[i]; playing++; }
                }
                level = Plugin.RoofRainLevel.Value * (IndoorRain.Count > 0 && IndoorRain[0] != null ? Mathf.Clamp01(RainMultRef(IndoorRain[0])) : 1f);
                // 0.6.36 "9w" (raid 44 tags 1-2, the Capital): rays can't see storeys that have no colliders, but the game knows these rooms get no
                // rain: it marks you indoors with a rain value of 0. A trailer it thinks is outdoors isn't affected.
                bool noRainRoom = Plugin.RoofRainNoRainRooms.Value && playing > 0 && sum <= 0.01f && EnvProbe.GameSaysIndoor;
                _noRainGate = Mathf.MoveTowards(_noRainGate, noRainRoom ? 0f : 1f, dt / 1.5f);
                level *= _noRainGate;
            }
            for (int i = 0; i < RainSrc.Count; i++)
            {
                AudioSource s = RainSrc[i];
                if (s == null) continue;
                if (Mathf.Abs(s.volume - _rainWritten[i]) > 1e-4f) _rainGame[i] = s.volume; // the game changed it (crossfade)
                float baseVol = ownLevel && playing > 0 && s.isPlaying && s.clip != null ? level * (sum > 1e-3f ? _rainGame[i] / sum : 1f / playing) : _rainGame[i];
                float wanted = Mathf.Min(1f, baseVol * RoofRainApplied * RoofRainComp);
                _rainWanted[i] = s.isPlaying && s.clip != null ? wanted : 0f;
                // step 3: one player for all roof rain, the mod's copy; the game's own is held silent so the two never play at once
                // (doorways sounded phasey with both, and the hand-over at a door was heard as rain cutting in and out)
                _rainWritten[i] = one ? 0f : wanted;
                s.volume = _rainWritten[i];
            }
        }

        // 0.9.3 "roof rain in spots the map calls outdoor" (raid_20261115_160302 mark 5, a train car: roof directly overhead, roof rain
        // traced at full and the indoor rain player at 0.30, yet the user heard none; the same in containers in yards). The indoor rain
        // player plays into the game's indoor rain group, which the game's outdoor mix silences where its zone says outdoor. There, the
        // mod plays a copy of each rain source (same clip, kept in step, same level, position, spread and muffling) into the master
        // group, which the outdoor mix doesn't touch, with Unity's own panning. It fades in over 0.5 s as the
        // zone turns outdoor and out as it turns indoor, where the game's own player is heard again. No extra voice indoors.
        private static readonly List<AudioSource> Mirror = new List<AudioSource>();
        private static readonly List<AudioLowPassFilter> MirrorLp = new List<AudioLowPassFilter>(), SrcLp = new List<AudioLowPassFilter>();
        private static UnityEngine.Audio.AudioMixerGroup _outRoofGroup;
        public static float OutdoorRoofMix;
        public static float RoofNear, RoofNearShare;

        // 0.9.3 step 3 of the roof rain review ("Roof rain - one player, from the roof it hits"): the level comes from the roofs the probe's
        // paths actually hit, each with its own material (its tag, else the first tagged surface just above it) and its height, summed over
        // the probe's 3 s memory, so the material no longer flips per update (155 flips in 21.5 still minutes on 15 Nov) and a canopy's
        // metal counts from the first rays that reach it (no +11 dB step at its edge). Heavy roofs (concrete, stone) fade with
        // (1 - outdoorness)^2: silent under an overpass outdoors, a faint trace under a concrete roof indoors. "Roofs" below the ears
        // (undersides of heaps, trailers, trucks) don't count. Calibrated on Customs raid_20261115_183233:
        //   above  sqrt of the material-weighted upward level, cut below a roof level of 1% (traces from nearby rooftops in the open)
        //   side   roofs 1 m+ above the ears reached by other paths: nothing below a side level of 0.02 (open ground p90 0.022, indoors
        //          under a floor p90 0.020), half level (-6 dB) at 0.06 (the canopy edge), times the side roofs' own material
        private const float UpKnee = 0.01f, SideKnee = 0.02f, SideK = 6.25f;
        private static float StepThreeFactor()
        {
            float o = DoorwayBlend.Active && !float.IsNaN(DoorwayBlend.Outdoorness) ? Mathf.Clamp01(DoorwayBlend.Outdoorness) : (EnvProbe.GameSaysOutdoor ? 1f : 0f);
            float h2 = (1f - o) * (1f - o);
            float rl = AcousticProbe.RoofLevel;
            float u = Mathf.Max(0f, AcousticProbe.RoofUpLight + AcousticProbe.RoofUpHeavy * h2);
            float up = rl > UpKnee ? Mathf.Sqrt(u * (1f - UpKnee / rl)) : 0f;
            float side = 0f, sl = AcousticProbe.RoofSideLevel;
            if (Plugin.RoofRainNear.Value && sl > SideKnee)
            {
                float gSide = Mathf.Sqrt(Mathf.Max(0f, AcousticProbe.RoofSideLight + AcousticProbe.RoofSideHeavy * h2) / sl);
                side = gSide * Mathf.Min(NearMax, Mathf.Sqrt(SideK * (sl - SideKnee)));
            }
            RoofNear = side;
            RoofNearShare = side > up ? Mathf.Clamp01((side - up) / Mathf.Max(side, 1e-4f)) : 0f;
            return Mathf.Max(up, side);
        }

        // 0.9.3 "wide when the roof is overhead": how one-sided the roof is, 0 (all around and above you) .. 1 (off to one side).
        // Rain on a roof right over you comes from the whole roof, so it plays mostly flat and wide at full level; only a roof to one
        // side (walking up to it, near its edge, a car partly open to the sky, a clear direction) is placed in 3D. Fully 3D through
        // the spatializer cost the stereo recording much of its level (raid_20261115_175337: a metal container at the volume cap,
        // "still too quiet"). Off = the 3D share follows how enclosed you are, as before.
        internal static float RoofSidedness()
        {
            if (!Plugin.RoofRainWideOverhead.Value) return 1f;
            float s = Mathf.Max(RoofNearShare, Mathf.InverseLerp(0.35f, 0.8f, AcousticProbe.RoofFocus));
            if (AcousticProbe.RoofLevel > 0.05f) s = Mathf.Max(s, 0.6f * Mathf.InverseLerp(0.1f, 0.5f, AcousticProbe.SkyOpen));
            return Mathf.Clamp01(s);
        } // 0.9.3: roof rain from a roof beside you (0..NearMax), and how much of what plays it is
        private const float NearFrom = 0.01f, NearTo = 0.06f, NearMax = 0.5f;
        public static EFT.Ballistics.MaterialType RoofMatNow = EFT.Ballistics.MaterialType.None;
        private static AudioListener _listener;
        private const float RoofAttackS = 0.5f, RoofReleaseS = 1.5f, CopyTurnS = 1f, CopyStopAfterS = 5f;
        private static Vector3 _copyDir = Vector3.up;
        private static float _copyBlend, _copySilentSince = -1f, _copyCut = 22000f;
        private const float CopyDistM = 3f;
        private const float OutdoorSnapshotRainDb = -80f; // assumed: the outdoor snapshot's level for the indoor rain group
        public static long OutdoorRoofFrames, OutdoorRoofOnFrames;

        private static void OutdoorRoofRain(float dt)
        {
            // 0.9.3: the copy fills in what the game's own player has lost, following fix 3's snapshot fade rather than the on/off zone
            // (raid_20261115_165446: silent in doorways, outdoorness 0.29-0.51, the zone already indoor so the copy was off). Snapshot
            // weights lerp the indoor rain group's level in dB, so at outdoorness o the game's rain is at about 10^(o*SnapDb/20) and
            // the copy adds the rest of the power.
            float o = DoorwayBlend.Active && !float.IsNaN(DoorwayBlend.Outdoorness) ? Mathf.Clamp01(DoorwayBlend.Outdoorness) : (EnvProbe.GameSaysOutdoor ? 1f : 0f);
            float gIn = Mathf.Pow(10f, o * OutdoorSnapshotRainDb / 20f);
            bool one = Plugin.RoofRainOnePlayer.Value;
            float want = Plugin.RoofRainOutdoorSpots.Value && RainSrc.Count > 0 ? (one ? 1f : Mathf.Sqrt(Mathf.Max(0f, 1f - gIn * gIn))) : 0f;
            OutdoorRoofMix += (want - OutdoorRoofMix) * (1f - Mathf.Exp(-dt / 0.1f));
            if (want <= 0f && OutdoorRoofMix < 0.01f) OutdoorRoofMix = 0f;
            OutdoorRoofFrames++;
            if (OutdoorRoofMix <= 0f)
            {
                // 0.9.3: stop only after 5 s of silence (it started and stopped 2-42 times a raid)
                if (_copySilentSince < 0f) _copySilentSince = Time.time;
                foreach (AudioSource mm in Mirror) if (mm != null && mm.isPlaying) { mm.volume = 0f; if (Time.time - _copySilentSince > CopyStopAfterS) mm.Stop(); }
                return;
            }
            _copySilentSince = -1f;
            OutdoorRoofOnFrames++;
            EnsureMirrors();
            // 0.9.3: the copy is placed by the roof tracing itself (RoofArrival, CopyDistM away), always 3D with the spatializer, spread
            // wider the less focused the roof is. Before, it followed the game's source, which is often flat (2D, 477 m away in
            // raid_20261115_171426), so it sounded stereo and from nowhere. Under a concrete or stone roof the map calls outdoor (an
            // overpass) there's no roof rain: a heavy deck doesn't drum, you hear the rain around you.
            if (_listener == null) _listener = UnityEngine.Object.FindObjectOfType<AudioListener>();
            Vector3 head = _listener != null ? _listener.transform.position : Vector3.zero;
            Vector3 dir = AcousticProbe.RoofArrival.sqrMagnitude > 1e-6f ? AcousticProbe.RoofArrival.normalized : Vector3.up;
            if (RoofNearShare > 0f)
            {
                // near a roof the paths reach it from below (arrival elevation -30 to -77 deg): use its side, 30 deg up. Step 3 takes the
                // side from the side roofs' own arrival.
                Vector3 sideSrc = one && AcousticProbe.RoofSideArrival.sqrMagnitude > 1e-6f ? AcousticProbe.RoofSideArrival : dir;
                Vector3 flat = new Vector3(sideSrc.x, 0f, sideSrc.z);
                Vector3 side = flat.sqrMagnitude > 1e-6f ? (flat.normalized * 0.866f + Vector3.up * 0.5f).normalized : Vector3.up;
                dir = Vector3.Slerp(dir.y > 0f ? dir : Vector3.up, side, RoofNearShare).normalized;
            }
            // 0.9.3: the direction and the 3D share turn over about 1 s (they were raw every frame; the roof bed already slerps)
            float ka = 1f - Mathf.Exp(-dt / CopyTurnS);
            _copyDir = Vector3.Slerp(_copyDir, dir, ka).normalized;
            dir = _copyDir;
            // raid_20261115_172816: fully 3D with a flat rolloff and the spatializer's defaults, the copy was "much weaker" at the same volume
            // (the spatializer's own distance handling). Now the same recipe as the roof rain bed (BedDirection): part flat, part from the
            // roof (the bed's Blend, else the roof focus), logarithmic rolloff from 50 m (no loss at 3 m), and with the Meta mode a
            // volumetric radius, no reflections and no reverb send.
            float blend = Plugin.RoofRainWideOverhead.Value ? RoofSidedness()
                : (BedDirection.Roof.Blend > 0f ? BedDirection.Roof.Blend : Mathf.Clamp01(AcousticProbe.RoofFocus * 2f));
            bool meta = !Plugin.RoofRainWideOverhead.Value && Plugin.RoofBedMode.Value == BedDirection.Mode.MetaHrtf; // Unity's panner keeps the level
            _copyBlend += (blend - _copyBlend) * (1f - Mathf.Exp(-dt / CopyTurnS));
            float radius = Mathf.Lerp(3f, 0.3f, AcousticProbe.RoofFocus);
            float heavy = !one && Acoustics.Materials.IsHeavyRoof(RoofMatNow) && EnvProbe.GameSaysOutdoor ? 0f : 1f; // step 3: the mapping fades heavy roofs
            _copyCut = Mathf.Exp(Mathf.Log(_copyCut) + (Mathf.Log(Mathf.Clamp(AcousticProbe.RoofCutoffHz, 300f, 22000f)) - Mathf.Log(_copyCut)) * (1f - Mathf.Exp(-dt / Smooth)));
            for (int i = 0; i < RainSrc.Count && i < Mirror.Count; i++)
            {
                AudioSource s = RainSrc[i], m = Mirror[i];
                if (s == null || m == null) continue;
                float lvl = one ? (i < _rainWanted.Length ? _rainWanted[i] : 0f) : s.volume;
                if (!s.isPlaying || s.clip == null || lvl <= 0f) { if (m.isPlaying) m.volume = 0f; continue; }
                if (m.clip != s.clip || !m.isPlaying)
                {
                    m.clip = s.clip;
                    m.timeSamples = Mathf.Clamp(s.timeSamples, 0, Mathf.Max(0, s.clip.samples - 1));
                    m.Play();
                }
                m.volume = lvl * OutdoorRoofMix * heavy;
                m.transform.position = head + dir * CopyDistM;
                m.spatialBlend = _copyBlend; m.spread = 0f;
                if (m.spatialize != meta) m.spatialize = meta;
                if (meta) { m.SetSpatializerFloat(4, radius); m.SetSpatializerFloat(5, 1f); m.SetSpatializerFloat(11, -60f); }
                AudioLowPassFilter sl = SrcLp[i], ml = MirrorLp[i];
                if (ml != null && one) { ml.enabled = _copyCut < 21500f; ml.cutoffFrequency = _copyCut; } // the roof's own muffling
                else if (ml != null)
                {
                    bool on = sl != null && sl.enabled;
                    ml.enabled = on;
                    if (on) ml.cutoffFrequency = sl.cutoffFrequency;
                }
            }
        }

        private static void EnsureMirrors()
        {
            if (_outRoofGroup == null && MonoBehaviourSingleton<BetterAudio>.Instantiated)
            {
                // raid_20261115_164015: on CommonAmbOutBypass the copy was heard in the part of a train car open to the sky (outdoor level
                // -4.4 dB) but not under its full roof (-10 dB), at the same volume: that group follows the outdoor mix. The master group
                // (where the mod's ear ring and heartbeat play) follows only the overall volume.
                // 0.9.3: the master group sits above the concussion cut, the world low-pass and the headset, so roof rain on it stayed
                // loud through a blast. The game's standard world group (VeryStandartMixerGroup) is under those and isn't silenced by
                // the outdoor mix. Master only if it is missing.
                BetterAudio ba = MonoBehaviourSingleton<BetterAudio>.Instance;
                _outRoofGroup = ba.VeryStandartMixerGroup != null ? ba.VeryStandartMixerGroup : ba.MasterMixerGroup;
                Plugin.Log.LogInfo("roof rain in outdoor spots: plays into " + (_outRoofGroup != null ? _outRoofGroup.name : "none"));
            }
            while (Mirror.Count < RainSrc.Count) { Mirror.Add(null); MirrorLp.Add(null); SrcLp.Add(null); }
            for (int i = 0; i < RainSrc.Count; i++)
            {
                AudioSource s = RainSrc[i];
                if (s == null || Mirror[i] != null) continue;
                var go = new GameObject("RealSoundSimulation outdoor roof rain " + i);
                UnityEngine.Object.DontDestroyOnLoad(go); // placed each frame by the roof tracing, not by the game's source
                AudioSource m = go.AddComponent<AudioSource>();
                m.playOnAwake = false; m.loop = true; m.dopplerLevel = 0f; m.priority = s.priority;
                m.spatialize = false; m.spatialBlend = 0f; m.rolloffMode = AudioRolloffMode.Logarithmic; m.minDistance = 50f; m.maxDistance = 500f; // no loss at 3 m
                m.bypassReverbZones = s.bypassReverbZones; m.reverbZoneMix = s.reverbZoneMix;
                m.outputAudioMixerGroup = _outRoofGroup;
                AudioLowPassFilter lp = go.AddComponent<AudioLowPassFilter>(); lp.lowpassResonanceQ = 1f; lp.enabled = false;
                Mirror[i] = m; MirrorLp[i] = lp; SrcLp[i] = s.GetComponent<AudioLowPassFilter>();
            }
        }

        private static void StopMirrors()
        {
            OutdoorRoofMix = 0f;
            foreach (AudioSource m in Mirror) if (m != null && m.isPlaying) m.Stop();
        }

        private static void DestroyMirrors()
        {
            foreach (AudioSource m in Mirror) if (m != null) UnityEngine.Object.Destroy(m.gameObject);
            Mirror.Clear(); MirrorLp.Clear(); SrcLp.Clear(); OutdoorRoofMix = 0f; _outRoofGroup = null; _listener = null;
        }

        // 0.6.8: the game's indoor rain player, every 0.25 s (raid 27: roof rain went silent at car dealer spots while our
        // factor held at 0.55). rain2.csv.
        private static float _nextRainRow;
        private static float[] _rainWanted = new float[0]; // 0.9.3 step 3: the roof rain level each source should play at
        public const string RainHeader = "t,source,clip,channels,playing,virtual,mute,gameVolume,ourFactor,volume,spatialBlend,spread,spatialize,mixer,fadeComp,lowpassHz,noRainGate,outdoorCopy";
        private static void LogRain()
        {
            if (!DiagLog.IsOpen || Time.time < _nextRainRow) return;
            _nextRainRow = Time.time + 0.25f;
            float t = Time.time - DiagRunner.RaidStartTime;
            for (int i = 0; i < RainSrc.Count; i++)
            {
                AudioSource s = RainSrc[i];
                if (s == null) continue;
                DiagLog.Row("rain2.csv", string.Join(",", DiagLog.F(t, "0.00"), i.ToString(), s.clip != null ? s.clip.name : "", s.clip != null ? s.clip.channels.ToString() : "",
                    s.isPlaying ? "1" : "0", s.isVirtual ? "1" : "0", s.mute ? "1" : "0", i < _rainGame.Length ? DiagLog.F(_rainGame[i]) : "", DiagLog.F(RoofRainApplied),
                    DiagLog.F(s.volume), DiagLog.F(s.spatialBlend), DiagLog.F(s.spread, "0"), s.spatialize ? "1" : "0",
                    s.outputAudioMixerGroup != null ? s.outputAudioMixerGroup.name : "", DiagLog.F(RoofRainComp),
                    LpOf(s), DiagLog.F(_noRainGate), DiagLog.F(i < Mirror.Count && Mirror[i] != null && Mirror[i].isPlaying ? Mirror[i].volume : 0f, "0.000")));
            }
        }

        private static string LpOf(AudioSource s)
        {
            AudioLowPassFilter lp = s.GetComponent<AudioLowPassFilter>();
            return lp != null && lp.enabled ? DiagLog.F(lp.cutoffFrequency, "0") : "off";
        }

        // 0.6.9: the game's own volume for an indoor rain source (before our roof factor), for fix 7's volume ramps
        public static float RainGameVolume(AudioSource s)
        {
            if (_rainTracking)
                for (int i = 0; i < RainSrc.Count && i < _rainGame.Length; i++)
                    if (RainSrc[i] == s) return Mathf.Abs(s.volume - _rainWritten[i]) < 1e-4f ? _rainGame[i] : s.volume;
            return s.volume;
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
                    BedDirection.RestoreAll();
                    RestoreRain();
                    StopMirrors();
                    RoomToneBlend.Off();
                    RoomTone.Off();
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
                bool roofV2 = v2 && Plugin.RoofRainFromProbe.Value;
                if (roofV2)
                {
                    // 0.6.7 ("9i"): traced from the roof (AcousticProbe.RoofLevel, energy, already smooth) -> volume (amplitude).
                    // Raid 26: the one-ray floor count flipped 0-1-0 at the car dealer's mezzanine edge (roof rain 30-100-30%).
                    // 0.6.9: with two or more floors above (the exposure probe's 5 up-rays, held 0.5 s) there is no roof rain,
                    // whatever the probe's roof test says (raid 28 tag 4: 0.68 under two floors). One floor above stays the
                    // probe's call (the mezzanine edge flipped 0-1-0 in raid 26).
                    bool u2 = !Plugin.RoofCountsFloors.Value && OutdoorExposure.AboveValid && OutdoorExposure.FloorsAbove >= 2; // 0.6.16: the up-rays only; 0.6.29: 9p replaces this rule
                    if (u2 != _under2Candidate) { _under2Candidate = u2; _under2Since = Time.time; }
                    // 0.6.28: raid 40 (Capital, escalators): the 5-ray count flickered and snapped roof rain off and on 85 times in
                    // the raid, even standing still. Now it must hold 2 s (was 0.5) and the change fades over 1.5 s.
                    if (Time.time - _under2Since >= Under2HoldS) _under2 = _under2Candidate;
                    _under2Gate = Mathf.MoveTowards(_under2Gate, _under2 ? 0f : 1f, dt / Under2FadeS);
                    // 0.9.3: probe v2 read the concrete overpass as untagged (RoofMat None, gain 1) where the exposure probe saw Concrete
                    // (raid_20261115_171426 mark 2), so the untagged case falls back to it; and the stretched gain can't go below 0.
                    RoofMatNow = AcousticProbe.RoofMat != EFT.Ballistics.MaterialType.None ? AcousticProbe.RoofMat : OutdoorExposure.RoofMat;
                    float matGain = Mathf.Max(0f, Mathf.LerpUnclamped(1f, Acoustics.Materials.RoofRainGain(RoofMatNow), Plugin.RoofRainByMaterial.Value));
                    _roofMatGain += (matGain - _roofMatGain) * (1f - Mathf.Exp(-dt / 0.5f)); // 0.6.31 "9r"
                    // 0.9.3 "near a roof": in the open beside a canopy or overpass the probe's rays already reach its underside (the
                    // all-paths roof level 0.03-0.05 a few metres from the edge, raid_20261115_172816 at 156-162 s) while the overhead
                    // level stays near 0. That share brings roof rain in as you approach, up to half level (-6 dB) at the edge, from the
                    // roof's side. Under the roof the overhead level takes over. Only with the sky mostly open around you.
                    if (Plugin.RoofRainOnePlayer.Value) RoofRainFactor = _under2Gate * StepThreeFactor();
                    else
                    {
                        float over = Mathf.Sqrt(Mathf.Clamp01(AcousticProbe.RoofLevel));
                        RoofNear = Plugin.RoofRainNear.Value && AcousticProbe.SkyOpen > 0.3f ? NearMax * Mathf.InverseLerp(NearFrom, NearTo, AcousticProbe.RoofLevelAll) : 0f;
                        RoofNearShare = RoofNear > over ? Mathf.Clamp01((RoofNear - over) / Mathf.Max(RoofNear, 1e-4f)) : 0f;
                        RoofRainFactor = _under2Gate * Mathf.Max(over, RoofNear) * _roofMatGain;
                    }
                    _roofCandidate = RoofRainFactor; _roofCandidateSince = Time.time;
                }
                else
                {
                    int fl = OutdoorExposure.FloorsAbove;
                    float want = fl <= 0 ? 1f : fl == 1 ? Mathf.Clamp01(Plugin.RoofRainUnderFloors.Value) : 0f;
                    if (v2) want *= Mathf.Clamp01(_coverSm); // 0.6.0: a hole in the roof above you lets less rain hit "your" roof
                    if (Mathf.Abs(want - _roofCandidate) > 0.01f) { _roofCandidate = want; _roofCandidateSince = Time.time; }
                    if (Time.time - _roofCandidateSince >= 0.5f) RoofRainFactor = _roofCandidate;
                }
                RoofRain(dt);
                try { OutdoorRoofRain(dt); } // 0.9.3
                catch (Exception e) { DiagRunner.ReportOnce("roof rain in outdoor spots", e); }
                LogRain();
                try { RoomToneBlend.Tick(dt); } // 0.9.3 "8h"
                catch (Exception e) { DiagRunner.ReportOnce("8h room tone blend", e); }
                RoomTone.Tick(dt);

                if (v2)
                {
                    // probe v2: the beds themselves become directional; no copies, no pan/filter cue
                    OpeningSounds.Restore();
                    if (BedPan != 0f) foreach (Bed b in Beds) if (b.Src != null) b.Src.panStereo = 0f;
                    BedPan = 0f; BedCutoff = 22000f;
                    BedDirection.Outdoor.Tick(dt, lt2, true, AcousticProbe.Arrival, AcousticProbe.Focus * (1f - FloorShare(V2Level)), AcousticProbe.Enclosed);
                    BedDirection.Roof.Tick(dt, lt2, roofV2, AcousticProbe.RoofArrival, AcousticProbe.RoofFocus, AcousticProbe.Enclosed * RoofSidedness(), AcousticProbe.RoofCutoffHz);
                    return;
                }
                BedDirection.Outdoor.Tick(dt, lt2, false, Vector3.zero, 0f, 0f);
                BedDirection.Roof.Tick(dt, lt2, false, Vector3.zero, 0f, 0f);

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

        private static bool SpatialAudioSystem_InRaid() => Raid.IsOn;

        private static void Find()
        {
            long t0 = StartupTiming.Now();
            try { FindInner(); }
            finally { StartupTiming.Add("first use", _sceneSearched ? "ambience sounds search (+ scene)" : "ambience sounds search", StartupTiming.Now() - t0); } // 0.9.3
        }

        private static void FindInner()
        {
            _nextFind = Time.time + 5f;
            var seen = new HashSet<AudioSource>();
            // 0.9.2: the game only sets up the ambience blenders under its AmbientAudioSystem, so look there every 5 s and search
            // the whole scene at most once per raid. Before, a map with no ambience at all got three whole-scene searches every 5 s.
            Collect(false, seen);
            if (Beds.Count == 0 && IndoorRain.Count == 0 && !_sceneSearched)
            {
                _sceneSearched = true;
                Collect(true, seen);
                Plugin.Log.LogInfo(Beds.Count > 0 || IndoorRain.Count > 0
                    ? "fix 8: ambience sounds found by a scene search (not under the ambient audio system)"
                    : "fix 8: no ambience sounds found under the ambient audio system or in the scene; looking under it every 5 s");
            }
            _found = Beds.Count > 0 || IndoorRain.Count > 0;
            BedsFound = Beds.Count; IndoorRainFound = IndoorRain.Count;
            var srcs = new List<AudioSource>();
            foreach (Bed b in Beds) srcs.Add(b.Src);
            if (srcs.Count > 0) OpeningSounds.Init(srcs);
            var lps = new List<AudioLowPassFilter>(); var own = new List<bool>();
            foreach (Bed b in Beds) { lps.Add(b.Lp); own.Add(b.OwnFilter); }
            BedDirection.Outdoor.Init(srcs, lps, own);
            // 0.6.10: the roof rain gets its own low-pass too (path muffling), added only where the game has none
            var rLps = new List<AudioLowPassFilter>(); var rOwn = new List<bool>();
            foreach (AudioSource s in RainSrc)
            {
                AudioLowPassFilter lp = s != null ? s.GetComponent<AudioLowPassFilter>() : null;
                bool mine = false;
                if (s != null && lp == null) { lp = s.gameObject.AddComponent<AudioLowPassFilter>(); lp.lowpassResonanceQ = 1f; lp.cutoffFrequency = 22000f; lp.enabled = false; mine = true; }
                rLps.Add(lp); rOwn.Add(mine);
            }
            BedDirection.Roof.Init(new List<AudioSource>(RainSrc), rLps, rOwn);
        }

        public static int BedsFound, IndoorRainFound;
        private static bool _sceneSearched;

        private static void Collect(bool scene, HashSet<AudioSource> seen)
        {
            foreach (WindAmbientBlender w in Search<WindAmbientBlender>(scene)) { AddBed(w._windSource, seen); AddBed(w._windMixSource, seen); }
            foreach (DayTimeAmbientBlender d in Search<DayTimeAmbientBlender>(scene)) { AddBed(d._outdoorAmbientDaySource, seen); AddBed(d._outdoorAmbientNightSource, seen); }
            foreach (PrecipitationAmbientBlender p in Search<PrecipitationAmbientBlender>(scene))
            {
                if (p._environment == EnvironmentType.Outdoor) { AddBed(p._precipitationSource, seen); AddBed(p._precipitationMixSource, seen); }
                else if (!IndoorRain.Contains(p))
                {
                    IndoorRain.Add(p);
                    if (p._precipitationSource != null) RainSrc.Add(p._precipitationSource);
                    if (p._precipitationMixSource != null) RainSrc.Add(p._precipitationMixSource);
                }
            }
        }

        private static T[] Search<T>(bool scene) where T : Component
        {
            if (scene) return FindObjectsOfType<T>();
            if (!MonoBehaviourSingleton<AmbientAudioSystem>.Instantiated) return Array.Empty<T>();
            AmbientAudioSystem a = MonoBehaviourSingleton<AmbientAudioSystem>.Instance;
            return a != null ? a.GetComponentsInChildren<T>() : Array.Empty<T>();
        }

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
            $"  indoor-layer mixer volumes: {(DoorwayBlend.ReportNames.Length > 0 ? DoorwayBlend.ReportNames : "not read")}; rain among them {(DoorwayBlend.ReportRain ? "yes (roof rain compensated)" : "no")}\n" +
            RoomTone.Summary() +
            BedDirection.Outdoor.Summary() + BedDirection.Roof.Summary();
    }
}
