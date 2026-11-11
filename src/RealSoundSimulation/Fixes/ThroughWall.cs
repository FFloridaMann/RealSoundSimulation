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
    //            the same); hit from one side only, 0.2 m. Hits under 0.6 m apart are one wall, counted as the heaviest material among them.
    //            Low-detail colliders count only if wall-sized. Chain fences, grates and broken glass aren't walls.
    //   loss     per wall: the material's loss at 125 Hz (Materials) + 6 dB per doubling above 0.2 m, up to +12. Thick metal 15 dB, not the table's 6.
    //            Earth is 75 dB flat: soil, mud, gravel, grass, snow and terrain. Terrain has no bullet tag and no back side, so its thickness can't be read.
    //   reach    0.6.38 ("6.2e"): gunshot "6.2f", explosion "6.2g", suppressed gun 20 less. Distance + full wall loss use it up; none left, no thump.
    //            Fades over the last 6 dB. User, 9 Nov 2026: "not every gunshot and explosion should get through", then chose numbers set from physics
    //            (one concrete wall rarely stops a shot; several heavy walls or earth do).
    //   level    "6.2a" - distance (0 dB at 5 m or closer) - loss x "6.2d"; explosions +6 dB + "6.2h" (0.6.41), and "6.2i" with no wall in the way (0.6.42: user, "grenades should also have more thump to them (in general"); suppressed guns -10 dB
    //   voice    at most 4 at once, from fix 6's pool, past its headroom cap; full-auto: a 0.3 s slice at most every 0.25 s per shooter
    // Adds a voice; the game's own sound for the shot is left alone. No raid numbers behind the levels or reach yet: thump.csv records each decision.
    internal static class ThroughWall
    {
        private const float GunRangeM = 60f, ExplosionRangeM = 100f, RefM = 5f, MergeM = 0.6f, DefaultThickM = 0.2f, RefThickM = 0.2f, MaxThickDb = 12f,
            UntaggedDb = 20f, EarthDb = 75f, ThickMetalDb = 15f, SilencedReachDb = 20f, ReachFadeDb = 6f,
            MeasureEveryS = 0.25f, AutoGapS = 0.25f, AutoSliceS = 0.3f, MaxLengthS = 1.5f, ExplosionDb = 6f, SilencedDb = -10f, MinVolume = 0.002f,
            WallLongM = 4f, WallWideM = 2f, ExplosionFadeShare = 0.5f;
        private const int MaxCasts = 8, MaxAtOnce = 4;

        private static readonly AccessTools.FieldRef<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge> BridgeRef =
            AccessTools.FieldRefAccess<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge>("playersBridge");

        private struct Hit { public int Id; public MaterialType M; public float D, Db; public bool Tagged, Earth; }
        private class Measure { public float Time, LossDb, ThickM; public int Walls; public string Text = ""; }
        private class Shooter { public Measure M = new Measure { Time = -100f }; public float LastThump = -100f; }

        private static readonly Hit[] Forward = new Hit[MaxCasts], Back = new Hit[MaxCasts];
        private static readonly Dictionary<int, Shooter> Shooters = new Dictionary<int, Shooter>();
        private static readonly double[] Ends = new double[MaxAtOnce];
        private static readonly Dictionary<string, int> Skips = new Dictionary<string, int>();
        private static readonly StringBuilder Sb = new StringBuilder(64);
        private static readonly float[] Sides = new float[3];

        public static int PlayedGun, PlayedExplosion, AtOnceMax, NoRoute, StoppedGun, StoppedExplosion;
        private static double _lossSum;

        private const float ShockHeadStartM = 1.5f;
        public const string Header = "t,kind,played,reason,distM,walls,materials,thickM,lossDb,spentDb,reachDb,volume,cutoffHz,delayMs,hasRoute,clip";

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
            PlayedGun = PlayedExplosion = AtOnceMax = NoRoute = StoppedGun = StoppedExplosion = 0; _lossSum = 0;
        }

        // 0.6.45: the hideout has no spatial audio system (the game only creates it when loading a raid), so explosions fall back to the game's listener.
        // User, 9 Nov 2026: "can we make it work in the hide out?"
        internal static Transform Listener()
        {
            if (MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated)
            {
                Transform t = MonoBehaviourSingleton<SpatialAudioSystem>.Instance.ListenerTransform;
                if (t != null) return t;
            }
            if (MonoBehaviourSingleton<BetterAudio>.Instantiated && MonoBehaviourSingleton<BetterAudio>.Instance.ListenerTransform != null)
                return MonoBehaviourSingleton<BetterAudio>.Instance.ListenerTransform;
            return Camera.main != null ? Camera.main.transform : null;
        }

        // Before the probe has run (a hideout visit with no raid yet) its masks are empty.
        private static int FallbackMask => LayerMask.GetMask("HighPolyCollider", "LowPolyCollider", "Terrain");
        private static int FallbackBlock => LayerMask.GetMask("HighPolyCollider", "Terrain");

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
                float reach = Plugin.ThumpGunReach.Value - (__instance.IsSilenced ? SilencedReachDb : 0f);
                bool route = PortalPositioning.HasRoute(__instance);
                if (!route) NoRoute++;
                if (Thump("gun", __instance, clip, shotPosition, dist, s.M, Plugin.ThumpCutoffHz.Value, extra, reach, auto ? AutoSliceS : MaxLengthS, route ? "1" : "0")) s.LastThump = now;
            }
            catch (Exception e) { DiagRunner.ReportOnce("6.2 through-wall thump (gun)", e); }
            finally { Perf.End(Perf.Part.ThroughWall); }
        }

        private static void ExplosionPostfix(Vector3 position, SoundBank bank)
        {
            if (BlastRecording.InLayer || (!Plugin.ThumpOn.Value && !Plugin.ShockOn.Value) || bank == null || bank.SourceType != BetterAudio.AudioSourceGroupType.Grenades) return;
            Perf.Begin();
            try
            {
                Transform lt = Listener();
                if (lt == null) return;
                float dist = Vector3.Distance(lt.position, position);
                if (dist > ExplosionRangeM) return;
                var m = new Measure();
                MeasureWalls(lt.position, position, m);
                AudioClip clip = bank.PickSingleClip(0);
                float reach = Plugin.ThumpExplosionReach.Value;
                if (Plugin.ThumpOn.Value) Thump("explosion", bank, clip, position, dist, m, Plugin.ThumpExplosionCutoffHz.Value, ExplosionDb + Plugin.ThumpExplosionDb.Value + (m.Walls == 0 ? Plugin.ThumpOpenDb.Value : 0f), reach, Plugin.ThumpExplosionLengthS.Value, ""); // 0.6.43: "6.2j"
                float spent = 20f * Mathf.Log10(Mathf.Max(dist, RefM) / RefM) + m.LossDb;
                float wallFade = Plugin.ThumpWallsStop.Value ? Mathf.Clamp01((reach - spent) / ReachFadeDb) : 1f;
                ShockWave.Explosion(lt, position, dist, m.Walls, m.LossDb, wallFade, Mathf.Max(dist - ShockHeadStartM, 0f) / OwnEchoes.SpeedOfSound, clip); // 0.6.53: "6.9"
            }
            catch (Exception e) { DiagRunner.ReportOnce("6.2 through-wall thump (explosion)", e); }
            finally { Perf.End(Perf.Part.ThroughWall); }
        }

        private static bool Thump(string kind, object owner, AudioClip clip, Vector3 pos, float dist, Measure m, float cutoff, float extraDb, float reach, float maxLen, string route)
        {
            float distDb = 20f * Mathf.Log10(Mathf.Max(dist, RefM) / RefM), spent = distDb + m.LossDb;
            if (m.Walls == 0 && kind != "explosion") return Skip(kind, "no wall", dist, m, spent, reach, 0f, cutoff, 0f, route, clip); // 0.6.42: an explosion has its thump with no wall too
            float fade = 1f;
            if (Plugin.ThumpWallsStop.Value)
            {
                if (spent >= reach)
                {
                    if (kind == "gun") StoppedGun++; else StoppedExplosion++;
                    Row(kind, "0", "stopped by walls", dist, m, spent, reach, 0f, cutoff, 0f, route, clip);
                    return false;
                }
                fade = Mathf.Clamp01((reach - spent) / ReachFadeDb);
            }
            if (clip == null) return Skip(kind, "no clip", dist, m, spent, reach, 0f, cutoff, 0f, route, clip);
            float db = Plugin.ThumpDb.Value + extraDb - distDb - m.LossDb * Plugin.ThumpWallLoss.Value;
            float vol = Mathf.Clamp01(Mathf.Pow(10f, db / 20f)) * fade;
            // 0.6.51: a grenade's shock front only outruns sound for the first few metres, worth 1.1 to 2 m (3 to 6 ms) by 28 m (Kinney-Graham, 60 g to 180 g charges; at 28 m the front is under 1% faster than sound). User: "blasts waves move faster than sound".
            float delay = (kind == "explosion" ? Mathf.Max(dist - ShockHeadStartM, 0f) : dist) / OwnEchoes.SpeedOfSound;
            if (vol < MinVolume) return Skip(kind, "too quiet", dist, m, spent, reach, vol, cutoff, delay, route, clip);

            double now = AudioSettings.dspTime;
            int free = -1, busy = 0;
            for (int i = 0; i < MaxAtOnce; i++) { if (Ends[i] > now) busy++; else if (free < 0) free = i; }
            if (free < 0) return Skip(kind, "four playing", dist, m, spent, reach, vol, cutoff, delay, route, clip);

            double start = now + delay, length = Mathf.Min(clip.length, maxLen);
            // 0.6.44: an explosion's thump fades out over the second half of its length ("6.2j"), so a short setting doesn't cut off. User, 9 Nov 2026.
            bool felt = kind == "explosion" && ConcussionProof.WillCut(dist, ShockWave.PeekDose(pos, dist, m.Walls, m.LossDb, fade, Listener())); // 0.6.58: the thump survives the concussion cut ("6.9j")
            if (felt) { ConcussionProof.FeltOn(); cutoff = Plugin.FeltThumpCutoffHz.Value; } // 0.7.7: its own depth while concussed ("6.9p"); thump.csv logs the cutoff used
            try
            {
                EchoVoices.Play(owner, clip, pos, start, length, false, vol, 1f, cutoff, delay, minCutoff: 20f, ignoreCap: true, fadeS: kind == "explosion" ? (float)length * ExplosionFadeShare : 0f);
                // 0.6.53: a voice can't go over full volume, so a second one exactly in step takes a close explosion up to +6 dB ("6.9").
                float second = kind == "explosion" ? ShockWave.ThumpExtra(db, fade) : 0f;
                if (second > 0f) EchoVoices.Play(owner, clip, pos, start, length, false, second, 1f, cutoff, delay, minCutoff: 20f, ignoreCap: true, fadeS: (float)length * ExplosionFadeShare);
            }
            finally { if (felt) ConcussionProof.FeltOff(); }
            Ends[free] = start + length;
            if (busy + 1 > AtOnceMax) AtOnceMax = busy + 1;
            if (kind == "gun") PlayedGun++; else PlayedExplosion++;
            _lossSum += m.LossDb;
            Row(kind, "1", "", dist, m, spent, reach, vol, cutoff, delay, route, clip);
            return true;
        }

        private static bool Skip(string kind, string why, float dist, Measure m, float spent, float reach, float vol, float cutoff, float delay, string route, AudioClip clip)
        {
            Skips.TryGetValue(why, out int n); Skips[why] = n + 1;
            Row(kind, "0", why, dist, m, spent, reach, vol, cutoff, delay, route, clip);
            return false;
        }

        private static void Row(string kind, string played, string why, float dist, Measure m, float spent, float reach, float vol, float cutoff, float delay, string route, AudioClip clip)
        {
            if (!DiagLog.IsOpen) return;
            DiagLog.Row("thump.csv", string.Join(",", DiagLog.F(Time.time - DiagRunner.RaidStartTime, "0.00"), kind, played, why, DiagLog.F(dist, "0.0"), m.Walls.ToString(), m.Text,
                DiagLog.F(m.ThickM, "0.00"), DiagLog.F(m.LossDb, "0.0"), DiagLog.F(spent, "0.0"), DiagLog.F(reach, "0"), DiagLog.F(vol, "0.000"), DiagLog.F(cutoff, "0"), DiagLog.F(delay * 1000f, "0"), route, clip != null ? clip.name.Replace(',', ';') : ""));
        }

        private static void MeasureWalls(Vector3 listener, Vector3 source, Measure m)
        {
            m.Walls = 0; m.LossDb = 0f; m.ThickM = 0f; m.Text = "";
            int mask = AcousticProbe.FullMask;
            if (mask == 0) mask = FallbackMask;
            if (mask == 0) return;
            int nF = Cast(listener, source, Forward, mask), nB = Cast(source, listener, Back, mask);
            if (nF == 0) return;
            float len = Vector3.Distance(listener, source);
            Sb.Clear();
            int i = 0;
            while (i < nF)
            {
                // one wall: hits close together along the ray (a low-detail collider and its detailed surface are the same wall)
                float start = Forward[i].D, thick = 0f;
                Hit wall = Forward[i];
                bool matched = false;
                for (; i < nF && Forward[i].D - start < MergeM; i++)
                {
                    if (Forward[i].Tagged && (!wall.Tagged || Forward[i].Db > wall.Db)) wall = Forward[i];
                    // 0.6.38: the nearest far-side hit past this point. One mesh can hold several walls, and the first match could be the last wall.
                    float gap = float.MaxValue;
                    for (int k = 0; k < nB; k++)
                    {
                        if (Back[k].Id != Forward[i].Id) continue;
                        float g = len - Back[k].D - Forward[i].D;
                        if (g > 0f && g < gap) gap = g;
                    }
                    if (gap < float.MaxValue) { thick = Mathf.Max(thick, gap); matched = true; }
                }
                thick = matched ? Mathf.Clamp(thick, 0.02f, 1.5f) : DefaultThickM;
                m.LossDb += wall.Earth ? wall.Db : wall.Db + Mathf.Clamp(20f * Mathf.Log10(thick / RefThickM), 0f, MaxThickDb);
                m.ThickM += thick;
                m.Walls++;
                if (Sb.Length > 0) Sb.Append('+');
                Sb.Append(wall.Earth ? "earth" : wall.Tagged ? wall.M.ToString() : "untagged").Append('@').Append(thick.ToString("0.00"));
            }
            m.Text = Sb.ToString();
        }

        // One wall's loss before thickness. Earth isn't in the table's finite rows: soil and the like are "no transmission", forest ground reads as foliage (0 dB).
        private static float WallDb(MaterialType m)
        {
            switch (m)
            {
                case MaterialType.Soil: case MaterialType.SoilForest: case MaterialType.Mud: case MaterialType.Swamp: case MaterialType.Gravel:
                case MaterialType.Pebbles: case MaterialType.GrassLow: case MaterialType.GrassHigh: case MaterialType.Snow:
                    return EarthDb;
                case MaterialType.MetalThick: case MaterialType.MetalNoDecal:
                    return ThickMetalDb;
            }
            return m != MaterialType.None && Acoustics.Materials.TryGet(m, out Acoustics.Materials.Surface s) && !float.IsInfinity(s.T125) ? s.T125 : UntaggedDb;
        }

        private static int Cast(Vector3 from, Vector3 to, Hit[] into, int mask)
        {
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 0.1f) return 0;
            d /= len;
            int n = 0, block = AcousticProbe.BlockMask != 0 ? AcousticProbe.BlockMask : FallbackBlock;
            float t = 0f;
            for (int i = 0; i < MaxCasts && t < len; i++)
            {
                if (!CountedRay.Raycast(from + d * t, d, out RaycastHit h, len - t, mask, QueryTriggerInteraction.Ignore)) break;
                float at = t + h.distance;
                t = at + 0.05f;
                if (!IsWall(h.collider, block)) continue;
                MaterialType m = Surfaces.Material(h.collider);
                if (m == MaterialType.Chainfence || m == MaterialType.Grate || m == MaterialType.GlassShattered) continue; // see-through, not a wall
                float db = h.collider is TerrainCollider ? EarthDb : WallDb(m);
                into[n++] = new Hit { Id = h.collider.GetInstanceID(), M = m, D = at, Db = db, Earth = db == EarthDb, Tagged = m != MaterialType.None || db == EarthDb };
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
            string stop = Plugin.ThumpWallsStop.Value ? $"walls stop ON, reach {Plugin.ThumpGunReach.Value:0} gun, {Plugin.ThumpExplosionReach.Value:0} explosion" : "walls stop off";
            return $"THROUGH-WALL THUMP (6.2 {(Plugin.ThumpOn.Value ? "ON" : "off")}, {Plugin.ThumpCutoffHz.Value:0} Hz, explosions {Plugin.ThumpExplosionCutoffHz.Value:0} Hz, level {Plugin.ThumpDb.Value:+0;-0;0} dB, wall loss x{Plugin.ThumpWallLoss.Value:0.00}, {stop}): played {PlayedGun} gun, {PlayedExplosion} explosion" +
                   (played > 0 ? $", mean wall loss before 6.2d {_lossSum / played:0} dB, most at once {AtOnceMax}" : "") + $", gun shots with no route {NoRoute}; stopped by walls {StoppedGun} gun, {StoppedExplosion} explosion; not played otherwise: {(parts.Count > 0 ? string.Join(", ", parts) : "-")}  (thump.csv)\n";
        }
    }
}
