using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;

namespace RealSoundSimulation.Diagnostics
{
    // 0.6.20: Unity's own audio counters, if this (release) game exposes them. AudioThreadProbe sees blocks that came LATE,
    // not how close the others came to their deadline (ROADMAP 6.0a). Unity's audio profiler has a DSP load and voice
    // counts; in a release player only counters marked "available in release" can be recorded, and which ones those are
    // for audio is not documented. So this does not guess names:
    //   1. at raid start, list every counter the runtime offers (ProfilerRecorderHandle.GetAvailable) and write the list
    //      to audio-counters.txt (raid folder + latest copy next to selfcheck.txt): all audio ones, plus a count per category
    //   2. record every counter in the Audio category (cap 24): sampled each frame on the main thread (one read per counter,
    //      no allocation), window avg/max to audiocounters.csv every 5 s, raid avg/max in the summary
    //   3. a counter whose name says DSP or CPU and whose unit is Percent is read as the share of the audio deadline used:
    //      the summary gives the share of frames over 50% and 80%. Both scales (0-100 and 0-1) are counted; the summary
    //      picks one from the raid's max and prints which, because the scale is not documented either.
    // If nothing in the Audio category is available, the file says so and nothing else runs.
    internal static class AudioCounters
    {
        public const int MaxRecorders = 24;

        private struct Rec
        {
            public ProfilerRecorder R;
            public string Name, Unit;
            public bool IsLoad;           // DSP/CPU percent: share of the audio deadline
            public double WSum, WMax; public int WN;   // window (5 s)
            public double TSum, TMax; public long TN;  // raid
            public long Over50, Over80, Over50f, Over80f; // 0-100 scale / 0-1 scale (chosen at the end from the raid max)
        }

        private static Rec[] _recs = new Rec[0];
        public static int Count => _recs.Length;
        public static bool Ran { get; private set; }
        public static int Available { get; private set; }
        public static string Header { get; private set; } = "t";

        public static void RaidStart()
        {
            Stop();
            Ran = true;
            var sb = new StringBuilder();
            sb.AppendLine($"Real Sound Simulation: Unity profiler counters this game offers  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            var handles = new List<ProfilerRecorderHandle>();
            try { ProfilerRecorderHandle.GetAvailable(handles); }
            catch (Exception e) { sb.AppendLine("GetAvailable failed: " + e.Message); Write(sb.ToString()); return; }
            Available = handles.Count;

            var perCat = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var audio = new List<(ProfilerRecorderHandle H, ProfilerRecorderDescription D)>();
            foreach (var h in handles)
            {
                ProfilerRecorderDescription d;
                try { d = ProfilerRecorderHandle.GetDescription(h); } catch { continue; }
                string cat = d.Category.Name ?? "?";
                perCat.TryGetValue(cat, out int c); perCat[cat] = c + 1;
                bool isAudio = cat.IndexOf("Audio", StringComparison.OrdinalIgnoreCase) >= 0
                               || (d.Name != null && (d.Name.IndexOf("Audio", StringComparison.OrdinalIgnoreCase) >= 0 || d.Name.IndexOf("DSP", StringComparison.Ordinal) >= 0));
                if (isAudio) audio.Add((h, d));
            }
            sb.AppendLine($"Counters available: {handles.Count}");
            foreach (var kv in perCat) sb.AppendLine($"  {kv.Key,-24} {kv.Value}");
            sb.AppendLine();
            sb.AppendLine($"Audio counters ({audio.Count}; recorded: up to {MaxRecorders}):");
            if (audio.Count == 0) sb.AppendLine("  none: this build exposes no audio counters to ProfilerRecorder; the late-block count (AudioThreadProbe) is all there is");

            var recs = new List<Rec>();
            var hdr = new StringBuilder("t");
            foreach (var a in audio)
            {
                string unit = a.D.UnitType.ToString();
                string line = $"  {a.D.Category.Name,-12} {a.D.Name,-40} unit {unit,-16} type {a.D.DataType}";
                if (recs.Count < MaxRecorders)
                {
                    try
                    {
                        var r = new ProfilerRecorder(a.H, 1);
                        if (!r.IsRunning) r.Start();
                        if (r.Valid)
                        {
                            string n = a.D.Name ?? "?";
                            bool load = a.D.UnitType == ProfilerMarkerDataUnit.Percent && (n.IndexOf("DSP", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("CPU", StringComparison.OrdinalIgnoreCase) >= 0);
                            recs.Add(new Rec { R = r, Name = n, Unit = unit, IsLoad = load });
                            string col = Col(n);
                            hdr.Append(',').Append(col).Append("_avg,").Append(col).Append("_max");
                            line += load ? "  RECORDED (read as audio deadline share)" : "  RECORDED";
                        }
                        else { r.Dispose(); line += "  not valid"; }
                    }
                    catch (Exception e) { line += "  could not record: " + e.Message; }
                }
                sb.AppendLine(line);
            }
            _recs = recs.ToArray();
            Header = hdr.ToString();
            Write(sb.ToString());
            if (_recs.Length > 0 && DiagLog.IsOpen) DiagLog.Row("audiocounters.csv", Header);
        }

        private static void Write(string text)
        {
            try { if (DiagLog.HasRaidDir) DiagLog.Text("audio-counters.txt", text); } catch { }
            try { File.WriteAllText(Path.Combine(DiagLog.RootDir, "audio-counters.txt"), text); } catch { }
        }

        private static string Col(string name)
        {
            var b = new StringBuilder(name.Length);
            foreach (char ch in name) b.Append(char.IsLetterOrDigit(ch) ? ch : '_');
            return b.ToString();
        }

        // Main thread, every frame in raid. One read per counter, no allocation.
        public static void Sample()
        {
            for (int i = 0; i < _recs.Length; i++)
            {
                ref Rec r = ref _recs[i];
                if (!r.R.Valid) continue;
                double v;
                try { v = r.R.LastValueAsDouble; } catch { continue; }
                if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                r.WSum += v; r.WN++; if (v > r.WMax) r.WMax = v;
                r.TSum += v; r.TN++; if (v > r.TMax) r.TMax = v;
                if (r.IsLoad)
                {
                    if (v > 50.0) r.Over50++; if (v > 80.0) r.Over80++;   // if the counter is 0-100
                    if (v > 0.5) r.Over50f++; if (v > 0.8) r.Over80f++;   // if it is 0-1
                }
            }
        }

        // Every 5 s from DiagRunner's timing step: one row (allocation-free), then the window restarts.
        public static void WriteWindow(float t)
        {
            if (_recs.Length == 0) return;
            bool open = DiagLog.IsOpen;
            StringBuilder sb = open ? DiagLog.Begin() : null;
            if (open) DiagLog.AppendF(sb, t, 1);
            for (int i = 0; i < _recs.Length; i++)
            {
                ref Rec r = ref _recs[i];
                if (open)
                {
                    sb.Append(',');
                    if (r.WN > 0) DiagLog.AppendF(sb, (float)(r.WSum / r.WN), 3);
                    sb.Append(',');
                    if (r.WN > 0) DiagLog.AppendF(sb, (float)r.WMax, 3);
                }
                r.WSum = r.WMax = 0; r.WN = 0;
            }
            if (open) DiagLog.EndRow("audiocounters.csv");
        }

        public static string Summary()
        {
            if (!Ran) return "UNITY AUDIO COUNTERS: not read (Time the audio thread off)\n";
            var sb = new StringBuilder();
            sb.AppendLine($"UNITY AUDIO COUNTERS ({_recs.Length} recorded of {Available} counters offered; list in audio-counters.txt)");
            if (_recs.Length == 0) sb.AppendLine("  none available in this build: only the late-block count above");
            foreach (Rec r in _recs)
            {
                string avg = r.TN > 0 ? (r.TSum / r.TN).ToString("0.###") : "-";
                sb.Append($"  {r.Name,-40} avg {avg,10}  max {r.TMax,10:0.###}  ({r.Unit}, {r.TN} frames)");
                if (r.IsLoad && r.TN > 0)
                {
                    bool unit = r.TMax <= 1.0001; // never above 1 in the whole raid: read as 0-1
                    long o50 = unit ? r.Over50f : r.Over50, o80 = unit ? r.Over80f : r.Over80;
                    sb.Append($"  deadline share: frames over 50% {100.0 * o50 / r.TN:0.00}%, over 80% {100.0 * o80 / r.TN:0.00}% (read as {(unit ? "0-1" : "0-100")}, from the raid max)");
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        public static void Stop()
        {
            for (int i = 0; i < _recs.Length; i++) { try { _recs[i].R.Dispose(); } catch { } }
            _recs = new Rec[0];
        }
    }
}
