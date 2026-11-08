using System.Collections.Generic;
using System.Reflection;
using Audio.SpatialSystem;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Audio;

namespace RealSoundSimulation.Diagnostics
{
    // Logs every remote gunshot as the game plays it: distance, the bank's rolloff (audible range), the occlusion the
    // shooter's container had, which gun mixer group was used, and a straight line-of-sight check. This is the direct
    // test of the "250 m cliff" (#4) and the mixer flip-flop (#7).
    internal static class ShotProbe
    {
        private static readonly AccessTools.FieldRef<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge> BridgeRef =
            AccessTools.FieldRefAccess<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge>("playersBridge");
        private static readonly AccessTools.FieldRef<WeaponSoundPlayer, SuperBetterAudioQueue> QueueRef =
            AccessTools.FieldRefAccess<WeaponSoundPlayer, SuperBetterAudioQueue>("_queue");
        private static readonly AccessTools.FieldRef<WeaponSoundPlayer, AudioMixerGroup> MixerRef =
            AccessTools.FieldRefAccess<WeaponSoundPlayer, AudioMixerGroup>("_currentMixerGroup");

        private static readonly Dictionary<int, float> LastLogged = new Dictionary<int, float>();
        private static readonly Dictionary<int, AudioMixerGroup> LastMixer = new Dictionary<int, AudioMixerGroup>();

        // Raid totals for the summary.
        public static int Shots, ShotsBeyond250WithLos, ShotsBeyond250WithLosOccluded, ShotsWithLosOccluded, ShotsWithLos;
        public static int MixerFlips;
        public static readonly SortedDictionary<int, int> RolloffHistogram = new SortedDictionary<int, int>();

        public static void Reset()
        {
            Shots = ShotsBeyond250WithLos = ShotsBeyond250WithLosOccluded = ShotsWithLosOccluded = ShotsWithLos = MixerFlips = 0;
            RolloffHistogram.Clear();
            LastLogged.Clear();
            LastMixer.Clear();
        }

        public static void Apply(Harmony h)
        {
            MethodInfo fire = AccessTools.Method(typeof(WeaponSoundPlayer), nameof(WeaponSoundPlayer.FireBullet));
            MethodInfo mixer = AccessTools.Method(typeof(WeaponSoundPlayer), nameof(WeaponSoundPlayer.UpdateMixerGroup));
            if (fire != null) h.Patch(fire, postfix: new HarmonyMethod(typeof(ShotProbe), nameof(FirePostfix)));
            if (mixer != null) h.Patch(mixer, postfix: new HarmonyMethod(typeof(ShotProbe), nameof(MixerPostfix)));
        }

        private static void MixerPostfix(WeaponSoundPlayer __instance)
        {
            if (!DiagLog.IsOpen) return;
            try
            {
                int id = __instance.GetInstanceID();
                AudioMixerGroup now = MixerRef(__instance);
                if (LastMixer.TryGetValue(id, out AudioMixerGroup before) && before != now) MixerFlips++;
                LastMixer[id] = now;
            }
            catch { }
        }

        private static void FirePostfix(WeaponSoundPlayer __instance)
        {
            if (!DiagLog.IsOpen || !Plugin.LogsOn.Value) return;
            try
            {
                BaseSoundPlayer.IObserverToPlayerBridge bridge = BridgeRef(__instance);
                if (bridge == null || bridge.PointOfView == EPointOfView.FirstPerson) return;

                // Full-auto calls FireBullet every round; log at most 4 per second per weapon.
                int key = __instance.GetInstanceID();
                float now = Time.time;
                if (LastLogged.TryGetValue(key, out float last) && now - last < 0.25f) return;
                LastLogged[key] = now;

                if (!MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated) return;
                SpatialAudioSystem sas = MonoBehaviourSingleton<SpatialAudioSystem>.Instance;
                Transform listener = sas.ListenerTransform;
                if (listener == null) return;

                float dist = __instance.Distance;
                SuperBetterAudioQueue queue = QueueRef(__instance);
                float rolloff = queue != null && queue.AudioSources != null && queue.AudioSources.Length > 0 && queue.AudioSources[0] != null
                    ? queue.AudioSources[0].MaxDistance : float.NaN;
                AudioMixerGroup mixer = MixerRef(__instance);
                bool occludedMixer = mixer != null && Singleton<BetterAudio>.Instantiated && mixer == Singleton<BetterAudio>.Instance.GunshotOccludedMixerGroup;

                float occl = float.NaN, prop = float.NaN, obst = float.NaN;
                string shooterRoom = "";
                int shooterId = bridge.iPlayer != null ? bridge.iPlayer.Id : -1;
                if (shooterId >= 0 && sas.TryGetPlayerAudioContainer(shooterId, out AudioSourceContainer c) && c != null)
                {
                    occl = c.OcclusionEffect; prop = c.PropagationEffect; obst = c.ObstructionEffect;
                    shooterRoom = RoomName(c.CurrentAudioRoom);
                }
                string listenerRoom = RoomName(sas.ListenerCurrentRoom);

                // Straight-line check against the same layers the game's occlusion uses.
                Vector3 from = listener.position, to = __instance.WeaponPosition;
                int mask = sas.OcclusionSettings != null ? sas.OcclusionSettings.commonSettings.occlusionLayerMask.value : 1;
                bool los = !Physics.Linecast(from, to, mask, QueryTriggerInteraction.Ignore);

                Shots++;
                if (!float.IsNaN(rolloff)) Count(RolloffHistogram, Mathf.RoundToInt(rolloff / 50f) * 50);
                if (los) { ShotsWithLos++; if (occludedMixer) ShotsWithLosOccluded++; }
                bool beyond = dist > 250f;
                if (beyond && los) { ShotsBeyond250WithLos++; if (occludedMixer) ShotsBeyond250WithLosOccluded++; }

                DiagLog.Row("shots.csv", string.Join(",",
                    DiagLog.F(now - DiagRunner.RaidStartTime, "0.00"), shooterId.ToString(), DiagLog.F(dist, "0.0"), DiagLog.F(rolloff, "0"), // 0.6.21: raid seconds (was the game clock)
                    __instance.IsSilenced ? "1" : "0", DiagLog.Csv(mixer != null ? mixer.name : ""),
                    DiagLog.F(occl), DiagLog.F(prop), DiagLog.F(obst),
                    DiagLog.Csv(shooterRoom), DiagLog.Csv(listenerRoom), los ? "1" : "0", beyond ? "1" : "0", System.DateTime.Now.ToString("HH:mm:ss.fff")));
            }
            catch (System.Exception e)
            {
                DiagRunner.ReportOnce("shot probe", e);
            }
        }

        private static void Count(SortedDictionary<int, int> h, int k) { h.TryGetValue(k, out int n); h[k] = n + 1; }

        internal static string RoomName(ISpatialAudioRoom r)
        {
            if (r == null) return "null";
            try { return r.IsValid ? (r.Name ?? "?") + (r.IsOutdoor ? "[out]" : "") : "invalid"; }
            catch { return "?"; }
        }
    }
}
