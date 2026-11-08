using System;
using System.Diagnostics;
using System.Text;
using UnityEngine;

namespace RealSoundSimulation.Diagnostics
{
    // 0.6.20 (ROADMAP 6.0b): how long the main thread waits for probe v2's ray batch. The batch is scheduled in Update and
    // runs on the worker threads, which it shares with the game's occlusion rays, Burst jobs and SAIN's vision rays; it is
    // completed in LateUpdate (0.6.16). If the workers are busy, Complete() blocks the main thread until the rays are
    // done. The muzzle measure (64 rays x 3 legs per shot) completes its batches at once, so it always waits; it is
    // counted separately.
    //
    // Outputs: timing.csv (window avg/max wait per frame, frames that waited), slowframes.csv (one row per slow frame: over
    // 25 ms, or a wait of 1 ms or more, with wall-clock time to line up with FrameDiag's frames.log), and the summary.
    // If the wait is near zero on the bad frames, neither fix in 6.0b (ray quality by core count, next-frame batch) is
    // needed. Allocation-free per frame.
    internal static class BatchWait
    {
        public const float SlowFrameMs = 25f, SlowWaitMs = 1f;
        private static readonly double TickMs = 1000.0 / Stopwatch.Frequency;

        // this frame
        private static double _batchMs, _muzzleMs; private static bool _waited; private static int _rays;
        // the previous frame (Time.unscaledDeltaTime in frame N is frame N-1's length, so rows describe frame N-1)
        private static double _prevBatchMs, _prevMuzzleMs; private static int _prevRays; private static bool _havePrev;
        private static int _frame = -1;

        // window (timing.csv, 5 s) and raid totals
        public static double WinSum, WinMax; public static int WinFrames, WinWaited;
        public static double TotSum, TotMax, MuzzleSum, MuzzleMax; public static long TotFrames, TotWaited, Muzzles, SlowRows;

        public static long Now() => Stopwatch.GetTimestamp();
        public static double Ms(long since) => (Stopwatch.GetTimestamp() - since) * TickMs;

        public static void Reset()
        {
            _batchMs = _muzzleMs = _prevBatchMs = _prevMuzzleMs = 0; _waited = _havePrev = false; _rays = _prevRays = 0; _frame = -1;
            WinSum = WinMax = 0; WinFrames = WinWaited = 0;
            TotSum = TotMax = MuzzleSum = MuzzleMax = 0; TotFrames = TotWaited = Muzzles = SlowRows = 0;
        }

        // The continuous batch's Complete(): wasDone = the job had already finished (no real wait).
        public static void AddBatch(double ms, bool wasDone, int rays) { _batchMs += ms; if (!wasDone) _waited = true; _rays += rays; }

        // One shot's muzzle measure: all its Complete() calls together.
        public static void AddMuzzle(double ms) { _muzzleMs += ms; Muzzles++; MuzzleSum += ms; if (ms > MuzzleMax) MuzzleMax = ms; }

        // LateUpdate, once per frame while the probe runs in raid (after the batch was finished).
        public static void FrameEnd()
        {
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;
            double w = _batchMs + _muzzleMs;
            WinSum += w; WinFrames++; if (w > WinMax) WinMax = w; if (_waited) WinWaited++;
            TotSum += w; TotFrames++; if (w > TotMax) TotMax = w; if (_waited) TotWaited++;

            if (_havePrev && DiagLog.IsOpen)
            {
                float frameMs = Time.unscaledDeltaTime * 1000f; // the previous frame
                if (frameMs > SlowFrameMs || _prevBatchMs + _prevMuzzleMs >= SlowWaitMs)
                {
                    StringBuilder sb = DiagLog.Begin();
                    DiagLog.AppendF(sb, Time.time - DiagRunner.RaidStartTime, 2); sb.Append(',');
                    AppendClock(sb, DateTime.Now); sb.Append(',');
                    DiagLog.AppendF(sb, frameMs, 1); sb.Append(',');
                    DiagLog.AppendF(sb, (float)_prevBatchMs, 3); sb.Append(',');
                    DiagLog.AppendF(sb, (float)_prevMuzzleMs, 3); sb.Append(',');
                    DiagLog.AppendI(sb, _prevRays); sb.Append(',');
                    DiagLog.AppendI(sb, DiagRunner.LastAliveBots);
                    DiagLog.EndRow("slowframes.csv");
                    SlowRows++;
                }
            }
            _prevBatchMs = _batchMs; _prevMuzzleMs = _muzzleMs; _prevRays = _rays; _havePrev = true;
            _batchMs = _muzzleMs = 0; _waited = false; _rays = 0;
        }

        // HH:mm:ss.fff without allocating (the same clock as spawns.csv and FrameDiag's frames.log)
        private static void AppendClock(StringBuilder sb, DateTime t)
        {
            Two(sb, t.Hour); sb.Append(':'); Two(sb, t.Minute); sb.Append(':'); Two(sb, t.Second); sb.Append('.');
            int ms = t.Millisecond; sb.Append((char)('0' + ms / 100)); sb.Append((char)('0' + ms / 10 % 10)); sb.Append((char)('0' + ms % 10));
        }
        private static void Two(StringBuilder sb, int v) { sb.Append((char)('0' + v / 10)); sb.Append((char)('0' + v % 10)); }

        public static double WinAvg => WinFrames > 0 ? WinSum / WinFrames : 0;
        public static void ResetWindow() { WinSum = WinMax = 0; WinFrames = WinWaited = 0; }

        public static string Summary()
        {
            if (TotFrames == 0) return "PROBE V2 BATCH WAIT: probe v2 did not run\n";
            return "PROBE V2 BATCH WAIT (main thread waiting for the worker threads; ROADMAP 6.0b)\n" +
                   $"  {TotFrames} frames: avg {TotSum / TotFrames:0.000} ms, max {TotMax:0.00} ms, frames that really waited {100.0 * TotWaited / TotFrames:0.0}%\n" +
                   $"  muzzle measures {Muzzles}: avg {(Muzzles > 0 ? MuzzleSum / Muzzles : 0):0.00} ms, max {MuzzleMax:0.00} ms (included above)\n" +
                   $"  slow frames logged (over {SlowFrameMs:0} ms, or a wait of {SlowWaitMs:0} ms or more): {SlowRows} (slowframes.csv; sort by frameMs)\n";
        }
    }
}
