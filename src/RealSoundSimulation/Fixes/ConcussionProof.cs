using System;
using System.Reflection;
using Audio.SpatialSystem;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using RealSoundSimulation.Acoustics;
using RealSoundSimulation.Diagnostics;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using HarmonyLib;
using System.Collections.Generic;
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
    //
    // 0.6.60 "6.9k": how concussive a blast is comes from its pressure, not the straight-line distance. User, 9 Nov 2026: "shouldnt it be more concussive indoors", and research "a wall may protect but the blast through a door delivers concussive effects ... room indoors but very big? very small? lots of windows no windows".
    // ShockWave.Dose works out the pressure by the way it reached you and the blast's room. That's turned into the open-air distance with the same pressure, and that mod's own explosion patch is called with the blast moved to that distance, so its radius and closeness still decide.
    // Two levels: ears (the sound cut, and ringing) reach "6.9l" dB further than the full concussion. Length and strength scale with how long the pressure lasted and how close it was. That mod's patch is called a frame late, because the game queues the blast sound and the route is only known then.
    //
    // 0.7.10 "6.9q": headsets protect your hearing. User, 10 Nov 2026: "for the time being, all headsets should remove ear ringing and most hearing loss from concussions. so they function well then later actually improved and optionally simulated."
    // Ringing already never plays with a headset (the game's, that mod's, and the ears-only ringing all check the earpiece slot). The hearing cut didn't check, so with any headset on
    // its closeness is scaled by (1 - "6.9q"): at 75% the closest blast cuts to -20 dB with a shorter hold, and light blasts don't cut. Concussion itself, the felt thump and blur are unchanged.
    internal class ConcussionProof : MonoBehaviour
    {
        public const string Header = "t,closeness,endDb,cutS,tailS,holdS,fadeS,tinnitus,headset"; // 0.7.10: headset
        public const string DoseHeader = "t,doseDb,equivalentM,earsEquivalentM,earsCloseness,concussed,duration,ringOnly,blastRoomDb,yourRoomDb,yourRoomM,yourRoom"; // 0.7.1: the last four, 6.9n
        private const string OtherMod = "com.harmonyzt.BringBackConcussion", CutType = "BringBackConcussion.Patches.ExplosionSoundCutoff", ProximityType = "BringBackConcussion.Patches.ExplosionProximityPatch";
        private const float NoCutAtOrBelow = 0.05f, CutsBelow = 0.95f, DefaultFadeS = 6f, WriteStepDb = 0.01f, PendingMaxS = 0.25f, SamePosM = 1f, RingMinS = 1.5f, RingMaxS = 4f;
        private const int GunsIndex = 1, MaxPending = 6;

        private class Pending { public IExplosiveItem Item; public Vector3 Pos; public float T; }
        private static readonly List<Pending> Waiting = new List<Pending>();
        private static MethodInfo _proximity, _ring;
        private static bool _passing, _byDose;
        private static float _dur, _closenessNow;
        public static int Concussed, RingOnly, EarsOnly, Dropped, Plain;

        private static Harmony _h;
        private static MethodInfo _trigger;
        private static bool _reentry;
        private static bool _tried, _active;
        private static ConfigEntry<float> _radius, _fadeEntry;
        private static ConfigEntry<bool> _cutEntry;
        private static FieldInfo _tinnitusEnd;
        private static readonly string[] Keys = new string[5];
        private static readonly float[] Start = new float[5], Last = new float[5];
        private static float _t0, _end, _cut, _tail, _hold, _fade;

        public static int Soft, LeftToMod, Felt, Protected;
        private static bool _headsetNow;
        public static bool CutActive => _active;

        public static void Apply(Harmony h) => _h = h;

        public static void Reset()
        {
            Soft = LeftToMod = Felt = Protected = 0;
            Concussed = RingOnly = EarsOnly = Dropped = Plain = 0;
            Waiting.Clear();
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
                Type p = info.Instance.GetType().Assembly.GetType(ProximityType);
                _proximity = p != null ? AccessTools.Method(p, "PatchPostfix") : null;
                _ring = p != null ? AccessTools.Method(p, "TryStartTinnitus") : null;
                if (_proximity == null) { Plugin.Log.LogWarning("6.9k: Bring Back Concussion's explosion patch wasn't found, so it will use straight-line distance"); return; }
                _h.Patch(_proximity, prefix: new HarmonyMethod(typeof(ConcussionProof), nameof(ProximityPrefix)));
                _h.Patch(AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.DoContusion)), prefix: new HarmonyMethod(typeof(ConcussionProof), nameof(ContusionPrefix)));
            }
            catch (Exception e) { _trigger = null; DiagRunner.ReportOnce("6.9h soft concussion cut (patch)", e); }
        }

        private static bool ByPressure => Plugin.ConcussionPressureOn.Value && Plugin.ShockOn.Value && _proximity != null && _radius != null;

        private static float Closeness(float equivalentM) => Mathf.Clamp01(1f - equivalentM / _radius.Value);

        // "6.9q": is your player wearing a headset (anything in the earpiece slot that's a headset, as the game's own ringing check)?
        private static bool HeadsetOn()
        {
            try
            {
                Player player = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.MainPlayer : null;
                return player != null && player.Equipment?.GetSlot(EquipmentSlot.Earpiece)?.ContainedItem is Headphones;
            }
            catch { return false; }
        }

        // The closeness the hearing cut uses: scaled down with a headset on.
        private static float CutCloseness(float closeness)
        {
            _headsetNow = HeadsetOn();
            return _headsetNow ? closeness * (1f - Mathf.Clamp01(Plugin.HeadsetProtectionPct.Value / 100f)) : closeness;
        }

        // Will the sound be cut for this blast?
        public static bool WillCut(float distance, float doseDb)
        {
            if (!Plugin.SoftCutOn.Value || _trigger == null || _radius == null || (_cutEntry != null && !_cutEntry.Value)) return false;
            if (ByPressure) return CutCloseness(Closeness(ShockWave.EquivalentM(doseDb + Plugin.EarsReachDb.Value))) > NoCutAtOrBelow;
            return distance <= _radius.Value * CutsBelow && CutCloseness(Mathf.Clamp01(1f - distance / _radius.Value)) > NoCutAtOrBelow;
        }

        // That mod's explosion patch, held until the blast's route is known.
        private static bool ProximityPrefix(IExplosiveItem grenadeItem, Vector3 explosionPosition)
        {
            if (_passing || !ByPressure || grenadeItem == null) return true;
            if (Waiting.Count >= MaxPending) return true;
            Waiting.Add(new Pending { Item = grenadeItem, Pos = explosionPosition, T = Time.unscaledTime });
            return false;
        }

        private static void ContusionPrefix(ref float applyTime, ref float strength)
        {
            if (!_byDose) return;
            applyTime *= Mathf.Lerp(0.4f, 1.2f, _dur);
            strength = Mathf.Clamp01(strength * Mathf.Lerp(0.6f, 1.1f, _closenessNow));
        }

        private static int Find(Vector3 pos)
        {
            for (int i = 0; i < Waiting.Count; i++) if ((Waiting[i].Pos - pos).sqrMagnitude < SamePosM * SamePosM) return i;
            return -1;
        }

        // Not a blast this mod rates, or no answer in time: that mod's own distance rule.
        public static void DeliverPlain(Vector3 pos)
        {
            int i = Find(pos);
            if (i < 0) return;
            Pending p = Waiting[i];
            Waiting.RemoveAt(i);
            RunPlain(p);
        }

        private static void RunPlain(Pending p)
        {
            try
            {
                Plain++;
                _passing = true;
                _proximity.Invoke(null, new object[] { p.Item, p.Pos });
            }
            catch (Exception e) { DiagRunner.ReportOnce("6.9k concussion by pressure (plain)", e); }
            finally { _passing = false; }
        }

        public static void Deliver(Vector3 pos, float doseDb, float duration)
        {
            int i = Find(pos);
            if (i < 0) return;
            Pending p = Waiting[i];
            Waiting.RemoveAt(i);
            try
            {
                if (float.IsNegativeInfinity(doseDb)) { Dropped++; DoseRow(doseDb, float.PositiveInfinity, float.PositiveInfinity, 0f, false, duration, false); return; }
                float equiv = ShockWave.EquivalentM(doseDb), earsEquiv = ShockWave.EquivalentM(doseDb + Plugin.EarsReachDb.Value);
                float ears = Closeness(earsEquiv);
                Player player = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.MainPlayer : null;
                if (player == null) return;

                float cutEars = CutCloseness(ears);
                if (_headsetNow && ears > NoCutAtOrBelow) Protected++;
                if (cutEars > NoCutAtOrBelow && (_cutEntry == null || _cutEntry.Value))
                {
                    ShockWave.EndDipNow();
                    bool soft = Plugin.SoftCutOn.Value && TryStart(cutEars, Mathf.Lerp(0.6f, 1.5f, duration));
                    if (soft) { Soft++; }
                    else
                    {
                        LeftToMod++;
                        _reentry = true;
                        try { _trigger.Invoke(null, new object[] { cutEars }); }
                        finally { _reentry = false; }
                    }
                }

                bool concussed = equiv <= _radius.Value, ringOnly = false;
                if (concussed)
                {
                    Vector3 to = p.Pos - player.Position;
                    Vector3 moved = player.Position + (to.sqrMagnitude > 0.01f ? to.normalized : Vector3.forward) * equiv;
                    _dur = duration; _closenessNow = Closeness(equiv);
                    _passing = _byDose = true;
                    try { _proximity.Invoke(null, new object[] { p.Item, moved }); }
                    finally { _passing = _byDose = false; }
                    Concussed++;
                }
                else if (ears > NoCutAtOrBelow)
                {
                    EarsOnly++;
                    if (_ring != null)
                    {
                        _ring.Invoke(null, new object[] { player, Mathf.Lerp(RingMinS, RingMaxS, ears) });
                        ringOnly = true; RingOnly++;
                    }
                }
                DoseRow(doseDb, equiv, earsEquiv, ears, concussed, duration, ringOnly);
            }
            catch (Exception e) { DiagRunner.ReportOnce("6.9k concussion by pressure", e); }
        }

        private static void DoseRow(float doseDb, float equiv, float earsEquiv, float ears, bool concussed, float duration, bool ringOnly)
        {
            if (!DiagLog.IsOpen) return;
            DiagLog.Row("concdose.csv", string.Join(",", DiagLog.F(Time.time - DiagRunner.RaidStartTime, "0.00"), DiagLog.F(float.IsInfinity(doseDb) ? -999f : doseDb, "0.0"), DiagLog.F(float.IsInfinity(equiv) ? 999f : equiv, "0.0"), DiagLog.F(float.IsInfinity(earsEquiv) ? 999f : earsEquiv, "0.0"), DiagLog.F(ears, "0.00"), concussed ? "1" : "0", DiagLog.F(duration, "0.00"), ringOnly ? "1" : "0",
                DiagLog.F(ShockWave.LastBlastRoomDb, "0.0"), DiagLog.F(ShockWave.LastYourRoomDb, "0.0"), DiagLog.F(ShockWave.LastYourRoomM, "0.0"), ShockWave.LastYourRoom));
        }

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

        private static bool TriggerPrefix(ref float closeness)
        {
            if (_reentry) return true;
            if (_byDose) return false; // ears were already dealt with
            ShockWave.EndDipNow(); // before anyone reads the volumes that get put back
            float raw = closeness;
            closeness = CutCloseness(closeness); // "6.9q", also for that mod's own cut
            if (_headsetNow && raw > NoCutAtOrBelow) Protected++;
            if (closeness <= NoCutAtOrBelow) return !_headsetNow || raw <= NoCutAtOrBelow; // a headset took the whole cut away
            if (!Plugin.SoftCutOn.Value) return true;
            try
            {
                if (TryStart(closeness, 1f)) { Soft++; return false; }
            }
            catch (Exception e) { DiagRunner.ReportOnce("6.9h soft concussion cut", e); }
            LeftToMod++;
            return true;
        }

        private static bool TryStart(float closeness, float holdScale)
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
            _hold = Mathf.Lerp(0.2f, 1.5f, closeness) * holdScale;
            _fade = _fadeEntry != null ? _fadeEntry.Value : DefaultFadeS;
            _tail = Mathf.Max(_cut, Plugin.BlastTailFadeS.Value);
            _t0 = Time.time;
            _active = true;
            if (DiagLog.IsOpen) DiagLog.Row("concussion.csv", string.Join(",", DiagLog.F(_t0 - DiagRunner.RaidStartTime, "0.00"), DiagLog.F(closeness, "0.00"), DiagLog.F(_end, "0.0"), DiagLog.F(_cut, "0.00"), DiagLog.F(_tail, "0.00"), DiagLog.F(_hold, "0.00"), DiagLog.F(_fade, "0.0"), Tinnitus(ba) ? "1" : "0", _headsetNow ? "1" : "0"));
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
            if (Waiting.Count > 0)
            {
                float now = Time.unscaledTime;
                for (int i = Waiting.Count - 1; i >= 0; i--)
                {
                    if (now - Waiting[i].T < PendingMaxS) continue;
                    Pending p = Waiting[i];
                    Waiting.RemoveAt(i);
                    RunPlain(p); // the sound never came: that mod's distance rule
                }
            }
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

        public static string Summary() => $"CONCUSSION SOUND CUT (6.9h {(Plugin.SoftCutOn.Value ? $"ON, blast tail {Plugin.BlastTailFadeS.Value:0.0} s, felt {Plugin.FeltThroughCutDb.Value:+0;-0;0} dB" : "off")}): that mod's cut {(_trigger != null ? "found" : "not found or not installed")}; soft cuts {Soft}, left to that mod {LeftToMod}; layers kept through the cut for {Felt} blasts; by pressure (6.9k {(ByPressure ? $"ON, {Plugin.ConcussionPressureDb.Value:+0;-0;0} dB, ears +{Plugin.EarsReachDb.Value:0} dB" : "off or not found")}): concussed {Concussed}, ears only {EarsOnly} (with ringing {RingOnly}), softened by a headset {Protected} (6.9q {Plugin.HeadsetProtectionPct.Value:0}%), walled off {Dropped}, by distance as a fallback {Plain}; your room (6.9n {(Plugin.YourRoomOn.Value ? $"ON, up to +{Plugin.YourRoomDb.Value:0.#} dB" : "off")}) added to {ShockWave.YourRoomAdded}, same room as the blast {ShockWave.YourRoomSame}  (concussion.csv, concdose.csv)\n";
    }
}
