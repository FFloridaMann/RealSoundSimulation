using System;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;

namespace RealSoundSimulation.Diagnostics
{
    // Times the game's own audio systems per frame (main thread). Overhead: two Stopwatch reads per call.
    internal static class TimingPatches
    {
        public sealed class Meter
        {
            public readonly string Name;
            public long WindowTicks, WindowMaxTicks, TotalTicks, TotalMaxTicks;
            public int WindowCalls, TotalCalls, TotalOver1Ms;
            public Meter(string name) { Name = name; }

            public void Add(long ticks)
            {
                WindowTicks += ticks; WindowCalls++;
                if (ticks > WindowMaxTicks) WindowMaxTicks = ticks;
                TotalTicks += ticks; TotalCalls++;
                if (ticks > TotalMaxTicks) TotalMaxTicks = ticks;
                if (ticks > Stopwatch.Frequency / 1000) TotalOver1Ms++;
            }

            public float WindowAvgMs => WindowCalls == 0 ? 0f : (float)(WindowTicks * 1000.0 / Stopwatch.Frequency / WindowCalls);
            public float WindowMaxMs => (float)(WindowMaxTicks * 1000.0 / Stopwatch.Frequency);
            public float TotalAvgMs => TotalCalls == 0 ? 0f : (float)(TotalTicks * 1000.0 / Stopwatch.Frequency / TotalCalls);
            public float TotalMaxMs => (float)(TotalMaxTicks * 1000.0 / Stopwatch.Frequency);

            public void ResetWindow() { WindowTicks = WindowMaxTicks = 0; WindowCalls = 0; }
            public void ResetAll() { ResetWindow(); TotalTicks = TotalMaxTicks = 0; TotalCalls = TotalOver1Ms = 0; }
        }

        public static readonly Meter SpatialUpdate = new Meter("SpatialAudioSystem.Update");
        public static readonly Meter SpatialLate = new Meter("SpatialAudioSystem.LateUpdate");
        public static readonly Meter BetterAudioUpdate = new Meter("BetterAudio.Update");

        public static void Apply(Harmony h)
        {
            Patch(h, "Audio.SpatialSystem.SpatialAudioSystem", "Update", nameof(PreSpatialUpdate), nameof(PostSpatialUpdate));
            Patch(h, "Audio.SpatialSystem.SpatialAudioSystem", "LateUpdate", nameof(PreSpatialLate), nameof(PostSpatialLate));
            Patch(h, "BetterAudio", "Update", nameof(PreBetterAudio), nameof(PostBetterAudio));
        }

        private static void Patch(Harmony h, string type, string method, string pre, string post)
        {
            MethodInfo target = AccessTools.Method(AccessTools.TypeByName(type), method);
            if (target == null) { Plugin.Log.LogWarning($"Timing: {type}.{method} not found"); return; }
            h.Patch(target, new HarmonyMethod(typeof(TimingPatches), pre), new HarmonyMethod(typeof(TimingPatches), post));
        }

        private static void PreSpatialUpdate(out long __state) => __state = Stopwatch.GetTimestamp();
        private static void PostSpatialUpdate(long __state) { if (Plugin.TimingOn.Value && DiagRunner.LogsThisRaid) SpatialUpdate.Add(Stopwatch.GetTimestamp() - __state); }
        private static void PreSpatialLate(out long __state) => __state = Stopwatch.GetTimestamp();
        private static void PostSpatialLate(long __state) { if (Plugin.TimingOn.Value && DiagRunner.LogsThisRaid) SpatialLate.Add(Stopwatch.GetTimestamp() - __state); }
        private static void PreBetterAudio(out long __state) => __state = Stopwatch.GetTimestamp();
        private static void PostBetterAudio(long __state) { if (Plugin.TimingOn.Value && DiagRunner.LogsThisRaid) BetterAudioUpdate.Add(Stopwatch.GetTimestamp() - __state); }
    }
}
