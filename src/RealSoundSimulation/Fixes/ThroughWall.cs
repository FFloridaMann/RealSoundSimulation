using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Audio.SpatialSystem;
using RealSoundSimulation.Acoustics;
using RealSoundSimulation.Diagnostics;
using EFT;
using EFT.Ballistics;
using HarmonyLib;
using UnityEngine;

namespace RealSoundSimulation.Fixes
{
    // 6.2 (0.6.37): a shot or explosion on the other side of a wall also reaches you through the wall, as a deep thump from its true direction.
    // The clear sound still comes from the opening (6.1). User, 8 Nov 2026: both paths at once, and a low-pass around 50 Hz.
    //
    //   who      bot gunshots within 60 m in another room (not the same room, not both outdoors); explosions within 100 m with a wall between
    //   wall     rays from you to the source and back. A collider hit from both sides gives its thickness (the game's own transmission check does
    //            the same); hit from one side only, 0.2 m. Hits under 0.6 m apart are one wall. Low-detail colliders count only if wall-sized.
    //   loss     per wall: the material's loss at 125 Hz (Materials) + 6 dB per doubling above 0.2 m, up to +12; x "6.2d"
    //   level    "6.2a" - distance (0 dB at 5 m or closer) - loss; explosions +6 dB, suppressed guns -10 dB
    //   voice    at most 4 at once, from fix 6's pool, past its headroom cap; full-auto: a 0.3 s slice at most every 0.25 s per shooter
    // Adds a voice; the game's own sound for the shot is left alone. No raid numbers behind the levels yet: thump.csv records each decision.
    internal static class ThroughWall
    {
        private const float GunRangeM = 60f, ExplosionRangeM = 100f, RefM = 5f, MergeM = 0.6f, DefaultThickM = 0.2f, RefThickM = 0.2f, MaxThickDb = 12f,
            UntaggedDb = 20f, MeasureEveryS = 0.25f, AutoGapS = 0.25f, AutoSliceS = 0.3f, MaxLengthS = 1.5f, ExplosionDb = 6f, SilencedDb = -10f, MinVolume = 0.002f,
            WallLongM = 4f, WallWideM = 2f;
        private const int MaxCasts = 8, MaxAtOnce = 4;

        private static readonly AccessTools.FieldRef<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge> BridgeRef =
            AccessTools.FieldRefAccess<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge>("playersBridge");

        private struct Hit { public int Id; public Vector3 P; public MaterialType M; public float D; public bool Tagged; }
        private class Measure { public float Time, LossDb, ThickM; public int Walls; public string Text = ""; }
        private class Shooter { public Measure M = new Measure { Time = -100f }; public float LastThump = -100f; }

        private static readonly Hit[] Forward = new Hit[MaxCasts], Back = new Hit[MaxCasts];
        private static readonly Dictionary<int, Shooter> Shooters = new Dictionary<int, Shooter>();
        private static readonly double[] Ends = new double[MaxAtOnce];
        private static readonly Dictionary<string, int> Skips = new Dictionary<string, int>();
        private static readonly StringBuilder Sb = new StringBuilder(64);
        private static readonly float[] Sides = new float[3];

        public static int PlayedGun, PlayedExplosion, AtOnceMax, NoRoute;
        private static double _lossSum;

        public const string Header = "t,kind,played,reason,distM,walls,materials,thickM,lossDb,volume,cutoffHz,delayMs,hasRoute,clip";

        public static void Apply(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(WeaponSoundPlayer), nameof(WeaponSoundPlayer.FireBullet)), postfix: new HarmonyMethod(typeof(ThroughWall), nameof(FirePostfix)));
            var post = new HarmonyMethod(typeof(ThroughWall), nameof(ExplosionPostfix));
            h.Patch(typeof(BetterAudio).GetMethods().First(m => m.Name == "PlayAtPoint" && m.GetParameters().Length > 2 && m.GetParameters()[1].ParameterType == typeof(SoundBank) && m.GetParameters()[2].ParameterType == typeof(int)), postfix: post);
            h.Patch(AccessTools.Method(typeof(BetterAudio), nameof(BetterAudio.PlayAtPointDistant)), postfix: post);
        }

        public static void Reset()
        {
            Shooters.Clear(); Skips.Clear(); Array.Clear(Ends, 0, Ends.Length);
            PlayedGun = PlayedExplosion = AtOnceMax = NoRoute = 0; _lossSum = 0;
        }

        private static void FirePostfix(WeaponSoundPlayer __instance, Vector3 shotPosition)
        {
            if (!Plugin.ThumpOn.Value) return;
            Perf.Begin();
            try
            {
                BaseSoundPlayer.IObserverToPlayerBridge b = BridgeRef(__instance);
                if (b == null || b.iPlayer == null || b.PointOfView == EPointOfView.FirstPerson) return;
                if (!MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated) return;
                SpatialAudioSystem sas = MonoBehaviourSingleton<SpatialAudioSystem>.Instance;
                Transform lt = sas.ListenerTransform;
                if (lt == null) return;
                Vector3 l = lt.position;
                float dist = Vector3.Distance(l, shotPosition);
                if (dist > GunRangeM) return;
                if (!sas.TryGetPlayerAudioContainer(b.iPlayer.Id, out AudioSourceContainer c) || c == null) return;
                ISpatialAudioRoom room = c.CurrentAudioRoom;
                if (room == null || !room.IsValid || sas.IsSourceAndListenerInCommonOutdoor(room) || sas.IsSourceInListenerRoom(c)) return;

                int key = __instance.GetInstanceID();
                if (!Shooters.TryGetValue(key, out Shooter s)) Shooters[key] = s = new Shooter();
                bool auto = !__instance.Non_auto;
                float now = Time.time;
                if (auto && now - s.LastThump < AutoGapS) return;
                if (now - s.M.Time >= MeasureEveryS) { MeasureWalls(l, shotPosition, s.M); s.M.Time = now; }

                SoundBank bank = __instance.IsSilenced && __instance.BodySilenced != null ? __instance.BodySilenced : __instance.Body;
                AudioClip clip = bank != null ? bank.PickSingleClip(0) : null;
                float extra = __instance.IsSilenced ? SilencedDb : 0f;
                bool route = PortalPositioning.HasRoute(__instance);
                if (!route) NoRoute++;
                if (Thump("gun", __instance, clip, shotPosition, dist, s.M, Plugin.ThumpCutoffHz.Value, extra, auto ? AutoSliceS : MaxLengthS, route ? "1" : "0")) s.LastThump = now;
            }
            catch (Exception e) { DiagRunner.ReportOnce("6.2 through-wall thump (gun)", e); }
            finally { Perf.End(Perf.Part.ThroughWall); }
        }

        private static void ExplosionPostfix(Vector3 position, SoundBank bank)
        {
            if (!Plugin.ThumpOn.Value || bank == null || bank.SourceType != BetterAudio.AudioSourceGroupType.Grenades) return;
            Perf.Begin();
            try
            {
                if (!MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated) return;
                Transform lt = MonoBehaviourSingleton<SpatialAudioSystem>.Instance.ListenerTransform;
                if (lt == null) return;
                float dist = Vector3.Distance(lt.position, position);
                if (dist > ExplosionRangeM) return;
                var m = new Measure();
                MeasureWalls(lt.position, position, m);
                Thump("explosion", bank, bank.PickSingleClip(0), position, dist, m, Plugin.ThumpExplosionCutoffHz.Value, ExplosionDb, MaxLengthS, "");
            }
            catch (Exception e) { DiagRunner.ReportOnce("6.2 through-wall thump (explosion)", e); }
            finally { Perf.End(Perf.Part.ThroughWall); }
        }

        private static bool Thump(string kind, object owner, AudioClip clip, Vector3 pos, float dist, Measure m, float cutoff, float extraDb, float maxLen, string route)
        {
            if (m.Walls == 0) return Skip(kind, "no wall", dist, m, 0f, cutoff, 0f, route, clip);
            if (clip == null) return Skip(kind, "no clip", dist, m, 0f, cutoff, 0f, route, clip);
            float db = Plugin.ThumpDb.Value + extraDb - 20f * Mathf.Log10(Mathf.Max(dist, RefM) / RefM) - m.LossDb * Plugin.ThumpWallLoss.Value;
            float vol = Mathf.Clamp01(Mathf.Pow(10f, db / 20f));
            float delay = dist / OwnEchoes.SpeedOfSound;
            if (vol < MinVolume) return Skip(kind, "too quiet", dist, m, vol, cutoff, delay, route, clip);

            double now = AudioSettings.dspTime;
            int free = -1, busy = 0;
            for (int i = 0; i < MaxAtOnce; i++) { if (Ends[i] > now) busy++; else if (free < 0) free = i; }
            if (free < 0) return Skip(kind, "four playing", dist, m, vol, cutoff, delay, route, clip);

            double start = now + delay, length = Mathf.Min(clip.length, maxLen);
            EchoVoices.Play(owner, clip, pos, start, length, false, vol, 1f, cutoff, delay, minCutoff: 20f, ignoreCap: true);
            Ends[free] = start + length;
            if (busy + 1 > AtOnceMax) AtOnceMax = busy + 1;
            if (kind == "gun") PlayedGun++; else PlayedExplosion++;
            _lossSum += m.LossDb;
            Row(kind, "1", "", dist, m, vol, cutoff, delay, route, clip);
            return true;
        }

        private static bool Skip(string kind, string why, float dist, Measure m, float vol, float cutoff, float delay, string route, AudioClip clip)
        {
            Skips.TryGetValue(why, out int n); Skips[why] = n + 1;
            Row(kind, "0", why, dist, m, vol, cutoff, delay, route, clip);
            return false;
        }

        private static void Row(string kind, string played, string why, float dist, Measure m, float vol, float cutoff, float delay, string route, AudioClip clip)
        {
            if (!DiagLog.IsOpen) return;
            DiagLog.Row("thump.csv", string.Join(",", DiagLog.F(Time.time - DiagRunner.RaidStartTime, "0.00"), kind, played, why, DiagLog.F(dist, "0.0"), m.Walls.ToString(), m.Text,
                DiagLog.F(m.ThickM, "0.00"), DiagLog.F(m.LossDb, "0.0"), DiagLog.F(vol, "0.000"), DiagLog.F(cutoff, "0"), DiagLog.F(delay * 1000f, "0"), route, clip != null ? clip.name.Replace(',', ';') : ""));
        }

        private static void MeasureWalls(Vector3 listener, Vector3 source, Measure m)
        {
            m.Walls = 0; m.LossDb = 0f; m.ThickM = 0f; m.Text = "";
            int mask = AcousticProbe.FullMask;
            if (mask == 0) return;
            int nF = Cast(listener, source, Forward, mask), nB = Cast(source, listener, Back, mask);
            if (nF == 0) return;
            Sb.Clear();
            int i = 0;
            while (i < nF)
            {
                // one wall: hits close together along the ray (a low-detail collider and its detailed surface are the same wall)
                float start = Forward[i].D, thick = 0f;
                MaterialType mat = Forward[i].M;
                bool tagged = Forward[i].Tagged, matched = false;
                for (; i < nF && Forward[i].D - start < MergeM; i++)
                {
                    if (!tagged && Forward[i].Tagged) { mat = Forward[i].M; tagged = true; }
                    for (int k = 0; k < nB; k++)
                        if (Back[k].Id == Forward[i].Id) { thick = Mathf.Max(thick, Vector3.Distance(Forward[i].P, Back[k].P)); matched = true; break; }
                }
                thick = matched ? Mathf.Clamp(thick, 0.02f, 1.5f) : DefaultThickM;
                float baseDb = tagged && Acoustics.Materials.TryGet(mat, out Acoustics.Materials.Surface s) && !float.IsInfinity(s.T125) ? s.T125 : UntaggedDb;
                m.LossDb += baseDb + Mathf.Clamp(20f * Mathf.Log10(thick / RefThickM), 0f, MaxThickDb);
                m.ThickM += thick;
                m.Walls++;
                if (Sb.Length > 0) Sb.Append('+');
                Sb.Append(tagged ? mat.ToString() : "untagged").Append('@').Append(thick.ToString("0.00"));
            }
            m.Text = Sb.ToString();
        }

        private static int Cast(Vector3 from, Vector3 to, Hit[] into, int mask)
        {
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 0.1f) return 0;
            d /= len;
            int n = 0, block = AcousticProbe.BlockMask;
            float t = 0f;
            for (int i = 0; i < MaxCasts && t < len; i++)
            {
                if (!CountedRay.Raycast(from + d * t, d, out RaycastHit h, len - t, mask, QueryTriggerInteraction.Ignore)) break;
                float at = t + h.distance;
                t = at + 0.05f;
                if (!IsWall(h.collider, block)) continue;
                MaterialType m = Surfaces.Material(h.collider);
                into[n++] = new Hit { Id = h.collider.GetInstanceID(), P = h.point, M = m, D = at, Tagged = m != MaterialType.None };
            }
            return n;
        }

        // Solid layers always; low-detail colliders only if wall-sized, so cars and props don't count
        private static bool IsWall(Collider c, int block)
        {
            if ((block & (1 << c.gameObject.layer)) != 0) return true;
            Vector3 s = c.bounds.size;
            Sides[0] = s.x; Sides[1] = s.y; Sides[2] = s.z;
            Array.Sort(Sides);
            return Sides[2] >= WallLongM && Sides[1] >= WallWideM;
        }

        public static string Summary()
        {
            var parts = new List<string>();
            foreach (var kv in Skips) parts.Add($"{kv.Key} {kv.Value}");
            int played = PlayedGun + PlayedExplosion;
            return $"THROUGH-WALL THUMP (6.2 {(Plugin.ThumpOn.Value ? "ON" : "off")}, {Plugin.ThumpCutoffHz.Value:0} Hz, explosions {Plugin.ThumpExplosionCutoffHz.Value:0} Hz, level {Plugin.ThumpDb.Value:+0;-0;0} dB, wall loss x{Plugin.ThumpWallLoss.Value:0.00}): played {PlayedGun} gun, {PlayedExplosion} explosion" +
                   (played > 0 ? $", mean wall loss before 6.2d {_lossSum / played:0} dB, most at once {AtOnceMax}" : "") + $", gun shots with no route {NoRoute}; not played: {(parts.Count > 0 ? string.Join(", ", parts) : "-")}  (thump.csv)\n";
        }
    }
}
