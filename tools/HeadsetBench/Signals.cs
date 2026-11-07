using System;
using RealSoundSimulation.Dsp;

namespace HeadsetBench
{
    // Synthetic test sounds. Not meant to sound like the game's clips, only to have the right shape:
    // a gunshot is a ~2 ms blast pulse plus a noisy tail, a footstep is a low thump plus a high scuff.
    internal static class Signals
    {
        public const int Rate = 48000;
        private static uint _rng = 12345;

        private static float Noise()
        {
            _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5;
            return (_rng & 0xFFFFFF) / 8388608f - 1f;
        }

        public static float[] Stereo(float seconds) => new float[(int)(seconds * Rate) * 2];

        // Friedlander blast wave (positive spike, then a small negative phase) plus a decaying noise tail.
        public static void AddGunshot(float[] buf, float atSec, float peakDb, float pan = 0f)
        {
            _rng = 777u; // same noise every time, so two renders can be subtracted
            float peak = DspMath.DbToLin(peakDb);
            int start = (int)(atSec * Rate);
            var lp = new Biquad(); lp.SetLowPass(Rate, 6000f);
            int len = (int)(0.6f * Rate);
            float tPos = 0.002f;
            float l = 1f - Math.Max(0f, pan), r = 1f + Math.Min(0f, pan);
            for (int i = 0; i < len && start + i < buf.Length / 2; i++)
            {
                float t = i / (float)Rate;
                float blast = (1f - t / tPos) * (float)Math.Exp(-2.5f * t / tPos);
                if (t > tPos * 6f) blast = 0f;
                float tail = lp.Process(Noise(), 0) * 0.35f * (float)Math.Exp(-t / 0.12f);
                float s = (blast + tail) * peak;
                buf[(start + i) * 2] += s * l;
                buf[(start + i) * 2 + 1] += s * r;
            }
        }

        public static void AddFootstep(float[] buf, float atSec, float peakDb, float pan = 0f)
        {
            _rng = 4242u;
            float peak = DspMath.DbToLin(peakDb);
            int start = (int)(atSec * Rate);
            var bp = new Biquad(); bp.SetPeaking(Rate, 3500f, 1.5f, 12f);
            var hp = new Biquad(); hp.SetHighPass(Rate, 1500f);
            int len = (int)(0.12f * Rate);
            float l = 1f - Math.Max(0f, pan), r = 1f + Math.Min(0f, pan);
            for (int i = 0; i < len && start + i < buf.Length / 2; i++)
            {
                float t = i / (float)Rate;
                float thump = (float)Math.Sin(2 * Math.PI * 90 * t) * (float)Math.Exp(-t / 0.018f);
                float scuff = hp.Process(bp.Process(Noise(), 0), 0) * (float)Math.Exp(-Math.Abs(t - 0.02f) / 0.015f) * 0.6f;
                float s = (thump + scuff) * peak * 0.8f;
                buf[(start + i) * 2] += s * l;
                buf[(start + i) * 2 + 1] += s * r;
            }
        }

        // Steady rain-like bed: pink-ish noise at a given RMS.
        public static void AddRain(float[] buf, float rmsDb)
        {
            float g = DspMath.DbToLin(rmsDb) * 3.5f;
            float b0 = 0, b1 = 0, b2 = 0;
            for (int i = 0; i < buf.Length; i++)
            {
                float w = Noise();
                b0 = 0.99765f * b0 + w * 0.0990460f;
                b1 = 0.96300f * b1 + w * 0.2965164f;
                b2 = 0.57000f * b2 + w * 1.0526913f;
                buf[i] += (b0 + b1 + b2 + w * 0.1848f) * 0.11f * g;
            }
        }
    }
}
