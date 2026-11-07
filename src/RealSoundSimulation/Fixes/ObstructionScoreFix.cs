using Audio.SpatialSystem;
using Audio.SpatialSystem.SpatialAudioCalculator.CombinedOcclusionCalculator;
using HarmonyLib;

namespace RealSoundSimulation.Fixes
{
    // Fix 2: how the three obstruction checks are combined (solutions doc 2.2).
    // Scores: 0 = clear, 1 = blocked. The game averages whichever of transmission (through the wall), diffraction (around
    // the corner) and reflection (off surfaces) ran. A check that FAILS to find a path returns 1.0, and raid 2 showed
    // diffraction failing 64% of the time, so a thin wall (T 0.2) plus a failed corner search (D 1.0) averages to 0.6:
    // muffled gun mixer.
    //   Game              the game's average
    //   IgnoreFailedPaths average of the checks that found a path; if all failed, the game's value
    //   ParallelPaths     sound gets through if ANY path does: blocked = T x D x R (a failed check multiplies by 1)
    internal enum ObstructionMode { Game, IgnoreFailedPaths, ParallelPaths }

    internal static class ObstructionScoreFix
    {
        private const float Failed = 0.999f;
        public static int Changed; // calls where the result moved by > 0.05 (for the summary)

        public static void Apply(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(SpatialAudioOcclusionCombinedCalculator), nameof(SpatialAudioOcclusionCombinedCalculator.GetScore)),
                postfix: new HarmonyMethod(typeof(ObstructionScoreFix), nameof(Postfix)));
        }

        public static void Reset() => Changed = 0;

        private static void Postfix(SpatialAudioOcclusionCombinedCalculator __instance, ref float __result)
        {
            ObstructionMode mode = Plugin.ObstructionModeCfg.Value;
            if (mode == ObstructionMode.Game) return;
            try
            {
                AggregateOcclusionResult a = __instance.LastAggregateResult;
                bool t = __instance._ranTransmission, d = __instance._ranDiffraction, r = __instance._ranReflection;
                if (!t && !d && !r) return;

                float result;
                if (mode == ObstructionMode.ParallelPaths)
                {
                    result = 1f;
                    if (t) result *= Clamp01(a.transmissionScore);
                    if (d) result *= Clamp01(a.diffractionScore);
                    if (r) result *= Clamp01(a.reflectionScore);
                }
                else
                {
                    float sum = 0f; int n = 0;
                    if (t && a.transmissionScore < Failed) { sum += a.transmissionScore; n++; }
                    if (d && a.diffractionScore < Failed) { sum += a.diffractionScore; n++; }
                    if (r && a.reflectionScore < Failed) { sum += a.reflectionScore; n++; }
                    if (n == 0) return; // every check failed: keep the game's answer (blocked)
                    result = Clamp01(sum / n);
                }
                if (System.Math.Abs(result - __result) > 0.05f) Changed++;
                __result = result;
            }
            catch (System.Exception e)
            {
                Diagnostics.DiagRunner.ReportOnce("fix 2 obstruction score", e);
            }
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
