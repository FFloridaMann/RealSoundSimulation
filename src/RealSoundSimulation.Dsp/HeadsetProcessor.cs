using System;

namespace RealSoundSimulation.Dsp
{
    // The headset model. One instance sits on the listener (or runs in the offline bench).
    //
    // Threading: Configure() runs on the main thread and builds a fresh Chain; Process() runs on the audio thread and picks up
    // the newest Chain once per block. No locks, and no allocations in Process().
    public sealed class HeadsetProcessor
    {
        private readonly float _sampleRate;
        private volatile Chain _chain;

        // Telemetry for overlays and tests, written by Process(). Plain floats: a torn read is harmless.
        public float LastBlockMaxGainReductionDb;
        public float LastBlockPeakInDb = DspMath.MinDb;
        public float LastBlockPeakOutDb = DspMath.MinDb;

        // Calibration: added before everything, so the game's listener level lines up with the presets' "game dBFS".
        public float InputGainDb
        {
            get => _inputGainDb;
            set { _inputGainDb = value; _inputGain = DspMath.DbToLin(value); }
        }
        private float _inputGainDb;
        private float _inputGain = 1f;

        public HeadsetPreset Preset => _chain?.Preset;

        public HeadsetProcessor(float sampleRate, HeadsetPreset preset = null)
        {
            _sampleRate = sampleRate;
            Configure(preset ?? HeadsetPreset.Unprotected());
        }

        public void Configure(HeadsetPreset preset)
        {
            _chain = new Chain(preset.Clone(), _sampleRate);
        }

        // In-place on interleaved samples. grTrace (optional) receives the gain reduction in dB per frame.
        public void Process(float[] data, int channels, float[] grTrace = null)
        {
            Chain c = _chain;
            if (c == null || channels <= 0) return;
            if (channels > Biquad.MaxChannels) channels = Biquad.MaxChannels;
            c.Process(data, channels, _inputGain, this, grTrace);
        }

        private sealed class Chain
        {
            public readonly HeadsetPreset Preset;

            private readonly bool _passiveActive;
            private readonly float _passiveGain;
            private readonly Biquad _passiveLow = new Biquad();
            private readonly Biquad _passiveHigh = new Biquad();

            private readonly bool _electronic;
            private readonly Biquad _micHp = new Biquad();
            private readonly Biquad _micLp1 = new Biquad();
            private readonly Biquad _micLp2 = new Biquad();
            private readonly Biquad _eq1 = new Biquad();
            private readonly Biquad _eq2 = new Biquad();
            private readonly Biquad _eq3 = new Biquad();
            private readonly float _makeup;

            private readonly float _compThr, _compSlope, _compKnee;
            private readonly float _compAtt, _compRel;
            private readonly float _limCeil, _limAtt, _limRel;
            private readonly int _limHold;

            private readonly float _drive;
            private readonly bool _distortion;

            private readonly float _hissGain;
            private readonly float _hissCoef;

            // Lookahead delay line for the electronic path (interleaved ring buffer, sized once).
            private readonly int _lookahead;
            private readonly float[] _delay;
            private int _delayPos;

            // State.
            private float _compGr, _limGr;
            private int _holdLeft;
            private uint _rng = 0x9E3779B9u;
            private readonly float[] _hissState = new float[Biquad.MaxChannels];

            public Chain(HeadsetPreset p, float sr)
            {
                Preset = p;
                _passiveGain = DspMath.DbToLin(p.PassiveGainDb);
                _passiveLow.SetLowShelf(sr, p.PassiveLowShelfHz, p.PassiveLowShelfDb);
                _passiveHigh.SetHighShelf(sr, p.PassiveHighShelfHz, p.PassiveHighShelfDb);
                _passiveActive = Math.Abs(p.PassiveGainDb) > 0.01f || !_passiveLow.Bypass || !_passiveHigh.Bypass;

                _electronic = p.Electronic;
                _micHp.SetHighPass(sr, p.MicHighPassHz);
                _micLp1.SetLowPass(sr, p.MicLowPassHz);
                if (p.MicLowPassStages > 1) _micLp2.SetLowPass(sr, p.MicLowPassHz); else _micLp2.SetBypass();
                _eq1.SetPeaking(sr, p.Eq1Hz, p.Eq1Q, p.Eq1Db);
                _eq2.SetPeaking(sr, p.Eq2Hz, p.Eq2Q, p.Eq2Db);
                _eq3.SetPeaking(sr, p.Eq3Hz, p.Eq3Q, p.Eq3Db);
                _makeup = DspMath.DbToLin(p.MakeupDb);

                _compThr = p.CompThresholdDb;
                _compSlope = p.CompRatio > 1f ? 1f - 1f / p.CompRatio : 0f;
                _compKnee = Math.Max(0f, p.CompKneeDb);
                _compAtt = DspMath.TimeCoef(p.CompAttackMs, sr);
                _compRel = DspMath.TimeCoef(p.CompReleaseMs, sr);

                _limCeil = p.LimiterCeilingDb;
                _limAtt = DspMath.TimeCoef(p.LimiterAttackMs, sr);
                _limRel = DspMath.TimeCoef(p.LimiterReleaseMs, sr);
                _limHold = (int)(p.LimiterHoldMs * 0.001f * sr);

                _distortion = p.Distortion > 0.001f;
                _drive = 1f + p.Distortion * 10f;

                _hissGain = p.HissDb > DspMath.MinDb + 1f ? DspMath.DbToLin(p.HissDb) * 1.7320508f : 0f; // uniform noise RMS = 1/sqrt(3)
                _hissCoef = DspMath.TimeCoef(1000f / (2f * (float)Math.PI * Math.Max(100f, p.HissLowPassHz)), sr);

                _lookahead = Math.Max(0, (int)(p.LookaheadMs * 0.001f * sr));
                _delay = new float[(_lookahead + 1) * Biquad.MaxChannels];
            }

            public void Process(float[] data, int ch, float inGain, HeadsetProcessor owner, float[] grTrace)
            {
                int frames = data.Length / ch;
                float peakIn = 0f, peakOut = 0f, maxGr = 0f;
                int ring = _lookahead + 1;

                for (int f = 0; f < frames; f++)
                {
                    int i0 = f * ch;

                    // 1. Detector: the electronic path's level before the clamp (stereo-linked peak).
                    float det = 0f;
                    for (int c = 0; c < ch; c++)
                    {
                        float x = data[i0 + c] * inGain;
                        float ax = x < 0f ? -x : x;
                        if (ax > peakIn) peakIn = ax;
                        if (!_electronic) continue;

                        float e = _micHp.Process(x, c);
                        e = _micLp1.Process(e, c);
                        e = _micLp2.Process(e, c);
                        e = _eq1.Process(e, c);
                        e = _eq2.Process(e, c);
                        e = _eq3.Process(e, c);
                        e *= _makeup;
                        if (_distortion) e = SoftClip(e * _drive) / _drive;

                        float ae = e < 0f ? -e : e;
                        if (ae > det) det = ae;

                        // Electronic signal goes into the delay line; the clamp is applied when it comes out.
                        _delay[_delayPos * Biquad.MaxChannels + c] = e;
                    }

                    float grDb = 0f;
                    if (_electronic)
                    {
                        // 2. Gain computer (dB domain): soft-knee compressor, then a limiter on what's left above the ceiling.
                        float lvl = DspMath.LinToDb(det);
                        float compTarget = CompGain(lvl);
                        _compGr = compTarget > _compGr ? compTarget + (_compGr - compTarget) * _compAtt
                                                       : compTarget + (_compGr - compTarget) * _compRel;

                        float over = lvl - _compGr - _limCeil;
                        float limTarget = over > 0f ? over : 0f;
                        if (limTarget > _limGr)
                        {
                            _limGr = limTarget + (_limGr - limTarget) * _limAtt;
                            _holdLeft = _limHold;
                        }
                        else if (_holdLeft > 0)
                        {
                            _holdLeft--;
                        }
                        else
                        {
                            _limGr = limTarget + (_limGr - limTarget) * _limRel;
                        }
                        grDb = _compGr + _limGr;
                    }
                    if (grDb > maxGr) maxGr = grDb;
                    if (grTrace != null && f < grTrace.Length) grTrace[f] = grDb;
                    float g = _electronic ? DspMath.DbToLin(-grDb) : 0f;

                    int readPos = _delayPos + 1;
                    if (readPos >= ring) readPos = 0;

                    // 3. Mix: passive leak + clamped electronic + hiss.
                    for (int c = 0; c < ch; c++)
                    {
                        float x = data[i0 + c] * inGain;
                        float y = 0f;
                        if (_passiveActive)
                        {
                            float p = _passiveLow.Process(x, c);
                            p = _passiveHigh.Process(p, c);
                            y += p * _passiveGain;
                        }
                        else if (!_electronic)
                        {
                            y = x; // Unprotected: straight through.
                        }

                        if (_electronic)
                        {
                            float e = _lookahead > 0 ? _delay[readPos * Biquad.MaxChannels + c] : _delay[_delayPos * Biquad.MaxChannels + c];
                            y += e * g;
                            if (_hissGain > 0f)
                            {
                                float n = NextNoise() * _hissGain;
                                _hissState[c] = n + (_hissState[c] - n) * _hissCoef;
                                y += _hissState[c];
                            }
                        }

                        data[i0 + c] = y;
                        float ay = y < 0f ? -y : y;
                        if (ay > peakOut) peakOut = ay;
                    }

                    _delayPos = readPos;
                }

                owner.LastBlockMaxGainReductionDb = maxGr;
                owner.LastBlockPeakInDb = DspMath.LinToDb(peakIn);
                owner.LastBlockPeakOutDb = DspMath.LinToDb(peakOut);
            }

            private float CompGain(float lvl)
            {
                if (_compSlope <= 0f) return 0f;
                float over = lvl - _compThr;
                float half = _compKnee * 0.5f;
                if (over <= -half) return 0f;
                if (over >= half || _compKnee <= 0f) return over * _compSlope;
                float t = over + half;
                return t * t / (2f * _compKnee) * _compSlope;
            }

            // Rational tanh approximation (|error| < 2% and monotonic), cheaper than Math.Tanh.
            private static float SoftClip(float x)
            {
                if (x > 3f) return 1f;
                if (x < -3f) return -1f;
                float x2 = x * x;
                return x * (27f + x2) / (27f + 9f * x2);
            }

            private float NextNoise()
            {
                uint s = _rng;
                s ^= s << 13; s ^= s >> 17; s ^= s << 5;
                _rng = s;
                return (s & 0xFFFFFF) / 8388608f - 1f;
            }
        }
    }
}
