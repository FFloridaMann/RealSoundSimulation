using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace RealSoundSimulation.Diagnostics
{
    // 0.6.20: is the audio thread keeping up? Sits on the AudioListener's object for the raid (added and removed by
    // DiagRunner) so Unity calls OnAudioFilterRead once per DSP block, after the whole mix (every voice, the mixer, the
    // spatializer) has been rendered for that block. The samples are not touched. The only thing measured is the time
    // between consecutive calls: a block of 1024 samples at 48 kHz is due every 21.3 ms, so a gap well past that means the
    // DSP graph took longer than a block or the audio thread was held off the CPU - the condition behind crackles and
    // dropouts. Voices cost this thread, not the frame, so this is the ceiling the voice budget (ROADMAP) is set against.
    //
    // Allocation-free and Unity-free on the audio thread (rule: nothing allocates there; Unity's API may not be called
    // there either). Counters are read on the main thread every 5 s (timing.csv), on the overlay and in the summary.
    //   late15  gaps over 1.5 x the block   late2x  gaps over 2 x the block   stalls  gaps over 100 ms (alt-tab, loading)
    // Caveat: Unity double-buffers, so one late block is not always audible; a steady share of late blocks is.
    internal sealed class AudioThreadProbe : MonoBehaviour
    {
        private static readonly double TickMs = 1000.0 / Stopwatch.Frequency;

        private long _lastTs, _sampleRate = 48000;
        private long _calls, _gapSum, _gapMax, _late15, _late2, _stalls;           // window (since the last Snapshot)
        private long _tCalls, _tGapSum, _tGapMax, _tLate15, _tLate2, _tStalls;    // raid totals
        private long _blockSamples;

        public void Init(int sampleRate) { if (sampleRate > 0) _sampleRate = sampleRate; }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            long now = Stopwatch.GetTimestamp();
            long last = _lastTs;
            _lastTs = now;
            int samples = channels > 0 ? data.Length / channels : data.Length;
            _blockSamples = samples;
            if (last == 0) return; // first call: no gap yet
            long gap = now - last;
            long block = samples * Stopwatch.Frequency / _sampleRate;
            Interlocked.Increment(ref _calls); Interlocked.Increment(ref _tCalls);
            Interlocked.Add(ref _gapSum, gap); Interlocked.Add(ref _tGapSum, gap);
            Max(ref _gapMax, gap); Max(ref _tGapMax, gap);
            if (gap * 10 > block * 15) { Interlocked.Increment(ref _late15); Interlocked.Increment(ref _tLate15); }
            if (gap > block * 2) { Interlocked.Increment(ref _late2); Interlocked.Increment(ref _tLate2); }
            if (gap * TickMs > 100.0) { Interlocked.Increment(ref _stalls); Interlocked.Increment(ref _tStalls); }
        }

        private static void Max(ref long field, long value)
        {
            long cur = Interlocked.Read(ref field);
            while (value > cur)
            {
                long seen = Interlocked.CompareExchange(ref field, value, cur);
                if (seen == cur) return;
                cur = seen;
            }
        }

        public struct Window { public long Calls, Late15, Late2, Stalls; public double GapAvgMs, GapMaxMs, BlockMs; }

        // Main thread. Reads and clears the window counters.
        public Window Snapshot()
        {
            var w = new Window
            {
                Calls = Interlocked.Exchange(ref _calls, 0),
                Late15 = Interlocked.Exchange(ref _late15, 0),
                Late2 = Interlocked.Exchange(ref _late2, 0),
                Stalls = Interlocked.Exchange(ref _stalls, 0),
            };
            long sum = Interlocked.Exchange(ref _gapSum, 0), max = Interlocked.Exchange(ref _gapMax, 0);
            w.GapAvgMs = w.Calls > 0 ? sum * TickMs / w.Calls : 0;
            w.GapMaxMs = max * TickMs;
            w.BlockMs = BlockMs;
            return w;
        }

        public double BlockMs => _sampleRate > 0 ? Interlocked.Read(ref _blockSamples) * 1000.0 / _sampleRate : 0;
        public long TotalCalls => Interlocked.Read(ref _tCalls);
        public long TotalLate15 => Interlocked.Read(ref _tLate15);
        public long TotalLate2 => Interlocked.Read(ref _tLate2);
        public long TotalStalls => Interlocked.Read(ref _tStalls);
        public double TotalGapAvgMs { get { long c = TotalCalls; return c > 0 ? Interlocked.Read(ref _tGapSum) * TickMs / c : 0; } }
        public double TotalGapMaxMs => Interlocked.Read(ref _tGapMax) * TickMs;

        public string OverlayLine()
        {
            Window w = Peek();
            return $"audio thread: block {BlockMs:0.0} ms  gap avg {w.GapAvgMs:0.0} max {w.GapMaxMs:0.0} ms  late >1.5x {w.Late15} >2x {w.Late2}  stalls {w.Stalls}  (window)\n";
        }

        // Window values without clearing them (overlay).
        private Window Peek()
        {
            long c = Interlocked.Read(ref _calls);
            return new Window
            {
                Calls = c, Late15 = Interlocked.Read(ref _late15), Late2 = Interlocked.Read(ref _late2), Stalls = Interlocked.Read(ref _stalls),
                GapAvgMs = c > 0 ? Interlocked.Read(ref _gapSum) * TickMs / c : 0, GapMaxMs = Interlocked.Read(ref _gapMax) * TickMs, BlockMs = BlockMs,
            };
        }

        public string Summary()
        {
            long c = TotalCalls;
            double pct15 = c > 0 ? 100.0 * TotalLate15 / c : 0, pct2 = c > 0 ? 100.0 * TotalLate2 / c : 0;
            string verdict = c == 0 ? "no blocks seen (filter never ran?)"
                : pct2 > 1 ? "LATE: over 1% of blocks ran past twice their length - expect crackle; the audio thread is the ceiling"
                : pct15 > 5 ? "marginal: over 5% of blocks ran past 1.5x their length"
                : "healthy: the audio thread kept up";
            return "AUDIO THREAD (listener filter; voices cost this thread, not the frame)\n" +
                   $"  block {BlockMs:0.0} ms ({Interlocked.Read(ref _blockSamples)} samples at {_sampleRate} Hz), blocks {c}, gap avg {TotalGapAvgMs:0.00} ms max {TotalGapMaxMs:0.0} ms\n" +
                   $"  late >1.5x block {TotalLate15} ({pct15:0.00}%), >2x {TotalLate2} ({pct2:0.00}%), stalls >100 ms {TotalStalls}  -> {verdict}\n";
        }
    }
}
