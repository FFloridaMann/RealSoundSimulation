using System;
using System.Collections.Generic;
using System.Text;
using Audio.Extensions;
using Audio.SpatialSystem;
using RealSoundSimulation.Diagnostics;
using EFT;
using EFT.Ballistics;
using HarmonyLib;
using UnityEngine;

namespace RealSoundSimulation.Acoustics
{
    // Outdoor exposure (roadmap step 4b): how much of the outside reaches you, and through what. Drives fix 8.
    // Design and history: docs/AMBIENCE.md. Current behaviour (0.5.8):
    //   48 directions: 24 azimuths (15 deg apart) x elevations 0 and +15 deg. Measured continuously, indoors and outdoors:
    //   ~288 rays per second round robin, each re-measure nudged by up to +-7.5 deg in azimuth, totals rebuilt every frame.
    //   Per direction: one ray on the game's sound-blocking layers to the first wall (max 40 m). Points along it (1.5, 3,
    //   5, 8, 12, 18, 25, 35 m, then 3 halvings) are checked with IsOutside(): the game's room map says outdoor AND either
    //   open sky above, or a roof that is not your own ceiling ("covered outside", half weight). Facade glass and broken
    //   windows are not on the blocking layers, so the ray passes them; that is why this check, not the ray, decides
    //   "outside". A second ray on the other layers up to that point says what is in between (Classify): Open, Glass,
    //   Bars (80% open), Broken (open), Other (half). Directions that never reach outside are Blocked.
    //   Weight per direction: 1 / (1 + distance / 15 m) x the path weight (glass = "8c").
    //   Value = 1 - e^(-4 x open) + 8c x (1 - e^(-4 x glass)), indoors at least the room's baked floor level.
    //   OpenSide / OneSided: the weighted horizontal direction of the outside, and how one-sided it is.
    //   Sectors: 6 fixed world sectors (60 deg) sum their directions' weight, direction and glass share for OpeningSounds.
    //   Above you (4 per second): 5 rays straight up (median), the first ceiling and its material, floors above, roof.
    //   F10 dumps every direction to rays.csv; exposure.csv gets a row 8 times a second.
    internal class OutdoorExposure : MonoBehaviour
    {
        // 0.4.1: Bars (window bars/grilles: sound passes) and Broken (a broken window: shards only at the edges) added.
        public enum Path : byte { Blocked, Open, Glass, Other, Bars, Broken }

        private const float Interval = 0.125f; // exposure.csv rows, 8 per second
        public const int Az = 24;
        private static readonly float[] Elev = { 0f, 15f };
        public const int RayCount = Az * 2;
        private const float MaxRange = 40f;
        private static readonly float[] Samples = { 1.5f, 3f, 5f, 8f, 12f, 18f, 25f, 35f };
        private const float FarScale = 15f;
        private const float Strength = 4f; // how fast the value rises with exposure: 1 - e^(-4 x exposure)

        private static readonly Vector3[] Dirs = BuildDirs();

        public static bool Valid, ListenerIndoor;
        public static float ExposureOpen, ExposureGlass, ExposureOther, RoomFloor = float.NaN;
        // The value fix 8 applies, the open side (world, horizontal) and how one-sided it is (0 all round .. 1 one side only).
        public static float Value = float.NaN, OneSided;
        public static Vector3 OpenSide;
        public static int OpenN, GlassN, OtherN, BlockedN, BarsN, BrokenN;
        public const float BarsOpen = 0.8f; // share of a barred direction that counts as open
        public static float NearestOpenM = float.NaN, NearestOpenAz = float.NaN, NearestGlassM = float.NaN, NearestGlassAz = float.NaN;
        public static float CeilingM = float.NaN;
        public static MaterialType CeilingMat = MaterialType.None, RoofMat = MaterialType.None;
        public static int FloorsAbove = -1;
        public static readonly Path[] RayPath = new Path[RayCount];
        public static readonly float[] RayOutM = new float[RayCount];
        public static readonly float[] RayWallM = new float[RayCount];
        public static readonly float[] RayWeight = new float[RayCount];
        public static readonly Collider[] RayWallCol = new Collider[RayCount];
        public static readonly Collider[] RayBetweenCol = new Collider[RayCount];
        public static readonly ISpatialAudioRoom[] RayLastRoom = new ISpatialAudioRoom[RayCount];
        public static readonly float[] RayLastRoomM = new float[RayCount];
        public static readonly bool[] RayCovered = new bool[RayCount];
        public static readonly bool[] RayCoveredOutside = new bool[RayCount];
        public static readonly Collider[] RayRoofCol = new Collider[RayCount];
        public const float CoveredOutsideWeight = 0.5f;
        private static Collider _ceilingCol;
        public const float CoveredWeight = 0.35f;

        // 0.5.2: fixed world sectors for the opening sounds (6 x 60 deg). Raid 16: per-opening sources were re-pointed
        // every update (strongest opening swinging 40 -> 172 deg) and swept through the listener: "rain on top of me, moving".
        public const int SectorN = 6;
        public static readonly float[] SectorWeight = new float[SectorN];
        public static readonly float[] SectorGlass = new float[SectorN];
        public static readonly Vector3[] SectorDir = new Vector3[SectorN];

        public static int OpenSectors; // sectors with any outside in them

        // what stood between you and the outside, by layer:object (summary; to learn what facade glass is)
        public static readonly Dictionary<string, int> BetweenHits = new Dictionary<string, int>();
        public static int Updates, IndoorUpdates;
        private static int _otherMask = -1;
        private static string _otherMaskNames = "";


        private static readonly AccessTools.FieldRef<SpatialAudioSystem, AudioRoomStorage> StorageRef =
            AccessTools.FieldRefAccess<SpatialAudioSystem, AudioRoomStorage>("_audioRoomStorage");

        public static void Reset()
        {
            OpenSectors = 0; GlassRendererCache.Clear();
            Valid = false; AboveValid = false; SweepSkipped = false; SkippedUpdates = 0; Updates = IndoorUpdates = 0; TotalMs = MaxMs = 0; TimedN = 0;
            BetweenHits.Clear();
            _otherMask = -1;
        }

        // 0.5.7: continuous. Every frame a few directions are re-measured (round robin, ~RaysPerSecond in total), each with a
        // fresh random nudge of up to half the ray spacing, and the totals are rebuilt from the latest result of every
        // direction. Raid 20: measuring all 48 at once 8 times a second made the value jump 0.13 <-> 0.42 as you walked
        // (rays finding and losing windows together) - the outdoor rain cut in and out instead of fading; and cost spikes
        // up to 12 ms. Now one direction changes at a time and the nudges cover all angles over time.
        private const float RaysPerSecond = 288f; // = 48 directions x 6 per second
        private static float _budget, _nextAbove, _nextRow;
        private static int _cursor;
        private static readonly Vector3[] RayDir = (Vector3[])BuildDirs().Clone();

        private void Update()
        {
            bool inRaid = SpatialAudioSystem.Initialized && MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated && GamePlayerOwner.MyPlayer != null;
            if (!inRaid || !Plugin.ExposureProbeOn.Value) { Valid = false; AboveValid = false; return; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { Measure(MonoBehaviourSingleton<SpatialAudioSystem>.Instance); }
            catch (Exception e) { DiagRunner.ReportOnce("outdoor exposure", e); }
            double ms = sw.Elapsed.TotalMilliseconds;
            TotalMs += ms; TimedN++; if (ms > MaxMs) MaxMs = ms;
        }

        public static double TotalMs, MaxMs; public static int TimedN;
        public static bool AboveValid, SweepSkipped; // 0.6.16: the up-rays measured; the 48-direction sweep skipped (fix 9 on)
        public static int SkippedUpdates;

        private static void Measure(SpatialAudioSystem sas)
        {
            Diagnostics.Perf.Begin();
            try { MeasureCore(sas); }
            finally { Diagnostics.Perf.End(Diagnostics.Perf.Part.Exposure); }
        }

        private static void MeasureCore(SpatialAudioSystem sas)
        {
            Transform lt = sas.ListenerTransform;
            if (lt == null) return;
            Vector3 o = lt.position;
            int mask = EnclosureProbe.CurrentMask();
            if (_otherMask == -1) BuildOtherMask(mask);

            if (Time.time >= _nextAbove) { _nextAbove = Time.time + 0.25f; MeasureAbove(o, mask); AboveValid = true; }
            // 0.6.16: while fix 9 runs, nothing that changes sound reads the 48-direction sweep (only exposure.csv and the
            // marks columns did: 6-7 ms per second plus the odd 50 ms frame). The 5 up-rays above stay - fix 9's roof rain
            // cut under two floors uses them. The sweep resumes by itself when fix 9 is off.
            SweepSkipped = Plugin.AmbienceV2On.Value && Plugin.AcousticProbeOn.Value && AcousticProbe.Valid;
            if (SweepSkipped) { SkippedUpdates++; return; }
            ISpatialAudioRoom room = sas.ListenerCurrentRoom;
            ListenerIndoor = room != null && room.IsValid && !room.Type.IsOutdoor();
            RoomFloor = ListenerIndoor && room.RoomAmbientData != null ? room.RoomAmbientData.OutdoorAmbientVolume : float.NaN;
            AudioRoomStorage storage = StorageRef(sas);
            if (storage == null) return;

            _budget = Mathf.Min(_budget + RaysPerSecond * Mathf.Max(Time.unscaledDeltaTime, 0f), 12f);
            float glassW = Mathf.Clamp01(Plugin.ThroughGlass.Value);
            while (_budget >= 1f)
            {
                _budget -= 1f;
                MeasureRay(_cursor, o, mask, storage, room, glassW);
                _cursor = (_cursor + 1) % RayCount;
            }
            Aggregate(lt, glassW);
            if (Time.time >= _nextRow)
            {
                _nextRow = Time.time + Interval;
                Updates++; if (ListenerIndoor) IndoorUpdates++;
                if (Valid && DiagLog.IsOpen) DiagLog.Row("exposure.csv", RowCsv(Time.time - DiagRunner.RaidStartTime));
            }
        }

        // One direction: nudged randomly by up to half the ray spacing (both ways), then measured.
        private static void MeasureRay(int i, Vector3 o, int mask, AudioRoomStorage storage, ISpatialAudioRoom room, float glassW)
        {
            float jit = UnityEngine.Random.Range(-0.5f, 0.5f) * 360f / Az;
            Vector3 d = Quaternion.AngleAxis(jit, Vector3.up) * Dirs[i];
            RayDir[i] = d;
            bool hitWall = CountedRay.Raycast(o, d, out RaycastHit hit, MaxRange, mask, QueryTriggerInteraction.Ignore);
            float wall = hitWall ? hit.distance : MaxRange;
            RayWallM[i] = hitWall ? wall : float.PositiveInfinity;
            RayWallCol[i] = hitWall ? hit.collider : null;
            RayBetweenCol[i] = null; RayLastRoom[i] = null; RayLastRoomM[i] = float.NaN; RayWeight[i] = 0f; RayCovered[i] = false;
            RayCoveredOutside[i] = false; RayRoofCol[i] = null;
            float outM = float.NaN, prevS = 0f;
            for (int k = 0; k < Samples.Length; k++)
            {
                float s = Samples[k];
                if (s > wall - 0.3f) break;
                ISpatialAudioRoom r = CountedRay.Room(storage, room, o + d * s);
                RayLastRoom[i] = r; RayLastRoomM[i] = s;
                if (IsOutside(r, o + d * s, mask, i))
                {
                    // 0.5.1: narrow down where it actually crosses outside (3 halvings between the last indoor sample and this one)
                    float lo = prevS, hi = s;
                    for (int bb = 0; bb < 3; bb++)
                    {
                        float mid = 0.5f * (lo + hi);
                        ISpatialAudioRoom rm = CountedRay.Room(storage, room, o + d * mid);
                        if (IsOutside(rm, o + d * mid, mask, -1)) hi = mid; else lo = mid;
                    }
                    outM = Mathf.Max(0.5f, hi);
                    break;
                }
                prevS = s;
            }
            if (float.IsNaN(outM) && wall >= MaxRange) outM = MaxRange; // nothing in 40 m: outside
            RayOutM[i] = outM;
            if (float.IsNaN(outM)) { RayPath[i] = Path.Blocked; return; }

            Path p = Path.Open;
            if (_otherMask != 0 && CountedRay.Raycast(o, d, out RaycastHit bh, outM, _otherMask, QueryTriggerInteraction.Ignore))
            {
                p = Classify(bh.collider);
                RayBetweenCol[i] = bh.collider;
                Tally(bh.collider, p);
            }
            RayPath[i] = p;
            // 0.5.4: gentle distance weight (distance is already counted by how many rays an opening covers)
            float prox = 1f / (1f + outM / FarScale);
            float pw = p == Path.Glass ? glassW : p == Path.Bars ? BarsOpen : p == Path.Other ? 0.5f : 1f;
            if (RayCoveredOutside[i]) pw *= CoveredOutsideWeight;
            RayWeight[i] = prox * pw;
        }

        // Totals from the latest result of every direction (cheap; every frame).
        private static void Aggregate(Transform lt, float glassW)
        {
            Vector3 fwd = lt.forward; fwd.y = 0f;
            float eo = 0f, eg = 0f, ex = 0f, wSum = 0f;
            Vector3 side = Vector3.zero;
            OpenN = GlassN = OtherN = BlockedN = BarsN = BrokenN = 0;
            NearestOpenM = NearestGlassM = NearestOpenAz = NearestGlassAz = float.NaN;
            for (int i = 0; i < RayCount; i++)
            {
                Path p = RayPath[i];
                if (p == Path.Blocked) { BlockedN++; continue; }
                float outM = RayOutM[i];
                Vector3 d = RayDir[i];
                float prox = 1f / (1f + outM / FarScale);
                float w = RayWeight[i];
                Vector3 dh = new Vector3(d.x, 0f, d.z).normalized;
                side += dh * w; wSum += w;
                float cov = RayCoveredOutside[i] ? CoveredOutsideWeight : 1f;
                float az = fwd.sqrMagnitude > 1e-4f ? Vector3.SignedAngle(fwd, new Vector3(d.x, 0f, d.z), Vector3.up) : 0f;
                switch (p)
                {
                    case Path.Open:
                        eo += prox * cov; OpenN++;
                        if (float.IsNaN(NearestOpenM) || outM < NearestOpenM) { NearestOpenM = outM; NearestOpenAz = az; }
                        break;
                    case Path.Glass:
                        eg += prox * cov; GlassN++;
                        if (float.IsNaN(NearestGlassM) || outM < NearestGlassM) { NearestGlassM = outM; NearestGlassAz = az; }
                        break;
                    case Path.Bars:
                        eo += prox * cov * BarsOpen; ex += prox * cov * (1f - BarsOpen); BarsN++;
                        break;
                    case Path.Broken:
                        eo += prox * cov; BrokenN++;
                        if (float.IsNaN(NearestOpenM) || outM < NearestOpenM) { NearestOpenM = outM; NearestOpenAz = az; }
                        break;
                    default:
                        ex += prox * cov; OtherN++;
                        break;
                }
            }
            ExposureOpen = eo / RayCount; ExposureGlass = eg / RayCount; ExposureOther = ex / RayCount;
            float open = ExposureOpen + 0.5f * ExposureOther;
            float v = (1f - Mathf.Exp(-Strength * open)) + glassW * (1f - Mathf.Exp(-Strength * ExposureGlass));
            Value = Mathf.Clamp01(Mathf.Max(v, float.IsNaN(RoomFloor) ? 0f : RoomFloor));
            OpenSide = side;
            OneSided = wSum > 1e-4f ? Mathf.Clamp01(side.magnitude / wSum) : 0f;
            BuildSectors();
            Valid = true;
        }

        // 0.5.3: "outside" = the game's room map says outdoor AND there is open sky above (no sound-blocking surface within
        // 40 m straight up). Raid 17: inside the Capital building the room map calls big interior patches outdoor (found
        // 0.6 m away in ~20 directions, with escalators and indoor walls beyond), so rain played loud indoors; 0.5.2's
        // "covered" check only reduced the 3D sources, not the level. Now such points are simply not outside, and the ray
        // keeps looking until it reaches real sky (past the glass). RayCovered = some sample was "outdoor" on the map but
        // had a roof over it (rays.csv shows "covered").
        // 0.5.4: a roofed outdoor point counts as "covered outside" (half weight) unless its roof is the same ceiling you're
        // under - that is the room map's interior patches (raid 17, Capital lobby). Raid 18: requiring open sky rejected the
        // real outside beyond shot-out windows, which is under the building's canopy/overhangs.
        private static bool IsOutside(ISpatialAudioRoom r, Vector3 p, int mask, int ray)
        {
            if (r != null && r.IsValid && !r.Type.IsOutdoor()) return false;
            if (CountedRay.Raycast(p + Vector3.up * 0.2f, Vector3.up, out RaycastHit roof, 40f, mask, QueryTriggerInteraction.Ignore))
            {
                if (ray >= 0) { RayCovered[ray] = true; RayRoofCol[ray] = roof.collider; }
                if (_ceilingCol != null && roof.collider == _ceilingCol) return false; // same ceiling as yours: inside
                if (ray >= 0) RayCoveredOutside[ray] = true;
                return true;
            }
            if (ray >= 0) RayCoveredOutside[ray] = false;
            return true;
        }

        // Fill the 6 fixed world sectors used by the opening sounds. (0.5.0-0.5.7 also grouped directions into "openings";
        // only the log used them after 0.5.2, removed in 0.5.8.)
        private static void BuildSectors()
        {
            for (int s = 0; s < SectorN; s++) { SectorWeight[s] = 0f; SectorGlass[s] = 0f; SectorDir[s] = Vector3.zero; }
            for (int i = 0; i < RayCount; i++)
            {
                float w = RayWeight[i];
                if (w <= 0f) continue;
                Vector3 d = RayDir[i];
                float az = Mathf.Atan2(d.z, d.x); if (az < 0f) az += Mathf.PI * 2f;
                int s = Mathf.Min(SectorN - 1, (int)(az / (Mathf.PI * 2f / SectorN)));
                SectorWeight[s] += w; SectorDir[s] += new Vector3(d.x, 0f, d.z) * w;
                if (RayPath[i] == Path.Glass) SectorGlass[s] += w;
            }
            for (int s = 0; s < SectorN; s++)
            {
                if (SectorWeight[s] <= 0f) continue;
                SectorGlass[s] /= SectorWeight[s];
                SectorDir[s] = SectorDir[s].sqrMagnitude > 1e-6f ? SectorDir[s].normalized : SectorHome(s);
            }
            OpenSectors = 0;
            for (int s = 0; s < SectorN; s++) if (SectorWeight[s] > 0f) OpenSectors++;
        }

        public static Vector3 SectorHome(int s)
        {
            float a = (s + 0.5f) * Mathf.PI * 2f / SectorN;
            return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        }

        // F10: every direction, for diagnosing a spot (rays.csv).
        public static void DumpRays(int mark, float t)
        {
            if (!DiagLog.IsOpen || !Valid) return;
            Vector3 f = Vector3.forward;
            if (MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated && MonoBehaviourSingleton<SpatialAudioSystem>.Instance.ListenerTransform != null)
            { f = MonoBehaviourSingleton<SpatialAudioSystem>.Instance.ListenerTransform.forward; f.y = 0f; }
            for (int i = 0; i < RayCount; i++)
            {
                Vector3 d = RayDir[i];
                float az = f.sqrMagnitude > 1e-4f ? Vector3.SignedAngle(f, new Vector3(d.x, 0f, d.z), Vector3.up) : 0f;
                Collider w = RayWallCol[i], b = RayBetweenCol[i];
                DiagLog.Row("rays.csv", string.Join(",", DiagLog.F(t, "0.00"), mark.ToString(), i.ToString(), DiagLog.F(az, "0"), DiagLog.F(Elev[i / Az], "0"),
                    float.IsInfinity(RayWallM[i]) ? "none" : DiagLog.F(RayWallM[i], "0.0"),
                    w != null ? Clean(LayerMask.LayerToName(w.gameObject.layer) + ":" + w.name + ":" + Surfaces.Material(w)) : "",
                    DiagLog.F(RayOutM[i], "0.0"), RayPath[i].ToString() + (RayCoveredOutside[i] ? " covered-outside" : RayCovered[i] ? " roofed-rejected" : ""),
                    b != null ? Clean(LayerMask.LayerToName(b.gameObject.layer) + ":" + b.name + ":" + Surfaces.Material(b)) : "",
                    RayLastRoom[i] != null ? Clean(ShotProbe.RoomName(RayLastRoom[i])) : "", DiagLog.F(RayLastRoomM[i], "0.0"), DiagLog.F(RayWeight[i]),
                    RayRoofCol[i] != null ? Clean(RayRoofCol[i].name) : "", _ceilingCol != null ? Clean(_ceilingCol.name) : ""));
            }
        }

        private static string Clean(string s) => s.Replace(',', ' ');

        // 0.5.1: five columns (straight up and 4 tilted 20 deg), floors = the median count. Raid 15: one vertical ray kept
        // catching beams/ducts/stairs, so the count flipped every step and roof rain cut in and out (and clicked).
        private static readonly Vector3[] UpDirs = BuildUp();
        private static readonly int[] UpCounts = new int[5];

        private static void MeasureAbove(Vector3 o, int mask)
        {
            FloorsAbove = -1; CeilingM = float.NaN; CeilingMat = RoofMat = MaterialType.None; _ceilingCol = null;
            for (int k = 0; k < UpDirs.Length; k++)
            {
                UpCounts[k] = -1;
                if (!CountedRay.Raycast(o, UpDirs[k], out RaycastHit h, 60f, mask, QueryTriggerInteraction.Ignore)) continue; // open sky
                int n = 0;
                MaterialType ceil = Surfaces.Material(h.collider), roof = ceil;
                Vector3 p = h.point;
                for (int m = 0; m < 4; m++)
                {
                    if (!CountedRay.Raycast(p + UpDirs[k] * 0.5f, UpDirs[k], out RaycastHit h2, 100f, mask, QueryTriggerInteraction.Ignore)) break;
                    n++; roof = Surfaces.Material(h2.collider); p = h2.point;
                }
                UpCounts[k] = n;
                if (k == 0) { CeilingM = h.distance; CeilingMat = ceil; RoofMat = roof; _ceilingCol = h.collider; }
            }
            Array.Sort(UpCounts);
            FloorsAbove = UpCounts[UpDirs.Length / 2];
        }

        private static Vector3[] BuildUp()
        {
            var v = new Vector3[5];
            v[0] = Vector3.up;
            float t = 20f * Mathf.Deg2Rad;
            for (int i = 0; i < 4; i++) { float a = i * Mathf.PI * 0.5f; v[i + 1] = new Vector3(Mathf.Sin(t) * Mathf.Cos(a), Mathf.Cos(t), Mathf.Sin(t) * Mathf.Sin(a)); }
            return v;
        }

        // What a "between" hit is (0.4.1):
        //   Bars    window bars / grilles / defenders (raid 11: Window_defender counted as glass)
        //   Broken  a shard left in a broken frame (StuckPiece) whose collider is larger than a shard (> 0.6 m), i.e. it
        //           would make a broken window read as closed glass. A shard-sized collider stays Glass: the ray really
        //           hit glass. The shapes are tallied in the summary to check this.
        //   Glass   glass material, or a glass/window object
        //   Other   anything else
        internal static Path Classify(Collider c)
        {
            string self = c.name;
            if (Has(self, "defender") || Has(self, "bars") || Has(self, "grate") || Has(self, "grill") || Has(self, "fence")) return Path.Bars;
            // 0.4.2: shards are mesh colliders shaped like the shard (raid 12), so a hit is a real glass hit -> Glass.
            // Only a box larger than a shard (would cover the opening) counts as Broken.
            if (Has(self, "stuckpiece")) return c is MeshCollider || MaxExtent(c) <= 0.6f ? Path.Glass : Path.Broken;
            MaterialType m = Surfaces.Material(c);
            if (m == MaterialType.Glass || m == MaterialType.GlassShattered || m == MaterialType.GlassVisor) return Path.Glass;
            if (m == MaterialType.Chainfence || m == MaterialType.Grate) return Path.Bars;
            Transform t = c.transform;
            for (int i = 0; i < 3 && t != null; i++, t = t.parent)
            {
                string n = t.name;
                if (Has(n, "defender") || Has(n, "bars")) return Path.Bars;
                if (Has(n, "glass")) return Path.Glass;
                // 0.5.1: a "window" object that isn't tagged glass is the window opening's low-detail shape, not glass
                // (raid 15: Nakatani_tower_windows_01_COLLIDER, bullets pass there - the user hears them as open windows).
                // 0.5.2: ...unless a glass surface is actually drawn there (a glass material on a renderer of this object,
                // its parent or children). A glass wall built that way stays glass; an empty window frame is open.
                if (Has(n, "window")) return HasGlassRenderer(c) ? Path.Glass : Path.Open;
            }
            return Path.Other;
        }

        private static readonly Dictionary<int, bool> GlassRendererCache = new Dictionary<int, bool>();

        private static bool HasGlassRenderer(Collider c)
        {
            int id = c.GetInstanceID();
            if (GlassRendererCache.TryGetValue(id, out bool g)) return g;
            g = false;
            try
            {
                Transform root = c.transform.parent != null ? c.transform.parent : c.transform;
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (Material m in r.sharedMaterials)
                    {
                        if (m == null) continue;
                        if (Has(m.name, "glass") || (m.shader != null && Has(m.shader.name, "glass"))) { g = true; break; }
                    }
                    if (g) break;
                }
            }
            catch (Exception) { g = false; }
            GlassRendererCache[id] = g;
            return g;
        }

        private static bool Has(string s, string part) => s != null && s.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;

        private static float MaxExtent(Collider c)
        {
            Vector3 s = c.bounds.size;
            return Mathf.Max(s.x, Mathf.Max(s.y, s.z));
        }

        private static void Tally(Collider c, Path p)
        {
            if (BetweenHits.Count > 400) return;
            Vector3 s = c.bounds.size;
            string key = LayerMask.LayerToName(c.gameObject.layer) + ":" + c.name + ":" + Surfaces.Material(c) + ":" + c.GetType().Name +
                         $" {s.x:0.0}x{s.y:0.0}x{s.z:0.0}m -> {p}";
            BetweenHits.TryGetValue(key, out int n); BetweenHits[key] = n + 1;
        }

        internal static int OtherMask(int blockMask)
        {
            if (_otherMask == -1) BuildOtherMask(blockMask);
            return _otherMask;
        }

        // Every layer except the sound-blocking ones (already tested), Ignore Raycast, and player/loot/trigger-type layers.
        private static void BuildOtherMask(int blockMask)
        {
            int m = 0; var names = new List<string>();
            string[] skip = { "player", "deadbody", "hitcollider", "loot", "trigger", "foliage", "grass", "ui", "sky", "water", "ignore", "interactiveaim", "camera", "weapon", "hands" };
            for (int l = 0; l < 32; l++)
            {
                string n = LayerMask.LayerToName(l);
                if (string.IsNullOrEmpty(n) || (blockMask & (1 << l)) != 0) continue;
                string low = n.ToLowerInvariant();
                bool bad = false;
                foreach (string s in skip) if (low.Contains(s)) { bad = true; break; }
                if (bad) continue;
                m |= 1 << l; names.Add(n);
            }
            _otherMask = m;
            _otherMaskNames = string.Join(", ", names);
        }

        private static Vector3[] BuildDirs()
        {
            var v = new Vector3[RayCount];
            for (int e = 0; e < 2; e++)
                for (int a = 0; a < Az; a++)
                {
                    float az = a * Mathf.PI * 2f / Az, el = Elev[e] * Mathf.Deg2Rad;
                    v[e * Az + a] = new Vector3(Mathf.Cos(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Sin(az) * Mathf.Cos(el));
                }
            return v;
        }

        public static string RowCsv(float t)
        {
            return string.Join(",", DiagLog.F(t, "0.00"), ListenerIndoor ? "1" : "0", DiagLog.F(RoomFloor),
                OpenN.ToString(), GlassN.ToString(), OtherN.ToString(), BlockedN.ToString(), BarsN.ToString(), BrokenN.ToString(),
                DiagLog.F(ExposureOpen), DiagLog.F(ExposureGlass), DiagLog.F(ExposureOther),
                DiagLog.F(NearestOpenM, "0.0"), DiagLog.F(NearestOpenAz, "0"), DiagLog.F(NearestGlassM, "0.0"), DiagLog.F(NearestGlassAz, "0"),
                DiagLog.F(CeilingM, "0.0"), CeilingMat.ToString(), FloorsAbove.ToString(), RoofMat.ToString(), DiagLog.F(Value),
                DiagLog.F(SideAz(), "0"), DiagLog.F(OneSided), OpenSectors.ToString());
        }

        public const string Header = "t,listenerIndoor,roomOutdoorFloor,openRays,glassRays,otherRays,blockedRays,barsRays,brokenRays,exposureOpen,exposureGlass,exposureOther,nearestOpenM,nearestOpenAz,nearestGlassM,nearestGlassAz,ceilingM,ceilingMat,floorsAbove,roofMat,outdoorValue,openSideAz,oneSided,openSectors";

        public static float RelAz(Vector3 dir)
        {
            if (!MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated) return float.NaN;
            Transform lt = MonoBehaviourSingleton<SpatialAudioSystem>.Instance.ListenerTransform;
            if (lt == null) return float.NaN;
            Vector3 f = lt.forward; f.y = 0f;
            return f.sqrMagnitude > 1e-4f ? Vector3.SignedAngle(f, new Vector3(dir.x, 0f, dir.z), Vector3.up) : float.NaN;
        }

        // open side relative to where you face (0 ahead, +90 right, 180 behind)
        public static float SideAz()
        {
            if (OpenSide.sqrMagnitude < 1e-6f || !MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated) return float.NaN;
            Transform lt = MonoBehaviourSingleton<SpatialAudioSystem>.Instance.ListenerTransform;
            if (lt == null) return float.NaN;
            Vector3 f = lt.forward; f.y = 0f;
            return f.sqrMagnitude > 1e-4f ? Vector3.SignedAngle(f, new Vector3(OpenSide.x, 0f, OpenSide.z), Vector3.up) : float.NaN;
        }

        public static string OverlayLine()
        {
            if (!Valid) return "exposure: -\n";
            // 0.6.34: with 9p on, show the floors the sound really uses. The old 5-ray count and "roof" (whatever the last ray hit) read wrong in raid 43.
            string above = FloorsAbove < 0 ? "open sky" : AcousticProbe.FloorsNow >= 0 ? $"ceiling {CeilingM:0.0} m {CeilingMat}, {AcousticProbe.AboveText()}"
                : $"ceiling {CeilingM:0.0} m {CeilingMat}, floors above {FloorsAbove}, roof {RoofMat}";
            if (!ListenerIndoor) return $"exposure: outdoors   above: {above}\n";
            return $"exposure open {ExposureOpen:0.00} ({OpenN}+{BrokenN} broken+{BarsN} bars)  glass {ExposureGlass:0.00} ({GlassN})  other {ExposureOther:0.00} ({OtherN})  blocked {BlockedN}  " +
                   $"room floor {RoomFloor:0.00} -> value {Value:0.00}  open side {SideAz():0} deg ({OneSided:0.00} one-sided)  open sectors {OpenSectors}\n   above: {above}\n";
        }

        public static string Summary()
        {
            var sb = new StringBuilder();
            sb.AppendLine("OUTDOOR EXPOSURE (drives fix 8)");
            sb.AppendLine($"  0.6.16: 48-direction sweep skipped while fix 9 ran in {SkippedUpdates} frames (only the 5 up-rays measured then; exposure.csv and the marks exposure columns are stale in those)");
            sb.AppendLine($"  updates {Updates}, indoors {IndoorUpdates}   cost per update avg {(TimedN > 0 ? TotalMs / TimedN : 0):0.000} ms, max {MaxMs:0.00} ms (per frame)");
            sb.AppendLine($"  other-layer mask: {_otherMaskNames}");
            var top = new List<KeyValuePair<string, int>>(BetweenHits);
            top.Sort((a, b) => b.Value.CompareTo(a.Value));
            sb.AppendLine("  between you and the outside (layer:object:material:collider size -> counted as, count), top 25:");
            for (int i = 0; i < top.Count && i < 25; i++) sb.AppendLine($"    {top[i].Key}  {top[i].Value}");
            return sb.ToString();
        }
    }
}
