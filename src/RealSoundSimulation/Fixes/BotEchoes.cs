using System;
using System.Collections.Generic;
using RealSoundSimulation.Acoustics;
using RealSoundSimulation.Diagnostics;
using EFT;
using EFT.Ballistics;
using HarmonyLib;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace RealSoundSimulation.Fixes
{
    // 6.3 (0.6.61): echoes for bot gunshots, under the voice priority system in EchoVoices. User, 9 Nov 2026: prioritise your own gun "without allowing the cracks to show due to voice limits".
    //
    //   who      any gun that isn't in first person, so another human player would count too
    //   when     one set per shooter at most every 0.25 s. A shot or the end of a burst gets the full set; a full-auto burst that runs past 0.2 s gets its loudest echo, looping, until it stops
    //   where    the same fan of 25 rays as explosion echoes, on the worker threads, from the gun; when a wall is in the way and a route is known, from the opening nearest you
    //            A wall echoes only if the source and you can both reach it. Reused for 1.5 s while gun and listener stay put, so a burst costs one search
    //   level    the gun echo rule (-20 log10(path / 2 m), surface loss, scattered -3 dB and x share) over the whole path, 3 dB per opening, + "6.3i"; from the gun also louder forward ("6c")
    //   tone     the outdoor close recording of the gun, the driest there is; air and surface as usual, plus the route's dulling
    //   voices   tier = the echo's place in its set. A shooter keeps the echo count it started its burst with, so echoes don't flicker from round to round
    // Searches take two frames, so a shooter's first shot can start a few ms late if it's very close. Starting values, nothing measured.
    [DefaultExecutionOrder(32001)]
    internal class BotEchoes : MonoBehaviour
    {
        public const string Header = "t,shooter,sample,distM,viaOpening,answer,walls,wanted,played,dropped,replaced,gated,botVoices,allowed,topDb";
        public const string VoicesHeader = "t,gameSources,allowed,echoVoices,own,bot,other";

        private const int SearchN = 6, MaxPlanes = 16, MaxTaps = 6, MaxCmds = 80;
        private const float RayM = 100f, MinHitM = 0.2f, MinExtraM = 3f, RefPath = 2f, ScatterDb = -3f, MinLevelDb = -60f, MaxLevelDb = -3f, MergeDelayS = 0.004f, MergeCos = 0.866f,
            PlaneCos = 0.9f, PlaneM = 0.75f, MirrorM = 0.6f, OffWallM = 0.05f, EveryS = 0.25f, BurstGapS = 1f, CacheS = 1.5f, StaleS = 4f, SameFromM = 1.5f, SameListenerM = 1f,
            GiveUpS = 0.2f, RouteLossDb = 3f, LogEveryS = 0.5f, FadeShare = 0.5f;
        private const double LoopHoldS = 0.2, SoonestS = 0.008;

        private enum Sample : byte { Shot, Loop, Tail }

        private sealed class Search
        {
            public int Serial, Phase; // 0 free, 1 fan, 2 checks, 3 done
            public Vector3 S, L, Aim;
            public float BaseM;
            public bool Directional;
            public int Mask, Planes;
            public JobHandle H;
            public NativeArray<RaycastCommand> C;
            public NativeArray<RaycastHit> R;
            public readonly Vector3[] P = new Vector3[MaxPlanes], N = new Vector3[MaxPlanes], Q = new Vector3[MaxPlanes], D = new Vector3[MaxPlanes];
            public readonly float[] ToQ = new float[MaxPlanes], Dist = new float[MaxPlanes], H0 = new float[MaxPlanes];
            public readonly int[] MirrorCmd = new int[MaxPlanes], MirrorReach = new int[MaxPlanes], DiffReach = new int[MaxPlanes];
            public readonly Collider[] Col = new Collider[MaxPlanes];
        }

        private sealed class Shooter
        {
            public WeaponSoundPlayer W;
            public int Key, SampleSerial, BurstTaps, N;
            public float LastSet = -100f, LastSample = -100f, FoundAt = -100f;
            public bool HasAnswer, OpenLoop, LastWasLoop;
            public Vector3 From, L;
            public Search Job;
            public int JobSerial;
            public readonly OwnEchoes.Tap[] T = new OwnEchoes.Tap[MaxTaps];
        }

        private struct Req
        {
            public Shooter S;
            public AudioClip Clip;
            public double Start;
            public float Vol, Pitch, Asked;
            public Sample Kind;
            public int Serial;
            public Vector3 Aim;
        }

        private static readonly AccessTools.FieldRef<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge> BridgeRef =
            AccessTools.FieldRefAccess<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge>("playersBridge");
        private static readonly AccessTools.FieldRef<WeaponSoundPlayer, bool> FiringRef = AccessTools.FieldRefAccess<WeaponSoundPlayer, bool>("_isFiring");

        private static readonly Search[] Pool = new Search[SearchN];
        private static readonly List<Shooter> Shooters = new List<Shooter>();
        private static readonly Dictionary<int, Shooter> ByKey = new Dictionary<int, Shooter>();
        private static readonly List<Req> Reqs = new List<Req>();
        private static readonly OwnEchoes.Tap[] Cands = new OwnEchoes.Tap[MaxPlanes], Picks = new OwnEchoes.Tap[MaxTaps];
        private static int _candN, _pickN;
        private static float _nextLog;

        private static WeaponSoundPlayer _cur;
        private static bool _curTail;
        private static AudioClip _curClip;
        private static Vector3 _curAim;

        public static int Samples, Sets, Played, Dropped, Replaced, Gated, Throttled, OutOfRange, NoWalls, SearchBusy, Searches, Reused, LoopsSkipped;
        private static float _levelSum;

        private static bool On => Plugin.BotEchoOn.Value && Plugin.EchoOn.Value;

        public static void Apply(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(WeaponSoundPlayer), nameof(WeaponSoundPlayer.FireBullet)),
                prefix: new HarmonyMethod(typeof(BotEchoes), nameof(FirePrefix)),
                finalizer: new HarmonyMethod(typeof(BotEchoes), nameof(ClearFinalizer)));
            h.Patch(AccessTools.Method(typeof(WeaponSoundPlayer), nameof(WeaponSoundPlayer.StopFiringLoop)),
                prefix: new HarmonyMethod(typeof(BotEchoes), nameof(TailPrefix)),
                finalizer: new HarmonyMethod(typeof(BotEchoes), nameof(ClearFinalizer)));
            h.Patch(AccessTools.Method(typeof(SoundBank), nameof(SoundBank.PickClipsByDistance)),
                postfix: new HarmonyMethod(typeof(BotEchoes), nameof(PickPostfix)));
            h.Patch(AccessTools.Method(typeof(SuperBetterAudioQueue), nameof(SuperBetterAudioQueue.Enqueue)),
                postfix: new HarmonyMethod(typeof(BotEchoes), nameof(EnqueuePostfix)));
        }

        public static void Reset()
        {
            for (int i = 0; i < SearchN; i++)
            {
                Search j = Pool[i];
                if (j == null) continue;
                if (j.Phase == 1 || j.Phase == 2) j.H.Complete();
                j.Phase = 0; j.Serial++;
            }
            Shooters.Clear(); ByKey.Clear(); Reqs.Clear();
            _nextLog = 0f;
            Samples = Sets = Played = Dropped = Replaced = Gated = Throttled = OutOfRange = NoWalls = SearchBusy = Searches = Reused = LoopsSkipped = 0;
            _levelSum = 0f;
        }

        private static bool IsRemote(WeaponSoundPlayer w)
        {
            BaseSoundPlayer.IObserverToPlayerBridge b = BridgeRef(w);
            return b != null && b.iPlayer != null && b.PointOfView != EPointOfView.FirstPerson;
        }

        private static void FirePrefix(WeaponSoundPlayer __instance, Vector3 shotDirection)
        {
            _cur = null; _curClip = null; _curTail = false;
            try
            {
                if (!On || !IsRemote(__instance)) return;
                _cur = __instance;
                _curAim = shotDirection.sqrMagnitude > 1e-6f ? shotDirection.normalized : Vector3.zero;
            }
            catch (Exception e) { _cur = null; DiagRunner.ReportOnce("6.3h bot echoes (fire)", e); }
        }

        private static void TailPrefix(WeaponSoundPlayer __instance)
        {
            _cur = null; _curClip = null; _curTail = false;
            try
            {
                if (!On || !IsRemote(__instance)) return;
                _cur = __instance;
                _curTail = true;
                _curAim = Vector3.zero;
            }
            catch (Exception e) { _cur = null; DiagRunner.ReportOnce("6.3h bot echoes (tail)", e); }
        }

        private static Exception ClearFinalizer(Exception __exception)
        {
            _cur = null; _curClip = null; _curTail = false;
            return __exception;
        }

        // Path loss is worked out here, so the echoes use the close recording whatever distance set the game picked. The outdoor one: the indoor set has a room baked in.
        private static void PickPostfix(SoundBank __instance, ref AudioClip clip1)
        {
            if (_cur == null) return;
            try
            {
                _curClip = clip1;
                if (!__instance.HasEnvironment || __instance.Environments == null || __instance.Environments.Length < 1 || __instance.Environments[0] == null) return;
                AudioClip outdoor = null;
                __instance.SelectRandomClip(__instance.Environments[0][0], ref outdoor);
                if (outdoor != null) _curClip = outdoor;
            }
            catch (Exception e) { DiagRunner.ReportOnce("6.3h bot echoes (clip)", e); }
        }

        private static void EnqueuePostfix(AudioClip clip1, AudioClip clip2, double startTime, float clipTime, float volume, float pitch)
        {
            if (_cur == null) return;
            try
            {
                int key = _cur.GetInstanceID();
                if (!ByKey.TryGetValue(key, out Shooter s))
                {
                    s = new Shooter { W = _cur, Key = key };
                    Shooters.Add(s); ByKey[key] = s;
                }
                Samples++;
                float now = Time.time;
                if (now - s.LastSample > BurstGapS) s.BurstTaps = 0; // a new burst decides its echo count again
                s.LastSample = now;
                s.SampleSerial++;
                if (s.OpenLoop) { EchoVoices.CloseLoops(s, startTime); s.OpenLoop = false; }

                Sample kind = clipTime <= 0f ? Sample.Loop : (_curTail || s.LastWasLoop ? Sample.Tail : Sample.Shot);
                s.LastWasLoop = kind == Sample.Loop;
                AudioClip clip = _curClip != null ? _curClip : (clip2 != null ? clip2 : clip1);
                if (clip == null) return;
                if (kind != Sample.Tail && now - s.LastSet < EveryS) { Throttled++; return; }
                s.LastSet = now;
                Reqs.Add(new Req { S = s, Clip = clip, Start = startTime, Vol = volume, Pitch = pitch > 0f ? pitch : 1f, Asked = now, Kind = kind, Serial = s.SampleSerial, Aim = _curAim });
            }
            catch (Exception e) { DiagRunner.ReportOnce("6.3h bot echoes (enqueue)", e); }
        }

        private void LateUpdate()
        {
            LogVoices();
            if (Shooters.Count == 0) return;
            Perf.Begin();
            try { Tick(); }
            catch (Exception e) { DiagRunner.ReportOnce("6.3h bot echoes", e); Reqs.Clear(); }
            finally { Perf.End(Perf.Part.EchoSearch); }
        }

        private static void LogVoices()
        {
            if (!DiagLog.IsOpen || Time.time < _nextLog) return;
            _nextLog = Time.time + LogEveryS;
            EchoVoices.Snapshot(out int busy, out int own, out int bot);
            DiagLog.Row("voices.csv", string.Join(",", DiagLog.F(Time.time - DiagRunner.RaidStartTime, "0.00"), DiagRunner.LastPlayingInContainers.ToString(), EchoVoices.Allowed().ToString(),
                busy.ToString(), own.ToString(), bot.ToString(), (busy - own - bot).ToString()));
        }

        private static void Tick()
        {
            for (int i = 0; i < SearchN; i++)
            {
                Search j = Pool[i];
                if (j != null && (j.Phase == 1 || j.Phase == 2) && j.H.IsCompleted) Step(j);
            }

            double dsp = AudioSettings.dspTime;
            for (int i = Shooters.Count - 1; i >= 0; i--)
            {
                Shooter s = Shooters[i];
                if (s.W == null || !s.OpenLoop || FiringRef(s.W)) { if (s.W != null) continue; }
                if (s.OpenLoop) { EchoVoices.CloseLoops(s, dsp); s.OpenLoop = false; } // it stopped without a tail: killed, or out of ammo
                if (s.W != null) continue;
                if (s.Job != null && s.Job.Serial == s.JobSerial && s.Job.Phase == 3) s.Job.Phase = 0;
                ByKey.Remove(s.Key);
                Shooters[i] = Shooters[Shooters.Count - 1]; Shooters.RemoveAt(Shooters.Count - 1);
            }
            if (Reqs.Count == 0) return;
            if (!On) { Reqs.Clear(); return; }

            Transform lt = ThroughWall.Listener();
            if (lt == null) return;
            Vector3 l = lt.position, fwd = lt.forward;
            float now = Time.time;
            int mask = EnclosureProbe.CurrentMask();

            for (int i = Reqs.Count - 1; i >= 0; i--)
            {
                Req r = Reqs[i];
                if (Process(ref r, l, fwd, now, dsp, mask)) { Reqs[i] = Reqs[Reqs.Count - 1]; Reqs.RemoveAt(Reqs.Count - 1); }
            }
        }

        // True when this request is finished with.
        private static bool Process(ref Req r, Vector3 l, Vector3 fwd, float now, double dsp, int mask)
        {
            Shooter s = r.S;
            if (s.W == null) return true;
            if (r.Kind == Sample.Loop)
            {
                if (s.SampleSerial != r.Serial || !FiringRef(s.W)) { LoopsSkipped++; return true; } // one round: the tail's echoes cover it
                if (dsp + 0.05 < r.Start + LoopHoldS) return false;
            }

            Vector3 gun = s.W.WeaponPosition;
            float straight = Vector3.Distance(gun, l);
            if (straight > Plugin.BotEchoRangeM.Value) { OutOfRange++; return true; }

            bool via = PortalPositioning.TryOpening(s.W, out Vector3 near, out float routeM, out int openings);
            Vector3 from = via ? near : gun;
            float baseM = via ? Mathf.Max(routeM - Vector3.Distance(near, l), 0f) : 0f;
            string answer = "cached";

            bool fresh = s.HasAnswer && now - s.FoundAt < CacheS && (s.From - from).sqrMagnitude < SameFromM * SameFromM && (s.L - l).sqrMagnitude < SameListenerM * SameListenerM;
            if (fresh) Reused++;
            else
            {
                if (s.Job != null && s.Job.Serial != s.JobSerial) s.Job = null;
                if (s.Job == null)
                {
                    Search j = Free();
                    if (j != null)
                    {
                        Begin(j, from, l, baseM, via ? Vector3.zero : r.Aim, mask);
                        s.Job = j; s.JobSerial = j.Serial;
                        Searches++;
                        return false;
                    }
                    if (!s.HasAnswer || now - s.FoundAt > StaleS) { if (now - r.Asked < GiveUpS) return false; SearchBusy++; return true; }
                    answer = "stale";
                }
                else if (s.Job.Phase == 3)
                {
                    TakeAnswer(s);
                    answer = "searched";
                }
                else
                {
                    if (now - r.Asked < GiveUpS) return false;
                    if (!s.HasAnswer || now - s.FoundAt > StaleS) { SearchBusy++; return true; }
                    answer = "stale";
                }
            }

            PlaySet(ref r, s, l, fwd, gun, straight, via, near, openings, dsp, answer);
            return true;
        }

        private static Search Free()
        {
            for (int i = 0; i < SearchN; i++)
            {
                if (Pool[i] == null) Pool[i] = new Search();
                if (Pool[i].Phase == 0) return Pool[i];
            }
            // an answer nobody came back for
            for (int i = 0; i < SearchN; i++) if (Pool[i].Phase == 3) { Pool[i].Serial++; Pool[i].Phase = 0; return Pool[i]; }
            return null;
        }

        private static void TakeAnswer(Shooter s)
        {
            Search j = s.Job;
            Pick(j);
            s.N = _pickN;
            Array.Copy(Picks, s.T, _pickN);
            s.From = j.S; s.L = j.L; s.FoundAt = Time.time; s.HasAnswer = true;
            j.Phase = 0; j.Serial++;
            s.Job = null;
        }

        // ---------------- the search ----------------

        private static void Begin(Search j, Vector3 s, Vector3 l, float baseM, Vector3 aim, int mask)
        {
            if (!j.C.IsCreated) { j.C = new NativeArray<RaycastCommand>(MaxCmds, Allocator.Persistent); j.R = new NativeArray<RaycastHit>(MaxCmds, Allocator.Persistent); }
            j.Serial++;
            j.S = s; j.L = l; j.BaseM = baseM; j.Aim = aim; j.Directional = aim.sqrMagnitude > 0.5f; j.Mask = mask;
            var qp = new QueryParameters(mask, false, QueryTriggerInteraction.Ignore, true);
            Vector3[] dirs = ExplosionEchoes.Dirs;
            for (int i = 0; i < dirs.Length; i++) j.C[i] = new RaycastCommand(s, dirs[i], qp, RayM);
            CountedRay.Rays += dirs.Length;
            j.Phase = 1;
            j.H = RaycastCommand.ScheduleBatch(j.C.GetSubArray(0, dirs.Length), j.R.GetSubArray(0, dirs.Length), 16, 1);
            JobHandle.ScheduleBatchedJobs();
        }

        private static void Step(Search j)
        {
            j.H.Complete();
            if (j.Phase == 1) BuildChecks(j);
            else j.Phase = 3;
        }

        private static void BuildChecks(Search j)
        {
            var qp = new QueryParameters(j.Mask, false, QueryTriggerInteraction.Ignore, true);
            Vector3[] dirs = ExplosionEchoes.Dirs;
            int planes = 0;
            for (int i = 0; i < dirs.Length && planes < MaxPlanes; i++)
            {
                RaycastHit hit = j.R[i];
                if (hit.collider == null || hit.distance < MinHitM || EnclosureProbe.IsGround(hit.normal)) continue;
                float h = Vector3.Dot(j.S - hit.point, hit.normal);
                bool dup = false;
                for (int k = 0; k < planes && !dup; k++) dup = Vector3.Dot(j.N[k], hit.normal) > PlaneCos && Mathf.Abs(j.H0[k] - h) < PlaneM;
                if (dup) continue;
                j.N[planes] = hit.normal; j.H0[planes] = h; j.P[planes] = hit.point; j.D[planes] = dirs[i]; j.Dist[planes] = hit.distance; j.Col[planes] = hit.collider;
                planes++;
            }
            j.Planes = planes;

            int n = 0;
            for (int k = 0; k < planes; k++)
            {
                j.MirrorCmd[k] = j.MirrorReach[k] = -1;
                Vector3 nrm = j.N[k], p = j.P[k];
                float hs = Vector3.Dot(j.S - p, nrm), hl = Vector3.Dot(j.L - p, nrm);
                if (hs > 0f && hl > OffWallM)
                {
                    Vector3 sOn = j.S - nrm * hs, lOn = j.L - nrm * hl, q = sOn + (lOn - sOn) * (hs / (hs + hl));
                    float toQ = Vector3.Distance(j.S, q);
                    if (toQ > 0.01f)
                    {
                        j.Q[k] = q; j.ToQ[k] = toQ;
                        j.MirrorCmd[k] = n;
                        j.C[n++] = new RaycastCommand(j.S, (q - j.S) / toQ, qp, toQ + 1f);
                        j.MirrorReach[k] = n;
                        j.C[n++] = ToListener(q + nrm * OffWallM, j.L, qp);
                    }
                }
                j.DiffReach[k] = n;
                j.C[n++] = ToListener(p + nrm * OffWallM, j.L, qp);
            }
            if (n == 0) { j.Phase = 3; return; }
            CountedRay.Rays += n;
            j.Phase = 2;
            j.H = RaycastCommand.ScheduleBatch(j.C.GetSubArray(0, n), j.R.GetSubArray(0, n), 16, 1);
            JobHandle.ScheduleBatchedJobs();
        }

        private static RaycastCommand ToListener(Vector3 from, Vector3 l, QueryParameters qp)
        {
            Vector3 v = l - from;
            float m = v.magnitude;
            return new RaycastCommand(from, m > 0.001f ? v / m : Vector3.up, qp, Mathf.Max(m, 0.001f));
        }

        private static void Pick(Search j)
        {
            _candN = 0; _pickN = 0;
            float direct = Vector3.Distance(j.S, j.L);
            for (int k = 0; k < j.Planes; k++)
            {
                bool mirror = false;
                if (j.MirrorCmd[k] >= 0)
                {
                    RaycastHit mh = j.R[j.MirrorCmd[k]];
                    if (mh.collider != null && Mathf.Abs(mh.distance - j.ToQ[k]) < MirrorM && Vector3.Dot(mh.normal, j.N[k]) > PlaneCos && j.R[j.MirrorReach[k]].collider == null)
                    {
                        mirror = true;
                        Add(j, OwnEchoes.Kind.Spec, mh.point, (j.Q[k] - j.S) / j.ToQ[k], j.ToQ[k] + Vector3.Distance(mh.point, j.L), direct, Surfaces.Material(mh.collider), 0f, 0f);
                    }
                }
                if (mirror || j.R[j.DiffReach[k]].collider != null) continue;
                float cos = Mathf.Max(0.05f, Vector3.Dot(-j.D[k], j.N[k]));
                Add(j, OwnEchoes.Kind.Diff, j.P[k], j.D[k], j.Dist[k] + Vector3.Distance(j.P[k], j.L), direct, Surfaces.Material(j.Col[k]), 10f * Mathf.Log10(0.25f * cos), ScatterDb);
            }

            for (int i = 1; i < _candN; i++)
            {
                OwnEchoes.Tap x = Cands[i];
                int m = i - 1;
                while (m >= 0 && Cands[m].Energy < x.Energy) { Cands[m + 1] = Cands[m]; m--; }
                Cands[m + 1] = x;
            }
            for (int c = 0; c < _candN; c++)
            {
                ref OwnEchoes.Tap cand = ref Cands[c];
                bool merged = false;
                for (int t = 0; t < _pickN && !merged; t++)
                {
                    if (Mathf.Abs(Picks[t].Path - cand.Path) / OwnEchoes.SpeedOfSound >= MergeDelayS
                        || Vector3.Dot((Picks[t].Pos - j.L).normalized, (cand.Pos - j.L).normalized) <= MergeCos) continue;
                    Picks[t].Energy += cand.Energy;
                    Picks[t].LevelDb = 10f * Mathf.Log10(Picks[t].Energy);
                    merged = true;
                }
                if (!merged && _pickN < MaxTaps && cand.LevelDb >= MinLevelDb) Picks[_pickN++] = cand;
            }
        }

        private static void Add(Search j, OwnEchoes.Kind kind, Vector3 pos, Vector3 outDir, float path, float direct, MaterialType mat, float shareDb, float extraDb)
        {
            if (_candN >= Cands.Length || path - direct < MinExtraM) return;
            Acoustics.Materials.Echo(mat, out float matDb, out float matCut);
            float total = j.BaseM + path;
            float dirDb = j.Directional ? -Plugin.EchoDirectivityDb.Value * (1f - Vector3.Dot(outDir.normalized, j.Aim)) * 0.5f : 0f;
            float level = -20f * Mathf.Log10(Mathf.Max(total, RefPath) / RefPath) + matDb + shareDb + extraDb + dirDb;
            Cands[_candN++] = new OwnEchoes.Tap
            {
                Pos = pos, OutDir = outDir, Path = total, LevelDb = level, DirDb = dirDb, Mat = mat, Kind = kind,
                Cutoff = Mathf.Min(OwnEchoes.AirCutoff(total), matCut) * (kind == OwnEchoes.Kind.Spec ? 1f : 0.75f),
                Energy = Mathf.Pow(10f, level / 10f),
            };
        }

        // ---------------- playing a set ----------------

        private static void PlaySet(ref Req r, Shooter s, Vector3 l, Vector3 fwd, Vector3 gun, float straight, bool via, Vector3 near, int openings, double dsp, string answer)
        {
            if (s.N == 0) { NoWalls++; Log(ref r, s, straight, via, answer, 0, 0, 0, 0, 0, 0f); return; }

            int max = Mathf.Clamp(Plugin.BotEchoCount.Value, 1, MaxTaps);
            if (s.BurstTaps == 0)
            {
                int head = EchoVoices.BotHeadroom();
                s.BurstTaps = head >= 2 * max ? max : head >= max ? Mathf.Max(1, max - 1) : 1;
            }
            bool loop = r.Kind == Sample.Loop;
            int count = Mathf.Min(loop ? 1 : s.BurstTaps, s.N);
            float dull = via && Plugin.RouteDullOn.Value ? PortalPositioning.DullHzFor(gun - l, near - l, true, openings, Plugin.RouteDullStrength.Value) : 22000f;
            float routeDb = via ? -RouteLossDb * openings : 0f;
            float trim = Plugin.BotEchoDb.Value, gate = Plugin.BotEchoGateDb.Value;
            float length = Mathf.Min(r.Clip.length / r.Pitch, Plugin.BotEchoLengthS.Value);

            int played = 0, dropped = 0, replaced = 0, gated = 0;
            float top = -100f;
            for (int i = 0; i < count; i++)
            {
                ref OwnEchoes.Tap t = ref s.T[i];
                float lvl = Mathf.Min(t.LevelDb + routeDb + trim, MaxLevelDb + Mathf.Max(0f, trim));
                float vol = r.Vol * Mathf.Pow(10f, lvl / 20f);
                if (vol <= 1e-5f || 20f * Mathf.Log10(vol) < gate) { gated = count - i; Gated += gated; break; }
                float extra = Mathf.Max(t.Path - straight, 0f) / OwnEchoes.SpeedOfSound;
                float cutoff = Mathf.Min(t.Cutoff, dull) * OwnEchoes.HeadShadow(l, fwd, t.Pos);
                if (loop)
                {
                    double late = Math.Max(LoopHoldS, dsp + SoonestS - extra - r.Start);
                    EchoVoices.Play(s, r.Clip, t.Pos, r.Start + late + extra, 0.0, true, vol, r.Pitch, cutoff, extra, (float)late * r.Pitch, r.Start, cls: EchoClass.Bot, tier: i);
                }
                else
                {
                    double start = Math.Max(r.Start + extra, dsp + SoonestS);
                    EchoVoices.Play(s, r.Clip, t.Pos, start, length, false, vol, r.Pitch, cutoff, extra, fadeS: length * FadeShare, cls: EchoClass.Bot, tier: i);
                }
                if (EchoVoices.LastDropped) { dropped++; continue; }
                played++;
                if (loop) s.OpenLoop = true;
                if (EchoVoices.LastReplaced) replaced++;
                if (lvl > top) top = lvl;
                _levelSum += lvl;
            }
            Sets++; Played += played; Dropped += dropped; Replaced += replaced;
            Log(ref r, s, straight, via, answer, count, played, dropped, replaced, gated, top);
        }

        private static void Log(ref Req r, Shooter s, float straight, bool via, string answer, int wanted, int played, int dropped, int replaced, int gated, float top)
        {
            if (!DiagLog.IsOpen) return;
            EchoVoices.Snapshot(out int _, out int _, out int bot);
            DiagLog.Row("botechoes.csv", string.Join(",", DiagLog.F(Time.time - DiagRunner.RaidStartTime, "0.00"), s.Key.ToString(), r.Kind.ToString().ToLowerInvariant(), DiagLog.F(straight, "0.0"), via ? "1" : "0", answer,
                s.N.ToString(), wanted.ToString(), played.ToString(), dropped.ToString(), replaced.ToString(), gated.ToString(), bot.ToString(), EchoVoices.Allowed().ToString(), played > 0 ? DiagLog.F(top, "0.0") : ""));
        }

        public static string Summary()
        {
            string head = $"BOT ECHOES (6.3h {(Plugin.BotEchoOn.Value ? "ON" : "off")}, level {Plugin.BotEchoDb.Value:+0;-0;0} dB, {Plugin.BotEchoCount.Value} per bot, ceiling {Plugin.BotEchoCeiling.Value}, range {Plugin.BotEchoRangeM.Value:0} m, gate {Plugin.BotEchoGateDb.Value:0} dB, {Plugin.BotEchoLengthS.Value:0.0} s): ";
            string echoes = $"bot sounds {Samples}, sets {Sets}, echoes played {Played}" + (Played > 0 ? $", mean level {_levelSum / Played:0.0} dB" : "") + $", most bot voices at once {EchoVoices.MaxBot}" +
                            $"; not played: limit {Dropped}, too quiet {Gated}, one set per 0.25 s {Throttled}, out of range {OutOfRange}, no usable walls {NoWalls}, search busy {SearchBusy}, one-round loops {LoopsSkipped}; searches {Searches}, answers reused {Reused}  (botechoes.csv)\n";
            string prio = $"ECHO VOICE PRIORITY (6.3n your gun keeps {Plugin.OwnEchoShare.Value}, 6.3p replace quietest {(Plugin.EchoReplaceOn.Value ? "ON" : "off")}): bot echoes replaced for your gun {EchoVoices.ReplacedForOwn}, for other sounds {EchoVoices.ReplacedForOther}, by a louder bot echo {EchoVoices.ReplacedForBot}; " +
                          $"your echoes not started {EchoVoices.OwnDropped}, of those while a bot echo held a voice {EchoVoices.OwnDroppedWithBots}; " +
                          $"blast layers past the cap {EchoVoices.BlastOverCap} (up to {EchoVoices.BlastReserve} at once), not played {EchoVoices.BlastDropped}  (voices.csv every 0.5 s)\n";
            return head + echoes + prio;
        }
    }
}
