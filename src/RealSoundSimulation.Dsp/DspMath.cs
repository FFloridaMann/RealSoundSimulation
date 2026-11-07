using System;

namespace RealSoundSimulation.Dsp
{
    public static class DspMath
    {
        public const float MinDb = -120f;

        public static float DbToLin(float db) => (float)Math.Pow(10.0, db / 20.0);

        public static float LinToDb(float lin) => lin <= 1e-6f ? MinDb : 20f * (float)Math.Log10(lin);

        // One-pole smoothing coefficient: the fraction of the old value kept per sample, reaching ~63% of a step in `ms`.
        // 0 ms means "jump immediately".
        public static float TimeCoef(float ms, float sampleRate)
        {
            if (ms <= 0f) return 0f;
            return (float)Math.Exp(-1.0 / (ms * 0.001 * sampleRate));
        }

        public static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
