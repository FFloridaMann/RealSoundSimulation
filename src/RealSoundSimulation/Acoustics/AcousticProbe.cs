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
    //   128 directions spread evenly over the whole sphere (Fibonacci), a new random rotation every cycle. About
    //   PathsPerSecond paths start per second (each direction ~4 times a second). Every frame, every path in flight casts
    //   one leg; all legs of a frame go in one batch on the worker threads (RaycastCommand).
    //   A leg that hits nothing within 60 m has ESCAPED: that is the outside (open sky). A level or downward leg that
    //   escapes is only outside if there is no roof above its end point (otherwise it is a long interior: trapped).
    //   A hit:
    //     glass        passes at "9d Glass loss dB" (physical: about -22 dB, muffled to 1.5 kHz), else reflects
    //     thin planks, cardboard, fabric   pass at -10 dB (2.5 kHz), else reflect
    //     bars, fences, grates             pass at 0.9
    //     shattered glass, empty window frames   pass
    //     other non-blocking props         pass half
    //     anything else reflects: energy x (1 - absorption of its material), mirror or scattered by its roughness, and
    //     duller by its brightness. The ground/floor under the first leg absorbs nothing (it is part of the rain field).
    //   Up to 3 reflections (6 casts). Energy still in flight then is "remaining".
    //   Rooms ring: the remaining energy would keep bouncing; the share of it that eventually escapes is estimated the way
    //   Sabine's room formula does: escape rate per reflected leg p, absorption a -> p / (p + (1 - p) a), x "9e".
    //   So a bare concrete garage open on one side is loud inside (p 0.1, a 0.03 -> ~0.77), a furnished one is not.
    //
    // Outputs (aggregated every frame from the latest path of each direction):
    //   Level      outdoor sound energy reaching you, 0..1 (1 in open air): direct + after bounces + room build-up
    //   CutoffHz   how bright it is (energy-weighted; glass ~1.5 kHz, concrete reflections ~13 kHz, open 22 kHz)
    //   Arrival    the direction it comes from (first leg of each escaping path), Focus 0 all round .. 1 one direction
    //   SkyOpen    direct escape / half the sphere (1 under open sky); Enclosed from it (indoor layers, directional beds)
    //   Coverage   share of the upward cone (within 53 deg of straight up) covered within 40 m; RoofMat
    //   For gunshots: Room = 1 - share escaping within one wall/roof bounce (floors don't count, thin surfaces by "3f"),
    //   MeanFreePath, Absorption, Size (0.5 small .. 1), HardRoom, Rt60 (estimate)
    internal class AcousticProbe : MonoBehaviour
    {
        public const int N = 128;
        public const int MaxCasts = 6, MaxBounces = 3, MaxPts = MaxCasts + 1;
        private const float MaxLeg = 60f, Skin = 0.02f, PathsPerSecond = 512f;
        private const int MaxActive = 256;

        public enum Fate : byte { None, Sky, SkyAfterBounces, Trapped, LongInterior }

        public struct Result
        {
            public Vector3 First;
            public float Direct, Reflected, Remaining, Geo, Cut;
            public bool ViaGlass, FirstHit;
            public float FirstHitM;
            public MaterialType FirstMat;
            public Fate Fate;
            public int Casts;
        }

        private struct PathState
        {
            public bool Active;
            public int Dir;
            public Vector3 O, D, First;
            public float E, G, Cut;
            public int Casts, Bounces, GeoBounces;
            public bool ViaGlass, FirstHit;
            public float FirstHitM;
            public MaterialType FirstMat;
            public int Pn;
        }

        // latest completed path per direction, and its points (ray view, paths.csv)
        public static readonly Result[] Results = new Result[N];
        public static readonly Vector3[] Pts = new Vector3[N * MaxPts];
        public static readonly int[] PtN = new int[N];
        public static readonly string[] Hits = new string[N];

        private static readonly PathState[] Paths = new PathState[MaxActive];
        private static readonly Vector3[] Scratch = new Vector3[MaxActive * MaxPts];
        private static readonly StringBuilder[] ScratchHits = new StringBuilder[MaxActive];
        private static readonly int[] Idx = new int[MaxActive];
        private static readonly Vector3[] Fib = BuildFib(N);
        private static Quaternion _rot = Quaternion.identity;
        private static int _cursor;
        private static float _budget, _nextRow;
        private static NativeArray<RaycastCommand> _cmds;
        private static NativeArray<RaycastHit> _hits;

        // running statistics (exponential, ~2 s): escape rate per reflected leg, absorption, free path, brightness
        private static double _escAcc, _legAcc, _alphaAcc, _hitAcc, _lenAcc, _lenN, _cutLogAcc;

        public static bool Valid;
        public static float Level = float.NaN, Direct, Traced, Tail, Remaining, EscapeRate, Absorption, MeanFreePath = float.NaN,
            CutoffHz = 22000f, Focus, SkyOpen, Enclosed, Coverage, DiffuseShare, Room = float.NaN, Size = float.NaN, HardRoom = float.NaN,
            Rt60 = float.NaN;
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
            for (int i = 0; i < N; i++) { Results[i] = default; PtN[i] = 0; Hits[i] = null; }
            for (int i = 0; i < MaxActive; i++) Paths[i].Active = false;
            _escAcc = _legAcc = _alphaAcc = _hitAcc = _lenAcc = _lenN = _cutLogAcc = 0;
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

            // start new paths
            _budget = Mathf.Min(_budget + PathsPerSecond * dt, 32f);
            for (int i = 0; i < MaxActive && _budget >= 1f; i++)
            {
                if (Paths[i].Active) continue;
                _budget -= 1f;
                Start(ref Paths[i], i, _cursor, lt.position, _rot * Fib[_cursor]);
                _cursor++;
                if (_cursor >= N) { _cursor = 0; _rot = UnityEngine.Random.rotationUniform; }
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
            if (n > 0)
            {
                JobHandle h = RaycastCommand.ScheduleBatch(_cmds.GetSubArray(0, n), _hits.GetSubArray(0, n), 8, 1);
                h.Complete();
                RaysCast += n; CountedRay.Rays += n;
                double esc = 0, legs = 0, alpha = 0, hits = 0, len = 0, lenN = 0, cutLog = 0;
                for (int k = 0; k < n; k++)
                {
                    int i = Idx[k];
                    Advance(ref Paths[i], i, _hits[k], block, true, ref esc, ref legs, ref alpha, ref hits, ref len, ref lenN, ref cutLog);
                }
                double decay = Math.Exp(-dt / 2.0);
                _escAcc = _escAcc * decay + esc; _legAcc = _legAcc * decay + legs;
                _alphaAcc = _alphaAcc * decay + alpha; _hitAcc = _hitAcc * decay + hits; _cutLogAcc = _cutLogAcc * decay + cutLog;
                _lenAcc = _lenAcc * decay + len; _lenN = _lenN * decay + lenN;
            }
            Aggregate();
            if (Time.time >= _nextRow)
            {
                _nextRow = Time.time + 0.125f;
                if (Valid && DiagLog.IsOpen) DiagLog.Row("probe2.csv", RowCsv(Time.time - DiagRunner.RaidStartTime, lt));
            }
        }

        private static void Start(ref PathState p, int slot, int dir, Vector3 o, Vector3 d)
        {
            p = default;
            p.Active = true; p.Dir = dir; p.O = o; p.D = d; p.First = d; p.E = 1f; p.G = 1f; p.Cut = 22000f;
            p.Pn = 0;
            Scratch[slot * MaxPts + p.Pn++] = o;
            if (ScratchHits[slot] == null) ScratchHits[slot] = new StringBuilder(128); else ScratchHits[slot].Clear();
        }

        // One leg's result. Shared by the continuous probe (record = true) and the muzzle measure.
        private static void Advance(ref PathState p, int slot, RaycastHit hit, int block, bool record,
            ref double esc, ref double legs, ref double alpha, ref double hits, ref double len, ref double lenN, ref double cutLog)
        {
            bool fromReflection = p.Bounces >= 1;
            p.Casts++;
            if (fromReflection) legs++;
            Collider c = hit.collider;
            if (c == null)
            {
                Vector3 end = p.O + p.D * MaxLeg;
                if (record) AddPt(ref p, slot, end);
                bool sky = p.D.y > 0.5f || !CountedRay.Raycast(end + Vector3.up * 0.5f, Vector3.up, out RaycastHit _, 60f, block, QueryTriggerInteraction.Ignore);
                if (sky) { if (fromReflection) esc++; Finish(ref p, slot, p.Bounces == 0 ? Fate.Sky : Fate.SkyAfterBounces, record); }
                else Finish(ref p, slot, Fate.LongInterior, record);
                return;
            }
            if (record) AddPt(ref p, slot, hit.point);
            MaterialType m = Surfaces.Material(c);
            if (p.Casts == 1) { p.FirstHit = true; p.FirstHitM = hit.distance; p.FirstMat = m; }
            len += hit.distance; lenN++;

            // what kind of surface: pass-through (glass, thin, bars, open frames, props) or reflect
            bool blocking = (block & (1 << c.gameObject.layer)) != 0;
            float t, tg, passCut; bool glass = false;
            Kind(c, m, blocking, out t, out tg, out passCut, ref glass);
            if (record && ScratchHits[slot].Length < 400)
                ScratchHits[slot].Append(c.name).Append(':').Append(m).Append('@').Append(hit.distance.ToString("0.0")).Append(' ');
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
                    if (p.Casts >= MaxCasts || p.E < 1e-5f) Finish(ref p, slot, Fate.Trapped, record);
                    return;
                }
                float wr = 1f / (1f - pp);
                p.E *= (1f - t) * wr; p.G *= (1f - tg) * wr;
            }

            // reflect
            Props(m, out float a, out float scatter, out float bright);
            bool ground = hit.normal.y > 0.7f;
            alpha += a; hits++; cutLog += Math.Log(bright); // the room's statistics use the real surface
            if (p.Casts == 1 && ground) a = 0f; // ...but the ground under you is part of the outdoor sound field: no loss
            p.E *= 1f - a; p.Cut = Mathf.Min(p.Cut, bright);
            p.Bounces++;
            if (!ground) p.GeoBounces++;
            if (p.Bounces > MaxBounces || p.Casts >= MaxCasts || p.E < 1e-5f) { Finish(ref p, slot, Fate.Trapped, record); return; }
            Vector3 nrm = hit.normal;
            if (Vector3.Dot(nrm, p.D) > 0f) nrm = -nrm;
            p.D = UnityEngine.Random.value < scatter ? CosineHemisphere(nrm) : Vector3.Reflect(p.D, nrm).normalized;
            p.O = hit.point + nrm * Skin;
        }

        private static void Finish(ref PathState p, int slot, Fate fate, bool record)
        {
            p.Active = false;
            if (!record) { _syncFate = fate; return; }
            Result r = default;
            r.First = p.First; r.Cut = p.Cut; r.ViaGlass = p.ViaGlass; r.Casts = p.Casts; r.Fate = fate;
            r.FirstHit = p.FirstHit; r.FirstHitM = p.FirstHitM; r.FirstMat = p.FirstMat;
            bool escaped = fate == Fate.Sky || fate == Fate.SkyAfterBounces;
            if (escaped) { if (p.Bounces == 0) r.Direct = p.E; else r.Reflected = p.E; }
            else r.Remaining = p.E;
            r.Geo = escaped && p.GeoBounces <= 1 ? p.G : 0f;
            Results[p.Dir] = r;
            int o = slot * MaxPts, d = p.Dir * MaxPts;
            for (int k = 0; k < p.Pn; k++) Pts[d + k] = Scratch[o + k];
            PtN[p.Dir] = p.Pn;
            Hits[p.Dir] = ScratchHits[slot]?.ToString();
            PathsDone++;
        }

        private static Fate _syncFate;

        private static void AddPt(ref PathState p, int slot, Vector3 v)
        {
            if (p.Pn < MaxPts) Scratch[slot * MaxPts + p.Pn++] = v;
        }

        // Pass-through share for sound (t) and for the gunshot room measure (tg), and how dull the passed sound gets.
        private static void Kind(Collider c, MaterialType m, bool blocking, out float t, out float tg, out float cut, ref bool glass)
        {
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
                        // a solid object on a low-detail layer (parked cars, doors, elevator doors): blocks, small leak
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
            float direct = 0f, refl = 0f, rem = 0f, geo = 0f, eW = 0f; double cutLog = 0; Vector3 arr = Vector3.zero;
            int n = 0, up = 0, upHit = 0;
            Array.Clear(RoofCount, 0, RoofCount.Length);
            for (int i = 0; i < N; i++)
            {
                ref Result r = ref Results[i];
                if (r.Fate == Fate.None) continue;
                n++;
                direct += r.Direct; refl += r.Reflected; rem += r.Remaining; geo += r.Geo;
                float e = r.Direct + r.Reflected;
                if (e > 0f) { arr += r.First * e; eW += e; cutLog += e * Math.Log(Mathf.Max(r.Cut, 100f)); }
                if (r.First.y > 0.6f)
                {
                    up++;
                    if (r.FirstHit && r.FirstHitM < 40f) { upHit++; int mi = (int)r.FirstMat; if (mi >= 0 && mi < RoofCount.Length) RoofCount[mi]++; }
                }
            }
            if (n < N / 2) { Valid = false; return; }
            Direct = direct / n; Traced = refl / n; Remaining = rem / n;
            EscapeRate = _legAcc > 1 ? (float)(_escAcc / _legAcc) : 0f;
            Absorption = _hitAcc > 1 ? (float)(_alphaAcc / _hitAcc) : 0.05f;
            MeanFreePath = _lenN > 1 ? (float)(_lenAcc / _lenN) : float.NaN;
            float p = EscapeRate, a = Absorption;
            float share = p + (1f - p) * a > 1e-6f ? p / (p + (1f - p) * a) : 0f;
            Tail = Remaining * share * Mathf.Max(0f, Plugin.RoomBuildUp.Value);
            Level = Mathf.Clamp01(Direct + Traced + Tail);
            // brightness: the direct/bounced paths' own, and for the build-up the room's surfaces (many bounces: duller)
            float roomCut = _hitAcc > 1 ? (float)Math.Exp(_cutLogAcc / _hitAcc) * 0.8f : 12000f;
            double wSum = eW + Tail;
            CutoffHz = wSum > 1e-6 ? (float)Math.Exp((cutLog + Tail * Math.Log(Mathf.Max(roomCut, 100f))) / wSum) : 22000f;
            Arrival = arr.sqrMagnitude > 1e-8f ? arr.normalized : Vector3.forward;
            float focus = eW > 1e-6f ? arr.magnitude / eW : 0f;
            DiffuseShare = Level > 1e-6f ? Mathf.Clamp01(Tail / Level) : 0f;
            Focus = Mathf.Clamp01(focus * (1f - DiffuseShare));
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
                for (int i = 0; i < 64; i++) { Sync[i] = default; Sync[i].Active = true; Sync[i].O = o; Sync[i].D = rot * Fib64[i]; Sync[i].First = Sync[i].D; Sync[i].E = Sync[i].G = 1f; Sync[i].Cut = 22000f; }
                double esc = 0, legs = 0, alpha = 0, hits = 0, len = 0, lenN = 0, cutLog = 0;
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
                        Advance(ref Sync[i], i, _hits[k], block, false, ref esc, ref legs, ref alpha, ref hits, ref len, ref lenN, ref cutLog);
                        if (!Sync[i].Active && (_syncFate == Fate.Sky || _syncFate == Fate.SkyAfterBounces) && Sync[i].GeoBounces <= 1) geo += Sync[i].G;
                        if (leg == 0 && Sync[i].First.y > 0.6f) { up++; if (Sync[i].FirstHit && Sync[i].FirstHitM < 40f) upHit++; }
                    }
                }
                MuzzleRoom = Mathf.Clamp01(1f - geo / 64f);
                float mfp = lenN > 0 ? (float)(len / lenN) : float.NaN;
                float a = hits > 0 ? (float)(alpha / hits) : 0.05f;
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

        public const string Header = "t,level,levelDb,direct,traced,tail,remaining,escapeRate,absorption,meanFreePathM,rt60S,skyOpen,enclosed,coverage,roofMat,cutoffHz,arrivalAz,arrivalEl,focus,diffuseShare,room,size,hardRoom,raysPerS";

        private static float _lastRaysT; private static long _lastRays; private static float _raysPerS;

        private static string RowCsv(float t, Transform lt)
        {
            float now = Time.realtimeSinceStartup;
            if (now - _lastRaysT >= 1f) { _raysPerS = (RaysCast - _lastRays) / Mathf.Max(0.001f, now - _lastRaysT); _lastRays = RaysCast; _lastRaysT = now; }
            return string.Join(",", DiagLog.F(t, "0.00"), DiagLog.F(Level), DiagLog.F(10f * Mathf.Log10(Mathf.Max(Level, 1e-4f)), "0.0"),
                DiagLog.F(Direct), DiagLog.F(Traced), DiagLog.F(Tail), DiagLog.F(Remaining), DiagLog.F(EscapeRate), DiagLog.F(Absorption),
                DiagLog.F(MeanFreePath, "0.0"), DiagLog.F(Rt60, "0.00"), DiagLog.F(SkyOpen), DiagLog.F(Enclosed), DiagLog.F(Coverage), RoofMat.ToString(),
                DiagLog.F(CutoffHz, "0"), DiagLog.F(RelAz(lt, Arrival), "0"), DiagLog.F(Mathf.Asin(Mathf.Clamp(Arrival.y, -1f, 1f)) * Mathf.Rad2Deg, "0"),
                DiagLog.F(Focus), DiagLog.F(DiffuseShare), DiagLog.F(Room), DiagLog.F(Size), DiagLog.F(HardRoom), DiagLog.F(_raysPerS, "0"));
        }

        public static float RelAz(Transform lt, Vector3 dir)
        {
            if (lt == null) return float.NaN;
            Vector3 f = lt.forward; f.y = 0f; Vector3 d = new Vector3(dir.x, 0f, dir.z);
            return f.sqrMagnitude > 1e-4f && d.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(f, d, Vector3.up) : float.NaN;
        }

        public const string PathsHeader = "t,mark,dir,azimuthDeg,elevationDeg,fate,casts,direct,reflected,remaining,geo,cutoffHz,viaGlass,hits";

        // F10: every direction's latest path (paths.csv)
        public static void DumpPaths(int mark, float t)
        {
            if (!DiagLog.IsOpen || !Valid) return;
            Transform lt = MonoBehaviourSingleton<SpatialAudioSystem>.Instance.ListenerTransform;
            for (int i = 0; i < N; i++)
            {
                ref Result r = ref Results[i];
                if (r.Fate == Fate.None) continue;
                DiagLog.Row("paths.csv", string.Join(",", DiagLog.F(t, "0.00"), mark.ToString(), i.ToString(), DiagLog.F(RelAz(lt, r.First), "0"),
                    DiagLog.F(Mathf.Asin(Mathf.Clamp(r.First.y, -1f, 1f)) * Mathf.Rad2Deg, "0"), r.Fate.ToString(), r.Casts.ToString(),
                    DiagLog.F(r.Direct), DiagLog.F(r.Reflected), DiagLog.F(r.Remaining), DiagLog.F(r.Geo), DiagLog.F(r.Cut, "0"), r.ViaGlass ? "1" : "0",
                    (Hits[i] ?? "").Replace(',', ';').Trim()));
            }
        }

        public static string OverlayLine()
        {
            if (!Plugin.AcousticProbeOn.Value) return "";
            if (!Valid) return "probe v2: -\n";
            return $"probe v2: level {Level:0.00} ({10f * Mathf.Log10(Mathf.Max(Level, 1e-4f)):0.0} dB: direct {Direct:0.00} bounced {Traced:0.00} build-up {Tail:0.00})  " +
                   $"{CutoffHz:0} Hz  focus {Focus:0.00}  sky {SkyOpen:0.00}  roof {Coverage:0.00} {RoofMat}  room {Room:0.00} RT {Rt60:0.00} s  " +
                   $"ambience {(Plugin.AmbienceV2On.Value ? "v2" : "v1")} guns {(Plugin.GunV2On.Value ? "v2" : "v1")}\n";
        }

        public static string Summary()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"ACOUSTIC PROBE V2 (ambience {(Plugin.AmbienceV2On.Value ? "ON" : "off")}, gunshots {(Plugin.GunV2On.Value ? "ON" : "off")}, glass loss {Plugin.GlassLossDb.Value:0} dB, build-up x{Plugin.RoomBuildUp.Value:0.00})");
            sb.AppendLine($"  rays cast {RaysCast}, paths completed {PathsDone}");
            return sb.ToString();
        }
    }
}
