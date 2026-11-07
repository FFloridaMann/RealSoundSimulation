using System;
using System.Collections.Generic;
using System.Text;
using Audio.SpatialSystem;
using RealSoundSimulation.Diagnostics;
using EFT;
using EFT.Ballistics;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace RealSoundSimulation.Acoustics
{
    // Probe v2 (0.6.0): one acoustic ray-tracer for both the ambience and your own gunshots. Design: docs/AMBIENCE.md §12-13,
    // current behaviour: docs/DESIGN.md §2.6. Replaces the room-map/ceiling rules of the exposure probe (raid 21: a garage
    // whose walls and roof are one collider read as "2 storeys underground") and straight-line-only exposure.
    //
    //   Per sweep 192 paths: 128 directions spread evenly over the whole sphere (Fibonacci, a new random rotation every
    //   sweep) and 64 GUIDED paths aimed at the openings found so far (0.6.4, below). About PathsPerSecond paths start per
    //   second. Every frame, every path in flight casts one leg; all legs of a frame go in one batch on the worker threads.
    //   A leg that hits nothing within 60 m has ESCAPED: that is the outside (open sky). A level or downward leg that
    //   escapes is only outside if there is no roof above its end point (otherwise it is a long interior: trapped).
    //   A hit:
    //     glass        passes at "9d Glass loss dB" (physical: about -22 dB, muffled to 1.5 kHz), else reflects
    //     thin planks, cardboard, fabric   pass at -10 dB (2.5 kHz), else reflect
    //     bars, fences, grates             pass at 0.9
    //     shattered glass, empty window frames   pass
    //     solid objects on low-detail layers (cars, doors, building shells) reflect (0.6.2: no leak)
    //     anything else reflects: energy x (1 - absorption of its material), mirror or scattered by its roughness, and
    //     duller by its brightness. The ground/floor under the first leg absorbs nothing (it is part of the rain field).
    //   A path's own result (direct + after up to 3 reflections, and the energy still bouncing then = "remaining") is what
    //   the level, direction and brightness are made of.
    //
    // 0.6.4 (raid 24: outdoor ambience too loud far inside; lowering "9e" helped but then it jumped in and out):
    //   BUILD-UP MEASURED, not assumed. Up to 0.6.3 the remaining energy's escape was one room formula (Sabine) with a leak
    //   rate counted in RAYS: an escape through closed glass (-22 dB) counted like an open door, and the whole interior was
    //   treated as one room evenly fed by its openings. Now half the paths keep bouncing after their 3rd reflection, up to
    //   8, only to measure where the remaining energy goes in THIS building: the share that gets out in reflections 4-8
    //   (energy, glass at its real loss) and the share still bouncing after 8. Only that last part uses the room formula,
    //   with a leak rate in ENERGY from legs after 5+ reflections. Averaged over ~3 s (thousands of paths), so it is smooth:
    //   build-up = remaining x (escaped in 4-8 + still bouncing x p / (p + (1 - p) a)) / energy at the 3rd reflection, x "9e".
    //   The 0.6.3 formula is still computed and logged (probe2.csv tailOld) for comparison.
    //   GUIDED RAYS ("9f"; user: "what if we weighted the rays?"): every escaping path leaves an "opening": the point its first
    //   leg went to, weighted by the energy it brought (fades over ~2 s unless found again). Each sweep, 64 paths are aimed
    //   at the remembered openings (spread around each by its size seen from you, at least ~5% of them per opening, so
    //   every one is re-checked every sweep, ~0.3 s). Every path then counts as p_uniform / p_mixture (multiple importance
    //   sampling, balance heuristic): the totals stay right, but a window 15 m down a corridor gets dozens of paths instead
    //   of two or three. Directions where nothing was found are still covered by the 128 even directions.
    //   SMOOTHING BY CONFIDENCE: the statistical error of the level (from the escaping paths' spread) sets how long it is
    //   averaged: 0.35 s when many paths get out (doorways stay quick), up to 2.5 s when only a few do (deep interiors).
    //
    // Outputs (aggregated every frame from the last two sweeps):
    //   Level      outdoor sound energy reaching you, 0..1 (1 in open air): direct + after bounces + room build-up
    //   CutoffHz   how bright it is (energy-weighted; glass ~1.5 kHz, concrete reflections ~13 kHz, open 22 kHz)
    //   Arrival    the direction it comes from (first leg of each escaping path, earliest arrivals weigh most), Focus
    //   SkyOpen    direct escape / half the sphere (1 under open sky); Enclosed from it (indoor layers, directional beds)
    //   Coverage   share of the upward cone (within 53 deg of straight up) covered within 40 m; RoofMat (even directions only)
    //   SmoothS    how long the users of Level should average it (confidence)
    //   For gunshots: Room = 1 - share escaping within one wall/roof bounce (floors don't count, thin surfaces by "3f"),
    //   MeanFreePath, Absorption, Size (0.5 small .. 1), HardRoom, Rt60 (estimate)
    // 0.6.1: direction from the earliest sound (precedence, exp(-path length / 20 m)); two sweeps averaged; kind cache.
    internal class AcousticProbe : MonoBehaviour
    {
        public const int N = 128, G = 64, M = N + G;
        public const int EarlyBounces = 3, MaxBounces = 8, MaxCasts = 12, MaxPts = MaxCasts + 1;
        private const int LateFrom = 5, MaxLobes = 16;
        private const float MaxLeg = 60f, Skin = 0.02f, PathsPerSecond = 640f, PrecedenceM = 20f;
        private const float DeepShare = 0.5f, StatTau = 3f, LobeTau = 2f, LobeFloor = 0.05f;
        public const int Sweeps = 2;
        private const int MaxActive = 256;
        private const float PUniform = 1f / (4f * Mathf.PI);

        public enum Fate : byte { None, Sky, SkyAfterBounces, Trapped, LongInterior }

        public struct Result
        {
            public Vector3 First;
            public float Direct, Reflected, Remaining, Geo, Cut;
            public bool ViaGlass, FirstHit, Guided;
            public float FirstHitM;
            public MaterialType FirstMat;
            public Fate Fate;   // the path's own result (after up to 3 reflections)
            public Fate Final;  // where it ended after deep tracing (ray view, paths.csv)
            public int Casts;
            public float PathLen;
            public float W;     // importance weight (1 = an even direction with no openings known)
        }

        private struct PathState
        {
            public bool Active, Reported, Deep, Guided;
            public int Dir;
            public Vector3 O, D, First, FirstPt;
            public float E, G, Cut, W;
            public int Casts, Bounces, GeoBounces;
            public bool ViaGlass, FirstHit;
            public float FirstHitM;
            public MaterialType FirstMat;
            public int Pn;
            public int Layer;
            public float Len;
            public Result Rep;
        }

        // Running sums (exponential, StatTau). Everything is weighted by the path's importance weight W.
        private struct Stats
        {
            public double Alpha, Hits, Len, LenN, CutLog;
            public double LateEsc, LateLeg, LateLegN;   // energy escaping / entering legs after LateFrom+ reflections
            public double OldEsc, OldLeg;               // 0.6.3's leak rate: rays, legs after 2-3 reflections (log only)
            public double DeepBase, DeepEsc, DeepRem;   // energy at the 3rd reflection, escaping in 4-8, still bouncing after
            public void Scale(double k)
            {
                Alpha *= k; Hits *= k; Len *= k; LenN *= k; CutLog *= k; LateEsc *= k; LateLeg *= k; LateLegN *= k;
                OldEsc *= k; OldLeg *= k; DeepBase *= k; DeepEsc *= k; DeepRem *= k;
            }
            public void Add(in Stats o)
            {
                Alpha += o.Alpha; Hits += o.Hits; Len += o.Len; LenN += o.LenN; CutLog += o.CutLog; LateEsc += o.LateEsc;
                LateLeg += o.LateLeg; LateLegN += o.LateLegN; OldEsc += o.OldEsc; OldLeg += o.OldLeg;
                DeepBase += o.DeepBase; DeepEsc += o.DeepEsc; DeepRem += o.DeepRem;
            }
        }

        // An opening: where an escaping path's first leg went (wall point, or 20 m along it for a straight escape).
        private struct Lobe { public Vector3 P; public float W; }

        // the last two sweeps' results (aggregated together), and the latest path per slot with its points and hits
        // (ray view, paths.csv). Slots 0..127 are the even directions, 128..191 the guided paths.
        public static readonly Result[] Results = new Result[Sweeps * M];
        public static readonly Result[] Latest = new Result[M];
        public static readonly Vector3[] Pts = new Vector3[M * MaxPts];
        public static readonly int[] PtN = new int[M];
        public static readonly Collider[] HitCol = new Collider[M * MaxPts];
        public static readonly float[] HitM = new float[M * MaxPts];
        public static readonly int[] HitN = new int[M];
        private static int _layer;

        private static readonly PathState[] Paths = new PathState[MaxActive];
        private static readonly Vector3[] Scratch = new Vector3[MaxActive * MaxPts];
        private static readonly Collider[] ScratchCol = new Collider[MaxActive * MaxPts];
        private static readonly float[] ScratchM = new float[MaxActive * MaxPts];
        private static readonly int[] ScratchHitN = new int[MaxActive];
        private static readonly int[] Idx = new int[MaxActive];
        private static readonly Vector3[] Fib = BuildFib(N);
        private static Quaternion _rot = Quaternion.identity;
        private static int _cursor;
        private static float _budget, _nextRow;
        private static NativeArray<RaycastCommand> _cmds;
        private static NativeArray<RaycastHit> _hits;
        private static Stats _acc;
        private static Vector3 _listener;

        // openings, and the guide built from them at the start of each sweep
        private static readonly Lobe[] Lobes = new Lobe[MaxLobes];
        private static readonly Vector3[] GP = new Vector3[MaxLobes];
        private static readonly float[] GKappa = new float[MaxLobes], GPi = new float[MaxLobes], GNorm = new float[MaxLobes];
        private static int _gN;
        public static int Openings, MaxOpenings;
        public static long GuidedPaths;

        public static bool Valid;
        public static float Level = float.NaN, Direct, Traced, Tail, TailOld, Remaining, EscapeRate, EscapeRateOld, Absorption,
            MeanFreePath = float.NaN, DeepEscShare, DeepRemShare,
            CutoffHz = 22000f, Focus, SkyOpen, Enclosed, Coverage, DiffuseShare, Room = float.NaN, Size = float.NaN, HardRoom = float.NaN,
            Rt60 = float.NaN, RelErr, SmoothS = 0.35f;
        public static int Escapes;
        public static Vector3 Arrival = Vector3.forward;
        public static MaterialType RoofMat = MaterialType.None;
        public static long RaysCast, PathsDone;
        private static readonly int[] RoofCount = new int[64];

        // muzzle (gunshots): measured when you fire
        public static float MuzzleRoom = float.NaN, MuzzleSize = float.NaN, MuzzleCoverage = float.NaN, MuzzleHardRoom = float.NaN;
        private static Vector3 _muzzleAt; private static float _muzzleTime = -10f;

        public static void Reset()
        {
            Valid = false; Level = Room = Size = HardRoom = Rt60 = MeanFreePath = float.NaN;
            MuzzleRoom = MuzzleSize = MuzzleCoverage = MuzzleHardRoom = float.NaN; _muzzleTime = -10f;
            for (int i = 0; i < Results.Length; i++) Results[i] = default;
            for (int i = 0; i < M; i++) { Latest[i] = default; PtN[i] = 0; HitN[i] = 0; }
            for (int i = 0; i < HitCol.Length; i++) HitCol[i] = null;
            for (int i = 0; i < MaxLobes; i++) Lobes[i] = default;
            _gN = 0; Openings = MaxOpenings = 0; GuidedPaths = 0; SmoothS = 0.35f; RelErr = 0f; Escapes = 0;
            KindCache.Clear(); _layer = 0; _cursor = 0;
            for (int i = 0; i < MaxActive; i++) Paths[i].Active = false;
            _acc = default;
            RaysCast = PathsDone = 0; _budget = 0f; _nextRow = 0f; _maskBlock = -1;
        }

        private void OnDestroy()
        {
            if (_cmds.IsCreated) _cmds.Dispose();
            if (_hits.IsCreated) _hits.Dispose();
        }

        private void Update()
        {
            bool inRaid = SpatialAudioSystem.Initialized && MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated && GamePlayerOwner.MyPlayer != null;
            if (inRaid && Plugin.RayViewKey.Value.IsDown()) RayView.Toggle();
            if (!inRaid || !Plugin.AcousticProbeOn.Value) { Valid = false; RayView.Hide(); return; }
            Perf.Begin();
            try { Step(MonoBehaviourSingleton<SpatialAudioSystem>.Instance); }
            catch (Exception e) { DiagRunner.ReportOnce("acoustic probe v2", e); }
            finally { Perf.End(Perf.Part.AcousticProbe); }
            try { RayView.Tick(); } catch (Exception e) { DiagRunner.ReportOnce("probe ray view", e); }
        }

        private static void Step(SpatialAudioSystem sas)
        {
            Transform lt = sas.ListenerTransform;
            if (lt == null) return;
            if (!_cmds.IsCreated) { _cmds = new NativeArray<RaycastCommand>(MaxActive, Allocator.Persistent); _hits = new NativeArray<RaycastHit>(MaxActive, Allocator.Persistent); }
            int block = EnclosureProbe.CurrentMask();
            int mask = V2Mask(block);
            float dt = Mathf.Max(Time.unscaledDeltaTime, 0f);
            _listener = lt.position;

            // openings fade unless found again
            float fade = Mathf.Exp(-dt / LobeTau);
            for (int k = 0; k < MaxLobes; k++) { Lobes[k].W *= fade; if (Lobes[k].W < 1e-6f) Lobes[k].W = 0f; }

            // start new paths
            _budget = Mathf.Min(_budget + PathsPerSecond * dt, 32f);
            for (int i = 0; i < MaxActive && _budget >= 1f; i++)
            {
                if (Paths[i].Active) continue;
                _budget -= 1f;
                int j = _cursor;
                bool guided = j >= N;
                Vector3 d = guided ? SampleGuide(_listener) : _rot * Fib[j];
                Start(ref Paths[i], i, j, _listener, d, WeightFor(d, _listener), guided);
                if (guided && _gN > 0) GuidedPaths++;
                _cursor++;
                if (_cursor >= M) { _cursor = 0; _rot = UnityEngine.Random.rotationUniform; _layer = (_layer + 1) % Sweeps; BuildGuide(_listener); }
            }

            // one leg for every path in flight, in one batch
            var qp = new QueryParameters(mask, false, QueryTriggerInteraction.Ignore, false);
            int n = 0;
            for (int i = 0; i < MaxActive; i++)
            {
                if (!Paths[i].Active) continue;
                _cmds[n] = new RaycastCommand(Paths[i].O, Paths[i].D, qp, MaxLeg);
                Idx[n++] = i;
            }
            _acc.Scale(Math.Exp(-dt / StatTau));
            if (n > 0)
            {
                JobHandle h = RaycastCommand.ScheduleBatch(_cmds.GetSubArray(0, n), _hits.GetSubArray(0, n), 8, 1);
                h.Complete();
                RaysCast += n; CountedRay.Rays += n;
                Stats s = default;
                for (int k = 0; k < n; k++)
                {
                    int i = Idx[k];
                    Advance(ref Paths[i], i, _hits[k], block, true, ref s);
                }
                _acc.Add(s);
            }
            Aggregate();
            if (Time.time >= _nextRow)
            {
                _nextRow = Time.time + 0.125f;
                if (Valid && DiagLog.IsOpen) DiagLog.Row("probe2.csv", RowCsv(Time.time - DiagRunner.RaidStartTime, lt));
            }
        }

        private static void Start(ref PathState p, int slot, int dir, Vector3 o, Vector3 d, float w, bool guided)
        {
            p = default;
            p.Active = true; p.Dir = dir; p.O = o; p.D = d; p.First = d; p.E = 1f; p.G = 1f; p.Cut = 22000f; p.W = w; p.Guided = guided;
            p.Deep = UnityEngine.Random.value < DeepShare;
            p.Pn = 0; p.Layer = _layer;
            Scratch[slot * MaxPts + p.Pn++] = o;
            ScratchHitN[slot] = 0;
        }

        // ---------------- guided rays ----------------

        // The guide for the next sweep, from the remembered openings: a von Mises-Fisher lobe around each, as wide as an
        // opening ~3 m across looks from here (6-20 deg), weighted by the energy it brought (at least LobeFloor of the total,
        // so weak openings are still re-checked).
        private static void BuildGuide(Vector3 l)
        {
            float total = 0f; int cnt = 0;
            for (int k = 0; k < MaxLobes; k++) if (Lobes[k].W > 0f) { total += Lobes[k].W; cnt++; }
            Openings = cnt; if (cnt > MaxOpenings) MaxOpenings = cnt;
            _gN = 0;
            if (!Plugin.GuidedRays.Value || cnt == 0 || total <= 0f) return;
            float sum = 0f;
            for (int k = 0; k < MaxLobes; k++)
            {
                if (Lobes[k].W <= 0f) continue;
                Vector3 v = Lobes[k].P - l;
                float dist = v.magnitude;
                if (dist < 0.3f) continue;
                float sigma = Mathf.Clamp(Mathf.Atan2(1.5f, dist), 0.1f, 0.35f);
                float kappa = 1f / (sigma * sigma);
                GP[_gN] = Lobes[k].P; GKappa[_gN] = kappa;
                GNorm[_gN] = kappa / (2f * Mathf.PI * (1f - Mathf.Exp(-2f * kappa)));
                GPi[_gN] = Mathf.Max(Lobes[k].W, LobeFloor * total);
                sum += GPi[_gN];
                _gN++;
            }
            for (int k = 0; k < _gN; k++) GPi[k] /= sum;
        }

        private static Vector3 SampleGuide(Vector3 l)
        {
            if (_gN == 0) return UnityEngine.Random.onUnitSphere;
            float u = UnityEngine.Random.value, c = 0f; int k = _gN - 1;
            for (int i = 0; i < _gN; i++) { c += GPi[i]; if (u <= c) { k = i; break; } }
            Vector3 mu = GP[k] - l;
            if (mu.sqrMagnitude < 1e-6f) return UnityEngine.Random.onUnitSphere;
            mu.Normalize();
            float kappa = GKappa[k];
            float r = Mathf.Max(UnityEngine.Random.value, 1e-7f);
            float w = Mathf.Clamp(1f + Mathf.Log(r + (1f - r) * Mathf.Exp(-2f * kappa)) / kappa, -1f, 1f);
            float phi = 2f * Mathf.PI * UnityEngine.Random.value, s = Mathf.Sqrt(Mathf.Max(0f, 1f - w * w));
            Vector3 t = Mathf.Abs(mu.y) < 0.9f ? Vector3.Cross(mu, Vector3.up).normalized : Vector3.Cross(mu, Vector3.right).normalized;
            Vector3 b = Vector3.Cross(mu, t);
            return (mu * w + (t * Mathf.Cos(phi) + b * Mathf.Sin(phi)) * s).normalized;
        }

        // Importance weight: p_uniform / p_mixture, where the mixture is N even + G guided paths per sweep.
        private static float WeightFor(Vector3 d, Vector3 l)
        {
            if (_gN == 0) return 1f;
            float pg = 0f;
            for (int k = 0; k < _gN; k++)
            {
                Vector3 mu = GP[k] - l;
                if (mu.sqrMagnitude < 1e-6f) continue;
                mu.Normalize();
                pg += GPi[k] * GNorm[k] * Mathf.Exp(GKappa[k] * (Vector3.Dot(mu, d) - 1f));
            }
            return M * PUniform / (N * PUniform + G * pg);
        }

        private static void Remember(Vector3 first, Vector3 point, float c)
        {
            if (c <= 0f) return;
            int best = -1; float bestDot = Mathf.Cos(10f * Mathf.Deg2Rad);
            for (int k = 0; k < MaxLobes; k++)
            {
                if (Lobes[k].W <= 0f) continue;
                Vector3 mu = Lobes[k].P - _listener;
                if (mu.sqrMagnitude < 1e-6f) continue;
                float dot = Vector3.Dot(mu.normalized, first);
                if (dot > bestDot) { bestDot = dot; best = k; }
            }
            if (best >= 0)
            {
                Lobes[best].W += c;
                Lobes[best].P = Vector3.Lerp(Lobes[best].P, point, c / Lobes[best].W);
                return;
            }
            int slot = 0;
            for (int k = 1; k < MaxLobes; k++) if (Lobes[k].W < Lobes[slot].W) slot = k;
            if (Lobes[slot].W < c) { Lobes[slot].P = point; Lobes[slot].W = c; }
        }

        // ---------------- tracing ----------------

        // One leg's result. Shared by the continuous probe (record = true) and the muzzle measure.
        private static void Advance(ref PathState p, int slot, RaycastHit hit, int block, bool record, ref Stats s)
        {
            p.Casts++;
            Collider c = hit.collider;
            if (c == null)
            {
                Vector3 end = p.O + p.D * MaxLeg;
                if (p.Casts == 1) p.FirstPt = p.O + p.D * 20f;
                if (record) AddPt(ref p, slot, end);
                bool sky = p.D.y > 0.5f || !CountedRay.Raycast(end + Vector3.up * 0.5f, Vector3.up, out RaycastHit _, 60f, block, QueryTriggerInteraction.Ignore);
                p.Len += MaxLeg;
                if (sky) Escape(ref p, slot, record, ref s);
                else End(ref p, slot, Fate.LongInterior, record, ref s);
                return;
            }
            if (record) AddPt(ref p, slot, hit.point);
            MaterialType m = Surfaces.Material(c);
            if (p.Casts == 1) { p.FirstHit = true; p.FirstHitM = hit.distance; p.FirstMat = m; p.FirstPt = hit.point; }
            s.Len += hit.distance * p.W; s.LenN += p.W; p.Len += hit.distance;

            // what kind of surface: pass-through (glass, thin, bars, open frames, props) or reflect
            bool blocking = (block & (1 << c.gameObject.layer)) != 0;
            float t, tg, passCut; bool glass = false;
            Kind(c, m, blocking, out t, out tg, out passCut, ref glass);
            if (record && ScratchHitN[slot] < MaxPts) { int h = slot * MaxPts + ScratchHitN[slot]++; ScratchCol[h] = c; ScratchM[h] = hit.distance; }
            if (t > 0f)
            {
                float pp = glass ? 0.5f : Mathf.Clamp01(Mathf.Max(t, tg));
                if (pp >= 0.999f || UnityEngine.Random.value < pp)
                {
                    // through
                    float w = pp >= 0.999f ? 1f : 1f / pp;
                    p.E *= t * w; p.G *= tg * w; p.Cut = Mathf.Min(p.Cut, passCut);
                    if (glass) p.ViaGlass = true;
                    p.O = hit.point + p.D * Skin;
                    if (p.Casts >= MaxCasts || p.E < 1e-5f) End(ref p, slot, Fate.Trapped, record, ref s);
                    return;
                }
                float wr = 1f / (1f - pp);
                p.E *= (1f - t) * wr; p.G *= (1f - tg) * wr;
            }

            // reflect
            Props(m, out float a, out float scatter, out float bright);
            bool ground = hit.normal.y > 0.7f;
            s.Alpha += a * p.W; s.Hits += p.W; s.CutLog += Math.Log(bright) * p.W; // the room's statistics use the real surface
            if (p.Casts == 1 && ground) a = 0f; // ...but the ground under you is part of the outdoor sound field: no loss
            p.E *= 1f - a; p.Cut = Mathf.Min(p.Cut, bright);
            p.Bounces++;
            if (!ground) p.GeoBounces++;
            // the path's own result ends at its 4th reflection (as up to 0.6.3); deep paths go on for the build-up statistics
            if (!p.Reported && p.Bounces > EarlyBounces)
            {
                if (!record || !p.Deep) { End(ref p, slot, Fate.Trapped, record, ref s); return; }
                Report(ref p, Fate.Trapped);
                s.DeepBase += p.E * p.W;
            }
            if (p.Bounces > MaxBounces || p.Casts >= MaxCasts || p.E < 1e-5f) { End(ref p, slot, Fate.Trapped, record, ref s); return; }
            if (p.Bounces >= LateFrom) { s.LateLeg += p.E * p.W; s.LateLegN++; }
            if (p.Bounces == 2 || p.Bounces == 3) s.OldLeg++;
            Vector3 nrm = hit.normal;
            if (Vector3.Dot(nrm, p.D) > 0f) nrm = -nrm;
            p.D = UnityEngine.Random.value < scatter ? CosineHemisphere(nrm) : Vector3.Reflect(p.D, nrm).normalized;
            p.O = hit.point + nrm * Skin;
        }

        private static void Escape(ref PathState p, int slot, bool record, ref Stats s)
        {
            Fate fate = p.Bounces == 0 ? Fate.Sky : Fate.SkyAfterBounces;
            if (p.Bounces >= LateFrom) s.LateEsc += p.E * p.W;
            if (p.Bounces == 2 || p.Bounces == 3) s.OldEsc++;
            p.Active = false;
            if (!record) { _syncFate = fate; return; }
            if (!p.Reported) Report(ref p, fate);
            else s.DeepEsc += p.E * p.W;
            View(ref p, slot, fate);
        }

        private static void End(ref PathState p, int slot, Fate fate, bool record, ref Stats s)
        {
            p.Active = false;
            if (!record) { _syncFate = fate; return; }
            if (!p.Reported) Report(ref p, fate);
            else s.DeepRem += p.E * p.W;
            View(ref p, slot, fate);
        }

        // the path's own result (direct + up to 3 reflections) into the sweep
        private static void Report(ref PathState p, Fate fate)
        {
            p.Reported = true;
            Result r = default;
            r.First = p.First; r.Cut = p.Cut; r.ViaGlass = p.ViaGlass; r.Casts = p.Casts; r.Fate = fate; r.Final = fate;
            r.FirstHit = p.FirstHit; r.FirstHitM = p.FirstHitM; r.FirstMat = p.FirstMat; r.Guided = p.Guided; r.W = p.W;
            bool escaped = fate == Fate.Sky || fate == Fate.SkyAfterBounces;
            if (escaped) { if (p.Bounces == 0) r.Direct = p.E; else r.Reflected = p.E; }
            else r.Remaining = p.E;
            r.Geo = escaped && p.GeoBounces <= 1 ? p.G : 0f;
            r.PathLen = p.Len;
            Results[p.Layer * M + p.Dir] = r;
            p.Rep = r;
            PathsDone++;
            if (escaped) Remember(p.First, p.FirstPt, (r.Direct + r.Reflected) * p.W);
        }

        // ray view and paths.csv: the latest path per slot, with where it finally ended
        private static void View(ref PathState p, int slot, Fate final)
        {
            Result r = p.Rep; r.Final = final; r.Casts = p.Casts;
            Latest[p.Dir] = r;
            int o = slot * MaxPts, d = p.Dir * MaxPts;
            for (int k = 0; k < p.Pn; k++) Pts[d + k] = Scratch[o + k];
            PtN[p.Dir] = p.Pn;
            int hn = ScratchHitN[slot];
            for (int k = 0; k < hn; k++) { HitCol[d + k] = ScratchCol[o + k]; HitM[d + k] = ScratchM[o + k]; }
            HitN[p.Dir] = hn;
        }

        private static Fate _syncFate;

        private static void AddPt(ref PathState p, int slot, Vector3 v)
        {
            if (p.Pn < MaxPts) Scratch[slot * MaxPts + p.Pn++] = v;
        }

        // What a collider is for sound, worked out once per collider (0.6.1: names and parents were read on every hit).
        private enum Cat : byte { Reflect, Shattered, Bars, Glass, Open, Thin, Solid }
        private static readonly Dictionary<int, Cat> KindCache = new Dictionary<int, Cat>();

        private static void Kind(Collider c, MaterialType m, bool blocking, out float t, out float tg, out float cut, ref bool glass)
        {
            int id = c.GetInstanceID();
            if (!KindCache.TryGetValue(id, out Cat cat))
            {
                KindRaw(c, m, blocking, out float t0, out float tg0, out float cut0, out bool g0);
                cat = g0 ? Cat.Glass : t0 >= 1f ? (m == MaterialType.GlassShattered ? Cat.Shattered : Cat.Open) : Mathf.Approximately(t0, 0.9f) ? Cat.Bars
                    : Mathf.Approximately(t0, 0.1f) ? Cat.Thin : Mathf.Approximately(t0, 0.05f) ? Cat.Solid : Cat.Reflect;
                KindCache[id] = cat;
            }
            float thinGun = 1f - Mathf.Clamp01(Plugin.ThinSurfaceCount.Value);
            switch (cat)
            {
                case Cat.Shattered: case Cat.Open: t = tg = 1f; cut = 22000f; return;
                case Cat.Bars: t = tg = 0.9f; cut = 12000f; return;
                case Cat.Glass: t = GlassT(); tg = thinGun; cut = 1500f; glass = true; return;
                case Cat.Thin: t = 0.1f; tg = thinGun; cut = 2500f; return;
                // 0.6.2: blocks completely. Raid 23: the Capital's whole-floor low-detail collider sits in front of its real
                // walls; a 5% chance of passing at full strength put nearly all of mark 1's "outside" behind the back wall
                // (and one path down through the floor) and made the level jump. Sound through walls is step 6's job.
                case Cat.Solid: t = tg = 0f; cut = 22000f; return;
                default: t = tg = 0f; cut = 22000f; return;
            }
        }

        private static void KindRaw(Collider c, MaterialType m, bool blocking, out float t, out float tg, out float cut, out bool glass)
        {
            glass = false;
            float thinGun = 1f - Mathf.Clamp01(Plugin.ThinSurfaceCount.Value);
            t = 0f; tg = 0f; cut = 22000f;
            if (m == MaterialType.GlassShattered) { t = tg = 1f; return; }
            if (m == MaterialType.Chainfence || m == MaterialType.Grate) { t = tg = 0.9f; cut = 12000f; return; }
            if (m == MaterialType.Glass || m == MaterialType.GlassVisor)
            {
                t = GlassT(); tg = thinGun; cut = 1500f; glass = true; return;
            }
            if (!blocking)
            {
                switch (OutdoorExposure.Classify(c))
                {
                    case OutdoorExposure.Path.Glass: t = GlassT(); tg = thinGun; cut = 1500f; glass = true; return;
                    case OutdoorExposure.Path.Bars: t = tg = 0.9f; cut = 12000f; return;
                    case OutdoorExposure.Path.Broken: case OutdoorExposure.Path.Open: t = tg = 1f; return;
                    default:
                        if (Surfaces.IsThin(m)) break;
                        // a solid object on a low-detail layer (parked cars, doors, building shells): blocks (see Kind)
                        t = tg = 0.05f; cut = 1200f; return;
                }
            }
            switch (m)
            {
                case MaterialType.WoodThin: case MaterialType.Cardboard: case MaterialType.Fabric:
                case MaterialType.GenericSoft: case MaterialType.GarbagePaper:
                    t = 0.1f; tg = thinGun; cut = 2500f; return;
            }
        }

        // The sound-blocking layers plus only the see-through/low-detail layers that hold real objects (glass facades,
        // window bars, cars, doors). Not the map's invisible borders, culling volumes, rain drops or shell casings.
        private static readonly string[] SeeThroughLayers = { "Default", "LowPolyCollider", "TransparentCollider", "Interactive", "DoorLowPolyCollider" };
        private static int _maskBlock = -1, _mask;

        private static int V2Mask(int block)
        {
            if (block == _maskBlock) return _mask;
            int other = OutdoorExposure.OtherMask(block), m = block;
            foreach (string n in SeeThroughLayers)
            {
                int l = LayerMask.NameToLayer(n);
                if (l >= 0 && (other & (1 << l)) != 0) m |= 1 << l;
            }
            _maskBlock = block; _mask = m;
            return m;
        }

        private static float GlassT() => Mathf.Pow(10f, -Mathf.Max(0f, Plugin.GlassLossDb.Value) / 10f);

        // Absorption (energy lost per bounce, mid frequencies), scattering (share reflected diffusely), brightness (Hz)
        // after the bounce. Typical published values for each surface type (concrete 0.02, carpet/fabric 0.5, grass 0.5...).
        public static void Props(MaterialType m, out float a, out float s, out float bright)
        {
            switch (m)
            {
                case MaterialType.Concrete: case MaterialType.Tile: a = 0.02f; s = 0.15f; bright = 16000f; return;
                case MaterialType.Stone: case MaterialType.Asphalt: a = 0.03f; s = 0.25f; bright = 15000f; return;
                case MaterialType.GenericHard: a = 0.04f; s = 0.2f; bright = 14000f; return;
                case MaterialType.MetalThick: case MaterialType.MetalNoDecal: a = 0.05f; s = 0.2f; bright = 16000f; return;
                case MaterialType.MetalThin: a = 0.1f; s = 0.3f; bright = 14000f; return;
                case MaterialType.GarbageMetal: a = 0.15f; s = 0.6f; bright = 10000f; return;
                case MaterialType.Glass: case MaterialType.GlassVisor: a = 0.04f; s = 0.05f; bright = 18000f; return;
                case MaterialType.WoodThick: a = 0.1f; s = 0.25f; bright = 9000f; return;
                case MaterialType.WoodThin: a = 0.15f; s = 0.35f; bright = 7000f; return;
                case MaterialType.Plastic: a = 0.08f; s = 0.2f; bright = 12000f; return;
                case MaterialType.Rubber: case MaterialType.Tyre: a = 0.2f; s = 0.3f; bright = 6000f; return;
                case MaterialType.Cardboard: a = 0.3f; s = 0.4f; bright = 5000f; return;
                case MaterialType.Fabric: case MaterialType.GenericSoft: case MaterialType.Body: case MaterialType.BodyArmor:
                    a = 0.5f; s = 0.7f; bright = 2500f; return;
                case MaterialType.GarbagePaper: a = 0.4f; s = 0.7f; bright = 3000f; return;
                case MaterialType.Soil: case MaterialType.Swamp: a = 0.3f; s = 0.5f; bright = 5000f; return;
                case MaterialType.SoilForest: a = 0.4f; s = 0.6f; bright = 4500f; return;
                case MaterialType.Mud: a = 0.2f; s = 0.4f; bright = 6000f; return;
                case MaterialType.Gravel: case MaterialType.Pebbles: a = 0.35f; s = 0.7f; bright = 5000f; return;
                case MaterialType.GrassLow: a = 0.45f; s = 0.7f; bright = 4000f; return;
                case MaterialType.GrassHigh: a = 0.6f; s = 0.8f; bright = 3500f; return;
                case MaterialType.Snow: a = 0.6f; s = 0.6f; bright = 3000f; return;
                case MaterialType.Water: case MaterialType.WaterPuddle: a = 0.02f; s = 0.05f; bright = 18000f; return;
                default: a = 0.06f; s = 0.3f; bright = 10000f; return; // untagged: mostly level-design hard surfaces
            }
        }

        private static void Aggregate()
        {
            float direct = 0f, refl = 0f, rem = 0f, geo = 0f, eW = 0f, pW = 0f, c2 = 0f; double cutLog = 0; Vector3 arr = Vector3.zero;
            int n = 0, up = 0, upHit = 0, esc = 0;
            Array.Clear(RoofCount, 0, RoofCount.Length);
            for (int i = 0; i < Results.Length; i++)
            {
                ref Result r = ref Results[i];
                if (r.Fate == Fate.None) continue;
                n++;
                float w = r.W;
                direct += r.Direct * w; refl += r.Reflected * w; rem += r.Remaining * w; geo += r.Geo * w;
                float e = (r.Direct + r.Reflected) * w;
                if (e > 0f)
                {
                    esc++; c2 += e * e;
                    eW += e; cutLog += e * Math.Log(Mathf.Max(r.Cut, 100f));
                    // precedence: you place a sound where its EARLIEST arrival comes from, so short paths decide the direction
                    float pw = e * Mathf.Exp(-r.PathLen / PrecedenceM);
                    arr += r.First * pw; pW += pw;
                }
                if (!r.Guided && r.First.y > 0.6f) // coverage counts directions, so only the even ones
                {
                    up++;
                    if (r.FirstHit && r.FirstHitM < 40f) { upHit++; int mi = (int)r.FirstMat; if (mi >= 0 && mi < RoofCount.Length) RoofCount[mi]++; }
                }
            }
            if (n < N / 2) { Valid = false; return; }
            Direct = direct / n; Traced = refl / n; Remaining = rem / n; Escapes = esc;
            Absorption = _acc.Hits > 1 ? (float)(_acc.Alpha / _acc.Hits) : 0.05f;
            MeanFreePath = _acc.LenN > 1 ? (float)(_acc.Len / _acc.LenN) : float.NaN;
            float a = Absorption;
            // 0.6.3's leak rate (rays, legs after 2-3 reflections) and its build-up, for comparison in the log
            EscapeRateOld = _acc.OldLeg > 1 ? (float)(_acc.OldEsc / _acc.OldLeg) : 0f;
            float pOld = EscapeRateOld;
            float shareOld = pOld + (1f - pOld) * a > 1e-6f ? pOld / (pOld + (1f - pOld) * a) : 0f;
            // leak rate in ENERGY from legs after 5+ reflections (where the remaining energy has spread to)
            float p = _acc.LateLegN > 20 && _acc.LateLeg > 1e-9 ? (float)(_acc.LateEsc / _acc.LateLeg) : pOld;
            EscapeRate = p;
            float share = p + (1f - p) * a > 1e-6f ? p / (p + (1f - p) * a) : 0f;
            float buildUp = Mathf.Max(0f, Plugin.RoomBuildUp.Value);
            float factor;
            if (_acc.DeepBase > 1e-3)
            {
                DeepEscShare = (float)(_acc.DeepEsc / _acc.DeepBase);
                DeepRemShare = (float)(_acc.DeepRem / _acc.DeepBase);
                factor = DeepEscShare + DeepRemShare * share;
            }
            else { DeepEscShare = DeepRemShare = 0f; factor = share; }
            Tail = Remaining * factor * buildUp;
            TailOld = Remaining * shareOld * buildUp;
            Level = Mathf.Clamp01(Direct + Traced + Tail);
            // confidence: standard error of the traced part relative to the level -> how long to average it
            RelErr = Level > 1e-5f ? Mathf.Sqrt(c2) / n / Level : 1f;
            SmoothS = 0.35f + 2.15f * Mathf.Clamp01((RelErr - 0.08f) / 0.32f);
            // brightness: the direct/bounced paths' own, and for the build-up the room's surfaces (many bounces: duller)
            float roomCut = _acc.Hits > 1 ? (float)Math.Exp(_acc.CutLog / _acc.Hits) * 0.8f : 12000f;
            double wSum = eW + Tail;
            CutoffHz = wSum > 1e-6 ? (float)Math.Exp((cutLog + Tail * Math.Log(Mathf.Max(roomCut, 100f))) / wSum) : 22000f;
            Arrival = arr.sqrMagnitude > 1e-10f ? arr.normalized : Vector3.forward;
            DiffuseShare = Level > 1e-6f ? Mathf.Clamp01(Tail / Level) : 0f;
            Focus = pW > 1e-8f ? Mathf.Clamp01(arr.magnitude / pW) : 0f; // 0.6.1: the build-up no longer widens it
            SkyOpen = Mathf.Clamp01(Direct / 0.5f);
            Enclosed = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.85f, SkyOpen));
            Coverage = up > 0 ? (float)upHit / up : 0f;
            int best = 0; for (int i = 1; i < RoofCount.Length; i++) if (RoofCount[i] > RoofCount[best]) best = i;
            RoofMat = upHit > 0 ? (MaterialType)best : MaterialType.None;

            // gunshots
            Room = Mathf.Clamp01(1f - geo / n);
            Size = SizeFrom(MeanFreePath);
            HardRoom = Mathf.InverseLerp(0.5f, 0.85f, Room) * Mathf.InverseLerp(0.15f, 0.04f, a);
            Size = Mathf.Lerp(Size, 1f, HardRoom);
            float loss = p + (1f - p) * a;
            Rt60 = loss > 1e-4f && !float.IsNaN(MeanFreePath) ? Mathf.Min(10f, 6f * MeanFreePath / (343f * Mathf.Log10(1f / Mathf.Max(1e-4f, 1f - loss)))) : 10f;
            Valid = true;
        }

        private static float SizeFrom(float mfp) => float.IsNaN(mfp) ? 1f : Mathf.Lerp(0.5f, 1f, Mathf.InverseLerp(2.5f, 7f, mfp));

        // Your shot's room, measured at the muzzle when you fire: 64 directions, up to 3 casts each, all at once.
        // Reused while the muzzle stays within 0.15 m for 0.1 s.
        private static readonly Vector3[] Fib64 = BuildFib(64);
        private static readonly PathState[] Sync = new PathState[64];

        public static bool MeasureAt(Vector3 o)
        {
            if (!Valid || !_cmds.IsCreated) return false;
            if ((o - _muzzleAt).sqrMagnitude < 0.0225f && Time.time - _muzzleTime < 0.1f) return !float.IsNaN(MuzzleRoom);
            Perf.Begin();
            try
            {
                int block = EnclosureProbe.CurrentMask();
                int mask = V2Mask(block);
                var qp = new QueryParameters(mask, false, QueryTriggerInteraction.Ignore, false);
                Quaternion rot = UnityEngine.Random.rotationUniform;
                for (int i = 0; i < 64; i++) { Sync[i] = default; Sync[i].Active = true; Sync[i].O = o; Sync[i].D = rot * Fib64[i]; Sync[i].First = Sync[i].D; Sync[i].E = Sync[i].G = Sync[i].W = 1f; Sync[i].Cut = 22000f; }
                Stats s = default;
                float geo = 0f; int up = 0, upHit = 0;
                for (int leg = 0; leg < 3; leg++)
                {
                    int n = 0;
                    for (int i = 0; i < 64; i++) if (Sync[i].Active) { _cmds[n] = new RaycastCommand(Sync[i].O, Sync[i].D, qp, MaxLeg); Idx[n++] = i; }
                    if (n == 0) break;
                    RaycastCommand.ScheduleBatch(_cmds.GetSubArray(0, n), _hits.GetSubArray(0, n), 8, 1).Complete();
                    RaysCast += n; CountedRay.Rays += n;
                    for (int k = 0; k < n; k++)
                    {
                        int i = Idx[k];
                        _syncFate = Fate.None;
                        Advance(ref Sync[i], i, _hits[k], block, false, ref s);
                        if (!Sync[i].Active && (_syncFate == Fate.Sky || _syncFate == Fate.SkyAfterBounces) && Sync[i].GeoBounces <= 1) geo += Sync[i].G;
                        if (leg == 0 && Sync[i].First.y > 0.6f) { up++; if (Sync[i].FirstHit && Sync[i].FirstHitM < 40f) upHit++; }
                    }
                }
                MuzzleRoom = Mathf.Clamp01(1f - geo / 64f);
                float mfp = s.LenN > 0 ? (float)(s.Len / s.LenN) : float.NaN;
                float a = s.Hits > 0 ? (float)(s.Alpha / s.Hits) : 0.05f;
                MuzzleHardRoom = Mathf.InverseLerp(0.5f, 0.85f, MuzzleRoom) * Mathf.InverseLerp(0.15f, 0.04f, a);
                MuzzleSize = Mathf.Lerp(SizeFrom(mfp), 1f, MuzzleHardRoom);
                MuzzleCoverage = up > 0 ? (float)upHit / up : 0f;
                _muzzleAt = o; _muzzleTime = Time.time;
                return true;
            }
            finally { Perf.End(Perf.Part.AcousticProbe); }
        }

        private static Vector3 CosineHemisphere(Vector3 n)
        {
            float u = UnityEngine.Random.value, v = UnityEngine.Random.value;
            float r = Mathf.Sqrt(u), phi = 2f * Mathf.PI * v;
            Vector3 t = Mathf.Abs(n.y) < 0.9f ? Vector3.Cross(n, Vector3.up).normalized : Vector3.Cross(n, Vector3.right).normalized;
            Vector3 b = Vector3.Cross(n, t);
            return (t * (r * Mathf.Cos(phi)) + b * (r * Mathf.Sin(phi)) + n * Mathf.Sqrt(Mathf.Max(0f, 1f - u))).normalized;
        }

        private static Vector3[] BuildFib(int count)
        {
            var v = new Vector3[count];
            float ga = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (int i = 0; i < count; i++)
            {
                float y = 1f - 2f * (i + 0.5f) / count, r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y)), phi = i * ga;
                v[i] = new Vector3(Mathf.Cos(phi) * r, y, Mathf.Sin(phi) * r);
            }
            return v;
        }

        // ---------------- logs ----------------

        public const string Header = "t,level,levelDb,direct,traced,tail,remaining,escapeRate,absorption,meanFreePathM,rt60S,skyOpen,enclosed,coverage,roofMat,cutoffHz,arrivalAz,arrivalEl,focus,diffuseShare,room,size,hardRoom,raysPerS,escapeRateOld,tailOld,deepEscShare,deepRemShare,escapes,openings,relErr,smoothS";

        private static float _lastRaysT; private static long _lastRays; private static float _raysPerS;

        private static string RowCsv(float t, Transform lt)
        {
            float now = Time.realtimeSinceStartup;
            if (now - _lastRaysT >= 1f) { _raysPerS = (RaysCast - _lastRays) / Mathf.Max(0.001f, now - _lastRaysT); _lastRays = RaysCast; _lastRaysT = now; }
            return string.Join(",", DiagLog.F(t, "0.00"), DiagLog.F(Level), DiagLog.F(10f * Mathf.Log10(Mathf.Max(Level, 1e-4f)), "0.0"),
                DiagLog.F(Direct), DiagLog.F(Traced), DiagLog.F(Tail), DiagLog.F(Remaining), DiagLog.F(EscapeRate), DiagLog.F(Absorption),
                DiagLog.F(MeanFreePath, "0.0"), DiagLog.F(Rt60, "0.00"), DiagLog.F(SkyOpen), DiagLog.F(Enclosed), DiagLog.F(Coverage), RoofMat.ToString(),
                DiagLog.F(CutoffHz, "0"), DiagLog.F(RelAz(lt, Arrival), "0"), DiagLog.F(Mathf.Asin(Mathf.Clamp(Arrival.y, -1f, 1f)) * Mathf.Rad2Deg, "0"),
                DiagLog.F(Focus), DiagLog.F(DiffuseShare), DiagLog.F(Room), DiagLog.F(Size), DiagLog.F(HardRoom), DiagLog.F(_raysPerS, "0"),
                DiagLog.F(EscapeRateOld), DiagLog.F(TailOld), DiagLog.F(DeepEscShare), DiagLog.F(DeepRemShare), Escapes.ToString(), Openings.ToString(),
                DiagLog.F(RelErr), DiagLog.F(SmoothS, "0.00"));
        }

        public static float RelAz(Transform lt, Vector3 dir)
        {
            if (lt == null) return float.NaN;
            Vector3 f = lt.forward; f.y = 0f; Vector3 d = new Vector3(dir.x, 0f, dir.z);
            return f.sqrMagnitude > 1e-4f && d.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(f, d, Vector3.up) : float.NaN;
        }

        public const string PathsHeader = "t,mark,dir,azimuthDeg,elevationDeg,fate,casts,direct,reflected,remaining,geo,cutoffHz,viaGlass,guided,weight,finalFate,hits";

        // F10: every slot's latest path (paths.csv)
        public static void DumpPaths(int mark, float t)
        {
            if (!DiagLog.IsOpen || !Valid) return;
            Transform lt = MonoBehaviourSingleton<SpatialAudioSystem>.Instance.ListenerTransform;
            var sb = new StringBuilder(256);
            for (int i = 0; i < M; i++)
            {
                ref Result r = ref Latest[i];
                if (r.Fate == Fate.None) continue;
                sb.Clear();
                for (int k = 0; k < HitN[i]; k++)
                {
                    Collider c = HitCol[i * MaxPts + k];
                    if (c == null) { sb.Append("(gone) "); continue; }
                    sb.Append(c.name).Append(':').Append(Surfaces.Material(c)).Append('@').Append(HitM[i * MaxPts + k].ToString("0.0")).Append(' ');
                }
                DiagLog.Row("paths.csv", string.Join(",", DiagLog.F(t, "0.00"), mark.ToString(), i.ToString(), DiagLog.F(RelAz(lt, r.First), "0"),
                    DiagLog.F(Mathf.Asin(Mathf.Clamp(r.First.y, -1f, 1f)) * Mathf.Rad2Deg, "0"), r.Fate.ToString(), r.Casts.ToString(),
                    DiagLog.F(r.Direct), DiagLog.F(r.Reflected), DiagLog.F(r.Remaining), DiagLog.F(r.Geo), DiagLog.F(r.Cut, "0"), r.ViaGlass ? "1" : "0",
                    r.Guided ? "1" : "0", DiagLog.F(r.W), r.Final.ToString(),
                    sb.ToString().Replace(',', ';').Trim()));
            }
        }

        public static string OverlayLine()
        {
            if (!Plugin.AcousticProbeOn.Value) return "";
            if (!Valid) return "probe v2: -\n";
            return $"probe v2: level {Level:0.00} ({10f * Mathf.Log10(Mathf.Max(Level, 1e-4f)):0.0} dB: direct {Direct:0.00} bounced {Traced:0.00} build-up {Tail:0.00}, was {TailOld:0.00})  " +
                   $"{CutoffHz:0} Hz  focus {Focus:0.00}  sky {SkyOpen:0.00}  roof {Coverage:0.00} {RoofMat}  room {Room:0.00} RT {Rt60:0.00} s  " +
                   $"out {Escapes} paths, {Openings} openings, error {RelErr:0.00}, smooth {SmoothS:0.0} s  " +
                   $"ambience {(Plugin.AmbienceV2On.Value ? "v2" : "v1")} guns {(Plugin.GunV2On.Value ? "v2" : "v1")}\n";
        }

        public static string Summary()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"ACOUSTIC PROBE V2 (ambience {(Plugin.AmbienceV2On.Value ? "ON" : "off")}, gunshots {(Plugin.GunV2On.Value ? "ON" : "off")}, glass loss {Plugin.GlassLossDb.Value:0} dB, build-up x{Plugin.RoomBuildUp.Value:0.00}, guided rays {(Plugin.GuidedRays.Value ? "ON" : "off")})");
            sb.AppendLine($"  rays cast {RaysCast}, paths completed {PathsDone}, guided paths aimed at openings {GuidedPaths}, most openings remembered at once {MaxOpenings}");
            return sb.ToString();
        }
    }
}
