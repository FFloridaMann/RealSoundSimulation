using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using RealSoundSimulation.Dsp;

namespace HeadsetBench
{
    // Offline test bench for the headset model. Renders WAVs you can listen to outside the game, measures the numbers that
    // matter (quiet-sound gain, gunshot clamp, recovery/pumping, hiss, frequency response, CPU, allocations) and fails
    // loudly on NaNs or audio-thread allocations.
    //
    //   dotnet run -c Release --project tools/HeadsetBench -- [outDir]
    internal static class Program
    {
        private const int Rate = Signals.Rate;
        private const int Block = 1024; // Unity's usual DSP block size
        private static bool _failed;

        private static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            string outDir = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "bench_out");
            Directory.CreateDirectory(outDir);

            var presets = HeadsetPreset.All();
            var report = new StringBuilder();
            report.AppendLine("# Headset bench report");
            report.AppendLine();
            report.AppendLine($"Generated {DateTime.Now:yyyy-MM-dd HH:mm}. Levels are game dBFS (0 dBFS ~ rifle shot a few metres away). Block size {Block}, {Rate} Hz stereo.");
            report.AppendLine();

            // ---- Scenario renders ----
            float[] scenario = BuildScenario();
            Wav.Write(Path.Combine(outDir, "scenario_0_raw.wav"), scenario, 2, Rate);
            int idx = 1;
            foreach (var p in presets)
            {
                float[] buf = (float[])scenario.Clone();
                RunBlocks(new HeadsetProcessor(Rate, p), buf, null);
                CheckFinite(buf, p.Name + " scenario");
                Wav.Write(Path.Combine(outDir, $"scenario_{idx++}_{p.Name}.wav"), buf, 2, Rate);
            }
            report.AppendLine("## Listening files");
            report.AppendLine();
            report.AppendLine("`scenario_*.wav`: rain bed; footsteps every 0.5 s alternating left/right; a double-tap at 3.0 s; a quieter distant shot at 6.0 s. Listen for: how much louder the footsteps get, the click when the shot hits the clamp, how long the footsteps stay buried after the shot (pumping), and the hiss.");
            report.AppendLine();

            // ---- Measurements ----
            report.AppendLine("## Measurements");
            report.AppendLine();
            report.AppendLine("| Preset | Footstep gain dB | Shot out peak dBFS | Max clamp dB | Recovery to <1 dB ms | Footstep 300 ms after shot, dB vs normal | Hiss RMS dBFS | CPU % of realtime | Alloc bytes |");
            report.AppendLine("|---|---|---|---|---|---|---|---|---|");
            var freqRows = new StringBuilder();
            float[] bands = { 63, 125, 250, 500, 1000, 2000, 4000, 8000, 12000, 16000 };
            freqRows.AppendLine("| Preset | " + string.Join(" | ", bands.Select(b => b >= 1000 ? (b / 1000f) + "k" : b.ToString())) + " |");
            freqRows.AppendLine("|---|" + string.Concat(bands.Select(_ => "---|")));

            foreach (var p in presets)
            {
                float stepGain = FootstepGain(p, null);
                ShotResult shot = Shot(p);
                float stepAfter = FootstepGain(p, 0.3f) - stepGain;
                float hiss = HissRms(p);
                (double cpu, long alloc) = Perf(p);
                if (alloc != 0) { _failed = true; Console.Error.WriteLine($"FAIL {p.Name}: Process allocated {alloc} bytes"); }

                report.AppendLine($"| {p.Name} | {Signed(stepGain, "0.0")} | {shot.PeakOutDb:0.0} | {shot.MaxGrDb:0.0} | {Fmt(shot.RecoveryMs)} | {Signed(stepAfter, "0.0")} | {Fmt(hiss)} | {cpu:0.000} | {alloc} |");

                float[] resp = Response(p, bands);
                freqRows.AppendLine($"| {p.Name} | " + string.Join(" | ", resp.Select(v => Signed((float)Math.Round(v), "0"))) + " |");
            }

            report.AppendLine();
            report.AppendLine("- **Footstep gain**: output peak minus input peak for a lone −40 dBFS footstep. Positive = the headset makes quiet sounds louder.");
            report.AppendLine("- **Shot out peak**: what reaches your ears from a 0 dBFS shot (passive leak + clamped electronics, including any transient that pokes through before the clamp).");
            report.AppendLine("- **Recovery**: time from the shot until the clamp releases to under 1 dB.");
            report.AppendLine("- **Footstep after shot**: how much quieter a footstep 300 ms after the shot is than normal (pumping/masking).");
            report.AppendLine();
            report.AppendLine("## Frequency response (dB relative to Unprotected, at −60 dBFS so nothing is clamped; hiss off)");
            report.AppendLine();
            report.Append(freqRows);
            report.AppendLine();
            report.AppendLine(_failed ? "**RESULT: FAIL** (see console)" : "**RESULT: PASS** (no NaN/Inf, no allocations in Process)");

            File.WriteAllText(Path.Combine(outDir, "report.md"), report.ToString());
            Console.WriteLine(report.ToString());
            Console.WriteLine("Wrote " + outDir);
            return _failed ? 1 : 0;
        }

        private static string Signed(float v, string fmt)
        {
            string a = Math.Abs(v).ToString(fmt);
            bool zero = a.Trim('0', '.') == "";
            return zero ? a : (v < 0 ? "-" : "+") + a;
        }

        private static string Fmt(float v) => float.IsNaN(v) ? "n/a" : v.ToString("0");

        private static float[] BuildScenario()
        {
            float[] buf = Signals.Stereo(8f);
            Signals.AddRain(buf, -48f);
            bool left = true;
            for (float t = 0.25f; t < 7.8f; t += 0.5f)
            {
                Signals.AddFootstep(buf, t, -40f, left ? -0.6f : 0.6f);
                left = !left;
            }
            Signals.AddGunshot(buf, 3.0f, 0f, 0.3f);
            Signals.AddGunshot(buf, 3.15f, 0f, 0.3f);
            Signals.AddGunshot(buf, 6.0f, -20f, -0.5f);
            return buf;
        }

        private static void RunBlocks(HeadsetProcessor hp, float[] buf, float[] grTrace)
        {
            float[] block = new float[Block * 2];
            float[] gr = grTrace != null ? new float[Block] : null;
            int frames = buf.Length / 2;
            for (int f = 0; f < frames; f += Block)
            {
                int n = Math.Min(Block, frames - f);
                if (n < Block) Array.Clear(block, 0, block.Length);
                Array.Copy(buf, f * 2, block, 0, n * 2);
                hp.Process(block, 2, gr);
                Array.Copy(block, 0, buf, f * 2, n * 2);
                if (grTrace != null) Array.Copy(gr, 0, grTrace, f, n);
            }
        }

        private static float Peak(float[] b, int from = 0, int to = -1)
        {
            if (to < 0) to = b.Length;
            float p = 0f;
            for (int i = from; i < to; i++) p = Math.Max(p, Math.Abs(b[i]));
            return p;
        }

        // Gain on a lone −40 dBFS footstep; if shotBefore is set, a 0 dBFS shot plays that many seconds earlier.
        private static float FootstepGain(HeadsetPreset p, float? shotBefore)
        {
            var q = p.Clone(); q.HissDb = DspMath.MinDb;
            float stepAt = 1.0f;
            float[] step = Signals.Stereo(1.6f);
            Signals.AddFootstep(step, stepAt, -40f);
            float inPeak = Peak(step);
            float[] buf = Signals.Stereo(1.6f);
            if (shotBefore.HasValue) Signals.AddGunshot(buf, stepAt - shotBefore.Value, 0f);
            Signals.AddFootstep(buf, stepAt, -40f);
            // Measure the step alone: process (shot + step) and (shot only) and subtract, so the shot's tail doesn't count.
            float[] shotOnly = Signals.Stereo(1.6f);
            if (shotBefore.HasValue) Signals.AddGunshot(shotOnly, stepAt - shotBefore.Value, 0f);
            RunBlocks(new HeadsetProcessor(Rate, q), buf, null);
            RunBlocks(new HeadsetProcessor(Rate, q), shotOnly, null);
            int from = (int)(stepAt * Rate) * 2, to = (int)((stepAt + 0.12f) * Rate) * 2;
            float outPeak = 0f;
            for (int i = from; i < to; i++) outPeak = Math.Max(outPeak, Math.Abs(buf[i] - shotOnly[i]));
            return DspMath.LinToDb(outPeak) - DspMath.LinToDb(inPeak);
        }

        private struct ShotResult { public float PeakOutDb, MaxGrDb, RecoveryMs; }

        private static ShotResult Shot(HeadsetPreset p)
        {
            var q = p.Clone(); q.HissDb = DspMath.MinDb;
            float[] buf = Signals.Stereo(3f);
            Signals.AddGunshot(buf, 0.5f, 0f);
            float[] gr = new float[buf.Length / 2];
            RunBlocks(new HeadsetProcessor(Rate, q), buf, gr);
            CheckFinite(buf, p.Name + " shot");
            int onset = (int)(0.5f * Rate);
            float maxGr = 0f; int maxAt = onset;
            for (int i = onset; i < gr.Length; i++) if (gr[i] > maxGr) { maxGr = gr[i]; maxAt = i; }
            float rec = float.NaN;
            if (maxGr >= 1f)
            {
                for (int i = maxAt; i < gr.Length; i++) if (gr[i] < 1f) { rec = (i - onset) * 1000f / Rate; break; }
            }
            return new ShotResult { PeakOutDb = DspMath.LinToDb(Peak(buf)), MaxGrDb = maxGr, RecoveryMs = rec };
        }

        private static float HissRms(HeadsetPreset p)
        {
            if (!p.Electronic) return float.NaN;
            float[] buf = Signals.Stereo(1f);
            RunBlocks(new HeadsetProcessor(Rate, p), buf, null);
            double s = 0; foreach (float v in buf) s += v * v;
            return DspMath.LinToDb((float)Math.Sqrt(s / buf.Length));
        }

        private static float[] Response(HeadsetPreset p, float[] bands)
        {
            var q = p.Clone(); q.HissDb = DspMath.MinDb; q.Distortion = 0f;
            int n = 16384;
            float[] imp = new float[n * 2];
            imp[0] = imp[1] = 0.001f;
            RunBlocks(new HeadsetProcessor(Rate, q), imp, null);
            var res = new float[bands.Length];
            for (int b = 0; b < bands.Length; b++)
            {
                double re = 0, im = 0, w = 2 * Math.PI * bands[b] / Rate;
                for (int i = 0; i < n; i++) { re += imp[i * 2] * Math.Cos(w * i); im -= imp[i * 2] * Math.Sin(w * i); }
                res[b] = DspMath.LinToDb((float)(Math.Sqrt(re * re + im * im) / 0.001));
            }
            return res;
        }

        private static (double cpuPercent, long allocBytes) Perf(HeadsetPreset p)
        {
            var hp = new HeadsetProcessor(Rate, p);
            float[] block = new float[Block * 2];
            var rnd = new Random(1);
            for (int i = 0; i < block.Length; i++) block[i] = (float)(rnd.NextDouble() * 2 - 1) * 0.03f;
            int blocks = 60 * Rate / Block;
            hp.Process(block, 2); // warm up (JIT)
            var sw = new Stopwatch();
            long before = GC.GetAllocatedBytesForCurrentThread();
            sw.Start();
            for (int i = 0; i < blocks; i++) hp.Process(block, 2);
            sw.Stop();
            long alloc = GC.GetAllocatedBytesForCurrentThread() - before;
            double cpu = sw.Elapsed.TotalSeconds / 60.0 * 100.0;
            return (cpu, alloc);
        }

        private static void CheckFinite(float[] b, string what)
        {
            foreach (float v in b)
            {
                if (float.IsNaN(v) || float.IsInfinity(v)) { _failed = true; Console.Error.WriteLine($"FAIL {what}: NaN/Inf in output"); return; }
            }
        }
    }
}
