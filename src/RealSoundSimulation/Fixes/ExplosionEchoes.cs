using System;
using System.Linq;
using Audio.SpatialSystem;
using RealSoundSimulation.Acoustics;
using RealSoundSimulation.Diagnostics;
using EFT;
using EFT.Ballistics;
using HarmonyLib;
using UnityEngine;

namespace RealSoundSimulation.Fixes
{
    // 6.3, first half (0.6.39): echoes for explosions. User, 8 Nov 2026 (raid 35: three grenades in a stairwell got only the game's own sound).
    //
    //   find     25 rays from the blast (16 level, 8 at 35 degrees, 1 up), up to 150 m. Ground is ignored; the recording has it.
    //            A wall echoes only if the blast and you can both reach it: a mirror bounce if the true bounce point checks out, else a weaker scattered one.
    //   level    the gun echo rule (-20 log10(path / 2 m), the surface's loss, scattered -3 dB and x share) + 6 dB for an explosion + "6.3a"
    //   skip     paths under 3 m longer than the direct sound; they blur into the bang. Arrivals within 4 ms and 30 degrees merge.
    //   play     the loudest "6.3b", each cut to "6.3c" s, on fix 6's voices under its headroom cap (so on the gunshot mixer), at path / 343 m/s
    // One bounce only: a blast in another room with no wall you share gets no echoes; since 0.6.48 those get "6.3e", room echoes from the opening (BlastFromOpening).
    // Starting values, nothing measured: echoes.csv, sample "explosion".
    internal static class ExplosionEchoes
    {
        private const float RayM = 150f, BlastLiftM = 0.5f, MinExtraM = 3f, RefPath = 2f, ExplosionDb = 6f, ScatterDb = -3f, MinLevelDb = -48f, MaxLevelDb = -3f,
            MergeDelayS = 0.004f, MergeCos = 0.866f, PlaneCos = 0.9f, PlaneM = 0.75f, MirrorM = 0.6f, OffWallM = 0.05f;
        private const int RingRays = 16, UpperRays = 8, MaxChecks = 54, MaxEchoes = 12;

        private static readonly Vector3[] Dirs = BuildDirs();
        private static readonly OwnEchoes.Tap[] Cands = new OwnEchoes.Tap[Dirs.Length], Picks = new OwnEchoes.Tap[MaxEchoes];
        private static readonly Vector3[] PlaneN = new Vector3[Dirs.Length];
        private static readonly float[] PlaneH = new float[Dirs.Length];
        private static int _candN, _pickN;

        public static int Blasts, Silent, OutOfRange, NoClip, Played, Mirror, Scattered, Dropped;
        private static float _levelSum, _pathSum;

        public static void Apply(Harmony h)
        {
            var post = new HarmonyMethod(typeof(ExplosionEchoes), nameof(Postfix));
            h.Patch(typeof(BetterAudio).GetMethods().First(m => m.Name == "PlayAtPoint" && m.GetParameters().Length > 2 && m.GetParameters()[1].ParameterType == typeof(SoundBank) && m.GetParameters()[2].ParameterType == typeof(int)), postfix: post);
            h.Patch(AccessTools.Method(typeof(BetterAudio), nameof(BetterAudio.PlayAtPointDistant)), postfix: post);
        }

        public static void Reset()
        {
            Blasts = Silent = OutOfRange = NoClip = Played = Mirror = Scattered = Dropped = 0;
            _levelSum = _pathSum = 0f;
        }

        private static void Postfix(Vector3 position, SoundBank bank)
        {
            if (!Plugin.ExplosionEchoOn.Value || bank == null || bank.SourceType != BetterAudio.AudioSourceGroupType.Grenades) return;
            Perf.Begin();
            try
            {
                Transform lt = ThroughWall.Listener(); // 0.6.45: works in the hideout too
                if (lt == null) return;
                Blasts++;
                Vector3 l = lt.position, s = position + Vector3.up * BlastLiftM;
                if (Vector3.Distance(l, s) > Plugin.ExplosionEchoRangeM.Value) { OutOfRange++; return; }
                AudioClip clip = bank.PickSingleClip(0);
                if (clip == null) { NoClip++; return; }
                FindEchoes(s, l);
                Pick(l);
                if (_pickN == 0) { Silent++; LogNone(); return; }
                PlayPicks(bank, clip, l, lt.forward);
            }
            catch (Exception e) { DiagRunner.ReportOnce("6.3 explosion echoes", e); }
            finally { Perf.End(Perf.Part.EchoSearch); }
        }

        private static void FindEchoes(Vector3 s, Vector3 l)
        {
            _candN = 0;
            int mask = EnclosureProbe.CurrentMask(), planes = 0, checks = 0;
            float direct = Vector3.Distance(s, l);
            for (int i = 0; i < Dirs.Length && checks < MaxChecks; i++)
            {
                Vector3 dir = Dirs[i];
                if (!CountedRay.Raycast(s, dir, out RaycastHit hit, RayM, mask, QueryTriggerInteraction.Ignore) || EnclosureProbe.IsGround(hit.normal)) continue;
                Vector3 n = hit.normal, p = hit.point;
                float h = Vector3.Dot(s - p, n);
                bool dup = false;
                for (int k = 0; k < planes && !dup; k++) dup = Vector3.Dot(PlaneN[k], n) > PlaneCos && Mathf.Abs(PlaneH[k] - h) < PlaneM;
                if (dup) continue;
                PlaneN[planes] = n; PlaneH[planes] = h; planes++;

                bool mirror = false;
                if (MirrorPoint(s, l, p, n, out Vector3 q))
                {
                    float toQ = Vector3.Distance(s, q);
                    checks++;
                    if (toQ > 0.01f && CountedRay.Raycast(s, (q - s) / toQ, out RaycastHit mh, toQ + 1f, mask, QueryTriggerInteraction.Ignore)
                        && Mathf.Abs(mh.distance - toQ) < MirrorM && Vector3.Dot(mh.normal, n) > PlaneCos)
                    {
                        checks++;
                        if (ReachesYou(mh.point, n, l, mask))
                        {
                            mirror = true;
                            Add(OwnEchoes.Kind.Spec, mh.point, (q - s) / toQ, toQ + Vector3.Distance(mh.point, l), direct, Surfaces.Material(mh.collider), 0f, 0f);
                        }
                    }
                }
                if (mirror) continue;
                checks++;
                if (!ReachesYou(p, n, l, mask)) continue;
                float cos = Mathf.Max(0.05f, Vector3.Dot(-dir, n));
                Add(OwnEchoes.Kind.Diff, p, dir, hit.distance + Vector3.Distance(p, l), direct, Surfaces.Material(hit.collider), 10f * Mathf.Log10(0.25f * cos), ScatterDb);
            }
        }

        // Where a sound from s bounces off the plane (p, n) on its way to l. False if they aren't both on the wall's open side.
        private static bool MirrorPoint(Vector3 s, Vector3 l, Vector3 p, Vector3 n, out Vector3 q)
        {
            q = p;
            float hs = Vector3.Dot(s - p, n), hl = Vector3.Dot(l - p, n);
            if (hs <= 0f || hl <= OffWallM) return false;
            Vector3 sOn = s - n * hs, lOn = l - n * hl;
            q = sOn + (lOn - sOn) * (hs / (hs + hl));
            return true;
        }

        private static bool ReachesYou(Vector3 p, Vector3 n, Vector3 l, int mask)
        {
            Vector3 from = p + n * OffWallM, d = l - from;
            float len = d.magnitude;
            return len > 0.01f && !CountedRay.Raycast(from, d / len, out RaycastHit _, len, mask, QueryTriggerInteraction.Ignore);
        }

        private static void Add(OwnEchoes.Kind kind, Vector3 pos, Vector3 outDir, float path, float direct, MaterialType mat, float shareDb, float extraDb)
        {
            if (_candN >= Cands.Length || path - direct < MinExtraM) return;
            Acoustics.Materials.Echo(mat, out float matDb, out float matCut);
            float level = ExplosionDb - 20f * Mathf.Log10(Mathf.Max(path, RefPath) / RefPath) + matDb + shareDb + extraDb;
            Cands[_candN++] = new OwnEchoes.Tap
            {
                Pos = pos, OutDir = outDir, Path = path, LevelDb = level, DirDb = 0f, Mat = mat, Kind = kind,
                Cutoff = Mathf.Min(OwnEchoes.AirCutoff(path), matCut) * (kind == OwnEchoes.Kind.Spec ? 1f : 0.75f),
                Energy = Mathf.Pow(10f, level / 10f),
            };
        }

        private static void Pick(Vector3 l)
        {
            _pickN = 0;
            for (int i = 1; i < _candN; i++)
            {
                OwnEchoes.Tap x = Cands[i];
                int j = i - 1;
                while (j >= 0 && Cands[j].Energy < x.Energy) { Cands[j + 1] = Cands[j]; j--; }
                Cands[j + 1] = x;
            }
            int want = Mathf.Clamp(Plugin.ExplosionEchoCount.Value, 1, MaxEchoes);
            for (int c = 0; c < _candN; c++)
            {
                ref OwnEchoes.Tap cand = ref Cands[c];
                bool merged = false;
                for (int t = 0; t < _pickN && !merged; t++)
                {
                    if (Mathf.Abs(Picks[t].Path - cand.Path) / OwnEchoes.SpeedOfSound >= MergeDelayS
                        || Vector3.Dot((Picks[t].Pos - l).normalized, (cand.Pos - l).normalized) <= MergeCos) continue;
                    Picks[t].Energy += cand.Energy;
                    Picks[t].LevelDb = 10f * Mathf.Log10(Picks[t].Energy);
                    merged = true;
                }
                if (!merged && _pickN < want && cand.LevelDb >= MinLevelDb) Picks[_pickN++] = cand;
            }
        }

        private static void PlayPicks(object owner, AudioClip clip, Vector3 l, Vector3 fwd)
        {
            float trim = Plugin.ExplosionEchoDb.Value, length = Mathf.Min(clip.length, Plugin.ExplosionEchoLengthS.Value);
            double now = AudioSettings.dspTime;
            for (int i = 0; i < _pickN; i++)
            {
                ref OwnEchoes.Tap t = ref Picks[i];
                float delay = t.Path / OwnEchoes.SpeedOfSound;
                float lvl = Mathf.Min(t.LevelDb + trim, MaxLevelDb + Mathf.Max(0f, trim));
                float cutoff = t.Cutoff * OwnEchoes.HeadShadow(l, fwd, t.Pos);
                bool stole = EchoVoices.Play(owner, clip, t.Pos, now + delay, length, false, Mathf.Pow(10f, lvl / 20f), 1f, cutoff, delay);
                if (EchoVoices.LastDropped) { Dropped++; continue; }
                Played++;
                if (t.Kind == OwnEchoes.Kind.Spec) Mirror++; else Scattered++;
                _levelSum += lvl; _pathSum += t.Path;
                LogEcho(i, ref t, delay, lvl, cutoff, l, fwd, stole);
            }
        }

        private static void LogEcho(int i, ref OwnEchoes.Tap t, float delay, float lvl, float cutoff, Vector3 l, Vector3 fwd, bool stole)
        {
            if (!DiagLog.IsOpen) return;
            Vector3 a = t.Pos - l;
            Vector3 ah = new Vector3(a.x, 0f, a.z), fh = new Vector3(fwd.x, 0f, fwd.z);
            float az = ah.sqrMagnitude > 1e-4f && fh.sqrMagnitude > 1e-4f ? Vector3.SignedAngle(fh, ah, Vector3.up) : 0f;
            float el = a.sqrMagnitude > 1e-4f ? Mathf.Asin(Mathf.Clamp(a.normalized.y, -1f, 1f)) * Mathf.Rad2Deg : 0f;
            DiagLog.Row("echoes.csv", string.Join(",", DiagLog.F(Time.time - DiagRunner.RaidStartTime, "0.00"), "explosion", _pickN.ToString(), i.ToString(), t.Kind.ToString(), t.Mat.ToString(),
                DiagLog.F(t.Path, "0.0"), DiagLog.F(delay * 1000f, "0"), DiagLog.F(lvl, "0.0"), "0", DiagLog.F(cutoff, "0"), DiagLog.F(az, "0"), DiagLog.F(el, "0"), stole ? "1" : "0"));
        }

        private static void LogNone()
        {
            if (!DiagLog.IsOpen) return;
            DiagLog.Row("echoes.csv", string.Join(",", DiagLog.F(Time.time - DiagRunner.RaidStartTime, "0.00"), "explosion", "0", "", "none", "", "", "", "", "", "", "", "", ""));
        }

        private static Vector3[] BuildDirs()
        {
            var v = new Vector3[RingRays + UpperRays + 1];
            for (int i = 0; i < RingRays; i++) v[i] = Dir(i * 360f / RingRays, 3f);
            for (int i = 0; i < UpperRays; i++) v[RingRays + i] = Dir(i * 360f / UpperRays, 35f);
            v[RingRays + UpperRays] = Vector3.up;
            return v;
        }

        private static Vector3 Dir(float azDeg, float elDeg)
        {
            float az = azDeg * Mathf.Deg2Rad, el = elDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Sin(az) * Mathf.Cos(el));
        }

        public static string Summary()
        {
            string head = $"EXPLOSION ECHOES (6.3 {(Plugin.ExplosionEchoOn.Value ? "ON" : "off")}, level {Plugin.ExplosionEchoDb.Value:+0;-0;0} dB, up to {Plugin.ExplosionEchoCount.Value} per blast, {Plugin.ExplosionEchoLengthS.Value:0.0} s, range {Plugin.ExplosionEchoRangeM.Value:0} m): ";
            return head + $"explosions {Blasts}, echoes played {Played} (mirror {Mirror}, scattered {Scattered})" +
                   (Played > 0 ? $", mean level {_levelSum / Played:0.0} dB, mean path {_pathSum / Played:0.0} m" : "") +
                   $"; no usable walls {Silent}, out of range {OutOfRange}, no clip {NoClip}, dropped by the headroom cap {Dropped}  (echoes.csv, sample explosion)\n";
        }
    }
}
