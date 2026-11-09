using System.Diagnostics;
using System.Text;

namespace RealSoundSimulation.Diagnostics
{
    // Cost of each part of the plugin on the main thread (0.5.3; the logging itself since 0.5.5). Each part wraps its work in Begin()/End(part); raycasts
    // and room-map lookups are counted by CountedRay. Nested parts (e.g. the echo search running a sweep) are counted once,
    // under the part that did the work. The raid summary's COST section lists, per part: calls per second, average and
    // maximum ms per call, ms per second (its share of each second of game time), raycasts and room lookups per second.
    internal static class Perf
    {
        public enum Part { EnclosureSweep, MuzzleRoom, AimCone, EchoSearch, Exposure, AmbienceMix, DiagTick, SourceScan, EnvLog, LogFlush, AcousticProbe, PortalPositioning, ThroughWall, BlastFromOpening, Count }

        private static readonly string[] Names = { "enclosure sweep (32+5 rays, 10/s)", "muzzle room (37 rays, per shot)", "aim cone (9 rays, per shot)",
            "echo search (per shot, own rays)", "outdoor exposure (continuous, ~290 rays/s)", "ambience mix + opening sounds (per frame)",
            "diagnostics: containers, overlay, timing (per frame)", "diagnostics: source scan (every 15 s)", "diagnostics: ambience/probe rows (per frame)",
            "diagnostics: saving log files (every 2 s)", "acoustic probe v2 (batched, guided, rays by 9g)", "6.1 bot gunshots from the opening (per frame)", "6.2 through-wall thump (per qualifying shot)", "6.4 blast from the opening (per blast)" };

        private static readonly long[] Calls = new long[(int)Part.Count];
        private static readonly double[] TotalMs = new double[(int)Part.Count], MaxMs = new double[(int)Part.Count];
        private static readonly long[] Rays = new long[(int)Part.Count], Lookups = new long[(int)Part.Count];
        // 0.6.15: bytes allocated on the main thread per part (raid 31: heap 2.0 -> 3.1 GB, 67 GCs - our share measured)
        private static readonly long[] Bytes = new long[(int)Part.Count];
        private static readonly long[] Alloc0 = new long[MaxDepth], ChildBytes = new long[MaxDepth];
        // 0.6.17: raid 32's column read 0.0 everywhere - under Unity's Mono the per-thread counter returns 0 without failing.
        // At startup each counter is tested with a deliberate 64 KB allocation and the first that sees it is used:
        //   Thread  GC.GetAllocatedBytesForCurrentThread (exact, this thread only)
        //   Heap    GC.GetTotalMemory(false) deltas (whole managed heap: approximate - other threads count too, and a
        //           collection inside a part makes the delta negative; those are dropped)
        //   Mono    Profiler.GetMonoUsedSizeLong deltas (the same, from Unity)
        public enum AllocSource { None, Thread, Heap, Mono }
        public static AllocSource Source = AllocSource.None;
        public static long TestMeasured; // what the chosen counter read for the 64 KB test allocation
        private static bool _allocOk;

        private static long Read(AllocSource s)
        {
            switch (s)
            {
                case AllocSource.Thread: return System.GC.GetAllocatedBytesForCurrentThread();
                case AllocSource.Heap: return System.GC.GetTotalMemory(false);
                case AllocSource.Mono: return UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
                default: return 0;
            }
        }

        public static void InitAlloc()
        {
            foreach (AllocSource s in new[] { AllocSource.Thread, AllocSource.Heap, AllocSource.Mono })
            {
                try
                {
                    long a0 = Read(s);
                    byte[] test = new byte[64 * 1024];
                    test[test.Length - 1] = 1;
                    long a1 = Read(s);
                    System.GC.KeepAlive(test);
                    if (a1 - a0 >= 60000) { Source = s; TestMeasured = a1 - a0; _allocOk = true; break; }
                }
                catch { }
            }
            RealSoundSimulation.Plugin.Log.LogInfo(_allocOk ? $"Allocation counter: {Source} (64 KB test read as {TestMeasured / 1024.0:0.0} KB)" : "Allocation counter: none works in this runtime");
        }

        private static long Alloc()
        {
            if (!_allocOk) return 0;
            try { return Read(Source); }
            catch { _allocOk = false; return 0; }
        }
        private static double _startTime;
        private static readonly double TickMs = 1000.0 / Stopwatch.Frequency;

        private const int MaxDepth = 8;
        private static int _depth = -1;
        private static readonly long[] StartTs = new long[MaxDepth];
        private static readonly double[] ChildMs = new double[MaxDepth];
        private static readonly long[] Rays0 = new long[MaxDepth], Look0 = new long[MaxDepth], ChildRays = new long[MaxDepth], ChildLook = new long[MaxDepth];

        public static void Begin()
        {
            if (_depth >= MaxDepth - 1) { _depth++; return; }
            _depth++;
            StartTs[_depth] = Stopwatch.GetTimestamp(); ChildMs[_depth] = 0;
            Rays0[_depth] = Acoustics.CountedRay.Rays; Look0[_depth] = Acoustics.CountedRay.Lookups;
            ChildRays[_depth] = ChildLook[_depth] = 0;
            Alloc0[_depth] = Alloc(); ChildBytes[_depth] = 0;
        }

        public static void End(Part p)
        {
            if (_depth < 0) return;
            if (_depth >= MaxDepth) { _depth--; return; }
            double ms = (Stopwatch.GetTimestamp() - StartTs[_depth]) * TickMs;
            long rays = Acoustics.CountedRay.Rays - Rays0[_depth], look = Acoustics.CountedRay.Lookups - Look0[_depth];
            int i = (int)p;
            double self = ms - ChildMs[_depth];
            Calls[i]++; TotalMs[i] += self; if (self > MaxMs[i]) MaxMs[i] = self;
            Rays[i] += rays - ChildRays[_depth]; Lookups[i] += look - ChildLook[_depth];
            long bytes = System.Math.Max(0L, Alloc() - Alloc0[_depth]); // heap counters: a collection inside the part reads negative
            Bytes[i] += System.Math.Max(0L, bytes - ChildBytes[_depth]);
            _depth--;
            if (_depth >= 0 && _depth < MaxDepth) { ChildMs[_depth] += ms; ChildRays[_depth] += rays; ChildLook[_depth] += look; ChildBytes[_depth] += bytes; }
        }

        public static void Reset(double now)
        {
            for (int i = 0; i < (int)Part.Count; i++) { Calls[i] = 0; TotalMs[i] = MaxMs[i] = 0; Rays[i] = Lookups[i] = 0; Bytes[i] = 0; }
            _startTime = now; _depth = -1;
        }

        public static double TotalMsPerSecond(double now)
        {
            double secs = System.Math.Max(1.0, now - _startTime), t = 0;
            for (int i = 0; i < (int)Part.Count; i++) t += TotalMs[i];
            return t / secs;
        }

        public static string Summary(double now)
        {
            double secs = System.Math.Max(1.0, now - _startTime);
            var sb = new StringBuilder();
            sb.AppendLine("COST (Real Sound Simulation, main thread)");
            sb.AppendLine("  part                                          calls/s   avg ms   max ms   ms/s    rays/s  lookups/s   alloc KB/s");
            double total = 0; long rays = 0, look = 0, bytes = 0;
            for (int i = 0; i < (int)Part.Count; i++)
            {
                if (Calls[i] == 0) continue;
                double avg = TotalMs[i] / Calls[i], perSec = TotalMs[i] / secs;
                total += perSec; rays += Rays[i]; look += Lookups[i]; bytes += Bytes[i];
                sb.AppendLine($"  {Names[i],-44} {Calls[i] / secs,7:0.0} {avg,8:0.000} {MaxMs[i],8:0.00} {perSec,6:0.00} {Rays[i] / secs,8:0} {Lookups[i] / secs,9:0} {(_allocOk ? (Bytes[i] / secs / 1024.0).ToString("0.0").PadLeft(11) : "        n/a")}");
            }
            sb.AppendLine($"  total {total:0.00} ms per second of game time ({total / 10.0:0.00}% of one CPU core), {rays / secs:0} raycasts/s, {look / secs:0} room lookups/s, " +
                (_allocOk ? $"{bytes / secs / 1024.0:0.0} KB/s allocated ({bytes / 1048576.0:0.0} MB this raid; counter {Source}, 64 KB test read as {TestMeasured / 1024.0:0.0} KB{(Source == AllocSource.Thread ? "" : ", whole-heap deltas: approximate")})" : "allocations n/a (no counter saw the 64 KB startup test)"));
            return sb.ToString();
        }

        public static string OverlayLine(double now) => $"CA2 cost {TotalMsPerSecond(now):0.00} ms/s\n";
    }
}
