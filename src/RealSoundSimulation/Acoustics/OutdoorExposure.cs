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
    // Outdoor exposure (roadmap step 4b, phase 1 = measure only, 0.4.0). How much of the outside reaches you indoors, and
    // through what. Design and the reasons: docs/AMBIENCE.md.
    //
    // Every 0.25 s while you're in an indoor audio room:
    //   48 directions (24 around, 15 deg apart, x level and +15 deg up; 12 around before 0.5.0 - raid 14 missed the open
    //   windows of the Nakatani exit hallway, possibly between rays). For each, one ray on the game's sound-blocking layers to the
    //   first wall (max 40 m). Points along it (1.5, 3, 5, 8, 12, 18, 25, 35 m, stopping short of the wall) are looked up
    //   in the game's baked audio rooms; the first point in an OUTDOOR room is where this direction reaches the outside.
    //   (Facade glass and broken windows aren't on the blocking layers, so the ray passes them - which is why this
    //   lookup, not the ray, decides "outside".)
    //   Then one ray on all the OTHER layers (not players, loot, triggers) up to that point says what's in between:
    //     Glass    a glass surface (bullet material Glass, or a window/glass object)   -> will be quieter and muffled
    //     Other    something else that isn't a sound blocker (railing, fence, prop)     -> logged, partly open
    //     Open     nothing                                                               -> open air
    //   Directions that never reach outside are Blocked.
    //   Each open/glass direction counts by how near its opening is: 1 / (1 + (distance / 5 m)^2)  (1.5 m ~0.9, 5 m 0.5,
    //   10 m 0.2). ExposureOpen / ExposureGlass / ExposureOther = sum / 24. A room deep inside a building ~0; standing at
    //   a broken window wall ~0.3+.
    //   PreviewValue: what phase 2 would set the game's outdoor value to (logged only, changes nothing yet):
    //     1 - exp(-4 x open) + 0.45 x (1 - exp(-4 x glass)), at least the room's own baked floor level.
    // Also, every update (indoors or out): what's above you. Straight up to the first ceiling (its material), then again
    // from just above it, up to 4 times: FloorsAbove = surfaces above the first ceiling; RoofMat = the topmost one.
    // Openings (0.5.0, for fix 8's 3D sound): neighbouring directions that reach outside are grouped into openings
    // (contiguous runs around the ring, both elevations together). Each: direction (weighted), distance to where it
    // reaches outside, weight (sum of nearness x path weight), glass share, width. The 3 strongest are kept.
    // Every ray's result is kept for the F10 dump (rays.csv): what it hit, how far, and where it reached outside.
    internal class OutdoorExposure : MonoBehaviour
    {
        // 0.4.1: Bars (window bars/grilles: sound passes) and Broken (a broken window: shards only at the edges) added.
        public enum Path : byte { Blocked, Open, Glass, Other, Bars, Broken }

        private const float Interval = 0.125f; // 0.5.6: 8 per second (was 4)
        public const int Az = 24;
        private static readonly float[] Elev = { 0f, 15f };
        public const int RayCount = Az * 2;
        private const float MaxRange = 40f;
        private static readonly float[] Samples = { 1.5f, 3f, 5f, 8f, 12f, 18f, 25f, 35f };
        private const float NearScale = 5f;
        private const float FarScale = 15f;
        private const float GlassPreview = 0.45f, StrengthPreview = 4f;

        private static readonly Vector3[] Dirs = BuildDirs();

        public static bool Valid, ListenerIndoor;
        public static float ExposureOpen, ExposureGlass, ExposureOther, PreviewValue = float.NaN, RoomFloor = float.NaN;
        // 0.4.2: the value fix 8 applies (= PreviewValue), the open side (world, horizontal) and how one-sided it is (0 all
        // round .. 1 one side only).
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

        public struct Opening { public Vector3 Dir; public float Dist, Weight, Glass, WidthDeg; }
        public const int MaxOpenings = 3;
        public static readonly Opening[] Openings = new Opening[MaxOpenings];
        public static int OpeningN;
        private static readonly Opening[] Clusters = new Opening[Az];

        // what stood between you and the outside, by layer:object (summary; to learn what facade glass is)
        public static readonly Dictionary<string, int> BetweenHits = new Dictionary<string, int>();
        public static int Updates, IndoorUpdates;
        private static int _otherMask = -1;
        private static string _otherMaskNames = "";


        private static readonly AccessTools.FieldRef<SpatialAudioSystem, AudioRoomStorage> StorageRef =
            AccessTools.FieldRefAccess<SpatialAudioSystem, AudioRoomStorage>("_audioRoomStorage");

        public static void Reset()
        {
            OpeningN = 0; GlassRendererCache.Clear();
            Valid = false; Updates = IndoorUpdates = 0; TotalMs = MaxMs = 0; TimedN = 0;
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
            if (!inRaid || !Plugin.ExposureProbeOn.Value) { Valid = false; return; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { Measure(MonoBehaviourSingleton<SpatialAudioSystem>.Instance); }
            catch (Exception e) { DiagRunner.ReportOnce("outdoor exposure", e); }
            double ms = sw.Elapsed.TotalMilliseconds;
            TotalMs += ms; TimedN++; if (ms > MaxMs) MaxMs = ms;
        }

        public static double TotalMs, MaxMs; public static int TimedN;

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

            if (Time.time >= _nextAbove) { _nextAbove = Time.time + 0.25f; MeasureAbove(o, mask); }
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
            float preview = (1f - Mathf.Exp(-StrengthPreview * open)) + glassW * (1f - Mathf.Exp(-StrengthPreview * ExposureGlass));
            PreviewValue = Mathf.Clamp01(Mathf.Max(preview, float.IsNaN(RoomFloor) ? 0f : RoomFloor));
            Value = PreviewValue;
            OpenSide = side;
            OneSided = wSum > 1e-4f ? Mathf.Clamp01(side.magnitude / wSum) : 0f;
            BuildOpenings();
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

        // Group neighbouring exposed directions (both elevations of an azimuth together) into openings; keep the 3 strongest.
        // Also (0.5.2) fill the fixed world sectors used by the opening sounds.
        private static void BuildOpenings()
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
            OpeningN = 0;
            int start = -1, exposed = 0;
            for (int a = 0; a < Az; a++)
            {
                if (RayWeight[a] + RayWeight[Az + a] > 0f) exposed++;
                else if (start < 0) start = a;
            }
            if (exposed == 0) return;
            int n = 0;
            if (start < 0)
            {
                // open all round: three equal sectors
                for (int s = 0; s < 3; s++) { Clusters[n] = default; for (int a = s * Az / 3; a < (s + 1) * Az / 3; a++) AddToCluster(ref Clusters[n], a); n++; }
            }
            else
            {
                bool inRun = false;
                for (int step = 1; step <= Az; step++)
                {
                    int a = (start + step) % Az;
                    bool open = RayWeight[a] + RayWeight[Az + a] > 0f;
                    if (open)
                    {
                        if (!inRun) { Clusters[n] = default; inRun = true; }
                        AddToCluster(ref Clusters[n], a);
                    }
                    else if (inRun) { n++; inRun = false; }
                }
                if (inRun) n++;
            }
            for (int c = 0; c < n; c++)
            {
                ref Opening op = ref Clusters[c];
                if (op.Weight <= 0f) continue;
                op.Dist /= op.Weight; op.Glass /= op.Weight;
                op.Dir = op.Dir.sqrMagnitude > 1e-6f ? op.Dir.normalized : Vector3.forward;
            }
            // top 3 by weight
            for (int k = 0; k < MaxOpenings; k++)
            {
                int best = -1;
                for (int c = 0; c < n; c++) if (Clusters[c].Weight > 0f && (best < 0 || Clusters[c].Weight > Clusters[best].Weight)) best = c;
                if (best < 0) break;
                Openings[OpeningN++] = Clusters[best];
                Clusters[best].Weight = 0f;
            }
        }

        public static Vector3 SectorHome(int s)
        {
            float a = (s + 0.5f) * Mathf.PI * 2f / SectorN;
            return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        }

        private static void AddToCluster(ref Opening op, int a)
        {
            for (int e = 0; e < 2; e++)
            {
                int i = e * Az + a;
                float w = RayWeight[i];
                if (w <= 0f) continue;
                op.Dir += RayDir[i] * w;
                op.Dist += RayOutM[i] * w;
                if (RayPath[i] == Path.Glass) op.Glass += w;
                op.Weight += w;
            }
            op.WidthDeg += 360f / Az;
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
                DiagLog.F(CeilingM, "0.0"), CeilingMat.ToString(), FloorsAbove.ToString(), RoofMat.ToString(), DiagLog.F(PreviewValue),
                DiagLog.F(SideAz(), "0"), DiagLog.F(OneSided), OpeningN.ToString(),
                OpeningN > 0 ? DiagLog.F(RelAz(Openings[0].Dir), "0") : "", OpeningN > 0 ? DiagLog.F(Openings[0].Dist, "0.0") : "", OpeningN > 0 ? DiagLog.F(Openings[0].Weight) : "", OpeningN > 0 ? DiagLog.F(Openings[0].Glass) : "");
        }

        public const string Header = "t,listenerIndoor,roomOutdoorFloor,openRays,glassRays,otherRays,blockedRays,barsRays,brokenRays,exposureOpen,exposureGlass,exposureOther,nearestOpenM,nearestOpenAz,nearestGlassM,nearestGlassAz,ceilingM,ceilingMat,floorsAbove,roofMat,previewOutdoorValue,openSideAz,oneSided,openings,op1Az,op1DistM,op1Weight,op1Glass";

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
            string above = FloorsAbove < 0 ? "open sky" : $"ceiling {CeilingM:0.0} m {CeilingMat}, floors above {FloorsAbove}, roof {RoofMat}";
            if (!ListenerIndoor) return $"exposure: outdoors   above: {above}\n";
            return $"exposure open {ExposureOpen:0.00} ({OpenN}+{BrokenN} broken+{BarsN} bars)  glass {ExposureGlass:0.00} ({GlassN})  other {ExposureOther:0.00} ({OtherN})  blocked {BlockedN}  " +
                   $"room floor {RoomFloor:0.00} -> value {PreviewValue:0.00}  open side {SideAz():0} deg ({OneSided:0.00} one-sided)  openings {OpeningN}\n   above: {above}\n";
        }

        public static string Summary()
        {
            var sb = new StringBuilder();
            sb.AppendLine("OUTDOOR EXPOSURE (step 4b, measure only)");
            sb.AppendLine($"  updates {Updates}, indoors {IndoorUpdates}   cost per update avg {(TimedN > 0 ? TotalMs / TimedN : 0):0.000} ms, max {MaxMs:0.00} ms (8 per second)");
            sb.AppendLine($"  other-layer mask: {_otherMaskNames}");
            var top = new List<KeyValuePair<string, int>>(BetweenHits);
            top.Sort((a, b) => b.Value.CompareTo(a.Value));
            sb.AppendLine("  between you and the outside (layer:object:material:collider size -> counted as, count), top 25:");
            for (int i = 0; i < top.Count && i < 25; i++) sb.AppendLine($"    {top[i].Key}  {top[i].Value}");
            return sb.ToString();
        }
    }
}
