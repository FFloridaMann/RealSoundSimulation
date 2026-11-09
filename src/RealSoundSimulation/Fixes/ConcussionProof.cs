using System;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using RealSoundSimulation.Acoustics;
using RealSoundSimulation.Diagnostics;
using EFT;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Audio;

namespace RealSoundSimulation.Fixes
{
    // 6.9h (0.6.55): the blast is heard whole when it concusses you. User, 9 Nov 2026: "when concussions are triggered the echo and grenade background sounds bad it cuts out".
    // Bring Back Concussion, for a blast inside its radius, takes the Guns, Main, World and ambience mixer volumes down by up to 80 dB in 0.05 to 0.3 s, starting the moment the grenade goes off. Explosions and all of this mod's
    // echoes, thumps and hits are on the Guns bus, so the bang was chopped as it began and its echoes went with it. The game's own tinnitus also pulls down Guns and Main.
    // Inside that radius + 1 m, the blast's own sounds go to the Master group, which those cuts don't touch: the game sends its tinnitus ring there for the same reason. The rest of the world still drops out around the bang.
    // Master skips the Guns bus's own effects, so the level ("6.9i") is by ear. Off, or without that mod, nothing changes.
    internal static class ConcussionProof
    {
        private const string OtherMod = "com.harmonyzt.BringBackConcussion";
        private const float MarginM = 1f;

        private static bool _looked, _active;
        private static ConfigEntry<float> _radius;

        public static bool Active => _active;
        public static int Blasts;

        public static void Reset() => Blasts = 0;

        public static void Apply(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(BetterAudio), nameof(BetterAudio.PlayAtPointDistant)),
                prefix: new HarmonyMethod(typeof(ConcussionProof), nameof(PlayPrefix)),
                finalizer: new HarmonyMethod(typeof(ConcussionProof), nameof(PlayFinalizer)));
            h.Patch(AccessTools.Method(typeof(ExplosionSharedMethods), nameof(ExplosionSharedMethods.Explosion)),
                prefix: new HarmonyMethod(typeof(ConcussionProof), nameof(ExplosionPrefix)));
        }

        // The radius is that mod's own live setting. 0 = not installed.
        private static float Radius()
        {
            if (!_looked)
            {
                _looked = true;
                if (Chainloader.PluginInfos.TryGetValue(OtherMod, out BepInEx.PluginInfo info) && info.Instance != null)
                    info.Instance.Config.TryGetEntry("General", "Explosion Effect Radius", out _radius);
            }
            return _radius != null ? _radius.Value : 0f;
        }

        private static bool Applies(float distance)
        {
            if (!Plugin.ConcussionProofOn.Value) return false;
            float r = Radius();
            return r > 0f && distance <= r + MarginM;
        }

        private static float Trim => Mathf.Pow(10f, Plugin.ConcussionProofDb.Value / 20f);

        // Before Bring Back Concussion reads the mixer volumes it will later put back: a dip must not be mistaken for them.
        private static void ExplosionPrefix(Vector3 explosionPosition)
        {
            try
            {
                Transform l = ThroughWall.Listener();
                if (l != null && Applies(Vector3.Distance(l.position, explosionPosition))) ShockWave.EndDipNow();
            }
            catch (Exception e) { DiagRunner.ReportOnce("6.9h blast through concussion (explosion)", e); }
        }

        // 6.6's second recording goes through here too, so its level is scaled once, on its own call.
        private static void PlayPrefix(SoundBank bank, float distance, ref float volume, ref AudioMixerGroup forceGroup)
        {
            try
            {
                if (bank == null || bank.SourceType != BetterAudio.AudioSourceGroupType.Grenades || !Applies(distance) || !MonoBehaviourSingleton<BetterAudio>.Instantiated) return;
                AudioMixerGroup master = MonoBehaviourSingleton<BetterAudio>.Instance.MasterMixerGroup;
                if (master == null) return;
                forceGroup = master;
                volume *= Trim;
                if (!BlastRecording.InLayer)
                {
                    Set();
                    Blasts++;
                }
            }
            catch (Exception e) { DiagRunner.ReportOnce("6.9h blast through concussion", e); }
        }

        private static Exception PlayFinalizer(Exception __exception)
        {
            if (_active && !BlastRecording.InLayer) Clear();
            return __exception;
        }

        // While set, every voice the mod starts for this blast goes the same way. ShockWave uses it again if it has to wait for a corner search.
        public static void Set()
        {
            if (!MonoBehaviourSingleton<BetterAudio>.Instantiated) return;
            EchoVoices.OutputOverride = MonoBehaviourSingleton<BetterAudio>.Instance.MasterMixerGroup;
            EchoVoices.VolumeTrim = Trim;
            _active = true;
        }

        public static void Clear()
        {
            EchoVoices.OutputOverride = null;
            EchoVoices.VolumeTrim = 1f;
            _active = false;
        }

        public static string Summary() => $"BLAST THROUGH CONCUSSION (6.9h {(Plugin.ConcussionProofOn.Value ? $"ON, {Plugin.ConcussionProofDb.Value:+0;-0;0} dB" : "off")}): blasts inside the concussion radius sent past its cut {Blasts}; that mod's radius {Radius():0.0} m (0 = not installed)\n";
    }
}
