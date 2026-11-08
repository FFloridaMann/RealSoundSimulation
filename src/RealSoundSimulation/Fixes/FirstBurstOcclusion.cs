using System;
using System.Collections.Generic;
using Audio.SpatialSystem;
using Audio.AuxiliaryAudioUtils;
using RealSoundSimulation.Diagnostics;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace RealSoundSimulation.Fixes
{
    // 1d (0.6.21): a bot's first burst starts muffled if it is behind cover. Raid 34: a quiet bot beyond its own hearing range
    // (~80 m, set by its footsteps) has a processor marked OutOfRange, so the game never computes its occlusion and the value
    // stays 0. Firing raises the range to 600 m and the calculation starts, but it runs over several frames and is
    // smoothed, so the first rounds played un-muffled on the open gun mixer and were then cut down (container 9: 0, then
    // 0.68, then 0.997). Fix 1's "fresh shot" rule read that 0 too and its hold kept the open mixer for another 0.5 s.
    //
    // The real calculation can't be finished inside the call (the obstruction checks are a job chain over several frames).
    // So: only when this gun's voices are registered and the shooter's occlusion was never computed, one line-of-sight
    // test; blocked = start the smoothed value at "blocked" (propagation if the shooter is in another room, obstruction if
    // in yours) and let the game's own result take over from there. Clear line of sight = nothing changes.
    // Later bursts keep the last computed value, so this only touches a bot's first burst.
    internal static class FirstBurstOcclusion
    {
        private const float ComputedEps = 0.001f;

        private static readonly AccessTools.FieldRef<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge> BridgeRef =
            AccessTools.FieldRefAccess<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge>("playersBridge");
        private static readonly AccessTools.FieldRef<SpatialAudioSystem, Dictionary<int, SoundOcclusionProcessor>> ProcessorsRef =
            AccessTools.FieldRefAccess<SpatialAudioSystem, Dictionary<int, SoundOcclusionProcessor>>("_registeredPlayerProcessors");

        public static int Seeded, ClearLine;
        private static int _seededWeapon;

        public static void Apply(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(WeaponSoundPlayer), nameof(WeaponSoundPlayer.RegisterSourceForOcclusionProcess)),
                prefix: new HarmonyMethod(typeof(FirstBurstOcclusion), nameof(Prefix)), postfix: new HarmonyMethod(typeof(FirstBurstOcclusion), nameof(Postfix)));
        }

        public static void Reset() { Seeded = ClearLine = 0; _seededWeapon = 0; }

        // For botshots.csv: was this gun's next row the one this fix just started muffled?
        public static bool TakeSeeded(int weapon)
        {
            if (_seededWeapon != weapon || weapon == 0) return false;
            _seededWeapon = 0;
            return true;
        }

        // Before the gun's voices are added: their 600 m range takes the processor out of OutOfRange.
        private static void Prefix(WeaponSoundPlayer __instance, out bool __state)
        {
            __state = false;
            if (!Plugin.FirstBurstOn.Value) return;
            try
            {
                BaseSoundPlayer.IObserverToPlayerBridge b = BridgeRef(__instance);
                if (b == null || b.iPlayer == null || b.PointOfView == EPointOfView.FirstPerson) return;
                if (!MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated) return;
                SpatialAudioSystem sas = MonoBehaviourSingleton<SpatialAudioSystem>.Instance;
                int id = b.iPlayer.Id;
                if (!ProcessorsRef(sas).TryGetValue(id, out SoundOcclusionProcessor p) || p == null) { __state = true; return; } // the player's very first sound
                __state = p.Priority == EAudioSourcePriority.OutOfRange && sas.TryGetPlayerAudioContainer(id, out AudioSourceContainer c) && c != null
                          && c.OcclusionEffect <= ComputedEps && c.PropagationEffect <= ComputedEps && c.ObstructionEffect <= ComputedEps;
            }
            catch (Exception e) { DiagRunner.ReportOnce("1d first burst (before)", e); }
        }

        private static void Postfix(WeaponSoundPlayer __instance, bool __state)
        {
            if (!__state) return;
            try
            {
                BaseSoundPlayer.IObserverToPlayerBridge b = BridgeRef(__instance);
                SpatialAudioSystem sas = MonoBehaviourSingleton<SpatialAudioSystem>.Instance;
                int id = b.iPlayer.Id;
                if (!ProcessorsRef(sas).TryGetValue(id, out SoundOcclusionProcessor p) || p == null || p.Priority == EAudioSourcePriority.OutOfRange) return;
                if (!(p._occluder is CombinedSpatialAudioOccluder occ) || !sas.TryGetPlayerAudioContainer(id, out AudioSourceContainer c) || c == null) return;
                Transform listener = sas.ListenerTransform;
                if (listener == null || sas.OcclusionSettings == null) return;

                int mask = sas.OcclusionSettings.commonSettings.occlusionLayerMask.value;
                if (!Physics.Linecast(listener.position, __instance.WeaponPosition, mask, QueryTriggerInteraction.Ignore)) { ClearLine++; return; }

                if (sas.IsSourceInListenerRoom(c)) occ._smoothedObstructionEffect = 1f;
                else occ._smoothedPropagationEffect = 1f;
                p.ForceUpdate();                                  // start the real calculation now
                p.UpdateOcclusionEffects(updateImmediately: true); // container, voices and mixer choice see the new value
                Seeded++;
                _seededWeapon = __instance.GetInstanceID();
            }
            catch (Exception e) { DiagRunner.ReportOnce("1d first burst", e); }
        }
    }
}
