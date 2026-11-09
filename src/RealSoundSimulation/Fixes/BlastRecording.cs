using System;
using RealSoundSimulation.Acoustics;
using RealSoundSimulation.Diagnostics;
using EFT;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Audio;

namespace RealSoundSimulation.Fixes
{
    // 6.6 (0.6.49): which recording an explosion plays. User, 9 Nov 2026, raid_20261109_105838: a grenade at the mouth of a garage "sounded fully indoors but the
    // garage opens to the front to full open air"; "They can sound indoors partly but the front of the garage should have sounded outdoors".
    //
    // The game has an indoor and an outdoor set of recordings and picks one, all or nothing, by whether the blast point is inside one of its indoor boxes. Those
    // boxes aren't the room map, and a box can take in a garage's open front. Here the blast point itself is measured:
    //   open     48 rays over the upper half of the sphere from 0.6 m above the blast; the share that hit nothing within 30 m
    //   blend    all indoor at 0.08 or less, all outdoor at "6.6a" or more, a smooth blend between, equal power so the level holds
    //   voices   the stronger recording on the game's own voice; the weaker one on a second voice from the same game call, so distance, delay, mixer, occlusion, 6.4's
    //            move to the opening and its dulling are the same for both. Under -16 dB it isn't played. 6.2, 6.3 and 6.3e skip the second voice (InLayer).
    // "6.6b": the game fades from its close recording to its distant one between 10 and 30 m, which makes a blast 27 m away about 85% distant. The reach is widened
    // for this call only, because three banks share that table. 10 = the game's values. Beyond 200 m there is no indoor recording, so it's always outdoor.
    // No raid numbers behind the 0.08, 6.6a or 6.6b yet: blastrec.csv records each blast.
    internal static class BlastRecording
    {
        private const int RayCount = 48;
        internal const float AllIndoorBelow = 0.08f;
        private const float OpenM = 30f, LiftM = 0.6f, NoIndoorSetM = 200f, MinLayerGain = 0.158f; // -16 dB

        // 0.6.53 "6.6c": the rays above only look around the blast. User, 9 Nov 2026, standing beside a garage: "it still doesnt sound like its outdoor mix is right when its comes out the front of the entrance".
        // raid_20261109_114154: all 6 changed picks turned the game's Outdoor into Indoor with the player outside. When you're outside the sound reaches you through open air, so the outdoor share
        // can't fall below "6.6c", or half of it if the game also has the blast indoors. Outside = the enclosure probe under 0.45, fading out by 0.8; in that raid outside was 0.01 to 0.42 and inside 0.82 to 0.99.
        private const float OutsideBelow = 0.45f, InsideAbove = 0.8f, GameIndoorShare = 0.5f;
        private static float _enclosure, _floor;

        public const string Header = "t,x,y,z,distM,gameEnv,openShare,outdoorWeight,mainEnv,mainGain,layerGain,layerPlayed,closeReachM,listenerEnclosure,outdoorFloor";

        public static bool InLayer;
        private static Vector3[] _dirs;
        private static bool _job;
        private static float _orig, _open, _weight, _mainGain, _layerGain;
        private static EnvironmentType _gameEnv, _mainEnv;
        private static DistanceBlendOptions _opts;
        private static float[] _origValues;

        public static int Blasts, ChangedRecording, Layers, LayersMissed;
        private static double _openSum, _weightSum;

        public static void Apply(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(BetterAudio), nameof(BetterAudio.PlayAtPointDistant)),
                prefix: new HarmonyMethod(typeof(BlastRecording), nameof(Prefix)),
                postfix: new HarmonyMethod(typeof(BlastRecording), nameof(Postfix)),
                finalizer: new HarmonyMethod(typeof(BlastRecording), nameof(Finalizer)));
        }

        public static void Reset()
        {
            Blasts = ChangedRecording = Layers = LayersMissed = 0;
            _openSum = _weightSum = 0;
        }

        private static void Prefix(Vector3 position, SoundBank bank, float distance, ref float volume, ref EnvironmentType env)
        {
            _job = false;
            if (InLayer || !Plugin.BlastRecordingOn.Value || bank == null || bank.SourceType != BetterAudio.AudioSourceGroupType.Grenades || !bank.HasEnvironment) return;
            Perf.Begin();
            try
            {
                _gameEnv = env;
                _open = OpenShare(position);
                float high = Mathf.Max(Plugin.BlastOutdoorShare.Value, AllIndoorBelow + 0.01f);
                _weight = distance >= NoIndoorSetM ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(AllIndoorBelow, high, _open));
                _enclosure = EnclosureProbe.Valid && !float.IsNaN(EnclosureProbe.Enclosure) ? EnclosureProbe.Enclosure : 1f; // no reading: treated as inside, which leaves the blast's own measure alone
                _floor = Plugin.BlastOutsidePull.Value * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(OutsideBelow, InsideAbove, _enclosure))) * (_gameEnv == EnvironmentType.Outdoor ? 1f : GameIndoorShare);
                _weight = Mathf.Max(_weight, _floor);
                float outdoor = Mathf.Sin(_weight * Mathf.PI * 0.5f), indoor = Mathf.Cos(_weight * Mathf.PI * 0.5f);
                bool mainOutdoor = _weight >= 0.5f;
                _mainEnv = mainOutdoor ? EnvironmentType.Outdoor : EnvironmentType.Indoor;
                _mainGain = mainOutdoor ? outdoor : indoor;
                _layerGain = mainOutdoor ? indoor : outdoor;
                if (_layerGain < MinLayerGain) { _layerGain = 0f; _mainGain = 1f; }
                _orig = volume;
                volume *= _mainGain;
                env = _mainEnv;
                WidenCloseReach(bank);
                _job = true;
            }
            catch (Exception e) { DiagRunner.ReportOnce("6.6 blast recording", e); }
            finally { Perf.End(Perf.Part.BlastFromOpening); }
        }

        private static void WidenCloseReach(SoundBank bank)
        {
            float reach = Plugin.BlastCloseReachM.Value;
            DistanceBlendOptions o = bank.BlendOptions;
            if (o == null || o.Values == null || o.Values.Length != 4 || reach <= o.Values[0]) return;
            float mid = Mathf.Max(o.Values[1], reach * 2.4f), far = Mathf.Max(o.Values[2], mid);
            _origValues = o.Values;
            _opts = o;
            o.Values = new[] { reach, mid, far, Mathf.Max(o.Values[3], far) };
        }

        private static void Postfix(BetterAudio __instance, BetterSource __result, Vector3 position, SoundBank bank, float distance, float spatialBlend, EOcclusionTest occlusionTest, AudioMixerGroup forceGroup, bool needUpdate)
        {
            if (InLayer || !_job) return;
            _job = false;
            bool layered = false;
            if (__result != null && _layerGain > 0f)
            {
                EnvironmentType other = _mainEnv == EnvironmentType.Outdoor ? EnvironmentType.Indoor : EnvironmentType.Outdoor;
                InLayer = true;
                try { layered = __instance.PlayAtPointDistant(position, bank, distance, _orig * _layerGain, spatialBlend, other, occlusionTest, forceGroup, needUpdate) != null; }
                catch (Exception e) { DiagRunner.ReportOnce("6.6 blast recording (second voice)", e); }
                finally { InLayer = false; }
                if (layered) Layers++; else LayersMissed++;
            }
            if (__result == null) return;
            Blasts++;
            if (_mainEnv != _gameEnv) ChangedRecording++;
            _openSum += _open; _weightSum += _weight;
            if (!DiagLog.IsOpen) return;
            DiagLog.Row("blastrec.csv", string.Join(",", DiagLog.F(Time.time - DiagRunner.RaidStartTime, "0.00"), DiagLog.F(position.x, "0.0"), DiagLog.F(position.y, "0.0"), DiagLog.F(position.z, "0.0"), DiagLog.F(distance, "0.0"),
                _gameEnv.ToString(), DiagLog.F(_open, "0.00"), DiagLog.F(_weight, "0.00"), _mainEnv.ToString(), DiagLog.F(_mainGain, "0.00"), DiagLog.F(_layerGain, "0.00"), layered ? "1" : "0", DiagLog.F(Plugin.BlastCloseReachM.Value, "0"), DiagLog.F(_enclosure, "0.00"), DiagLog.F(_floor, "0.00")));
        }

        private static Exception Finalizer(Exception __exception)
        {
            if (!InLayer && _opts != null)
            {
                _opts.Values = _origValues;
                _opts = null; _origValues = null;
            }
            return __exception;
        }

        // 0.6.54: for 6.9. How much of the sky and horizon around a point is open, and the mean distance to what the other rays hit: the size of the room.
        public static float MeasureAt(Vector3 position, out float roomM)
        {
            if (_dirs == null) _dirs = BuildDirs();
            int full = AcousticProbe.FullMask;
            int mask = (full != 0 ? full : LayerMask.GetMask("HighPolyCollider", "LowPolyCollider", "Terrain")) | LayerMask.GetMask("TransparentCollider");
            Vector3 o = position + Vector3.up * LiftM;
            int open = 0;
            float sum = 0f;
            for (int i = 0; i < _dirs.Length; i++)
            {
                if (CountedRay.Raycast(o, _dirs[i], out RaycastHit hit, OpenM, mask, QueryTriggerInteraction.Ignore)) sum += hit.distance;
                else open++;
            }
            int hits = _dirs.Length - open;
            roomM = hits > 0 ? sum / hits : OpenM;
            return open / (float)_dirs.Length;
        }

        private static float OpenShare(Vector3 position)
        {
            if (_dirs == null) _dirs = BuildDirs();
            int full = AcousticProbe.FullMask;
            int mask = (full != 0 ? full : LayerMask.GetMask("HighPolyCollider", "LowPolyCollider", "Terrain")) | LayerMask.GetMask("TransparentCollider"); // intact windows count as closed
            Vector3 o = position + Vector3.up * LiftM;
            int open = 0;
            for (int i = 0; i < _dirs.Length; i++)
                if (!CountedRay.Raycast(o, _dirs[i], OpenM, mask, QueryTriggerInteraction.Ignore)) open++;
            return open / (float)_dirs.Length;
        }

        private static Vector3[] BuildDirs()
        {
            var d = new Vector3[RayCount];
            float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (int i = 0; i < RayCount; i++)
            {
                float y = (i + 0.5f) / RayCount, r = Mathf.Sqrt(1f - y * y), a = golden * i;
                d[i] = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
            }
            return d;
        }

        public static string Summary()
        {
            string state = Plugin.BlastRecordingOn.Value ? $"ON, all outdoor from {Plugin.BlastOutdoorShare.Value:0.00} open, close recording to {Plugin.BlastCloseReachM.Value:0} m, outside pull {Plugin.BlastOutsidePull.Value:0.00}" : "off";
            return $"BLAST RECORDING (6.6 {state}): {Blasts} blasts" + (Blasts > 0 ? $", mean open share {_openSum / Blasts:0.00}, mean outdoor weight {_weightSum / Blasts:0.00}" : "") +
                   $"; game's indoor/outdoor pick changed {ChangedRecording}; second recording played {Layers}, no voice for it {LayersMissed}  (blastrec.csv)\n";
        }
    }
}
