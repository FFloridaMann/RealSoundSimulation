using System;

namespace RealSoundSimulation.Dsp
{
    // Everything that defines how one class of hearing protection sounds. Levels are in "game dBFS": the bench and the
    // in-game listener both treat 0 dBFS as roughly "a rifle shot a few metres away". Use the processor's InputGainDb to
    // calibrate the game's actual listener level to that.
    //
    // Model: what you hear = PASSIVE path (outside sound leaking through the cups/plugs) + ELECTRONIC path (the ear-cup
    // microphones: band-limited, amplified for quiet sounds, clamped by a fast limiter for loud ones, plus hiss).
    [Serializable]
    public sealed class HeadsetPreset
    {
        public string Name = "Unprotected";

        // Passive path.
        public float PassiveGainDb;          // broadband attenuation of the cup/plug (negative)
        public float PassiveLowShelfHz = 250f;
        public float PassiveLowShelfDb;      // positive: low frequencies leak through more (real cups/plugs do)
        public float PassiveHighShelfHz = 2000f;
        public float PassiveHighShelfDb;     // negative: highs are blocked more

        // Electronic path.
        public bool Electronic;
        public float MicHighPassHz = 120f;
        public float MicLowPassHz = 7500f;
        public int MicLowPassStages = 1;     // each stage is 12 dB/oct; cheap units roll off more
        public float Eq1Hz = 400f, Eq1Db, Eq1Q = 1f;
        public float Eq2Hz = 1500f, Eq2Db, Eq2Q = 1f;
        public float Eq3Hz = 3500f, Eq3Db, Eq3Q = 1f;
        public float MakeupDb;               // talk-through gain for quiet sounds

        // Soft compressor (shapes quiet-to-mid sounds).
        public float CompThresholdDb = -40f;
        public float CompRatio = 1f;
        public float CompKneeDb = 6f;
        public float CompAttackMs = 2f;
        public float CompReleaseMs = 120f;

        // Limiter (the clamp on gunfire). Real units cap the output near 82-85 dB(A).
        public float LimiterCeilingDb = 0f;
        public float LimiterAttackMs = 0.5f;
        public float LimiterHoldMs = 20f;
        public float LimiterReleaseMs = 250f;
        public float LookaheadMs;            // 0 = transient can poke through before the clamp (the real "click")

        public float Distortion;             // 0..0.3, mic preamp saturation
        public float HissDb = DspMath.MinDb; // electronic noise floor
        public float HissLowPassHz = 6000f;

        public HeadsetPreset Clone() => (HeadsetPreset)MemberwiseClone();

        // ---- Starting points. These are engineering guesses from the research notes, meant to be tuned by ear. ----

        public static HeadsetPreset Unprotected() => new HeadsetPreset { Name = "Unprotected" };

        public static HeadsetPreset FoamPlugs() => new HeadsetPreset
        {
            Name = "FoamPlugs",
            PassiveGainDb = -28f, PassiveLowShelfHz = 300f, PassiveLowShelfDb = 8f, PassiveHighShelfHz = 2000f, PassiveHighShelfDb = -6f,
        };

        public static HeadsetPreset PassiveMuff() => new HeadsetPreset
        {
            Name = "PassiveMuff",
            PassiveGainDb = -22f, PassiveLowShelfHz = 250f, PassiveLowShelfDb = 6f, PassiveHighShelfHz = 1500f, PassiveHighShelfDb = -10f,
        };

        // Sordin / ComTac class: wide mic band, fast clamp, low hiss.
        public static HeadsetPreset ElectronicPremium() => new HeadsetPreset
        {
            Name = "ElectronicPremium",
            PassiveGainDb = -24f, PassiveLowShelfHz = 250f, PassiveLowShelfDb = 6f, PassiveHighShelfHz = 1500f, PassiveHighShelfDb = -10f,
            Electronic = true,
            MicHighPassHz = 120f, MicLowPassHz = 7500f, MicLowPassStages = 1,
            Eq2Hz = 3000f, Eq2Db = 2f, Eq2Q = 0.8f,
            MakeupDb = 12f,
            CompThresholdDb = -40f, CompRatio = 2f, CompKneeDb = 6f, CompAttackMs = 2f, CompReleaseMs = 120f,
            LimiterCeilingDb = -24f, LimiterAttackMs = 0.5f, LimiterHoldMs = 20f, LimiterReleaseMs = 250f, LookaheadMs = 0.5f,
            Distortion = 0.05f, HissDb = -66f, HissLowPassHz = 7000f,
        };

        // Military issue (Peltor/ComTac style): a bit more gain, slightly slower recovery.
        public static HeadsetPreset ElectronicMilitary() => new HeadsetPreset
        {
            Name = "ElectronicMilitary",
            PassiveGainDb = -25f, PassiveLowShelfHz = 250f, PassiveLowShelfDb = 6f, PassiveHighShelfHz = 1500f, PassiveHighShelfDb = -10f,
            Electronic = true,
            MicHighPassHz = 150f, MicLowPassHz = 7000f, MicLowPassStages = 1,
            Eq1Hz = 420f, Eq1Db = 1.5f, Eq1Q = 1.3f, Eq2Hz = 3500f, Eq2Db = 1.5f, Eq2Q = 0.8f,
            MakeupDb = 14f,
            CompThresholdDb = -42f, CompRatio = 2f, CompKneeDb = 6f, CompAttackMs = 2f, CompReleaseMs = 150f,
            LimiterCeilingDb = -26f, LimiterAttackMs = 0.7f, LimiterHoldMs = 25f, LimiterReleaseMs = 300f, LookaheadMs = 0f,
            Distortion = 0.08f, HissDb = -63f, HissLowPassHz = 6500f,
        };

        // Cheap talk-through / GSSh-01 class: narrow band, slower clamp (transient leaks), long pumping, audible hiss.
        public static HeadsetPreset ElectronicBudget() => new HeadsetPreset
        {
            Name = "ElectronicBudget",
            PassiveGainDb = -20f, PassiveLowShelfHz = 250f, PassiveLowShelfDb = 7f, PassiveHighShelfHz = 1500f, PassiveHighShelfDb = -8f,
            Electronic = true,
            MicHighPassHz = 300f, MicLowPassHz = 4500f, MicLowPassStages = 2,
            Eq2Hz = 2500f, Eq2Db = 3f, Eq2Q = 1.2f,
            MakeupDb = 10f,
            CompThresholdDb = -36f, CompRatio = 3f, CompKneeDb = 4f, CompAttackMs = 4f, CompReleaseMs = 200f,
            LimiterCeilingDb = -22f, LimiterAttackMs = 3f, LimiterHoldMs = 40f, LimiterReleaseMs = 400f, LookaheadMs = 0f,
            Distortion = 0.15f, HissDb = -56f, HissLowPassHz = 5000f,
        };

        public static HeadsetPreset[] All() => new[]
        {
            Unprotected(), FoamPlugs(), PassiveMuff(), ElectronicPremium(), ElectronicMilitary(), ElectronicBudget(),
        };
    }
}
