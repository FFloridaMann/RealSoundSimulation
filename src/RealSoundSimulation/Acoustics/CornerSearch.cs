using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace RealSoundSimulation.Acoustics
{
    // 0.6.51: the way around for a sound with no baked route (the room map has both ends outdoors). User, 9 Nov 2026: "why not more route searches".
    // A search from both ends, in two batches on the worker threads:
    //   1  a level fan from the listener, a level fan from the source, and a ray to each opening probe v2 remembers (those the listener can see join the listener's points)
    //   2  points tested shortest way first: listener point to source, listener to source point, and listener point to source point (two corners). The first clear one wins.
    // Small: 36 directions, 3 points each, 200 tests, about 290 rays, finished in the frame it's asked for.
    // Big:   72 directions, 4 points each, 800 tests, about 960 rays, over the next 2 frames. For a sound still travelling to you, or a gun that's already firing.
    // An answer is reused for 3 s by a request within 3 m at the source and 1.5 m at the listener, so a burst of grenades or bots side by side costs one search.
    internal static class CornerSearch
    {
        private const int PoolN = 8, MaxPoints = 320, MaxCands = 2400, MaxCmds = 1000;
        private const float BackOffM = 0.5f, MinFreeM = 1.5f, CacheS = 3f, SameSourceM = 3f, SameListenerM = 1.5f, SyncEveryS = 0.25f;
        private static readonly float[] Along3 = { 0.4f, 0.7f, 1f }, Along4 = { 0.25f, 0.5f, 0.75f, 1f };

        internal struct Cand { public float Len; public short Type, I, J; }
        private sealed class ByLen : IComparer<Cand> { public int Compare(Cand a, Cand b) => a.Len.CompareTo(b.Len); }
        private static readonly ByLen Comparer = new ByLen();

        internal sealed class Job
        {
            public int Serial, Phase; // 0 free, 1 fans, 2 pairs, 3 done
            public Vector3 L, T;
            public int Mask;
            public bool Big, Found, ViaOpening;
            public float DoneT, Path;
            public Vector3 Near;
            public int Corners;
            public bool Done => Phase == 3;
            internal JobHandle H;
            internal NativeArray<RaycastCommand> C;
            internal NativeArray<RaycastHit> R;
            internal int Fan, OpenN, An, Bn, FirstOpen, CandN, Tests;
            internal readonly Vector3[] Open = new Vector3[32], A = new Vector3[MaxPoints], B = new Vector3[MaxPoints];
            internal readonly Cand[] Cands = new Cand[MaxCands];
        }

        private static readonly Job[] Pool = new Job[PoolN];
        private static float _nextSync;
        public static int Small, Big, Reused, PoolFull, FoundOne, FoundTwo, None, ViaOpenings;

        public static void Reset()
        {
            for (int i = 0; i < PoolN; i++)
            {
                Job j = Pool[i];
                if (j == null) continue;
                if (j.Phase == 1 || j.Phase == 2) j.H.Complete();
                j.Phase = 0; j.Serial++;
            }
            _nextSync = 0f;
            Small = Big = Reused = PoolFull = FoundOne = FoundTwo = None = ViaOpenings = 0;
        }

        // A frame-stopping search for a gun's first shot; a few a second at most.
        public static bool TakeSyncBudget()
        {
            float now = Time.unscaledTime;
            if (now < _nextSync) return false;
            _nextSync = now + SyncEveryS;
            return true;
        }

        // null = every search slot is busy.
        public static Job Get(Vector3 listener, Vector3 target, int mask, bool canWait)
        {
            float now = Time.time;
            Job free = null;
            for (int i = 0; i < PoolN; i++)
            {
                Job j = Pool[i];
                if (j == null) { if (free == null || free.Phase != 0) { Pool[i] = j = new Job(); free = j; } continue; }
                if (j.Phase != 0 && (j.Phase != 3 || now - j.DoneT < CacheS) && (j.T - target).sqrMagnitude < SameSourceM * SameSourceM && (j.L - listener).sqrMagnitude < SameListenerM * SameListenerM)
                {
                    Reused++;
                    if (!canWait) Finish(j);
                    return j;
                }
                if (j.Phase == 0) { if (free == null || free.Phase != 0) free = j; }
                else if (j.Phase == 3 && (free == null || (free.Phase == 3 && j.DoneT < free.DoneT))) free = j;
            }
            if (free == null) { PoolFull++; return null; }
            free.Serial++;
            free.L = listener; free.T = target; free.Mask = mask; free.Big = canWait;
            free.Found = free.ViaOpening = false;
            if (canWait) Big++; else Small++;
            Begin(free);
            if (!canWait) Finish(free);
            return free;
        }

        public static void Tick()
        {
            for (int i = 0; i < PoolN; i++)
            {
                Job j = Pool[i];
                if (j != null && (j.Phase == 1 || j.Phase == 2) && j.H.IsCompleted) Step(j);
            }
        }

        private static void Finish(Job j)
        {
            while (j.Phase == 1 || j.Phase == 2)
            {
                j.H.Complete();
                Step(j);
            }
        }

        private static void Step(Job j)
        {
            j.H.Complete();
            if (j.Phase == 1) BuildPairs(j);
            else ReadPairs(j);
        }

        private static void Begin(Job j)
        {
            if (!j.C.IsCreated) { j.C = new NativeArray<RaycastCommand>(MaxCmds, Allocator.Persistent); j.R = new NativeArray<RaycastHit>(MaxCmds, Allocator.Persistent); }
            j.Fan = j.Big ? 72 : 36;
            float range = j.Big ? 60f : 40f;
            var qp = new QueryParameters(j.Mask, false, QueryTriggerInteraction.Ignore, true);
            int n = 0;
            for (int i = 0; i < j.Fan; i++)
            {
                float a = i * Mathf.PI * 2f / j.Fan;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                j.C[n] = new RaycastCommand(j.L, d, qp, range);
                j.C[n + j.Fan] = new RaycastCommand(j.T, d, qp, range);
                n++;
            }
            n += j.Fan;
            j.OpenN = 0;
            if (AcousticProbe.Valid)
            {
                int got = AcousticProbe.CopyOpenings(j.Open);
                for (int i = 0; i < got; i++)
                {
                    Vector3 v = j.Open[i] - j.L;
                    float m = v.magnitude;
                    if (m < MinFreeM) continue;
                    j.Open[j.OpenN++] = j.Open[i];
                    j.C[n++] = new RaycastCommand(j.L, v / m, qp, m - 0.1f);
                }
            }
            CountedRay.Rays += n;
            j.Phase = 1;
            j.H = RaycastCommand.ScheduleBatch(j.C.GetSubArray(0, n), j.R.GetSubArray(0, n), 16, 1);
            JobHandle.ScheduleBatchedJobs();
        }

        private static void BuildPairs(Job j)
        {
            float range = j.Big ? 60f : 40f;
            float[] along = j.Big ? Along4 : Along3;
            float straight = Vector3.Distance(j.L, j.T), limit = straight + range;
            j.An = j.Bn = 0;
            for (int i = 0; i < j.Fan; i++)
            {
                float a = i * Mathf.PI * 2f / j.Fan;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                AddPoints(j.A, ref j.An, j.L, d, j.R[i], range, along);
                AddPoints(j.B, ref j.Bn, j.T, d, j.R[i + j.Fan], range, along);
            }
            j.FirstOpen = j.An;
            for (int i = 0; i < j.OpenN && j.An < MaxPoints; i++)
                if (j.R[j.Fan * 2 + i].collider == null) j.A[j.An++] = j.Open[i];

            int c = 0;
            for (short i = 0; i < j.An && c < MaxCands; i++)
            {
                float len = Vector3.Distance(j.L, j.A[i]) + Vector3.Distance(j.A[i], j.T);
                if (len < limit) j.Cands[c++] = new Cand { Len = len, Type = 0, I = i };
            }
            for (short k = 0; k < j.Bn && c < MaxCands; k++)
            {
                float len = Vector3.Distance(j.L, j.B[k]) + Vector3.Distance(j.B[k], j.T);
                if (len < limit) j.Cands[c++] = new Cand { Len = len, Type = 1, J = k };
            }
            System.Array.Sort(j.Cands, 0, c, Comparer);

            // two corners: the most promising points from each end, paired
            int top = j.Big ? 40 : 24, oneCorner = c, ta = 0, tb = 0;
            for (int x = 0; x < oneCorner && (ta < top || tb < top); x++)
            {
                if (j.Cands[x].Type == 0 && ta < top) { Top1[ta++] = j.Cands[x].I; }
                else if (j.Cands[x].Type == 1 && tb < top) { Top2[tb++] = j.Cands[x].J; }
            }
            for (int x = 0; x < ta; x++)
                for (int y = 0; y < tb && c < MaxCands; y++)
                {
                    Vector3 p = j.A[Top1[x]], q = j.B[Top2[y]];
                    float len = Vector3.Distance(j.L, p) + Vector3.Distance(p, q) + Vector3.Distance(q, j.T);
                    if (len < limit) j.Cands[c++] = new Cand { Len = len, Type = 2, I = Top1[x], J = Top2[y] };
                }
            if (c > oneCorner) System.Array.Sort(j.Cands, 0, c, Comparer);
            j.CandN = c;
            j.Tests = Mathf.Min(c, j.Big ? 800 : 200);
            if (j.Tests == 0) { Complete(j); return; }

            var qp = new QueryParameters(j.Mask, false, QueryTriggerInteraction.Ignore, true);
            for (int x = 0; x < j.Tests; x++)
            {
                Cand t = j.Cands[x];
                Vector3 from = t.Type == 1 ? j.L : j.A[t.I], to = t.Type == 0 ? j.T : j.B[t.J], v = to - from;
                float m = v.magnitude;
                j.C[x] = new RaycastCommand(from, m > 0.001f ? v / m : Vector3.up, qp, Mathf.Max(m, 0.001f));
            }
            CountedRay.Rays += j.Tests;
            j.Phase = 2;
            j.H = RaycastCommand.ScheduleBatch(j.C.GetSubArray(0, j.Tests), j.R.GetSubArray(0, j.Tests), 16, 1);
            JobHandle.ScheduleBatchedJobs();
        }

        private static readonly short[] Top1 = new short[40], Top2 = new short[40];

        private static void AddPoints(Vector3[] into, ref int n, Vector3 from, Vector3 d, RaycastHit hit, float range, float[] along)
        {
            float free = (hit.collider != null ? hit.distance : range) - BackOffM;
            if (free < MinFreeM) return;
            for (int k = 0; k < along.Length && n < MaxPoints; k++) into[n++] = from + d * (free * along[k]);
        }

        private static void ReadPairs(Job j)
        {
            for (int x = 0; x < j.Tests; x++)
            {
                if (j.R[x].collider != null) continue;
                Cand t = j.Cands[x];
                j.Found = true; j.Path = t.Len; j.Corners = t.Type == 2 ? 2 : 1;
                j.Near = t.Type == 1 ? j.B[t.J] : j.A[t.I];
                j.ViaOpening = t.Type != 1 && t.I >= j.FirstOpen;
                break;
            }
            Complete(j);
        }

        private static void Complete(Job j)
        {
            j.Phase = 3;
            j.DoneT = Time.time;
            if (!j.Found) None++;
            else if (j.Corners == 2) FoundTwo++;
            else FoundOne++;
            if (j.Found && j.ViaOpening) ViaOpenings++;
        }

        public static string Summary() =>
            $"CORNER SEARCH (6.8): small {Small}, big {Big}, answers reused {Reused}, all slots busy {PoolFull}; found one corner {FoundOne}, two corners {FoundTwo}, none {None}; through an opening the probe remembered {ViaOpenings}\n";
    }
}
