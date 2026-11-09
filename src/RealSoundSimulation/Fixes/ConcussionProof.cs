using System;
using System.Reflection;
using Audio.SpatialSystem;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using RealSoundSimulation.Acoustics;
using RealSoundSimulation.Diagnostics;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Audio;

namespace RealSoundSimulation.Fixes
{
    // 6.9h (0.6.55, reworked 0.6.57, 0.6.58): hearing goes when concussion hits, the blast is still felt, and nothing is chopped. User, 9 Nov 2026: "better if the hearing cuts out with concussion when the effect is applied.
    // but we find a way to keep the feeling of the blast wave alive and no sharp cut offs for tails".
    // Bring Back Concussion takes Main, Guns, World and both ambience volumes down, by closeness, in 0.05 to 0.3 s. Explosions, echoes and tails are on Guns, so they were cut off mid-sound.
    //   cut      that mod's cut is taken over and copied: same depth, cut time, hold, fade-back (its own setting), same ringing check. World sounds still go at its speed.
    //   tail     only Guns differs: it goes down over "6.9i" s, so the bang, echoes and tails melt away.
    //   felt     inside the part of its radius where it cuts (95%), the pressure hit, thump, repeat hits and slow push go to the Master group, which no cut touches, at "6.9j". Echoes and the recording stay on Guns and fade.
    // The cut's depth, timing and tinnitus check are adapted from Bring Back Concussion by harmony (harmonyzt), MIT: see THIRD_PARTY_NOTICES.md.
    // That mod's code is found by name once a raid starts. If it's gone or changed, its own cut runs as normal. The game's own ringing (no headset) still drives Main and Guns, as that mod allows.
    internal class ConcussionProof : MonoBehaviour
    {
        public const string Header = "t,closeness,endDb,cutS,tailS,holdS,fadeS,tinnitus";
        private const string OtherMod = "com.harmonyzt.BringBackConcussion", CutType = "BringBackConcussion.Patches.ExplosionSoundCutoff";
        private const float NoCutAtOrBelow = 0.05f, CutsBelow = 0.95f, DefaultFadeS = 6f, WriteStepDb = 0.01f;
        private const int GunsIndex = 1;

        private static Harmony _h;
        private static MethodInfo _trigger;
        private static bool _tried, _active;
        private static ConfigEntry<float> _radius, _fadeEntry;
        private static ConfigEntry<bool> _cutEntry;
        private static FieldInfo _tinnitusEnd;
        private static readonly string[] Keys = new string[5];
        private static readonly float[] Start = new float[5], Last = new float[5];
        private static float _t0, _end, _cut, _tail, _hold, _fade;

        public static int Soft, LeftToMod, Felt;
        public static bool CutActive => _active;

        public static void Apply(Harmony h) => _h = h;

        public static void Reset()
        {
            Soft = LeftToMod = Felt = 0;
            _active = false;
            TryPatch();
        }

        private static void TryPatch()
        {
            if (_tried || _h == null) return;
            _tried = true;
            try
            {
                if (!Chainloader.PluginInfos.TryGetValue(OtherMod, out BepInEx.PluginInfo info) || info.Instance == null) return;
                ConfigFile cfg = info.Instance.Config;
                cfg.TryGetEntry("General", "Explosion Effect Radius", out _radius);
                cfg.TryGetEntry("Audio", "Cut Off Sound On Explosion", out _cutEntry);
                cfg.TryGetEntry("Audio", "Explosion Sound Fade In Time", out _fadeEntry);
                Type t = info.Instance.GetType().Assembly.GetType(CutType);
                _trigger = t != null ? AccessTools.Method(t, "Trigger") : null;
                if (_trigger == null) { Plugin.Log.LogWarning("6.9h: Bring Back Concussion's sound cut wasn't found, so its own cut will run"); return; }
                _h.Patch(_trigger, prefix: new HarmonyMethod(typeof(ConcussionProof), nameof(TriggerPrefix)));
                _tinnitusEnd = AccessTools.Field(typeof(BetterAudio), "float_2");
            }
            catch (Exception e) { _trigger = null; DiagRunner.ReportOnce("6.9h soft concussion cut (patch)", e); }
        }

        // Will that mod cut the sound for a blast this far away?
        public static bool WillCut(float distance) =>
            Plugin.SoftCutOn.Value && _trigger != null && _radius != null && (_cutEntry == null || _cutEntry.Value) && distance <= _radius.Value * CutsBelow;

        public static void FeltOn()
        {
            if (!MonoBehaviourSingleton<BetterAudio>.Instantiated) return;
            EchoVoices.OutputOverride = MonoBehaviourSingleton<BetterAudio>.Instance.MasterMixerGroup;
            EchoVoices.VolumeTrim = Mathf.Pow(10f, Plugin.FeltThroughCutDb.Value / 20f);
        }

        public static void FeltOff()
        {
            EchoVoices.OutputOverride = null;
            EchoVoices.VolumeTrim = 1f;
        }

        private static bool TriggerPrefix(float closeness)
        {
            ShockWave.EndDipNow(); // before anyone reads the volumes that get put back
            if (!Plugin.SoftCutOn.Value || closeness <= NoCutAtOrBelow) return true;
            try
            {
                if (TryStart(closeness)) { Soft++; return false; }
            }
            catch (Exception e) { DiagRunner.ReportOnce("6.9h soft concussion cut", e); }
            LeftToMod++;
            return true;
        }

        private static bool TryStart(float closeness)
        {
            if (!MonoBehaviourSingleton<BetterAudio>.Instantiated) return false;
            BetterAudio ba = MonoBehaviourSingleton<BetterAudio>.Instance;
            AudioMixer m = ba.Master;
            if (m == null || ba.AudioMixerData == null) return false;
            if (_active) { PutBack(m); _active = false; }
            Keys[0] = ba.AudioMixerData.MainMixerVolume; Keys[1] = ba.AudioMixerData.GunsMixerVolume; Keys[2] = ba.AudioMixerData.WorldMixerVolume; Keys[3] = ba.AudioMixerData.AmbientInMixerVolume; Keys[4] = ba.AudioMixerData.AmbientOutMixerVolume;
            for (int i = 0; i < Keys.Length; i++)
            {
                if (!m.GetFloat(Keys[i], out Start[i])) return false;
                if (Start[i] <= -75f) Start[i] = 0f;
                Last[i] = Start[i];
            }
            _end = Mathf.Lerp(0f, -80f, closeness);
            _cut = Mathf.Lerp(0.3f, 0.05f, closeness);
            _hold = Mathf.Lerp(0.2f, 1.5f, closeness);
            _fade = _fadeEntry != null ? _fadeEntry.Value : DefaultFadeS;
            _tail = Mathf.Max(_cut, Plugin.BlastTailFadeS.Value);
            _t0 = Time.time;
            _active = true;
            if (DiagLog.IsOpen) DiagLog.Row("concussion.csv", string.Join(",", DiagLog.F(_t0 - DiagRunner.RaidStartTime, "0.00"), DiagLog.F(closeness, "0.00"), DiagLog.F(_end, "0.0"), DiagLog.F(_cut, "0.00"), DiagLog.F(_tail, "0.00"), DiagLog.F(_hold, "0.00"), DiagLog.F(_fade, "0.0"), Tinnitus(ba) ? "1" : "0"));
            return true;
        }

        private static float Remaining(int i, float t)
        {
            float attack = i == GunsIndex ? _tail : _cut;
            if (t < attack) return t / attack;
            t -= attack;
            if (t < _hold) return 1f;
            t -= _hold;
            return _fade > 0f ? Mathf.Clamp01(1f - t / _fade) : 0f;
        }

        private static bool Tinnitus(BetterAudio ba)
        {
            try { return _tinnitusEnd != null && (float)_tinnitusEnd.GetValue(ba) > Time.time; }
            catch { return false; }
        }

        private void Update()
        {
            if (!_active) return;
            try
            {
                if (!SpatialAudioSystem.Initialized || !MonoBehaviourSingleton<BetterAudio>.Instantiated) { _active = false; return; } // raid over: the game owns these now
                BetterAudio ba = MonoBehaviourSingleton<BetterAudio>.Instance;
                AudioMixer m = ba.Master;
                if (m == null) { _active = false; return; }
                float t = Time.time - _t0;
                if (t >= Mathf.Max(_cut, _tail) + _hold + _fade) { PutBack(m); _active = false; return; }
                bool ringing = Tinnitus(ba);
                for (int i = 0; i < Keys.Length; i++)
                {
                    if (ringing && i <= GunsIndex) continue; // the game's ringing is driving Main and Guns, as in that mod
                    float v = Mathf.Lerp(Start[i], _end, Remaining(i, t));
                    if (Mathf.Abs(v - Last[i]) < WriteStepDb) continue; // nothing to change during the hold, so other writers aren't overruled
                    m.SetFloat(Keys[i], v);
                    Last[i] = v;
                }
            }
            catch (Exception e) { DiagRunner.ReportOnce("6.9h soft concussion cut (update)", e); _active = false; }
        }

        private static void PutBack(AudioMixer m)
        {
            for (int i = 0; i < Keys.Length; i++) if (!string.IsNullOrEmpty(Keys[i])) m.SetFloat(Keys[i], Start[i]);
        }

        public static string Summary() => $"CONCUSSION SOUND CUT (6.9h {(Plugin.SoftCutOn.Value ? $"ON, blast tail {Plugin.BlastTailFadeS.Value:0.0} s, felt {Plugin.FeltThroughCutDb.Value:+0;-0;0} dB" : "off")}): that mod's cut {(_trigger != null ? "found" : "not found or not installed")}; soft cuts {Soft}, left to that mod {LeftToMod}; layers kept through the cut for {Felt} blasts  (concussion.csv)\n";
    }
}
