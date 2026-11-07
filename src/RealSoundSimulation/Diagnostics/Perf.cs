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
        public enum Part { EnclosureSweep, MuzzleRoom, AimCone, EchoSearch, Exposure, AmbienceMix, DiagTick, SourceScan, EnvLog, LogFlush, AcousticProbe, Count }

        private static readonly string[] Names = { "enclosure sweep (32+5 rays, 10/s)", "muzzle room (37 rays, per shot)", "aim cone (9 rays, per shot)",
            "echo search (per shot, own rays)", "outdoor exposure (continuous, ~290 rays/s)", "ambience mix + opening sounds (per frame)",
            "diagnostics: containers, overlay, timing (per frame)", "diagnostics: source scan (every 15 s)", "diagnostics: ambience/probe rows (per frame)",
            "diagnostics: saving log files (every 2 s)", "acoustic probe v2 (~2000 rays/s, batched)" };

        private static readonly long[] Calls = new long[(int)Part.Count];
        private static readonly double[] TotalMs = new double[(int)Part.Count], MaxMs = new double[(int)Part.Count];
        private static readonly long[] Rays = new long[(int)Part.Count], Lookups = new long[(int)Part.Count];
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
            _depth--;
            if (_depth >= 0 && _depth < MaxDepth) { ChildMs[_depth] += ms; ChildRays[_depth] += rays; ChildLook[_depth] += look; }
        }

        public static void Reset(double now)
        {
            for (int i = 0; i < (int)Part.Count; i++) { Calls[i] = 0; TotalMs[i] = MaxMs[i] = 0; Rays[i] = Lookups[i] = 0; }
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
            sb.AppendLine("  part                                          calls/s   avg ms   max ms   ms/s    rays/s  lookups/s");
            double total = 0; long rays = 0, look = 0;
            for (int i = 0; i < (int)Part.Count; i++)
            {
                if (Calls[i] == 0) continue;
                double avg = TotalMs[i] / Calls[i], perSec = TotalMs[i] / secs;
                total += perSec; rays += Rays[i]; look += Lookups[i];
                sb.AppendLine($"  {Names[i],-44} {Calls[i] / secs,7:0.0} {avg,8:0.000} {MaxMs[i],8:0.00} {perSec,6:0.00} {Rays[i] / secs,8:0} {Lookups[i] / secs,9:0}");
            }
            sb.AppendLine($"  total {total:0.00} ms per second of game time ({total / 10.0:0.00}% of one CPU core), {rays / secs:0} raycasts/s, {look / secs:0} room lookups/s");
            return sb.ToString();
        }

        public static string OverlayLine(double now) => $"CA2 cost {TotalMsPerSecond(now):0.00} ms/s\n";
    }
}
