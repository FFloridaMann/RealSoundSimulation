using System;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace RealSoundSimulation.Fixes
{
    // 0.7.22 "A": adrenaline after close calls. User, 11 Nov 2026: "maybe its because we are missing the heartbeat sounds and rushed breathing
    // Adrenaline audio during the blast from my last audio mod", "I think that the hr and breathing will help alot with grenades", "Add it to 1.0".
    // That old private mod played clips ported from the Realism mod, so none of it is used here. A scan of the game's sounds (11 Nov 2026)
    // found no heartbeat anywhere, but every character voice has its own breathing.
    //   stress   a close blast sets it 0..1 from its pressure dose (ShockWave.Dose: -15 dB -> 0, +6 dB -> 1; a second blast on top pushes it
    //            higher). It holds HoldS, then falls to nothing over "Adrenaline - settles over s".
    //   heart    a built lub-dub (two low knocks with a little overtone so small headphones carry it), 2D on the Master group, so the
    //            concussion cut doesn't touch it: it's felt, like the shock wave. 75 bpm when it starts up to 150 at full stress, scheduled
    //            on the audio clock so the beat never stutters with the frame rate.
    //   breath   while stress is high: your character's own out-of-breath breathing, as the game plays it when you're exhausted
    //            (Speaker OnBreath, Healthy | Aware: the sprint set, not the hurt set with its winces).
    // Tuned after a preview (user): "heart beat sounds fake a bit. but timing and fade great", "the breathing sounds a bit to much like
    // you've been hurt", "maybe hr should be max 20 seconds but smooth fade". So: 4 beats made from noise through low resonances (a
    // valve's thud, not a tone), each a little different, with small timing and level changes beat to beat; stress settles along a
    // smooth curve over 17 s after the 3 s hold (about 20 s in all).
    // Then (user): "the breathing needs to go to the end like the heartbeat and slow down to normal with the heartbeat". Breathing now
    // runs as long as the heartbeat: out of breath (Healthy | Aware) while the heart is fast, then your character's normal breathing
    // (Healthy | Unaware) once stress falls below CalmAt (about 105 bpm), ending with the heartbeat. Clips change at their own ends.
    internal class Adrenaline : MonoBehaviour
    {
        private const float DoseLowDb = -15f, DoseHighDb = 6f, HoldS = 3f, StartAt = 0.08f, StopAt = 0.02f, FadeInSpan = 0.25f, BpmLow = 75f, BpmHigh = 150f,
            CalmAt = 0.4f, BreathCheckS = 0.5f, LeadS = 0.1f, StackShare = 0.15f, LevelJitter = 0.08f, TimeJitter = 0.02f;
        private const int Variants = 4;
        private static float _stress, _peakNow, _since = 999f, _nextBreath;
        private static double _nextBeat;
        private static readonly AudioClip[] _beats = new AudioClip[Variants];
        private static readonly System.Random Rng = new System.Random(7);
        private static int _beatN;
        private static AudioSource[] _src;
        private static int _which;
        public static int Blasts, Beats, Breaths;
        public static float Peak, HeartSeconds;

        public static void Reset()
        {
            _stress = _peakNow = 0f; _since = 999f; _nextBeat = 0; _nextBreath = 0f;
            Blasts = Beats = Breaths = 0; Peak = HeartSeconds = 0f;
            if (_src != null) foreach (AudioSource s in _src) if (s != null) s.Stop();
        }

        // ShockWave: a blast reached you with this pressure dose (dB against open air at 5 m).
        public static void Blast(float doseDb)
        {
            if (!Plugin.AdrenalineOn.Value || float.IsNaN(doseDb) || float.IsInfinity(doseDb)) return;
            float s = Mathf.Clamp01(Mathf.InverseLerp(DoseLowDb, DoseHighDb, doseDb));
            if (s <= StartAt) return;
            Blasts++;
            _stress = _peakNow = Mathf.Clamp01(Mathf.Max(_stress, s) + StackShare * Mathf.Min(_stress, s));
            _since = 0f;
            if (_stress > Peak) Peak = _stress;
        }

        private void Update()
        {
            try
            {
                if (_stress <= 0f) return;
                if (!Plugin.AdrenalineOn.Value || !Singleton<GameWorld>.Instantiated || Singleton<GameWorld>.Instance is HideoutGameWorld) { _stress = 0f; return; }
                Player me = Singleton<GameWorld>.Instance.MainPlayer;
                if (me == null || me.HealthController == null || !me.HealthController.IsAlive) { _stress = 0f; return; }
                float dt = Time.deltaTime;
                _since += dt;
                float x = (_since - HoldS) / Mathf.Max(Plugin.AdrenalineSeconds.Value, 1f);
                _stress = _peakNow * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(x)));
                if (_stress <= StopAt || x >= 1f) { _stress = 0f; return; }
                if (Plugin.HeartOn.Value) { Heart(); HeartSeconds += dt; }
                if (Plugin.HardBreathOn.Value && Time.time >= _nextBreath) Breathe(me);
            }
            catch (Exception e) { Diagnostics.DiagRunner.ReportOnce("A adrenaline", e); _stress = 0f; }
        }

        private static void Heart()
        {
            if (!MonoBehaviourSingleton<BetterAudio>.Instantiated) return;
            Build();
            double now = AudioSettings.dspTime;
            if (_nextBeat < now) _nextBeat = now + 0.05;
            if (_nextBeat - now > LeadS) return;
            float bpm = Mathf.Lerp(BpmLow, BpmHigh, _stress);
            float fade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(StopAt, StopAt + FadeInSpan, _stress));
            AudioSource s = _src[_which];
            _which = (_which + 1) % _src.Length;
            s.outputAudioMixerGroup = MonoBehaviourSingleton<BetterAudio>.Instance.MasterMixerGroup;
            s.clip = _beats[_beatN++ % Variants];
            s.volume = Mathf.Clamp01(Plugin.HeartVolume.Value * fade * (1f + Jitter(LevelJitter)));
            s.PlayScheduled(_nextBeat);
            _nextBeat += 60.0 / bpm * (1f + Jitter(TimeJitter));
            Beats++;
        }

        private static void Breathe(Player me)
        {
            _nextBreath = Time.time + BreathCheckS;
            BaseSpeaker sp = me.Speaker;
            if (sp == null || sp.Busy) return; // the clip playing runs to its end, so the change from fast to calm falls between clips
            // fast: the exhausted set; calm: the normal set (both as the game itself picks them in Player.UpdateBreathStatus)
            sp.Play(EPhraseTrigger.OnBreath, ETagStatus.Healthy | (_stress >= CalmAt ? ETagStatus.Aware : ETagStatus.Unaware), true);
            Breaths++;
        }

        private static void Build()
        {
            if (_beats[0] == null) for (int v = 0; v < Variants; v++) _beats[v] = MakeBeat(v);
            if (_src != null && _src[0] != null) return;
            _src = new AudioSource[3];
            for (int i = 0; i < _src.Length; i++)
            {
                var go = new GameObject("RealSoundSimulation heartbeat " + i);
                DontDestroyOnLoad(go);
                AudioSource s = go.AddComponent<AudioSource>();
                s.playOnAwake = false; s.loop = false; s.spatialBlend = 0f; s.priority = 0;
                _src[i] = s;
            }
        }

        private static float Jitter(float share) => (float)(Rng.NextDouble() * 2.0 - 1.0) * share;

        // One beat, 0.5 s: "lub" = noise through a 45 Hz resonance with a 38 Hz body, "dub" 0.16 s later = noise through 70 Hz with a 60 Hz
        // body, softer and shorter; all under a 200 Hz low-pass. Each variant shifts the resonances and the gap by up to 6%.
        private static AudioClip MakeBeat(int seed)
        {
            var r = new System.Random(1000 + seed);
            Func<float> j = () => 1f + (float)(r.NextDouble() * 0.12 - 0.06);
            float f1 = 45f * j(), f2 = 70f * j(), s1 = 38f * j(), s2 = 60f * j(), dubAt = 0.16f * j();
            Biquad bp1 = Biquad.BandPass(f1, 1.2f), bp2 = Biquad.BandPass(f2, 1.5f), lp = Biquad.LowPass(200f, 0.707f);
            float p1 = 0f, p2 = 0f;
            return ShockWave.MakeClip("rss_heartbeat_" + seed, 0.5f, t =>
            {
                float n = (float)(r.NextDouble() * 2.0 - 1.0);
                float e1 = (1f - Mathf.Exp(-t / 0.008f)) * Mathf.Exp(-t / 0.06f);
                p1 += 2f * Mathf.PI * s1 / 48000f;
                float lub = 3f * bp1.Step(n * e1) + 0.6f * e1 * Mathf.Sin(p1);
                float d = t - dubAt, dub = 0f;
                if (d > 0f)
                {
                    float e2 = (1f - Mathf.Exp(-d / 0.004f)) * Mathf.Exp(-d / 0.04f);
                    p2 += 2f * Mathf.PI * s2 / 48000f;
                    dub = 0.65f * (3f * bp2.Step(n * e2) + 0.5f * e2 * Mathf.Sin(p2));
                }
                else bp2.Step(0f);
                return lp.Step(lub + dub);
            });
        }

        // A plain RBJ biquad at 48 kHz, for building the beats once.
        private class Biquad
        {
            private float b0, b1, b2, a1, a2, x1, x2, y1, y2;
            public static Biquad BandPass(float f, float q) => Make(f, q, true);
            public static Biquad LowPass(float f, float q) => Make(f, q, false);
            private static Biquad Make(float f, float q, bool band)
            {
                float w = 2f * Mathf.PI * f / 48000f, al = Mathf.Sin(w) / (2f * q), c = Mathf.Cos(w), a0 = 1f + al;
                var bq = new Biquad();
                if (band) { bq.b0 = al / a0; bq.b1 = 0f; bq.b2 = -al / a0; }
                else { bq.b0 = (1f - c) / 2f / a0; bq.b1 = (1f - c) / a0; bq.b2 = (1f - c) / 2f / a0; }
                bq.a1 = -2f * c / a0; bq.a2 = (1f - al) / a0;
                return bq;
            }
            public float Step(float x)
            {
                float y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x; y2 = y1; y1 = y;
                return y;
            }
        }

        public static string Summary() =>
            $"ADRENALINE (A {(Plugin.AdrenalineOn.Value ? "ON" : "off")}, heartbeat {(Plugin.HeartOn.Value ? $"ON x{Plugin.HeartVolume.Value:0.00}" : "off")}, hard breathing {(Plugin.HardBreathOn.Value ? "ON" : "off")}, settles over {Plugin.AdrenalineSeconds.Value:0} s): " +
            $"close blasts {Blasts}, peak stress {Peak:0.00}, heartbeats {Beats} ({HeartSeconds:0} s), breaths {Breaths}\n";
    }
}
