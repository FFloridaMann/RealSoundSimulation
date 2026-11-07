using System.Collections.Generic;
using System.Text;
using EFT;
using UnityEngine;

namespace RealSoundSimulation.Acoustics
{
    // How loud each gun's indoor and outdoor recordings are (0.4.1, diagnostics only). Raid 11: stairwell shots sounded
    // quieter, and stairwells play mostly the indoor recording (fix 3c). Measured once per sound bank and distance tier,
    // the first time you fire it: RMS and peak (dBFS) over the first 0.4 s of every clip in each set. Needs the clip's
    // samples to be readable (AudioClip.GetData); clips that are compressed in memory can't be read and show "n/a".
    internal static class Loudness
    {
        private struct Row { public string Bank; public int Tier; public string Indoor, Outdoor; public float Diff; }
        private static readonly Dictionary<string, Row> Rows = new Dictionary<string, Row>();
        private static readonly HashSet<string> Seen = new HashSet<string>();

        public static void Reset() { Rows.Clear(); Seen.Clear(); }

        public static void Measure(SoundBank bank, int tier)
        {
            string key = bank.name + "#" + tier;
            if (!Seen.Add(key)) return;
            try
            {
                if (!bank.HasEnvironment || bank.Environments == null || bank.Environments.Length < 2) return;
                bool okO = Set(bank.Environments[0]?[tier]?.Clips, out float rmsO, out float peakO);
                bool okI = Set(bank.Environments[1]?[tier]?.Clips, out float rmsI, out float peakI);
                Rows[key] = new Row
                {
                    Bank = bank.name, Tier = tier,
                    Outdoor = okO ? $"RMS {rmsO:0.0} dBFS, peak {peakO:0.0}" : "n/a",
                    Indoor = okI ? $"RMS {rmsI:0.0} dBFS, peak {peakI:0.0}" : "n/a",
                    Diff = okO && okI ? rmsI - rmsO : float.NaN,
                };
            }
            catch (System.Exception e) { Diagnostics.DiagRunner.ReportOnce("recording loudness", e); }
        }

        // Average RMS (dB) and max peak over the first 0.4 s of each clip in the set.
        private static bool Set(AudioClip[] clips, out float rmsDb, out float peakDb)
        {
            rmsDb = peakDb = float.NaN;
            if (clips == null || clips.Length == 0) return false;
            double sumDb = 0; int n = 0; float peak = 0f;
            foreach (AudioClip c in clips)
            {
                if (c == null) continue;
                if (c.loadState != AudioDataLoadState.Loaded) c.LoadAudioData();
                int frames = Mathf.Min(c.samples, Mathf.CeilToInt(c.frequency * 0.4f));
                if (frames <= 0) continue;
                var buf = new float[frames * c.channels];
                if (!c.GetData(buf, 0)) continue;
                double sq = 0;
                for (int i = 0; i < buf.Length; i++) { float v = buf[i]; sq += v * v; if (Mathf.Abs(v) > peak) peak = Mathf.Abs(v); }
                double rms = System.Math.Sqrt(sq / buf.Length);
                if (rms <= 0) continue;
                sumDb += 20.0 * System.Math.Log10(rms); n++;
            }
            if (n == 0) return false;
            rmsDb = (float)(sumDb / n);
            peakDb = peak > 0f ? 20f * Mathf.Log10(peak) : float.NaN;
            return true;
        }

        public static string Summary()
        {
            if (Rows.Count == 0) return "  recording loudness: nothing measured\n";
            var sb = new StringBuilder();
            sb.AppendLine("  recording loudness (first 0.4 s; indoor minus outdoor RMS):");
            foreach (Row r in Rows.Values)
                sb.AppendLine($"    {r.Bank} tier {r.Tier}: outdoor {r.Outdoor} | indoor {r.Indoor} | indoor {(float.IsNaN(r.Diff) ? "n/a" : r.Diff.ToString("+0.0;-0.0") + " dB")}");
            return sb.ToString();
        }
    }
}
