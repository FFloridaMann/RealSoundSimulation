using System.Collections.Generic;
using Audio.SpatialSystem;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Audio;

namespace RealSoundSimulation.Fixes
{
    // Fix 1: gun mixer hysteresis (solutions doc 2.3).
    // The game picks the muffled "Occluded" gun mixer whenever occlusion > 0.55, re-checked on every shot. Raid 2 showed a
    // bot 40 m away flipping 5 times in 27 s. Here a shot only goes muffled above "Muffle above" and only comes back below
    // "Unmuffle below", and a change has to hold for "Hold s" before it is applied. The first shot after a pause uses the
    // game's 0.55 rule straight away, so a fresh engagement is never delayed.
    internal static class GunMixerFix
    {
        private static readonly AccessTools.FieldRef<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge> BridgeRef =
            AccessTools.FieldRefAccess<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge>("playersBridge");
        private static readonly AccessTools.FieldRef<WeaponSoundPlayer, SuperBetterAudioQueue> QueueRef =
            AccessTools.FieldRefAccess<WeaponSoundPlayer, SuperBetterAudioQueue>("_queue");
        private static readonly AccessTools.FieldRef<WeaponSoundPlayer, AudioMixerGroup> MixerRef =
            AccessTools.FieldRefAccess<WeaponSoundPlayer, AudioMixerGroup>("_currentMixerGroup");

        private const float GameThreshold = 0.55f;
        private const float FreshAfterSeconds = 2f;

        private static readonly Dictionary<int, float> LastCall = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> PendingSince = new Dictionary<int, float>();

        public static int HeldBack; // contrary readings that did not switch (for the summary)

        public static void Apply(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(WeaponSoundPlayer), nameof(WeaponSoundPlayer.UpdateMixerGroup)),
                prefix: new HarmonyMethod(typeof(GunMixerFix), nameof(Prefix)));
        }

        public static void Reset()
        {
            LastCall.Clear();
            PendingSince.Clear();
            HeldBack = 0;
        }

        private static bool Prefix(WeaponSoundPlayer __instance)
        {
            if (!Plugin.GunMixerOn.Value) return true;
            try
            {
                BaseSoundPlayer.IObserverToPlayerBridge bridge = BridgeRef(__instance);
                SuperBetterAudioQueue queue = QueueRef(__instance);
                // Same guards as the original; it does nothing in these cases either.
                if (bridge == null || bridge.PointOfView == EPointOfView.FirstPerson || queue == null) return false;
                if (!MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated || !MonoBehaviourSingleton<BetterAudio>.Instantiated) return true;
                if (!MonoBehaviourSingleton<SpatialAudioSystem>.Instance.TryGetPlayerAudioContainer(bridge.iPlayer.Id, out AudioSourceContainer c) || c == null) return false;

                BetterAudio ba = MonoBehaviourSingleton<BetterAudio>.Instance;
                AudioMixerGroup normal = ba.GunshotMixerGroup, occluded = ba.GunshotOccludedMixerGroup;
                AudioMixerGroup current = MixerRef(__instance);
                float effect = c.OcclusionEffect;
                int id = __instance.GetInstanceID();
                float now = Time.time;

                bool fresh = current == null || !LastCall.TryGetValue(id, out float last) || now - last > FreshAfterSeconds;
                LastCall[id] = now;

                AudioMixerGroup target;
                if (fresh)
                {
                    target = effect > GameThreshold ? occluded : normal;
                    PendingSince.Remove(id);
                }
                else
                {
                    bool curOccluded = current == occluded;
                    bool wantOccluded = curOccluded ? effect > Plugin.GunUnmuffleBelow.Value : effect > Plugin.GunMuffleAbove.Value;
                    if (wantOccluded == curOccluded)
                    {
                        PendingSince.Remove(id);
                        return false;
                    }
                    if (!PendingSince.TryGetValue(id, out float since)) { since = now; PendingSince[id] = now; }
                    if (now - since < Plugin.GunHoldSeconds.Value) { HeldBack++; return false; }
                    target = wantOccluded ? occluded : normal;
                    PendingSince.Remove(id);
                }

                if (current != target)
                {
                    MixerRef(__instance) = target;
                    queue.SetMixerGroup(target);
                }
                return false;
            }
            catch (System.Exception e)
            {
                Diagnostics.DiagRunner.ReportOnce("fix 1 gun mixer", e);
                return true;
            }
        }
    }
}
