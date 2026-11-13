using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace RealSoundSimulation.Fixes
{
    // 0.7.22 "A": adrenaline after close calls. User, 11 Nov 2026: "maybe its because we are missing the heartbeat sounds and rushed breathing
    // Adrenaline audio during the blast from my last audio mod", "I think that the hr and breathing will help alot with grenades", "Add it to 1.0".
    // That old private mod played clips ported from the Realism mod, so none of it is used here. A scan of the game's sounds (11 Nov 2026)
    // found no heartbeat anywhere, but every character voice has its own breathing.
    //   stress   a close blast sets it 0..1 from its pressure dose (ShockWave.Dose: -15 dB -> 0, +6 dB -> 1; a second blast on top pushes it
    //            higher). It holds HoldS, then settles along a smooth curve.
    //   heart    2D on the Master group, so the concussion cut doesn't touch it: it's felt, like the shock wave. 75 bpm at the start up to
    //            150 at full stress, scheduled on the audio clock so the beat never stutters with the frame rate. Only once a blast has
    //            concussed you, cut your hearing or rung your ears (user: "only grenades that play the concussion effects").
    //   breath   your character's own breathing from the voice's OnBreath bank, on our own source in the voice's mixer group: out of breath
    //            (Healthy | Aware) while the heart is fast, normal (Healthy | Unaware) below CalmAt, ending with the episode; at the clip's
    //            volume times "Adrenaline - breathing volume".
    //
    // 13 Nov 2026, after previews against a reference the user liked (Realism's recording, listened to only, nothing of it used): "ours
    // is to drummy", "the low volume parts ... need to be lower. and less bass", "heartbeats follow a similar beat interval the space between
    // both parts of the beat is what grows as a hr drops", "still maybe a bit bassy", "work on the fade out as this heartrate will have
    // alot of variability", "grenades thrown by the player [should] trigger a lesser effect", "if the player is being shot at or being hit
    // [it should] stay present (come down a bit but not all the way)", "I want to experiment with HR sound a little more".
    //   sound    each beat is two separate clips, "lub" then "dub" a fixed gap later (only the pause between beats changes with the rate):
    //            low noise through two low-passes over a high-pass, a soft raised-cosine rise, a short hold and a quick fall that fades to
    //            silence, with a gentle throb (no pitch, no drum attack). Four variants each. The tone is set in F12 ("Heartbeat - ..."),
    //            and the clips are rebuilt when a tone setting changes, so it can be tuned by ear in raid.
    //   size     a small scare is quieter (level by stress) and settles sooner (settle x 0.5..1 by its peak).
    //   own      a grenade you threw (Grenade.Explosion's thrower = you) counts at "Adrenaline - your own grenades" of its strength.
    //   fight    while an episode runs, a hit holds stress at HitFloor (~110 bpm) for HitHoldS, a bullet passing close at MissFloor (~95 bpm)
    //            for MissHoldS; after that it settles from there.
    internal class Adrenaline : MonoBehaviour
    {
        private const float DoseLowDb = -15f, DoseHighDb = 6f, HoldS = 3f, StartAt = 0.08f, StopAt = 0.02f, BpmLow = 75f, BpmHigh = 150f,
            CalmAt = 0.4f, BreathCheckS = 0.5f, LeadS = 0.15f, StackShare = 0.15f, LevelJitter = 0.08f, TimeJitter = 0.02f,
            LoudAtStress = 0.6f, DubLevel = 1f, OwnMatchM = 3f, OwnMatchS = 2f,
            HitFloor = 0.47f, HitHoldS = 10f, MissFloor = 0.27f, MissHoldS = 6f;
        private const int Variants = 4;
        private static float _stress, _peakNow, _since = 999f, _nextBreath, _breathUntil, _hitUntil = -1f, _missUntil = -1f;
        private static bool _heart; // a blast in this episode concussed you, cut your hearing or rang your ears
        private static AudioSource _breath;
        private static double _nextBeat;
        private static readonly AudioClip[] _lubs = new AudioClip[Variants], _dubs = new AudioClip[Variants];
        private static string _toneKey = "";
        private static readonly System.Random Rng = new System.Random(7);
        private static int _beatN;
        private static AudioSource[] _src;
        private static int _which;
        private static readonly List<(Vector3 Pos, float T)> OwnBlasts = new List<(Vector3, float)>();
        public static int Blasts, Beats, Breaths, Concussions, OwnGrenades, HitsHeld, MissesHeld;
        public static float Peak, HeartSeconds;

        public static void Apply(Harmony h)
        {
            // who threw it: the static Grenade.Explosion(grenade, item, position, thrower profile id, ...)
            var boom = AccessTools.GetDeclaredMethods(typeof(Grenade)).FirstOrDefault(m => m.Name == "Explosion" && m.IsStatic);
            if (boom != null) h.Patch(boom, prefix: new HarmonyMethod(typeof(Adrenaline), nameof(ExplosionPrefix)));
            else Plugin.Log.LogWarning("A: Grenade.Explosion not found, your own grenades count in full");
            h.Patch(AccessTools.Method(typeof(Player), nameof(Player.ApplyDamageInfo)), postfix: new HarmonyMethod(typeof(Adrenaline), nameof(DamagePostfix)));
            h.Patch(AccessTools.Method(typeof(FlyingBulletSoundPlayer), nameof(FlyingBulletSoundPlayer.PlaySound)), postfix: new HarmonyMethod(typeof(Adrenaline), nameof(FlybyPostfix)));
        }

        public static void Reset()
        {
            _stress = _peakNow = 0f; _since = 999f; _nextBeat = 0; _nextBreath = _breathUntil = 0f; _heart = false; _hitUntil = _missUntil = -1f;
            Blasts = Beats = Breaths = Concussions = OwnGrenades = HitsHeld = MissesHeld = 0; Peak = HeartSeconds = 0f;
            OwnBlasts.Clear();
            if (_breath != null) _breath.Stop();
            if (_src != null) foreach (AudioSource s in _src) if (s != null) s.Stop();
        }

        private static void ExplosionPrefix(Vector3 grenadePosition, string playerProfileIDWhoThrew)
        {
            try
            {
                Player me = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.MainPlayer : null;
                if (me == null || string.IsNullOrEmpty(playerProfileIDWhoThrew) || playerProfileIDWhoThrew != me.ProfileId) return;
                OwnBlasts.Add((grenadePosition, Time.time));
                if (OwnBlasts.Count > 8) OwnBlasts.RemoveAt(0);
            }
            catch (Exception e) { Diagnostics.DiagRunner.ReportOnce("A adrenaline (own grenade)", e); }
        }

        private static void DamagePostfix(Player __instance)
        {
            if (_stress <= 0f || !Plugin.AdrenalineUnderFireOn.Value || __instance == null || !__instance.IsYourPlayer) return;
            _hitUntil = Time.time + HitHoldS; HitsHeld++;
        }

        private static void FlybyPostfix()
        {
            if (_stress <= 0f || !Plugin.AdrenalineUnderFireOn.Value) return;
            _missUntil = Time.time + MissHoldS; MissesHeld++;
        }

        // ShockWave: a blast reached you with this pressure dose (dB against open air at 5 m), at this place.
        public static void Blast(float doseDb, Vector3 pos)
        {
            if (!Plugin.AdrenalineOn.Value || float.IsNaN(doseDb) || float.IsInfinity(doseDb)) return;
            float s = Mathf.Clamp01(Mathf.InverseLerp(DoseLowDb, DoseHighDb, doseDb));
            if (IsOwn(pos)) { s *= Mathf.Clamp01(Plugin.OwnGrenadeShare.Value); OwnGrenades++; }
            if (s <= StartAt) return;
            Blasts++;
            _stress = _peakNow = Mathf.Clamp01(Mathf.Max(_stress, s) + StackShare * Mathf.Min(_stress, s));
            _since = 0f;
            if (_stress > Peak) Peak = _stress;
        }

        private static bool IsOwn(Vector3 pos)
        {
            float now = Time.time;
            for (int i = OwnBlasts.Count - 1; i >= 0; i--)
                if (now - OwnBlasts[i].T <= OwnMatchS && (OwnBlasts[i].Pos - pos).sqrMagnitude <= OwnMatchM * OwnMatchM) return true;
            return false;
        }

        // ConcussionProof: this blast concussed you, cut your hearing or rang your ears, so the heart races.
        public static void Concussion()
        {
            if (!Plugin.AdrenalineOn.Value || _stress <= 0f) return;
            if (!_heart) Concussions++;
            _heart = true;
        }

        private void Update()
        {
            try
            {
                if (_stress <= 0f) return;
                if (!Plugin.AdrenalineOn.Value || !Singleton<GameWorld>.Instantiated || Singleton<GameWorld>.Instance is HideoutGameWorld) { _stress = 0f; return; }
                Player me = Singleton<GameWorld>.Instance.MainPlayer;
                if (me == null || me.HealthController == null || !me.HealthController.IsAlive) { _stress = 0f; return; }
                float dt = Time.deltaTime, now = Time.time;
                _since += dt;
                float settle = Mathf.Max(Plugin.AdrenalineSeconds.Value, 1f) * (0.5f + 0.5f * _peakNow); // a small scare settles sooner
                float x = (_since - HoldS) / settle;
                _stress = _peakNow * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(x)));
                // under fire: held at a floor (never above the blast's own peak); afterwards it settles from there
                float floor = Mathf.Max(now < _hitUntil ? HitFloor : 0f, now < _missUntil ? MissFloor : 0f);
                if (floor > 0f && _stress < floor) { _peakNow = Mathf.Max(floor, _stress); _stress = _peakNow; _since = HoldS; }
                if (_stress <= StopAt || (x >= 1f && floor <= 0f)) { _stress = 0f; _heart = false; return; }
                if (Plugin.HeartOn.Value && _heart) { Heart(); HeartSeconds += dt; }
                if (Plugin.HardBreathOn.Value && now >= _nextBreath) Breathe(me);
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
            float level = Plugin.HeartVolume.Value * Mathf.Pow(Mathf.Clamp01(_stress / LoudAtStress), 0.7f) * (1f + Jitter(LevelJitter));
            int v = _beatN++ % Variants;
            Play(_lubs[v], level, _nextBeat);
            Play(_dubs[v], level * DubLevel, _nextBeat + Mathf.Clamp(Plugin.HeartGapS.Value, 0.1f, 0.5f)); // the pair's gap stays the same
            _nextBeat += 60.0 / bpm * (1f + Jitter(TimeJitter));
            Beats++;
        }

        private static void Play(AudioClip clip, float volume, double at)
        {
            AudioSource s = _src[_which];
            _which = (_which + 1) % _src.Length;
            s.outputAudioMixerGroup = MonoBehaviourSingleton<BetterAudio>.Instance.MasterMixerGroup;
            s.clip = clip;
            s.volume = Mathf.Clamp01(volume);
            s.PlayScheduled(at);
        }

        private static void Breathe(Player me)
        {
            _nextBreath = Time.time + BreathCheckS;
            BaseSpeaker sp = me.Speaker;
            if (sp == null || sp.Busy || Time.time < _breathUntil) return; // a breath runs to its end, so fast to calm changes between clips
            // fast: the exhausted set; calm: the normal set (both as the game itself picks them in Player.UpdateBreathStatus)
            ETagStatus tags = ETagStatus.Healthy | (_stress >= CalmAt ? ETagStatus.Aware : ETagStatus.Unaware);
            TaggedClip tc = sp.PhrasesBanks.TryGetValue(EPhraseTrigger.OnBreath, out TagBank bank) && bank != null ? bank.Match((int)tags) : null;
            AudioSource voice = SpeechSource(me);
            if (tc == null || tc.Clip == null || voice == null)
            {
                sp.Play(EPhraseTrigger.OnBreath, tags, true); // fall back to the game's own, at its own volume
                Breaths++;
                return;
            }
            if (_breath == null)
            {
                var go = new GameObject("RealSoundSimulation breath");
                DontDestroyOnLoad(go);
                _breath = go.AddComponent<AudioSource>();
                _breath.playOnAwake = false; _breath.loop = false; _breath.spatialBlend = 0f; _breath.priority = 0;
            }
            _breath.outputAudioMixerGroup = voice.outputAudioMixerGroup; // the voice's own group: the cut and the headset act on it as before
            _breath.clip = tc.Clip;
            _breath.volume = Mathf.Clamp01((tc.Volume > 0f ? tc.Volume : 1f) * Plugin.BreathVolume.Value);
            _breath.Play();
            _breathUntil = Time.time + Mathf.Max(tc.Length, tc.Clip.length) + 0.1f;
            Breaths++;
        }

        private static readonly AccessTools.FieldRef<Player, BetterSource> SpeechRef = AccessTools.FieldRefAccess<Player, BetterSource>("_speechSource");

        private static AudioSource SpeechSource(Player me)
        {
            try { BetterSource b = SpeechRef(me); return b != null ? b.source1 : null; }
            catch { return null; }
        }

        private static void Build()
        {
            string key = $"{Plugin.HeartLowCutHz.Value:0}|{Plugin.HeartBodyHz.Value:0}|{Plugin.HeartDubPitch.Value:0.00}|{Plugin.HeartLength.Value:0.00}|{Plugin.HeartTexture.Value:0.00}";
            if (key != _toneKey)
            {
                for (int v = 0; v < Variants; v++) { _lubs[v] = MakeThump("rss_heart_lub_" + v, 1000 + v, false); _dubs[v] = MakeThump("rss_heart_dub_" + v, 2000 + v, true); }
                _toneKey = key;
            }
            if (_src != null && _src[0] != null) return;
            _src = new AudioSource[4];
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

        // One thump ("lub", or the shorter, slightly higher "dub"): noise through two low-passes (the body, "Heartbeat - body Hz") over a
        // high-pass ("Heartbeat - low cut Hz"); a raised-cosine rise, a short hold, a quick fall faded smoothly to silence; a gentle ~20 Hz
        // throb ("Heartbeat - texture"). Lengths scale with "Heartbeat - beat length". Each variant shifts the filters by up to 5%.
        private static AudioClip MakeThump(string name, int seed, bool dub)
        {
            var r = new System.Random(seed);
            Func<float> j = () => 1f + (float)(r.NextDouble() * 0.1 - 0.05);
            // user: "like realism I think our second beat needs more base": the dub sits lower ("Heartbeat - second beat pitch"), with a lower cut
            float body = Mathf.Clamp(Plugin.HeartBodyHz.Value, 30f, 300f) * (dub ? Mathf.Clamp(Plugin.HeartDubPitch.Value, 0.4f, 2f) : 1f);
            float cut = Mathf.Clamp(Plugin.HeartLowCutHz.Value - (dub ? 10f : 0f), 10f, 200f);
            float len = Mathf.Clamp(Plugin.HeartLength.Value, 0.3f, 3f), rough = Mathf.Clamp01(Plugin.HeartTexture.Value);
            float att = (dub ? 0.02f : 0.022f) * len, hold = 0.03f * len, dec = (dub ? 0.022f : 0.032f) * len, am = (dub ? 18f : 20f) * j();
            // v11 (user: "more lower volume after the bass of part 2 before part 1 of the next"): the dub rings less (lower Q), falls faster and is gated off sooner
            Biquad lp1 = Biquad.LowPass(body * j(), dub ? 1.1f : 1.8f), lp2 = Biquad.LowPass(body * 1.35f * j(), dub ? 0.9f : 1.4f), hp = Biquad.HighPass(cut, 0.707f);
            float gateLo = dub ? 0.03f : 0.01f, gateSpan = dub ? 0.07f : 0.03f;
            float length = att + hold + dec * 5f + 0.05f;
            return ShockWave.MakeClip(name, length, t =>
            {
                float e;
                if (t < att) e = 0.5f - 0.5f * Mathf.Cos(Mathf.PI * t / att);
                else if (t < att + hold) e = 1f;
                else e = Mathf.Exp(-(t - att - hold) / dec);
                float g = Mathf.Clamp01((e - gateLo) / gateSpan);
                e *= g * g * (3f - 2f * g); // quiet between beats, no click
                float throb = 1f - rough + rough * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * am * t));
                float n = (float)(r.NextDouble() * 2.0 - 1.0);
                return hp.Step(lp2.Step(lp1.Step(n))) * e * throb;
            });
        }

        // A plain RBJ biquad at 48 kHz, for building the beats.
        private class Biquad
        {
            private float b0, b1, b2, a1, a2, x1, x2, y1, y2;
            public static Biquad LowPass(float f, float q) => Make(f, q, 0);
            public static Biquad HighPass(float f, float q) => Make(f, q, 1);
            private static Biquad Make(float f, float q, int kind)
            {
                float w = 2f * Mathf.PI * f / 48000f, al = Mathf.Sin(w) / (2f * q), c = Mathf.Cos(w), a0 = 1f + al;
                var bq = new Biquad();
                if (kind == 1) { bq.b0 = (1f + c) / 2f / a0; bq.b1 = -(1f + c) / a0; bq.b2 = (1f + c) / 2f / a0; }
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
            $"ADRENALINE (A {(Plugin.AdrenalineOn.Value ? "ON" : "off")}, heartbeat {(Plugin.HeartOn.Value ? $"ON x{Plugin.HeartVolume.Value:0.00}" : "off")}, hard breathing {(Plugin.HardBreathOn.Value ? "ON" : "off")}, settles over up to {Plugin.AdrenalineSeconds.Value:0} s, own grenades x{Plugin.OwnGrenadeShare.Value:0.00}, under fire {(Plugin.AdrenalineUnderFireOn.Value ? "ON" : "off")}; tone {_toneKey}): " +
            $"close blasts {Blasts}, of them your own {OwnGrenades}, concussing (heartbeat) {Concussions}, peak stress {Peak:0.00}, heartbeats {Beats} ({HeartSeconds:0} s), breaths {Breaths} at x{Plugin.BreathVolume.Value:0.00}; held under fire by hits {HitsHeld}, by near misses {MissesHeld}\n";
    }
}
