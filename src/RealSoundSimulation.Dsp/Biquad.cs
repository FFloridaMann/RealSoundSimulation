using System;

namespace RealSoundSimulation.Dsp
{
    // RBJ "Audio EQ Cookbook" biquad, transposed direct form II, with its own state per channel.
    // Coefficients are set once; Process is allocation-free.
    public sealed class Biquad
    {
        public const int MaxChannels = 8;

        private float _b0 = 1f, _b1, _b2, _a1, _a2;
        private readonly float[] _z1 = new float[MaxChannels];
        private readonly float[] _z2 = new float[MaxChannels];

        public bool Bypass { get; private set; } = true;

        public void SetBypass()
        {
            _b0 = 1f; _b1 = _b2 = _a1 = _a2 = 0f;
            Bypass = true;
        }

        public void SetLowPass(float sampleRate, float freq, float q = 0.7071f)
        {
            if (freq >= sampleRate * 0.49f) { SetBypass(); return; }
            Calc(sampleRate, freq, q, out float cos, out float alpha);
            float b1 = 1f - cos;
            Set(b1 * 0.5f, b1, b1 * 0.5f, 1f + alpha, -2f * cos, 1f - alpha);
        }

        public void SetHighPass(float sampleRate, float freq, float q = 0.7071f)
        {
            if (freq <= 1f) { SetBypass(); return; }
            Calc(sampleRate, freq, q, out float cos, out float alpha);
            float b1 = 1f + cos;
            Set(b1 * 0.5f, -b1, b1 * 0.5f, 1f + alpha, -2f * cos, 1f - alpha);
        }

        public void SetPeaking(float sampleRate, float freq, float q, float gainDb)
        {
            if (Math.Abs(gainDb) < 0.01f) { SetBypass(); return; }
            Calc(sampleRate, freq, q, out float cos, out float alpha);
            float a = (float)Math.Pow(10.0, gainDb / 40.0);
            Set(1f + alpha * a, -2f * cos, 1f - alpha * a, 1f + alpha / a, -2f * cos, 1f - alpha / a);
        }

        public void SetLowShelf(float sampleRate, float freq, float gainDb, float slope = 1f)
        {
            if (Math.Abs(gainDb) < 0.01f) { SetBypass(); return; }
            Shelf(sampleRate, freq, gainDb, slope, out float cos, out float a, out float twoSqrtAAlpha);
            Set(a * ((a + 1f) - (a - 1f) * cos + twoSqrtAAlpha),
                2f * a * ((a - 1f) - (a + 1f) * cos),
                a * ((a + 1f) - (a - 1f) * cos - twoSqrtAAlpha),
                (a + 1f) + (a - 1f) * cos + twoSqrtAAlpha,
                -2f * ((a - 1f) + (a + 1f) * cos),
                (a + 1f) + (a - 1f) * cos - twoSqrtAAlpha);
        }

        public void SetHighShelf(float sampleRate, float freq, float gainDb, float slope = 1f)
        {
            if (Math.Abs(gainDb) < 0.01f) { SetBypass(); return; }
            Shelf(sampleRate, freq, gainDb, slope, out float cos, out float a, out float twoSqrtAAlpha);
            Set(a * ((a + 1f) + (a - 1f) * cos + twoSqrtAAlpha),
                -2f * a * ((a - 1f) + (a + 1f) * cos),
                a * ((a + 1f) + (a - 1f) * cos - twoSqrtAAlpha),
                (a + 1f) - (a - 1f) * cos + twoSqrtAAlpha,
                2f * ((a - 1f) - (a + 1f) * cos),
                (a + 1f) - (a - 1f) * cos - twoSqrtAAlpha);
        }

        public void Reset()
        {
            Array.Clear(_z1, 0, MaxChannels);
            Array.Clear(_z2, 0, MaxChannels);
        }

        public float Process(float x, int channel)
        {
            if (Bypass) return x;
            float y = _b0 * x + _z1[channel];
            _z1[channel] = _b1 * x - _a1 * y + _z2[channel];
            _z2[channel] = _b2 * x - _a2 * y;
            return y;
        }

        private static void Calc(float sampleRate, float freq, float q, out float cos, out float alpha)
        {
            double w0 = 2.0 * Math.PI * freq / sampleRate;
            cos = (float)Math.Cos(w0);
            alpha = (float)(Math.Sin(w0) / (2.0 * Math.Max(q, 0.05f)));
        }

        private static void Shelf(float sampleRate, float freq, float gainDb, float slope, out float cos, out float a, out float twoSqrtAAlpha)
        {
            a = (float)Math.Pow(10.0, gainDb / 40.0);
            double w0 = 2.0 * Math.PI * freq / sampleRate;
            cos = (float)Math.Cos(w0);
            double alpha = Math.Sin(w0) / 2.0 * Math.Sqrt((a + 1.0 / a) * (1.0 / slope - 1.0) + 2.0);
            twoSqrtAAlpha = (float)(2.0 * Math.Sqrt(a) * alpha);
        }

        private void Set(float b0, float b1, float b2, float a0, float a1, float a2)
        {
            _b0 = b0 / a0; _b1 = b1 / a0; _b2 = b2 / a0; _a1 = a1 / a0; _a2 = a2 / a0;
            Bypass = false;
        }
    }
}
