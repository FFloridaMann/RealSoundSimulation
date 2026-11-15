using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace RealSoundSimulation
{
    // Raid start loading: how long each piece of the mod's own raid-start work took and when it ran. "loading" runs on the
    // loading screen (Raid.Prepare), "spawn" in the frame you spawn (the resets), "first use" when a feature first needs
    // something later on (a scene scan, a voice pool). Always measured (a timestamp per step); the BepInEx log gets one line
    // at spawn, and with bug report logs on the steps go to startup.csv at raid end.
    internal static class StartupTiming
    {
        public const string Header = "phase,step,ms,atS";

        private struct Step { public string Phase, Name; public double Ms; public float RealT; }

        private static readonly List<Step> Steps = new List<Step>();
        private static float _spawnRealT = float.NaN;

        public static void Clear() { Steps.Clear(); _spawnRealT = float.NaN; }

        public static void Spawned() => _spawnRealT = Time.realtimeSinceStartup;

        public static void Run(string phase, string name, Action a)
        {
            long t0 = Stopwatch.GetTimestamp();
            try { a(); }
            finally { Add(phase, name, Stopwatch.GetTimestamp() - t0); }
        }

        public static long Now() => Stopwatch.GetTimestamp();

        public static void Add(string phase, string name, long ticks)
        {
            if (Steps.Count >= 400) return;
            Steps.Add(new Step { Phase = phase, Name = name, Ms = ticks * 1000.0 / Stopwatch.Frequency, RealT = Time.realtimeSinceStartup });
        }

        public static double Total(string phase)
        {
            double s = 0;
            foreach (Step st in Steps) if (st.Phase == phase) s += st.Ms;
            return s;
        }

        private static string Slowest(string phase)
        {
            Step best = default; bool any = false;
            foreach (Step st in Steps) if (st.Phase == phase && (!any || st.Ms > best.Ms)) { best = st; any = true; }
            return any ? $"{best.Name} {best.Ms:0.0} ms" : "none";
        }

        public static string Line() =>
            $"Raid start loading: mod work {Total("loading"):0} ms on the loading screen (slowest: {Slowest("loading")}), "
            + $"{Total("spawn"):0} ms at spawn (slowest: {Slowest("spawn")}), {Total("first use"):0} ms on first use since (slowest: {Slowest("first use")})";

        public static string Csv()
        {
            var sb = new StringBuilder();
            sb.AppendLine(Header);
            foreach (Step st in Steps)
            {
                string at = float.IsNaN(_spawnRealT) ? "" : (st.RealT - _spawnRealT).ToString("0.00", CultureInfo.InvariantCulture);
                sb.Append(st.Phase).Append(',').Append(Diagnostics.DiagLog.Csv(st.Name)).Append(',')
                  .Append(st.Ms.ToString("0.000", CultureInfo.InvariantCulture)).Append(',').AppendLine(at);
            }
            return sb.ToString();
        }
    }
}
