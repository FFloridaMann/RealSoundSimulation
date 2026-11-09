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
    //   Arrival    the direction it comes from, Focus 0 all round .. 1 one direction (0.6.5: by share of what you hear)
    //   SkyOpen    direct escape / half the sphere (1 under open sky); Enclosed from it (indoor layers, directional beds)
    //   Coverage   share of the upward cone (within 53 deg of straight up) covered within 40 m; RoofMat (even directions only)
    //   SmoothS    how long the users of Level should average it (confidence)
    //   For gunshots: Room = 1 - share escaping within one wall/roof bounce (floors don't count, thin surfaces by "3f"),
    //   MeanFreePath, Absorption, Size (0.5 small .. 1), HardRoom, Rt60 (estimate)
    // 0.6.5 (raid 25, Capital 1F: rain from both sides, from behind, "the bounce is deciding the direction instead of how
    // loud it is"): no path really got out there. The only "escapes" fell through a hole in the floor to the map's catch
    // plane 48 m below and came back UP through the building: raycasts don't hit the back of one-sided floor meshes. With
    // 1-2 of them and focus ~1, each one set the direction alone, while 85-90% of the level was build-up with no direction.
    //   LEAK FIX: all casts hit back faces too (QueryParameters hitBackfaces); a back face reflects like the front (a floor
    //   seen from below is still a floor). The far side of a pane/object a path has just passed through is skipped (no
    //   second loss). Every escape gets the roof test (back faces on), not only level and downward legs.
    //   DIRECTION BY WHAT YOU HEAR: the direct/bounced part keeps its earliest-arrival direction, but counts by its share of
    //   the level, and only firmly when several paths agree (ESS / (ESS + 3)). The build-up part gets its own direction:
    //   towards where escaping paths left the building (each escape's last contact: the glass or frame it passed through,
    //   or the last surface it came off), energy-weighted over ~3 s, never fully focused (x0.6: a diffuse field
    //   surrounds you). Arrival/Focus = the sum of both.
    // 0.6.8 EXIT POINTS (user: "the rain would come from the window or door itself"): for escaping paths while you're
    //   inside (up to ~200 a second), the point where the escaping leg leaves cover - roof tests along it at 0, 0.75, 1.5,
    //   3 ... 48 m, then halved twice - i.e. the window or door it went out of. With "9j Rain from openings" the rain's
    //   direction points at those exit points (near part and build-up) instead of the first leg. Through(s): how much
    //   sound from horizontal direction s gets in and from where (exit points), for "9k Distant gunfire through openings".
    // 0.6.7 (raid 26): (a) the build-up's direction is where escaping paths ARRIVE from at you - their first leg, energy-
    //   weighted over ~3 s, deep escapes counted x2 (only half the paths go deep). By reciprocity that is the direction the
    //   outside's energy reaches you from (the acoustic intensity), e.g. through your room's door towards the open part of
    //   the building. 0.6.5 used each escape's last contact, mostly a floor spot (40-75 deg below you), and it wandered.
    //   (b) ROOF RAIN traced like the outside: the top roof's underside is a sound source (rain hitting it). A path's first
    //   hit on a downward-facing surface with nothing above it (tested from 40 m above, cached per collider and 1 m cell)
    //   collects its energy and its first-leg direction. RoofLevel = that energy per path / 0.9 (a room right under its
    //   roof); RoofArrival / RoofFocus give the direction. So the floor below a roof hears it down the stairwell, a spot
    //   under a mezzanine hears the high roof beside it, and a hole in the roof just means fewer paths hit it.
    // 0.6.6: "9g Ray quality" (user idea) sets paths per second AND how many sweeps are averaged, so the averaging window
    // stays ~0.6 s and more paths really mean less noise (noise falls with the square root of the path count):
    //   Low 320/s, 1 sweep | Medium 640/s, 2 sweeps (0.6.5) | High 1280/s, 4 sweeps | Ultra 2560/s, 8 sweeps. Cost scales
    //   the same way (raid 25, Medium: 16 ms per second of game time).
    // 0.6.1: direction from the earliest sound (precedence, exp(-path length / 20 m)); two sweeps averaged; kind cache.
    internal class AcousticProbe : MonoBehaviour
    {
        public const int N = 128, G = 64, M = N + G;
        public const int EarlyBounces = 3, MaxBounces = 8, MaxCasts = 12, MaxPts = MaxCasts + 1;
        private const int LateFrom = 5, MaxLobes = 16;
        private const float MaxLeg = 60f, Skin = 0.02f, PrecedenceM = 20f;
        private const float DeepShare = 0.5f, StatTau = 3f, LobeTau = 2f, LobeFloor = 0.05f, DiffuseFocus = 0.6f, RoofRef = 0.9f;
        private const float UpY = 0.5f, RoofRefUp = 0.85f; // 0.6.9: "upward" = first leg more than 30 deg up
        private const float UpExitShare = 0.35f, SideDiffraction = 0.05f; // 0.6.9: Through() credit for exits up / facing away
        private const float MovingTau = 0.5f; // 0.6.14: running-sum memory when you move fast
        private static Vector3 _prevListener; private static bool _hasPrevListener; private static float _speed;
        // 0.6.32 "9s": what has come in over the last 0.4 s, compared with the 3 s memory
        private const float FastTau = 0.4f, ChangeCooldownS = 0.25f, ChangeFollowS = 1f;
        private const double ChangeZ = 3.5, MinRatio = 1.6, MinRoofDiff = 0.02, MinLeakDiff = 0.02, MinFastStarts = 8, MinFastDeep = 4;
        private const int FollowMinEscapes = 8; // 0.6.34: fewer escaping paths than this can't justify a quick rise
        private static float _levelTrend;
        private const float ShaftFreeM = 6f; // 0.6.34 "9u": roof rain carries this far along a bounced path before it starts to fade
        private const float TauRisePerS = 1.67f; // 0.6.33: memory grows back from 0.5 s to 3 s over about 1.5 s
        private static float _heldTau = StatTau;
        private static float _lastChange = -100f;
        private static Stats _fast;
        public static int ChangesRoof, ChangesLeak, ChangesStill;
        public static float StatTauNow = 3f;
        private const float RoofBounceDull = 0.7f; // 0.6.10: roof paths' brightness x this per reflection
        public static float RoofCutoffHz = 22000f; // 0.6.10: how bright the roof rain arrives (energy-weighted)
        private static float _startedUpW;
        public static float RoofLevelAll; // 0.6.9: the 0.6.7 measure (energy per path of every direction), logged
        public enum RayQuality { Low, Medium, High, Ultra }
        public const int MaxSweeps = 8;
        public static int Sweeps = 2;
        private static float _rate = 640f;
        private static int _quality = -1;
        private const int MaxActive = 1024;
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
            public Vector3 Exit; // escaped: direction from you to where it left the building (0.6.5)
            public Vector3 Out, ExitPt; public bool HasExit; // 0.6.8: escaping leg's direction; exit point (window/door)
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
            public Vector3 ExitPt; public bool HasExitPt;
            public Vector3 LastPt, ExitDir;
            public bool HasLast, RoofDone;
            public float RoofGot; // 0.6.29: the best share of roof rain this path has reached so far
            public int LastPassId, LastPassCast;
        }

        // Running sums (exponential, StatTau). Everything is weighted by the path's importance weight W.
        private struct Stats
        {
            public double Alpha, Hits, Len, LenN, CutLog;
            public double LateEsc, LateLeg, LateLegN;   // energy escaping / entering legs after LateFrom+ reflections
            public double OldEsc, OldLeg;               // 0.6.3's leak rate: rays, legs after 2-3 reflections (log only)
            public double DeepBase, DeepEsc, DeepRem;   // energy at the 3rd reflection, escaping in 4-8, still bouncing after
            public double ExX, ExY, ExZ, ExW, ExN;      // 0.6.7: first-leg directions of escaping paths (energy-weighted)
            public double XxX, XxY, XxZ, XxW, XxN; // 0.6.8: directions to exit points (energy-weighted)
            public double RoofE, RoofX, RoofY, RoofZ, RoofN, Starts; // 0.6.7: energy reaching the top roof, its first legs; paths
            public double UpRoofE, UpStarts; // 0.6.9: the same for paths that start upwards (first leg y > UpY), weighted
            public double UpRoofRaw;         // 0.6.34: UpRoofE before "9u", to log what the shaft took off
            public double RoofCutLog;        // 0.6.10: energy-weighted log of how bright roof paths arrive
            public double UpRoofCutLog;      // 0.6.14: the same for upward paths
            public void Scale(double k)
            {
                UpRoofE *= k; UpStarts *= k; RoofCutLog *= k; UpRoofCutLog *= k; UpRoofRaw *= k;
                Alpha *= k; Hits *= k; Len *= k; LenN *= k; CutLog *= k; LateEsc *= k; LateLeg *= k; LateLegN *= k;
                OldEsc *= k; OldLeg *= k; DeepBase *= k; DeepEsc *= k; DeepRem *= k;
                ExX *= k; ExY *= k; ExZ *= k; ExW *= k; ExN *= k;
                XxX *= k; XxY *= k; XxZ *= k; XxW *= k; XxN *= k;
                RoofE *= k; RoofX *= k; RoofY *= k; RoofZ *= k; RoofN *= k; Starts *= k;
            }
            public void Add(in Stats o)
            {
                UpRoofE += o.UpRoofE; UpStarts += o.UpStarts; RoofCutLog += o.RoofCutLog; UpRoofCutLog += o.UpRoofCutLog; UpRoofRaw += o.UpRoofRaw;
                Alpha += o.Alpha; Hits += o.Hits; Len += o.Len; LenN += o.LenN; CutLog += o.CutLog; LateEsc += o.LateEsc;
                LateLeg += o.LateLeg; LateLegN += o.LateLegN; OldEsc += o.OldEsc; OldLeg += o.OldLeg;
                DeepBase += o.DeepBase; DeepEsc += o.DeepEsc; DeepRem += o.DeepRem;
                ExX += o.ExX; ExY += o.ExY; ExZ += o.ExZ; ExW += o.ExW; ExN += o.ExN;
                XxX += o.XxX; XxY += o.XxY; XxZ += o.XxZ; XxW += o.XxW; XxN += o.XxN;
                RoofE += o.RoofE; RoofX += o.RoofX; RoofY += o.RoofY; RoofZ += o.RoofZ; RoofN += o.RoofN; Starts += o.Starts;
            }
        }

        // An opening: where an escaping path's first leg went (wall point, or 20 m along it for a straight escape).
        private struct Lobe { public Vector3 P; public float W; }

        // the last two sweeps' results (aggregated together), and the latest path per slot with its points and hits
        // (ray view, paths.csv). Slots 0..127 are the even directions, 128..191 the guided paths.
        public static readonly Result[] Results = new Result[MaxSweeps * M];
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
        private static int _started;
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
            Rt60 = float.NaN, RelErr, SmoothS = 0.35f, FocusEarly, FocusBuildUp, BuildUpCount, RoofLevel, RoofFocus, RoofPaths;
        public static int Escapes;
        public static Vector3 Arrival = Vector3.forward, BuildUpDir, RoofArrival = Vector3.up;
        public static long RoofTests, ExitsFound, ExitsMissed;
        private static float _exitBudget;
        private const float ExitsPerSecond = 200f;
        private static readonly Dictionary<long, bool> RoofCache = new Dictionary<long, bool>();
        public static long BackfaceHits, ExitFacesSkipped, SkyRejected;
        public static MaterialType RoofMat = MaterialType.None;
        public static long RaysCast, PathsDone;
        private static readonly int[] RoofCount = new int[64];

        // muzzle (gunshots): measured when you fire
        public static float MuzzleRoom = float.NaN, MuzzleSize = float.NaN, MuzzleCoverage = float.NaN, MuzzleHardRoom = float.NaN;
        private static Vector3 _muzzleAt; private static float _muzzleTime = -10f;

        public static void Reset()
        {
            DropPending(); // 0.6.15: nothing may still be writing _hits when the paths are cleared
            Valid = false; Level = Room = Size = HardRoom = Rt60 = MeanFreePath = float.NaN;
            MuzzleRoom = MuzzleSize = MuzzleCoverage = MuzzleHardRoom = float.NaN; _muzzleTime = -10f;
            for (int i = 0; i < Results.Length; i++) Results[i] = default;
            for (int i = 0; i < M; i++) { Latest[i] = default; PtN[i] = 0; HitN[i] = 0; }
            for (int i = 0; i < HitCol.Length; i++) HitCol[i] = null;
            for (int i = 0; i < MaxLobes; i++) Lobes[i] = default;
            _gN = 0; Openings = MaxOpenings = 0; GuidedPaths = 0; SmoothS = 0.35f; RelErr = 0f; Escapes = 0;
            BackfaceHits = ExitFacesSkipped = SkyRejected = 0; FocusEarly = FocusBuildUp = BuildUpCount = RoofLevel = RoofLevelAll = RoofFocus = RoofPaths = _startedUpW = 0f; RoofCutoffHz = 22000f; _hasPrevListener = false; _speed = 0f; StatTauNow = StatTau; BuildUpDir = Vector3.zero; RoofArrival = Vector3.up;
            RoofTests = 0; RoofCache.Clear(); FloorCache.Clear(); Array.Clear(FloorHist, 0, FloorHist.Length); OldRoofTestWrong = 0; FloorMoreSpots = FloorWasTop = 0; ExitsFound = ExitsMissed = 0; _exitBudget = 0f;
            KindCache.Clear(); _layer = 0; _cursor = 0;
            for (int i = 0; i < MaxActive; i++) Paths[i].Active = false;
            _acc = default; _fast = default; _lastChange = -100f; _heldTau = StatTau; ChangesRoof = ChangesLeak = ChangesStill = 0; _levelTrend = 0f; RoofLossDb = 0f; FloorsNow = FloorsOldNow = -1; Array.Clear(FloorVotes, 0, FloorVotes.Length); Array.Clear(FloorVotesOld, 0, FloorVotesOld.Length);
            RaysCast = PathsDone = 0; _budget = 0f; _nextRow = 0f; _maskBlock = -1; _quality = -1;
        }

        private void OnDestroy()
        {
            DropPending();
            if (_cmds.IsCreated) _cmds.Dispose();
            if (_hits.IsCreated) _hits.Dispose();
        }

        private void Update()
        {
            bool inRaid = SpatialAudioSystem.Initialized && MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated && GamePlayerOwner.MyPlayer != null;
            if (inRaid && Plugin.RayViewKey.Value.IsDown()) RayView.Toggle();
            if (!inRaid || !Plugin.AcousticProbeOn.Value) { DropPending(); Valid = false; RayView.Hide(); return; }
            _ranFrame = Time.frameCount;
            Perf.Begin();
            try { Step(MonoBehaviourSingleton<SpatialAudioSystem>.Instance); }
            catch (Exception e) { DiagRunner.ReportOnce("acoustic probe v2", e); }
            finally { Perf.End(Perf.Part.AcousticProbe); }
            try { RayView.Tick(); } catch (Exception e) { DiagRunner.ReportOnce("probe ray view", e); }
        }

        // 0.6.16: the batch scheduled in Update is finished here, in the same frame. 0.6.15 left it to the next frame's
        // Update, so it spanned FixedUpdate, where physics moves colliders - not documented as safe for raycast jobs. This
        // still overlaps the rays with the rest of the frame's Update work.
        private void LateUpdate()
        {
            if (_hasPending)
            {
                Perf.Begin();
                try { FinishPending(); }
                catch (Exception e) { DiagRunner.ReportOnce("acoustic probe v2 (finish)", e); }
                finally { Perf.End(Perf.Part.AcousticProbe); }
            }
            if (_ranFrame == Time.frameCount) // 0.6.20: the probe ran in raid this frame
            {
                try { BatchWait.FrameEnd(); }
                catch (Exception e) { DiagRunner.ReportOnce("probe batch wait", e); }
            }
        }
        private static int _ranFrame = -1; // 0.6.20

        private static void Step(SpatialAudioSystem sas)
        {
            Transform lt = sas.ListenerTransform;
            if (lt == null) return;
            if (!_cmds.IsCreated) { _cmds = new NativeArray<RaycastCommand>(MaxActive, Allocator.Persistent); _hits = new NativeArray<RaycastHit>(MaxActive, Allocator.Persistent); }
            FinishPending(); // last frame's batch
            int block = EnclosureProbe.CurrentMask();
            int mask = V2Mask(block);
            _blockMask = block;
            float dt = Mathf.Max(Time.unscaledDeltaTime, 0f);
            _listener = lt.position;

            // openings fade unless found again
            float fade = Mathf.Exp(-dt / LobeTau);
            for (int k = 0; k < MaxLobes; k++) { Lobes[k].W *= fade; if (Lobes[k].W < 1e-6f) Lobes[k].W = 0f; }

            // start new paths
            ApplyQuality();
            _budget = Mathf.Min(_budget + _rate * dt, Mathf.Max(32f, _rate * 0.1f));
            _exitBudget = Mathf.Min(_exitBudget + ExitsPerSecond * dt, 20f);
            for (int i = 0; i < MaxActive && _budget >= 1f; i++)
            {
                if (Paths[i].Active) continue;
                _budget -= 1f;
                int j = _cursor;
                bool guided = j >= N;
                Vector3 d = guided ? SampleGuide(_listener) : _rot * Fib[j];
                float wStart = WeightFor(d, _listener);
                Start(ref Paths[i], i, j, _listener, d, wStart, guided);
                _started++;
                if (d.y > UpY) _startedUpW += wStart;
                if (guided && _gN > 0) GuidedPaths++;
                _cursor++;
                if (_cursor >= M) { _cursor = 0; _rot = UnityEngine.Random.rotationUniform; _layer = (_layer + 1) % Sweeps; BuildGuide(_listener); }
            }

            // one leg for every path in flight, in one batch
            var qp = new QueryParameters(mask, false, QueryTriggerInteraction.Ignore, true); // 0.6.5: back faces too
            int n = 0;
            for (int i = 0; i < MaxActive; i++)
            {
                if (!Paths[i].Active) continue;
                _cmds[n] = new RaycastCommand(Paths[i].O, Paths[i].D, qp, MaxLeg);
                Idx[n++] = i;
            }
            // 0.6.14: movement-gated memory. 3 s while you stand or walk slowly (< 0.5 m/s), down to 0.5 s from 1.5 m/s: the
            // field changes the moment you step behind a wall (raid 30 stairwell: escapes went to 0 but the level took 3 s
            // to fall). All running sums use it (level, build-up direction, roof); the openings memory (LobeTau) does not.
            if (dt > 1e-4f && _hasPrevListener) _speed += (Vector3.Distance(_listener, _prevListener) / dt - _speed) * (1f - Mathf.Exp(-dt / 0.2f));
            _prevListener = _listener; _hasPrevListener = true;
            // 0.6.32 "9s": a fast estimate beside the slow one, and the memory ends when they really disagree (DetectChange), not by speed.
            bool detect = Plugin.ProbeChangeDetect.Value;
            float nowT = Time.time;
            // 0.6.33: raid 42's stairwell. Without the speed rule the memory stayed at 3 s on the move, and values too small to test never ended it.
            // So moving caps the memory again; it now grows back over about 1.5 s after you stop instead of jumping to 3 s.
            float speedTau = Mathf.Lerp(StatTau, MovingTau, Mathf.InverseLerp(0.5f, 1.5f, _speed));
            _heldTau = speedTau < _heldTau ? speedTau : Mathf.Min(speedTau, _heldTau + dt * TauRisePerS);
            float tauUsed = detect ? _heldTau : speedTau;
            StatTauNow = detect ? Mathf.Min(tauUsed, nowT - _lastChange) : speedTau;
            _acc.Scale(Math.Exp(-dt / tauUsed));
            _fast.Scale(Math.Exp(-dt / FastTau));
            for (int v = 0; v < FloorVotes.Length; v++) { FloorVotes[v] *= Mathf.Exp(-dt); FloorVotesOld[v] *= Mathf.Exp(-dt); }
            _acc.Starts += _started; _fast.Starts += _started; _started = 0;
            _acc.UpStarts += _startedUpW; _fast.UpStarts += _startedUpW; _startedUpW = 0f;
            if (detect) DetectChange(nowT);
            if (n > 0)
            {
                // 0.6.15: the batch runs on the worker threads while the frame goes on; since 0.6.16 its hits are processed in
                // LateUpdate of the same frame (FinishPending; also at the start of the next Update if LateUpdate missed it).
                // Was: at the start
                // of the next frame (FinishPending) instead of the main thread waiting here (raid 31 performance review)
                _pending = RaycastCommand.ScheduleBatch(_cmds.GetSubArray(0, n), _hits.GetSubArray(0, n), 8, 1);
                JobHandle.ScheduleBatchedJobs();
                _pendingN = n; _pendingBlock = block; _hasPending = true;
                RaysCast += n; CountedRay.Rays += n;
            }
            Aggregate();
            if (Time.time >= _nextRow)
            {
                _nextRow = Time.time + 0.125f;
                if (Valid && DiagLog.IsOpen) DiagLog.Row("probe2.csv", RowCsv(Time.time - DiagRunner.RaidStartTime, lt));
            }
        }

        // 0.6.15: the batch scheduled last frame (null-safe; also called before anything else reuses _cmds/_hits)
        private static JobHandle _pending;
        private static int _pendingN, _pendingBlock;
        private static bool _hasPending;

        private static void FinishPending()
        {
            if (!_hasPending) return;
            _hasPending = false;
            long w0 = BatchWait.Now(); bool wasDone = _pending.IsCompleted; // 0.6.20 (6.0b): how long the main thread waits here
            _pending.Complete();
            BatchWait.AddBatch(BatchWait.Ms(w0), wasDone, _pendingN);
            Stats s = default;
            for (int k = 0; k < _pendingN; k++)
            {
                int i = Idx[k];
                if (Paths[i].Active) Advance(ref Paths[i], i, _hits[k], _pendingBlock, true, ref s);
            }
            _acc.Add(s);
            _fast.Add(s);
        }

        // Complete without using the results (probe off, raid over): the paths are simply cast again later.
        private static void DropPending()
        {
            if (!_hasPending) return;
            _hasPending = false;
            _pending.Complete();
        }

        // "9g Ray quality": paths per second and sweeps averaged (window ~0.6 s at every level)
        private static void ApplyQuality()
        {
            int q = (int)Plugin.RayQualityCfg.Value;
            if (q == _quality) return;
            _quality = q;
            switch (Plugin.RayQualityCfg.Value)
            {
                case RayQuality.Low: _rate = 320f; Sweeps = 1; break;
                case RayQuality.High: _rate = 1280f; Sweeps = 4; break;
                case RayQuality.Ultra: _rate = 2560f; Sweeps = 8; break;
                default: _rate = 640f; Sweeps = 2; break;
            }
            for (int i = Sweeps * M; i < Results.Length; i++) Results[i] = default;
            _layer %= Sweeps;
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
                // 0.6.5: every escape is roof-tested with back faces on (raid 25: legs from under the building went up
                // through one-sided floors and counted as sky because they pointed upwards)
                bool sky = !RoofAbove(end, block);
                if (!sky && p.D.y > 0.5f && record) SkyRejected++;
                p.Len += MaxLeg;
                if (sky) Escape(ref p, slot, record, ref s);
                else End(ref p, slot, Fate.LongInterior, record, ref s);
                return;
            }
            if (record) AddPt(ref p, slot, hit.point);
            // 0.6.5: back faces are hit too. One seen from behind is still a surface (a floor seen from below), except
            // the far side of the pane/object this path has just passed through: no second loss.
            Vector3 nrm = hit.normal;
            bool back = Vector3.Dot(nrm, p.D) > 0f;
            if (back)
            {
                nrm = -nrm;
                if (p.LastPassCast == p.Casts - 1 && p.LastPassId == c.GetInstanceID())
                {
                    if (record) ExitFacesSkipped++;
                    p.Len += hit.distance; p.LastPt = hit.point; p.HasLast = true;
                    p.O = hit.point + p.D * Skin;
                    if (p.Casts >= MaxCasts) End(ref p, slot, Fate.Trapped, record, ref s);
                    return;
                }
                if (record) BackfaceHits++;
            }
            MaterialType m = Surfaces.Material(c);
            if (p.Casts == 1) { p.FirstHit = true; p.FirstHitM = hit.distance; p.FirstMat = m; p.FirstPt = hit.point; }
            // 0.6.7: the first time a path reaches the top roof's underside, it collects the roof rain (deep part x2: only
            // half the paths go on after 3 reflections)
            // 0.6.29 "9p": a ceiling with floors above still lets a little through; a path keeps only its best (it may reach the real
            // roof later, down a stair shaft), and a path that reaches the top roof is done.
            float through = record && !p.RoofDone && nrm.y < -0.3f ? RoofThrough(hit.point, c) : 0f;
            if (record && p.Casts == 1 && !p.Guided && p.First.y > 0.6f && nrm.y < -0.3f && Plugin.RoofCountsFloors.Value) { FloorVotes[Mathf.Min(_lastFloorN, 3)] += 1f; FloorVotesOld[Mathf.Min(_lastFloorOldN, 3)] += 1f; } // 0.6.34: overlay
            if (through > p.RoofGot)
            {
                float gain = through - p.RoofGot;
                p.RoofGot = through;
                if (through >= 0.999f) p.RoofDone = true;
                float er0 = p.E * p.W * (p.Bounces > EarlyBounces ? 1f / DeepShare : 1f) * gain;
                float er = er0 * ShaftLoss(p.Len + hit.distance, p.Bounces); // 0.6.34 "9u"
                s.RoofE += er; s.RoofX += p.First.x * er; s.RoofY += p.First.y * er; s.RoofZ += p.First.z * er; if (through >= 0.1f) s.RoofN++;
                if (p.First.y > UpY) { s.UpRoofE += er; s.UpRoofRaw += er0; }
                // 0.6.10: how bright this path is when it reaches the roof: the dullest surface it passed or reflected off,
                // and RoofBounceDull per reflection on the way (scattering and edge diffraction; a placeholder until the
                // materials overhaul). Straight up: 22 kHz; down a stair shaft after 3 bounces: ~5 kHz.
                float rc = Mathf.Clamp(p.Cut * Mathf.Pow(RoofBounceDull, p.Bounces), 300f, 22000f);
                s.RoofCutLog += Math.Log(rc) * er;
                if (p.First.y > UpY) s.UpRoofCutLog += Math.Log(rc) * er;
            }
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
                    p.LastPassId = c.GetInstanceID(); p.LastPassCast = p.Casts; p.LastPt = hit.point; p.HasLast = true;
                    p.O = hit.point + p.D * Skin;
                    if (p.Casts >= MaxCasts || p.E < 1e-5f) End(ref p, slot, Fate.Trapped, record, ref s);
                    return;
                }
                float wr = 1f / (1f - pp);
                p.E *= (1f - t) * wr; p.G *= (1f - tg) * wr;
            }

            // reflect
            Materials.Probe(m, out float a, out float scatter, out float bright);
            bool ground = nrm.y > 0.7f; // 0.6.5: the facing normal (a floor's underside is not ground)
            p.LastPt = hit.point; p.HasLast = true;
            // 0.6.14 "9n": past the 3rd reflection each further bounce dulls the build-up's brightness (tag 2 read 10-14 kHz
            // deep inside: it should rumble, not hiss)
            float deepK = p.Bounces + 1 > EarlyBounces ? Mathf.Pow(Mathf.Clamp(Plugin.DeepBounceDulling.Value, 0.3f, 1f), p.Bounces + 1 - EarlyBounces) : 1f;
            s.Alpha += a * p.W; s.Hits += p.W; s.CutLog += Math.Log(Mathf.Max(bright * deepK, 100f)) * p.W; // the room's statistics use the real surface
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
            // where it left the building: its last contact (the glass/frame it passed through, or the last surface it
            // came off); a straight escape: its direction
            Vector3 ex = p.D;
            if (p.HasLast) { Vector3 v = p.LastPt - _listener; if (v.sqrMagnitude > 0.25f) ex = v.normalized; }
            p.ExitDir = ex; // logged (paths.csv)
            float e = p.E * p.W;
            // 0.6.8: the exit point (where this leg leaves cover), while you're inside, within the budget
            p.HasExitPt = false;
            if (Enclosed > 0.2f && _exitBudget >= 1f)
            {
                _exitBudget -= 1f;
                p.HasExitPt = FindExit(p.O, p.D, _blockMask, out p.ExitPt);
                if (p.HasExitPt) ExitsFound++; else ExitsMissed++;
            }
            // 0.6.7: the build-up's direction = where escaping paths arrive from at you (first leg), deep ones x2
            float ed = p.Reported ? e / DeepShare : e;
            s.ExX += p.First.x * ed; s.ExY += p.First.y * ed; s.ExZ += p.First.z * ed; s.ExW += ed; s.ExN++;
            if (p.HasExitPt)
            {
                Vector3 xv = p.ExitPt - _listener;
                if (xv.sqrMagnitude > 0.25f) { xv.Normalize(); s.XxX += xv.x * ed; s.XxY += xv.y * ed; s.XxZ += xv.z * ed; s.XxW += ed; s.XxN++; }
            }
            if (!p.Reported) Report(ref p, fate);
            else s.DeepEsc += e;
            View(ref p, slot, fate);
        }

        // Is this downward-facing surface the top roof (nothing above it)? Looked at from 40 m above: the first thing hit
        // going down must be within 1.5 m of the point (the slab's own top). Cached per collider and 1 m cell.
        private static bool IsRoof(Vector3 pt, Collider c)
        {
            long key = ((long)c.GetInstanceID() * 73856093L) ^ ((long)Mathf.FloorToInt(pt.x) * 19349663L) ^ ((long)Mathf.FloorToInt(pt.z) * 83492791L);
            if (RoofCache.TryGetValue(key, out bool roof)) return roof;
            if (RoofCache.Count > 50000) RoofCache.Clear();
            RoofTests++;
            Vector3 top = pt + Vector3.up * 40f;
            roof = !CountedRay.Raycast(top, Vector3.down, out RaycastHit h, 40f, _mask, QueryTriggerInteraction.Ignore) || h.point.y <= pt.y + 1.5f;
            RoofCache[key] = roof;
            return roof;
        }

        // 0.6.29 "9p": how much rain energy from above reaches this ceiling: 1 = nothing above it (the top roof), less for every floor
        // above, by that floor's material. Raid 40: looking down from 40 m said "top roof" at spots with 1-2 floors above (upper floors
        // solid only from below let the ray through), so roof rain read 0.9-1.0 and only an on/off 5-ray rule kept it out. This looks
        // up from the ceiling, counting surfaces from either side; surfaces under 1 m apart are one slab. Cached per collider and 1 m cell.
        private const float FloorGapM = 1f, UntaggedFloorDb = 25f, FloorSearchM = 60f;
        private const int MaxFloorCasts = 16;
        private const float MinLowDetailFloorM = 4f; // 0.6.36: wider than a car both ways, so props above you aren't floors
        private struct Floors { public int N, BlockN; public float Db, BlockDb; } // 0.6.36: Block* = main solid layers only, the count up to 0.6.35
        private static readonly Dictionary<long, Floors> FloorCache = new Dictionary<long, Floors>();
        public static readonly long[] FloorHist = new long[4]; // 0, 1, 2, 3+ floors above
        public static long OldRoofTestWrong;
        public static long FloorMoreSpots, FloorWasTop; // 0.6.36: spots where low-detail floors added a floor; of those, spots that had read as top roof

        private static float RoofThrough(Vector3 pt, Collider c)
        {
            if (!Plugin.RoofCountsFloors.Value) return IsRoof(pt, c) ? 1f : 0f;
            long key = ((long)c.GetInstanceID() * 73856093L) ^ ((long)Mathf.FloorToInt(pt.x) * 19349663L) ^ ((long)Mathf.FloorToInt(pt.z) * 83492791L);
            if (!FloorCache.TryGetValue(key, out Floors f))
            {
                if (FloorCache.Count > 50000) FloorCache.Clear();
                f = CountFloors(pt);
                FloorCache[key] = f;
                FloorHist[Mathf.Min(f.N, 3)]++;
                if (f.N > f.BlockN) { FloorMoreSpots++; if (f.BlockN == 0) FloorWasTop++; }
                if (f.N > 0 && IsRoof(pt, c)) OldRoofTestWrong++;
            }
            bool low = Plugin.RoofCountsLowDetail.Value; // 0.6.36 "9v": both counts are cached, so the switch is live
            int n = low ? f.N : f.BlockN;
            float db = low ? f.Db : f.BlockDb;
            _lastFloorN = n; _lastFloorOldN = f.BlockN;
            if (n == 0) return 1f;
            return Mathf.Pow(10f, -db * Mathf.Max(0f, Plugin.RoofFloorLossScale.Value) / 10f);
        }

        // 0.6.34 "9u" (raid 43 tag 6): a path that bounces its way up a concrete stair shaft to the real roof lost only absorption (2% a bounce),
        // so roof rain arrived almost in full several floors down. Past the first few metres it now fades with the distance travelled. A path
        // straight up doesn't: a roof is a wide source, so a high hall still hears it.
        private static float ShaftLoss(float len, int bounces)
        {
            float rate = Plugin.RoofRainShaftLoss.Value;
            if (rate <= 0f || bounces == 0 || len <= ShaftFreeM) return 1f;
            return Mathf.Pow(10f, -rate * (len - ShaftFreeM) / 10f);
        }

        // 0.6.34: the floors the sound is using, from the straight-up rays' first ceilings (last second or so), for the overlay and probe2.csv
        private static readonly float[] FloorVotes = new float[4], FloorVotesOld = new float[4];
        private static int _lastFloorN, _lastFloorOldN;
        public static int FloorsNow = -1, FloorsOldNow = -1;
        public static float RoofLossDb;

        public static string AboveText()
        {
            string floors = FloorsNow >= 3 ? "3+ floors" : FloorsNow == 2 ? "2 floors" : FloorsNow == 1 ? "1 floor" : "no floors";
            return $"{floors} above the ceiling (9p; main layers alone {(FloorsOldNow >= 3 ? "3+" : FloorsOldNow.ToString())}), surface overhead {RoofMat}, roof rain {RoofLevel:0.00}, faded {-RoofLossDb:0} dB getting here";
        }

        private static Floors CountFloors(Vector3 pt)
        {
            var f = new Floors();
            bool was = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            try
            {
                // 0.6.36 "9v" (raid 44 tag 1, Capital meeting room: roof rain 0.18 with floors above): much of the Capital's floors are low-detail
                // colliders on layers the main mask leaves out, so looking up from a ceiling found nothing and it counted as the top roof.
                // Now those count too when they are large; the old count is kept beside it for the log.
                float lastY = pt.y, blockLastY = pt.y, top = pt.y + FloorSearchM;
                Vector3 o = pt + Vector3.up * Skin;
                for (int i = 0; i < MaxFloorCasts && o.y < top; i++)
                {
                    if (!CountedRay.Raycast(o, Vector3.up, out RaycastHit h, top - o.y, _mask, QueryTriggerInteraction.Ignore)) break;
                    bool block = (_blockMask & (1 << h.collider.gameObject.layer)) != 0;
                    Vector3 size = h.collider.bounds.size;
                    if (block || Mathf.Min(size.x, size.z) >= MinLowDetailFloorM)
                    {
                        float db = Materials.TryGet(Surfaces.Material(h.collider), out Materials.Surface t) && !float.IsInfinity(t.T1k) ? t.T1k : UntaggedFloorDb;
                        if (h.point.y - lastY >= FloorGapM) { f.N++; f.Db += db; }
                        lastY = h.point.y;
                        if (block)
                        {
                            if (h.point.y - blockLastY >= FloorGapM) { f.BlockN++; f.BlockDb += db; }
                            blockLastY = h.point.y;
                        }
                    }
                    o = h.point + Vector3.up * Skin;
                }
            }
            finally { Physics.queriesHitBackfaces = was; }
            return f;
        }

        // 0.6.32 "9s" (from a Fable design review of raid 41): memory used to depend on speed, 3 s below 0.5 m/s and 0.5 s above 1.5 m/s, so
        // stopping stretched out the sound of where you had just been, and creeping through a door into a different room was the worst case.
        // Now the last 0.4 s is compared with the 3 s memory: if they differ by more than chance explains (with the few paths the fast one
        // has), the memory is replaced by the fast estimate. Standing still they agree, so nothing changes.
        private static void DetectChange(float now)
        {
            if (now - _lastChange < ChangeCooldownS) return;
            int kind = 0;
            if (_fast.UpStarts >= MinFastStarts && _acc.UpStarts >= MinFastStarts
                && Differs(_acc.UpRoofE / _acc.UpStarts, _fast.UpRoofE / _fast.UpStarts, _fast.UpStarts, MinRoofDiff)) kind = 1;
            else if (_fast.DeepBase >= MinFastDeep && _acc.DeepBase >= MinFastDeep
                && Differs(_acc.DeepEsc / _acc.DeepBase, _fast.DeepEsc / _fast.DeepBase, _fast.DeepBase, MinLeakDiff)) kind = 2;
            // 0.6.34: the direction check is gone. Raid 43: 56 of 57 resets were direction, 43 of them standing still, because arrival direction
            // from a few paths wanders. Moving already shortens the memory, so it added noise and no speed.
            if (kind == 0) return;
            _acc = _fast;
            _lastChange = now;
            if (kind == 1) ChangesRoof++; else ChangesLeak++;
            if (_speed < 0.2f) ChangesStill++;
        }

        private static bool Differs(double slow, double fast, double n, double minDiff)
        {
            double d = Math.Abs(fast - slow);
            if (d < minDiff) return false;
            // 0.6.33: compare as ratios too, so a change matters by how it sounds (raid 42: fired 7 times a minute standing still)
            if (Math.Abs(Math.Log((fast + 1e-4) / (slow + 1e-4))) < Math.Log(MinRatio)) return false;
            double p = Math.Min(0.99, Math.Max(0.01, slow));
            return d > ChangeZ * Math.Sqrt(p * (1.0 - p) / n);
        }

        private static int _blockMask;
        public static int BlockMask => _blockMask; // 0.6.37: read by 6.2
        public static int FullMask => _mask;

        // 0.6.8: where an escaping leg (from o, direction d) leaves cover: the first point along it with no roof above,
        // sampled at 0, 0.75, 1.5, 3, 6, 12, 24, 48 m and refined twice. At most 10 short casts.
        private static readonly float[] ExitSteps = { 0.75f, 1.5f, 3f, 6f, 12f, 24f, 48f };

        // 0.6.11 (3m): the same test for one ray from outside the probe: where a leg from o along d leaves cover, if it does
        // within maxT (before the wall it hits). t = distance to that point.
        public static bool ExitAlong(Vector3 o, Vector3 d, float maxT, out Vector3 pt, out float t)
        {
            t = 0f;
            if (!FindExit(o, d, EnclosureProbe.CurrentMask(), out pt)) return false;
            t = Vector3.Distance(o, pt);
            return t <= maxT;
        }
        private static bool FindExit(Vector3 o, Vector3 d, int block, out Vector3 pt)
        {
            if (!RoofAbove(o - Vector3.up * 0.5f, block)) { pt = o; return true; }
            float prev = 0f;
            for (int i = 0; i < ExitSteps.Length; i++)
            {
                float t = ExitSteps[i];
                if (RoofAbove(o + d * t - Vector3.up * 0.5f, block)) { prev = t; continue; }
                float lo = prev, hi = t;
                for (int k = 0; k < 2; k++) { float mid = 0.5f * (lo + hi); if (RoofAbove(o + d * mid - Vector3.up * 0.5f, block)) lo = mid; else hi = mid; }
                pt = o + d * hi;
                return true;
            }
            pt = default;
            return false;
        }

        // 0.6.8, "9k": how much sound from horizontal direction s (unit, from you) gets in, 0..1 (open air ~1), and from
        // where: escaping paths whose last leg heads that way (cos^2 of the horizontal part), energy-weighted, x6 (the open-air
        // average of that lobe is 1/6), towards their exit points (else first legs); plus the build-up, leaning the way
        // the building's sound arrives.
        public static float Through(Vector3 s, out Vector3 dir)
        {
            float l = 0f; Vector3 acc = Vector3.zero; int n = 0;
            for (int i = 0; i < Results.Length; i++)
            {
                ref Result r = ref Results[i];
                if (r.Fate == Fate.None) continue;
                n++;
                float e = (r.Direct + r.Reflected) * r.W;
                if (e <= 0f) continue;
                // 0.6.9 (raid 28, last tag: four floors down a stairwell the only way out is up the shaft; the horizontal
                // lobe alone found nothing): every escaping path counts, by how its exit lets the battle's sound in -
                //   side openings facing it: cos^2 of the horizontal part (as before, scaled by how level the leg is);
                //   openings to the sky above: UpExitShare from any direction (the sound comes over the edge and down);
                //   side openings facing away: SideDiffraction (round the building).
                // Geometry only; retune with the materials overhaul.
                float h = Mathf.Sqrt(r.Out.x * r.Out.x + r.Out.z * r.Out.z);
                float c = h > 1e-4f ? (r.Out.x * s.x + r.Out.z * s.z) / h : 0f;
                float face = c > 0f ? c * c : 0f;
                float g = e * (h * (face + SideDiffraction) + Mathf.Max(0f, r.Out.y) * UpExitShare);
                if (g <= 0f) continue;
                l += g;
                Vector3 d = r.First;
                if (r.HasExit) { Vector3 v = r.ExitPt - _listener; if (v.sqrMagnitude > 0.25f) d = v.normalized; }
                acc += d * g;
            }
            if (n == 0 || !Valid) { dir = Arrival; return float.IsNaN(Level) ? 1f : Level; }
            float ld = Mathf.Clamp01(6f * l / n);
            float t = Mathf.Clamp01(ld + Tail);
            Vector3 v2 = (acc.sqrMagnitude > 1e-12f ? acc.normalized * ld : Vector3.zero) + Arrival * (Tail * 0.5f);
            dir = v2.sqrMagnitude > 1e-10f ? v2.normalized : Arrival;
            return t;
        }

        private static bool RoofAbove(Vector3 end, int block)
        {
            bool prev = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            try { return CountedRay.Raycast(end + Vector3.up * 0.5f, Vector3.up, out RaycastHit _, 60f, block, QueryTriggerInteraction.Ignore); }
            finally { Physics.queriesHitBackfaces = prev; }
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
            if (escaped) { r.Exit = p.ExitDir; r.Out = p.D; r.HasExit = p.HasExitPt; r.ExitPt = p.ExitPt; }
            Results[(p.Layer < Sweeps ? p.Layer : 0) * M + p.Dir] = r; // a path started before a quality change
            p.Rep = r;
            PathsDone++;
            if (escaped) Remember(p.First, p.FirstPt, (r.Direct + r.Reflected) * p.W);
        }

        // ray view and paths.csv: the latest path per slot, with where it finally ended
        private static void View(ref PathState p, int slot, Fate final)
        {
            Result r = p.Rep; r.Final = final; r.Casts = p.Casts;
            if (final == Fate.Sky || final == Fate.SkyAfterBounces) r.Exit = p.ExitDir;
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

        private static void Aggregate()
        {
            bool exitsMode = Plugin.RainFromOpenings.Value; // 0.6.8 "9j"
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
                    Vector3 fd = r.First;
                    if (exitsMode && r.HasExit) { Vector3 xv = r.ExitPt - _listener; if (xv.sqrMagnitude > 0.25f) fd = xv.normalized; }
                    arr += fd * pw; pW += pw;
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
            if (esc == 0) RelErr = 1f; // 0.6.14: nothing escaped this window - least confident, longest smoothing (raid 30: 0.35 s)
            SmoothS = 0.35f + 2.15f * Mathf.Clamp01((RelErr - 0.08f) / 0.32f);
            // 0.6.32: the sound just changed, so the listeners of these values follow quickly for a second. Otherwise few escapes still means a
            // long average, which is what stops deep rooms from fluttering.
            // 0.6.34 (raid 43 tags 2-3): with a couple of escaping paths each one is worth several dB (-34 to -20 dB from 2 paths), so a rise
            // on that little evidence keeps the long average. Falling stays quick, so walking deep inside still goes quiet at once.
            _levelTrend += (Level - _levelTrend) * (1f - Mathf.Exp(-Mathf.Max(Time.unscaledDeltaTime, 0f) / 0.5f));
            bool poorRise = esc < FollowMinEscapes && Level > _levelTrend;
            if (Plugin.ProbeChangeDetect.Value && (Time.time - _lastChange < ChangeFollowS || _heldTau < 1f) && !poorRise) SmoothS = 0.35f; // or you are on the move
            // brightness: the direct/bounced paths' own, and for the build-up the room's surfaces (many bounces: duller)
            float roomCut = _acc.Hits > 1 ? (float)Math.Exp(_acc.CutLog / _acc.Hits) * 0.8f : 12000f;
            double wSum = eW + Tail;
            CutoffHz = wSum > 1e-6 ? (float)Math.Exp((cutLog + Tail * Math.Log(Mathf.Max(roomCut, 100f))) / wSum) : 22000f;
            DiffuseShare = Level > 1e-6f ? Mathf.Clamp01(Tail / Level) : 0f;
            // 0.6.5: direction by what you hear. Direct/bounced part: earliest arrivals (precedence), as firm as the number of
            // paths that agree allows. Build-up: towards where escaping paths left the building (~3 s), never pinned.
            float essE = c2 > 1e-12f ? eW * eW / c2 : 0f;
            FocusEarly = pW > 1e-8f ? Mathf.Clamp01(arr.magnitude / pW) * essE / (essE + 3f) : 0f;
            Vector3 aE = arr.sqrMagnitude > 1e-12f ? arr.normalized : Vector3.zero;
            bool useX = exitsMode && _acc.XxN > 2;
            Vector3 ex = useX ? new Vector3((float)_acc.XxX, (float)_acc.XxY, (float)_acc.XxZ) : new Vector3((float)_acc.ExX, (float)_acc.ExY, (float)_acc.ExZ);
            BuildUpCount = (float)(useX ? _acc.XxN : _acc.ExN);
            double exW = useX ? _acc.XxW : _acc.ExW;
            FocusBuildUp = exW > 1e-9 ? Mathf.Clamp01(ex.magnitude / (float)exW) * BuildUpCount / (BuildUpCount + 5f) : 0f;
            BuildUpDir = ex.sqrMagnitude > 1e-12f ? ex.normalized : Vector3.zero;
            float wE = 1f - DiffuseShare;
            Vector3 dir = aE * (FocusEarly * wE) + BuildUpDir * (FocusBuildUp * DiffuseShare * DiffuseFocus);
            // 0.6.14: few escapes -> all round (raid 29 tag 7: no path escaped, yet the sound stayed front-left at focus 0.4)
            Focus = Mathf.Clamp01(dir.magnitude) * esc / (esc + 5f);
            if (dir.sqrMagnitude > 1e-8f) Arrival = dir.normalized; // else all round: keep the last direction
            // 0.6.7: roof rain, traced from the roof
            RoofPaths = (float)_acc.RoofN;
            RoofLevelAll = _acc.Starts > 1 ? Mathf.Clamp01((float)(_acc.RoofE / _acc.Starts) / RoofRef) : 0f;
            // 0.6.9: per UPWARD path (raid 28: under a roof right overhead in a room with big openings, most paths escaped
            // sideways first, so the all-paths level read 0.42 - "half a roof". The roof radiates the same either way.)
            RoofLevel = _acc.UpStarts > 1 ? Mathf.Clamp01((float)(_acc.UpRoofE / _acc.UpStarts) / RoofRefUp) : RoofLevelAll;
            // 0.6.14: brightness from the same upward paths as the level (raid 29 tag 3: 4.4 kHz under a roof right overhead,
            // because most roof energy came from sideways paths after several bounces)
            RoofCutoffHz = _acc.UpRoofE > 1e-9 ? (float)Math.Exp(_acc.UpRoofCutLog / _acc.UpRoofE)
                         : _acc.RoofE > 1e-9 ? (float)Math.Exp(_acc.RoofCutLog / _acc.RoofE) : 22000f;
            RoofLossDb = _acc.UpRoofRaw > 1e-9 && _acc.UpRoofE > 1e-12 ? (float)(10.0 * Math.Log10(_acc.UpRoofE / _acc.UpRoofRaw)) : 0f;
            int fBest = 0; for (int v = 1; v < FloorVotes.Length; v++) if (FloorVotes[v] > FloorVotes[fBest]) fBest = v;
            FloorsNow = FloorVotes[fBest] >= 0.5f ? fBest : -1;
            int fOld = 0; for (int v = 1; v < FloorVotesOld.Length; v++) if (FloorVotesOld[v] > FloorVotesOld[fOld]) fOld = v;
            FloorsOldNow = FloorVotesOld[fOld] >= 0.5f ? fOld : -1;
            Vector3 rv = new Vector3((float)_acc.RoofX, (float)_acc.RoofY, (float)_acc.RoofZ);
            RoofFocus = _acc.RoofE > 1e-9 ? Mathf.Clamp01(rv.magnitude / (float)_acc.RoofE) * RoofPaths / (RoofPaths + 5f) : 0f;
            if (rv.sqrMagnitude > 1e-12f) RoofArrival = rv.normalized;
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

        // 0.6.11 "3k Muzzle lobe": each muzzle direction is weighted by a forward lobe around the aim, ((1 + cos) / 2)^k.
        // Raid 29 tag 1: firing out of a window with the muzzle 0.5 m from the head, half the sphere looked back into the
        // room and the muzzle read "room" (0.72-0.83) although the blast went outside. k = 0 is the sphere (as before).
        // MuzzleRoomSphere keeps the sphere's value for the log.
        public static float MuzzleRoomSphere = float.NaN;
        private static Vector3 _muzzleAim;
        private static readonly float[] LobeW = new float[64];
        public static float LobeWeight(Vector3 d, Vector3 aim, float k) =>
            k <= 0f || aim.sqrMagnitude < 1e-6f ? 1f : Mathf.Pow(Mathf.Clamp01((1f + Vector3.Dot(d, aim)) * 0.5f), k);

        public static bool MeasureAt(Vector3 o) => MeasureAt(o, Vector3.zero);

        public static bool MeasureAt(Vector3 o, Vector3 aim)
        {
            if (!Valid || !_cmds.IsCreated) return false;
            FinishPending(); // 0.6.15: the continuous batch uses the same buffers
            if (aim.sqrMagnitude > 1e-6f) aim.Normalize();
            if ((o - _muzzleAt).sqrMagnitude < 0.0225f && Time.time - _muzzleTime < 0.1f && Vector3.Dot(aim, _muzzleAim) > 0.995f) return !float.IsNaN(MuzzleRoom);
            Perf.Begin();
            try
            {
                int block = EnclosureProbe.CurrentMask();
                int mask = V2Mask(block);
                var qp = new QueryParameters(mask, false, QueryTriggerInteraction.Ignore, true);
                Quaternion rot = UnityEngine.Random.rotationUniform;
                float lk = Mathf.Clamp(Plugin.MuzzleLobe.Value, 0f, 2f), lSum = 0f;
                for (int i = 0; i < 64; i++)
                {
                    Sync[i] = default; Sync[i].Active = true; Sync[i].O = o; Sync[i].D = rot * Fib64[i]; Sync[i].First = Sync[i].D; Sync[i].E = Sync[i].G = 1f; Sync[i].Cut = 22000f;
                    LobeW[i] = LobeWeight(Sync[i].D, aim, lk); lSum += LobeW[i];
                    Sync[i].W = LobeW[i]; // the size/absorption statistics are lobe-weighted too
                }
                Stats s = default;
                float geo = 0f, geoS = 0f, up = 0f, upHit = 0f;
                double waitMs = 0; // 0.6.20 (6.0b)
                for (int leg = 0; leg < 3; leg++)
                {
                    int n = 0;
                    for (int i = 0; i < 64; i++) if (Sync[i].Active) { _cmds[n] = new RaycastCommand(Sync[i].O, Sync[i].D, qp, MaxLeg); Idx[n++] = i; }
                    if (n == 0) break;
                    long w0 = BatchWait.Now();
                    RaycastCommand.ScheduleBatch(_cmds.GetSubArray(0, n), _hits.GetSubArray(0, n), 8, 1).Complete();
                    waitMs += BatchWait.Ms(w0);
                    RaysCast += n; CountedRay.Rays += n;
                    for (int k = 0; k < n; k++)
                    {
                        int i = Idx[k];
                        _syncFate = Fate.None;
                        Advance(ref Sync[i], i, _hits[k], block, false, ref s);
                        if (!Sync[i].Active && (_syncFate == Fate.Sky || _syncFate == Fate.SkyAfterBounces) && Sync[i].GeoBounces <= 1) { geo += Sync[i].G * LobeW[i]; geoS += Sync[i].G; }
                        if (leg == 0 && Sync[i].First.y > 0.6f) { up += LobeW[i]; if (Sync[i].FirstHit && Sync[i].FirstHitM < 40f) upHit += LobeW[i]; }
                    }
                }
                BatchWait.AddMuzzle(waitMs);
                MuzzleRoom = Mathf.Clamp01(1f - geo / Mathf.Max(lSum, 1e-3f));
                MuzzleRoomSphere = Mathf.Clamp01(1f - geoS / 64f);
                _muzzleAim = aim;
                float mfp = s.LenN > 0 ? (float)(s.Len / s.LenN) : float.NaN;
                float a = s.Hits > 0 ? (float)(s.Alpha / s.Hits) : 0.05f;
                MuzzleHardRoom = Mathf.InverseLerp(0.5f, 0.85f, MuzzleRoom) * Mathf.InverseLerp(0.15f, 0.04f, a);
                MuzzleSize = Mathf.Lerp(SizeFrom(mfp), 1f, MuzzleHardRoom);
                MuzzleCoverage = up > 1e-4f ? upHit / up : 0f;
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

        public const string Header = "t,level,levelDb,direct,traced,tail,remaining,escapeRate,absorption,meanFreePathM,rt60S,skyOpen,enclosed,coverage,roofMat,cutoffHz,arrivalAz,arrivalEl,focus,diffuseShare,room,size,hardRoom,raysPerS,escapeRateOld,tailOld,deepEscShare,deepRemShare,escapes,openings,relErr,smoothS,focusEarly,focusBuildUp,buildUpAz,buildUpEl,buildUpCount,roofLevel,roofAz,roofEl,roofFocus,roofCount,roofLevelAll,roofCutoffHz,memoryS,speedMs,fastRoof,changes,floorsNow,roofLossDb,floorsOldNow";

        private static float _lastRaysT; private static long _lastRays; private static float _raysPerS;

        private static string RowCsv(float t, Transform lt)
        {
            float now = Time.realtimeSinceStartup;
            if (now - _lastRaysT >= 1f) { _raysPerS = (RaysCast - _lastRays) / Mathf.Max(0.001f, now - _lastRaysT); _lastRays = RaysCast; _lastRaysT = now; }
            return string.Join(",", DiagLog.F(t, "0.00"), DiagLog.F(Level), DiagLog.F(10f * Mathf.Log10(Mathf.Max(Level, 1e-6f)), "0.0"), // 0.6.9: -60 dB floor, as applied (was -40)
                DiagLog.F(Direct), DiagLog.F(Traced), DiagLog.F(Tail), DiagLog.F(Remaining), DiagLog.F(EscapeRate), DiagLog.F(Absorption),
                DiagLog.F(MeanFreePath, "0.0"), DiagLog.F(Rt60, "0.00"), DiagLog.F(SkyOpen), DiagLog.F(Enclosed), DiagLog.F(Coverage), RoofMat.ToString(),
                DiagLog.F(CutoffHz, "0"), DiagLog.F(RelAz(lt, Arrival), "0"), DiagLog.F(Mathf.Asin(Mathf.Clamp(Arrival.y, -1f, 1f)) * Mathf.Rad2Deg, "0"),
                DiagLog.F(Focus), DiagLog.F(DiffuseShare), DiagLog.F(Room), DiagLog.F(Size), DiagLog.F(HardRoom), DiagLog.F(_raysPerS, "0"),
                DiagLog.F(EscapeRateOld), DiagLog.F(TailOld), DiagLog.F(DeepEscShare), DiagLog.F(DeepRemShare), Escapes.ToString(), Openings.ToString(),
                DiagLog.F(RelErr), DiagLog.F(SmoothS, "0.00"), DiagLog.F(FocusEarly), DiagLog.F(FocusBuildUp),
                BuildUpDir.sqrMagnitude > 0f ? DiagLog.F(RelAz(lt, BuildUpDir), "0") : "", BuildUpDir.sqrMagnitude > 0f ? DiagLog.F(Mathf.Asin(Mathf.Clamp(BuildUpDir.y, -1f, 1f)) * Mathf.Rad2Deg, "0") : "",
                DiagLog.F(BuildUpCount, "0.0"), DiagLog.F(RoofLevel), DiagLog.F(RelAz(lt, RoofArrival), "0"),
                DiagLog.F(Mathf.Asin(Mathf.Clamp(RoofArrival.y, -1f, 1f)) * Mathf.Rad2Deg, "0"), DiagLog.F(RoofFocus), DiagLog.F(RoofPaths, "0.0"), DiagLog.F(RoofLevelAll), DiagLog.F(RoofCutoffHz, "0"), DiagLog.F(StatTauNow, "0.00"), DiagLog.F(_speed, "0.0"),
                DiagLog.F(_fast.UpStarts > 1 ? Mathf.Clamp01((float)(_fast.UpRoofE / _fast.UpStarts) / RoofRefUp) : 0f), (ChangesRoof + ChangesLeak).ToString(),
                FloorsNow.ToString(), DiagLog.F(RoofLossDb, "0.0"), FloorsOldNow.ToString());
        }

        public static float RelAz(Transform lt, Vector3 dir)
        {
            if (lt == null) return float.NaN;
            Vector3 f = lt.forward; f.y = 0f; Vector3 d = new Vector3(dir.x, 0f, dir.z);
            return f.sqrMagnitude > 1e-4f && d.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(f, d, Vector3.up) : float.NaN;
        }

        public const string PathsHeader = "t,mark,dir,azimuthDeg,elevationDeg,fate,casts,direct,reflected,remaining,geo,cutoffHz,viaGlass,guided,weight,finalFate,exitAz,exitEl,exitPointAz,exitPointM,hits";

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
                    r.Exit.sqrMagnitude > 0f ? DiagLog.F(RelAz(lt, r.Exit), "0") : "", r.Exit.sqrMagnitude > 0f ? DiagLog.F(Mathf.Asin(Mathf.Clamp(r.Exit.y, -1f, 1f)) * Mathf.Rad2Deg, "0") : "",
                    r.HasExit ? DiagLog.F(RelAz(lt, r.ExitPt - lt.position), "0") : "", r.HasExit ? DiagLog.F((r.ExitPt - lt.position).magnitude, "0.0") : "",
                    sb.ToString().Replace(',', ';').Trim()));
            }
        }

        public static string OverlayLine()
        {
            if (!Plugin.AcousticProbeOn.Value) return "";
            if (!Valid) return "probe v2: -\n";
            return $"probe v2: level {Level:0.00} ({10f * Mathf.Log10(Mathf.Max(Level, 1e-4f)):0.0} dB: direct {Direct:0.00} bounced {Traced:0.00} build-up {Tail:0.00}, was {TailOld:0.00})  " +
                   $"{CutoffHz:0} Hz  focus {Focus:0.00}  sky {SkyOpen:0.00}  roof {Coverage:0.00} {RoofMat}  room {Room:0.00} RT {Rt60:0.00} s  " +
                   $"out {Escapes} paths, {Openings} openings, error {RelErr:0.00}, smooth {SmoothS:0.0} s, focus applied {Fixes.BedDirection.Outdoor.Focus:0.00}  roof rain {RoofLevel:0.00} focus {RoofFocus:0.00}  " +
                   $"ambience {(Plugin.AmbienceV2On.Value ? "v2" : "v1")} guns {(Plugin.GunV2On.Value ? "v2" : "v1")}\n";
        }

        public static string Summary()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"ACOUSTIC PROBE V2 (ambience {(Plugin.AmbienceV2On.Value ? "ON" : "off")}, gunshots {(Plugin.GunV2On.Value ? "ON" : "off")}, glass loss {Plugin.GlassLossDb.Value:0} dB, build-up x{Plugin.RoomBuildUp.Value:0.00}, guided rays {(Plugin.GuidedRays.Value ? "ON" : "off")}, ray quality {Plugin.RayQualityCfg.Value} at the end)");
            sb.AppendLine($"  rays cast {RaysCast}, paths completed {PathsDone}, guided paths aimed at openings {GuidedPaths}, most openings remembered at once {MaxOpenings}");
            sb.AppendLine($"  back faces hit (reflected as surfaces) {BackfaceHits}, far sides of passed objects skipped {ExitFacesSkipped}, upward escapes rejected by the roof test {SkyRejected}   <- 0.6.5 leak fix (raid 25: paths came up through floors)");
            sb.AppendLine($"  memory ended by a real change (9s): {ChangesRoof} roof rain, {ChangesLeak} sound getting out; {ChangesStill} of them while standing still (should be few)\n  floors above ceilings (9p, cached spots): 0 floors {FloorHist[0]}, 1 floor {FloorHist[1]}, 2 floors {FloorHist[2]}, 3+ {FloorHist[3]}; the old look-down test wrongly said top roof at {OldRoofTestWrong} of them; low-detail floors (9v) added a floor at {FloorMoreSpots} spots, {FloorWasTop} of which had read as top roof\n  roof tests (0.6.7 roof rain, cached per collider and 1 m cell) {RoofTests}, roof rain from the probe {(Plugin.RoofRainFromProbe.Value ? "ON" : "off")}, direction strength x{Plugin.DirectionStrength.Value:0.00}");
            sb.AppendLine($"  exit points (0.6.8) found {ExitsFound}, not found within 48 m {ExitsMissed}; rain from openings (9j) {(Plugin.RainFromOpenings.Value ? "ON" : "off")}");
            return sb.ToString();
        }
    }
}
