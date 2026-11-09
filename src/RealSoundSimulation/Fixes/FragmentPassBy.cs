using System;
using System.Collections.Generic;
using RealSoundSimulation.Acoustics;
using RealSoundSimulation.Diagnostics;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using UnityEngine;

namespace RealSoundSimulation.Fixes
{
    // 6.7f (0.6.56): a grenade fragment passing you. User, 9 Nov 2026: "fragment noises should also probably follow fragment path", "what if 1 fragment comes out the door at me", "grenade fragments do make alot of noise though and cracks".
    // The game gives a whoosh only for shots from someone else whose gun isn't muffled to you, so never for your own grenades or a bot behind cover, and fragments never get a crack.
    //   path     each fragment's real flight is followed, so one out of a door is heard from that side and one that hit a wall makes none. A pass counts when the path's nearest point to you is within "6.7h" m with nothing solid between (one ray, only for those).
    //   sound    generated whizzes from high to a low bass, each pitched differently (soldiers' accounts: each fragment its own note, whiz to bass, screech, tearing), louder and brighter the closer it is; inside 1.5 m a short sharp crack too.
    //   limit    "6.7i" per grenade (1.5 s), the last place kept for a close one. Inside the concussion radius they go past that cut too (6.9h). They obey the echo voice cap, so the game's own sounds keep their voices.
    // Fragments are told by speed (90 to 100 m/s in the item data). That's slower than real ones, so a pass lands later than it would. Sound shapes and levels are by ear. The game's own whoosh is left alone: it also drives the screen effect.
    [DefaultExecutionOrder(32000)]
    internal class FragmentPassBy : MonoBehaviour
    {
        public const string Header = "t,played,reason,missM,crack,volume,pitch,cutoffHz,clip";

        private const float MaxFragmentSpeed = 150f, CrackM = 1.5f, WindowS = 1.5f, MinMissM = 0.4f, MinVolume = 0.002f, CrackDb = -3f;
        private class Entry { public int Index; public bool Done; }

        private static readonly Dictionary<Shot, Entry> Seen = new Dictionary<Shot, Entry>();
        private static readonly Dictionary<string, int> Skips = new Dictionary<string, int>();
        private static AudioClip[] _whiz;
        private static AudioClip _crack;
        private static float _windowEnd;
        private static int _windowCount;
        private static readonly System.Random Rng = new System.Random(4711);

        public static int Passes, Cracks;
        private static double _missSum;

        public static void Reset()
        {
            Seen.Clear(); Skips.Clear();
            _windowEnd = 0f; _windowCount = 0;
            Passes = Cracks = 0;
            _missSum = 0;
        }

        private static void BuildClips()
        {
            if (_crack != null && _whiz != null) return;
            _whiz = new[] { Whiz("rss_frag_high", 2600f, 1100f, 0.07f, 180f, 0.25f), Whiz("rss_frag_mid", 1500f, 600f, 0.1f, 110f, 0.35f), Whiz("rss_frag_low", 600f, 220f, 0.16f, 70f, 0.45f) };
            var noise = new System.Random(77);
            _crack = ShockWave.MakeClip("rss_frag_crack", 0.03f, t =>
            {
                float n = (float)(noise.NextDouble() * 2.0 - 1.0);
                float wave = t < 0.004f ? Mathf.Clamp01(t / 0.0002f) * (1f - 2f * t / 0.004f) : 0f;
                return wave + 0.3f * n * Mathf.Exp(-t / 0.008f);
            });
        }

        private static AudioClip Whiz(string name, float fromHz, float toHz, float decayS, float flutterHz, float noiseShare)
        {
            var noise = new System.Random(name.GetHashCode());
            float phase = 0f;
            return ShockWave.MakeClip(name, 0.35f, t =>
            {
                phase += 2f * Mathf.PI * (toHz + (fromHz - toHz) * Mathf.Exp(-t / 0.06f)) / 48000f;
                float flutter = 0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * flutterHz * t);
                float n = (float)(noise.NextDouble() * 2.0 - 1.0);
                return Mathf.Clamp01(t / 0.015f) * Mathf.Exp(-t / decayS) * flutter * (Mathf.Sin(phase) * (1f - noiseShare) + n * noiseShare);
            });
        }

        private void LateUpdate()
        {
            if (!Plugin.FragmentPassOn.Value || !Singleton<GameWorld>.Instantiated) { if (Seen.Count > 0) Seen.Clear(); return; }
            GameWorld w = Singleton<GameWorld>.Instance;
            IBallisticsCalculator shared = w.SharedBallisticsCalculator, client = w.ClientBallisticCalculator;
            int active = (shared != null ? shared.ActiveShotsCount : 0) + (client != null && !ReferenceEquals(client, shared) ? client.ActiveShotsCount : 0);
            if (active == 0) { if (Seen.Count > 0) Seen.Clear(); return; }
            Perf.Begin();
            try
            {
                Transform l = ThroughWall.Listener();
                if (l == null) return;
                if (shared != null) Scan(shared, l.position);
                if (client != null && !ReferenceEquals(client, shared)) Scan(client, l.position);
            }
            catch (Exception e) { DiagRunner.ReportOnce("6.7f fragment pass-bys", e); }
            finally { Perf.End(Perf.Part.BlastFromOpening); }
        }

        private static void Scan(IBallisticsCalculator c, Vector3 listener)
        {
            float reach = Plugin.FragmentPassReachM.Value;
            for (int i = 0; i < c.ActiveShotsCount; i++)
            {
                Shot s = c.GetActiveShot(i);
                if (s == null || !(s.Ammo is Ammo a) || a.InitialSpeed >= MaxFragmentSpeed) continue;
                List<Vector3> p = s.PositionHistory;
                int n = p.Count;
                if (n < 2) continue;
                if (!Seen.TryGetValue(s, out Entry e)) Seen[s] = e = new Entry();
                else if (e.Index >= n) { e.Index = 0; e.Done = false; } // the game reuses its shot objects
                for (int k = e.Index; k < n - 1 && !e.Done; k++)
                {
                    Vector3 a0 = p[k], seg = p[k + 1] - a0;
                    float len2 = seg.sqrMagnitude;
                    if (len2 < 1e-6f) continue;
                    float t = Vector3.Dot(listener - a0, seg) / len2;
                    if (t <= 0f || t >= 1f) continue;
                    Vector3 near = a0 + seg * t;
                    float miss = Vector3.Distance(listener, near);
                    if (miss > reach) continue;
                    e.Done = true;
                    Pass(listener, near, miss, reach);
                }
                e.Index = n - 1;
            }
        }

        private static void Pass(Vector3 listener, Vector3 near, float miss, float reach)
        {
            if (miss > 0.05f)
            {
                int full = AcousticProbe.FullMask;
                int mask = full != 0 ? full : LayerMask.GetMask("HighPolyCollider", "LowPolyCollider", "Terrain");
                if (CountedRay.Raycast(listener, (near - listener) / miss, miss, mask, QueryTriggerInteraction.Ignore)) { Skip("behind something", miss); return; }
            }
            float now = Time.time;
            if (now >= _windowEnd) { _windowEnd = now + WindowS; _windowCount = 0; }
            int budget = Plugin.FragmentPassCount.Value;
            bool close = miss <= CrackM;
            if (_windowCount >= budget || (!close && _windowCount >= budget - 1)) { Skip("over the limit", miss); return; }

            BuildClips();
            float db = Plugin.FragmentPassDb.Value - 6f * Mathf.Log(Mathf.Max(miss, MinMissM), 2f);
            float vol = Mathf.Clamp01(Mathf.Pow(10f, db / 20f)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(reach * 0.7f, reach, miss)));
            if (vol < MinVolume) { Skip("too quiet", miss); return; }

            bool set = ConcussionProof.RecentBlast && !ConcussionProof.Active;
            if (set) ConcussionProof.Set();
            try
            {
                AudioClip clip = _whiz[Rng.Next(_whiz.Length)];
                float pitch = 0.8f + (float)Rng.NextDouble() * 0.45f;
                float cutoff = Mathf.Lerp(12000f, 3000f, Mathf.InverseLerp(MinMissM, reach, miss));
                double start = AudioSettings.dspTime + 0.005;
                Vector3 at = miss > 0.05f ? near : listener + Vector3.forward;
                EchoVoices.Play(clip, clip, at, start, clip.length / pitch, false, vol, pitch, cutoff, 0f);
                if (EchoVoices.LastDropped) { Skip("no voice free", miss); return; }
                bool cracked = false;
                if (close)
                {
                    float cv = Mathf.Clamp01(Mathf.Pow(10f, (db + CrackDb) / 20f));
                    EchoVoices.Play(_crack, _crack, at, start, _crack.length, false, cv, 1f, 22000f, 0f);
                    cracked = !EchoVoices.LastDropped;
                    if (cracked) Cracks++;
                }
                _windowCount++;
                Passes++; _missSum += miss;
                if (DiagLog.IsOpen) Row("1", "", miss, cracked ? "1" : "0", vol, pitch, cutoff, clip.name);
            }
            finally { if (set) ConcussionProof.Clear(); }
        }

        private static void Skip(string why, float miss)
        {
            Skips.TryGetValue(why, out int n); Skips[why] = n + 1;
            if (DiagLog.IsOpen) Row("0", why, miss, "0", 0f, 0f, 0f, "");
        }

        private static void Row(string played, string why, float miss, string crack, float vol, float pitch, float cutoff, string clip)
        {
            DiagLog.Row("passby.csv", string.Join(",", DiagLog.F(Time.time - DiagRunner.RaidStartTime, "0.00"), played, why, DiagLog.F(miss, "0.00"), crack, DiagLog.F(vol, "0.000"), DiagLog.F(pitch, "0.00"), DiagLog.F(cutoff, "0"), clip));
        }

        public static string Summary()
        {
            var parts = new List<string>();
            foreach (KeyValuePair<string, int> kv in Skips) parts.Add($"{kv.Key} {kv.Value}");
            string state = Plugin.FragmentPassOn.Value ? $"ON, {Plugin.FragmentPassDb.Value:+0;-0;0} dB, within {Plugin.FragmentPassReachM.Value:0.0} m, {Plugin.FragmentPassCount.Value} per grenade" : "off";
            return $"FRAGMENT PASS-BYS (6.7f {state}): played {Passes}" + (Passes > 0 ? $", mean miss {_missSum / Passes:0.0} m" : "") + $", with a crack {Cracks}; not played: {(parts.Count > 0 ? string.Join(", ", parts) : "-")}  (passby.csv)\n";
        }
    }
}
