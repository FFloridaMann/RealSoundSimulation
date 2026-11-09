using System;
using System.Reflection;
using BepInEx.Bootstrap;
using RealSoundSimulation.Diagnostics;
using HarmonyLib;
using UnityEngine;

namespace RealSoundSimulation.Fixes
{
    // 6.9h (0.6.55, reworked 0.6.57): the blast is heard before concussion takes the sound away. User, 9 Nov 2026: "when concussions are triggered the echo and grenade background sounds bad it cuts out".
    // Bring Back Concussion, for a blast inside its radius, takes the Guns, Main, World and ambience mixer volumes down by up to 80 dB in 0.05 to 0.3 s, starting the moment the grenade goes off, so the bang was chopped as it began.
    // 0.6.55 sent the blast's own sounds to the Master group to escape that, but Master misses the Guns bus's gain, so the recording was quieter than the layers around it (raid_20261109_131528). Now everything stays on its normal channel
    // and that mod's cut is held back "6.9i" seconds: bang, thump and early echoes at full level, then the world drops away. A later blast during the wait keeps the first time and the stronger cut.
    // That mod's own code is patched by name, once a raid starts and it's certainly loaded. If it changes, the patch simply doesn't apply and its cut happens at its normal time. The game's own ringing (no headset) isn't delayed.
    internal class ConcussionProof : MonoBehaviour
    {
        private const string OtherMod = "com.harmonyzt.BringBackConcussion", CutType = "BringBackConcussion.Patches.ExplosionSoundCutoff";
        private const float NoCutAtOrBelow = 0.05f; // that mod's own test

        private static Harmony _h;
        private static MethodInfo _trigger;
        private static bool _tried, _waiting, _reentry;
        private static float _at, _closeness;

        public static int Delayed, Immediate;

        public static void Apply(Harmony h) => _h = h;

        public static void Reset()
        {
            Delayed = Immediate = 0;
            _waiting = false;
            TryPatch();
        }

        private static void TryPatch()
        {
            if (_tried || _h == null) return;
            _tried = true;
            try
            {
                if (!Chainloader.PluginInfos.TryGetValue(OtherMod, out BepInEx.PluginInfo info) || info.Instance == null) return;
                Type t = info.Instance.GetType().Assembly.GetType(CutType);
                _trigger = t != null ? AccessTools.Method(t, "Trigger") : null;
                if (_trigger == null) { Plugin.Log.LogWarning("6.9h: Bring Back Concussion's sound cut wasn't found, so it won't be delayed"); return; }
                _h.Patch(_trigger, prefix: new HarmonyMethod(typeof(ConcussionProof), nameof(TriggerPrefix)));
            }
            catch (Exception e) { _trigger = null; DiagRunner.ReportOnce("6.9h concussion delay (patch)", e); }
        }

        private static bool TriggerPrefix(float closeness)
        {
            if (_reentry) return true;
            float delay = Plugin.ConcussionDelayOn.Value ? Plugin.ConcussionDelayS.Value : 0f;
            if (closeness <= NoCutAtOrBelow || delay <= 0f)
            {
                ShockWave.EndDipNow(); // before that mod reads the volumes it later restores
                if (closeness > NoCutAtOrBelow) Immediate++;
                return true;
            }
            if (_waiting) _closeness = Mathf.Max(_closeness, closeness);
            else { _waiting = true; _at = Time.unscaledTime + delay; _closeness = closeness; }
            Delayed++;
            return false;
        }

        private void Update()
        {
            if (!_waiting || Time.unscaledTime < _at) return;
            _waiting = false;
            try
            {
                ShockWave.EndDipNow();
                _reentry = true;
                _trigger.Invoke(null, new object[] { _closeness });
            }
            catch (Exception e) { DiagRunner.ReportOnce("6.9h concussion delay", e); }
            finally { _reentry = false; }
        }

        public static string Summary() => $"CONCUSSION SOUND CUT (6.9h {(Plugin.ConcussionDelayOn.Value ? $"ON, {Plugin.ConcussionDelayS.Value:0.0} s" : "off")}): that mod's cut {(_trigger != null ? "found" : "not found or not installed")}; delayed {Delayed}, at its normal time {Immediate}\n";
    }
}
