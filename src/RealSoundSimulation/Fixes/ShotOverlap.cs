using System;
using System.Collections.Generic;
using RealSoundSimulation.Diagnostics;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace RealSoundSimulation.Fixes
{
    // 1e (0.6.23): each shot rings out under the next one. A gun has two voices that alternate, and the game ends the older one
    // at the exact start of the next sound with no fade, so a semi-auto gun's next shot chops the last one's decay and a new
    // burst chops the last burst's tail (raid 36: 89 of 224 bot sounds, on average 1.85 s lost; the STM-9 on the FRT
    // "sounds like a looping single shot" for the same reason). This is SuperBetterAudioQueue.Update with one change: only a
    // firing loop is ended when the next sound starts. Ported from the old Gun Audio mod (removed 5 Nov), without its random
    // pitch and volume. A gun still has two voices, so a third sound takes back the oldest one.
    //
    // Holding a voice a little longer could leave the game's gun pool empty, and FireBullet then plays nothing at all. So if
    // a gun finds no free voices, the gun closest to finishing (not firing, never yours) gives up its two: a fading tail is
    // cut rather than a shot going silent. Only runs when the pool is actually empty.
    internal static class ShotOverlap
    {
        private static readonly AccessTools.FieldRef<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge> BridgeRef =
            AccessTools.FieldRefAccess<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge>("playersBridge");
        private static readonly AccessTools.FieldRef<WeaponSoundPlayer, SuperBetterAudioQueue> QueueRef =
            AccessTools.FieldRefAccess<WeaponSoundPlayer, SuperBetterAudioQueue>("_queue");
        private static readonly AccessTools.FieldRef<WeaponSoundPlayer, bool> FiringRef =
            AccessTools.FieldRefAccess<WeaponSoundPlayer, bool>("_isFiring");
        private static readonly AccessTools.FieldRef<WeaponSoundPlayer, double> ReleaseTimeRef =
            AccessTools.FieldRefAccess<WeaponSoundPlayer, double>("_releaseTime");
        private static readonly AccessTools.FieldRef<WeaponSoundPlayer, double> StartFireRef =
            AccessTools.FieldRefAccess<WeaponSoundPlayer, double>("_startFire");
        private static readonly AccessTools.FieldRef<WeaponSoundPlayer, float> DelayRef =
            AccessTools.FieldRefAccess<WeaponSoundPlayer, float>("_delay");

        private const double ReleaseMargin = 1.0;

        private static readonly HashSet<WeaponSoundPlayer> Holders = new HashSet<WeaponSoundPlayer>();
        private static bool _stealing;

        public static int Taken, TakenNone; // voices taken from another gun / the pool was empty and no gun could give any

        public static void Apply(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(SuperBetterAudioQueue), nameof(SuperBetterAudioQueue.Update)),
                prefix: new HarmonyMethod(typeof(ShotOverlap), nameof(UpdatePrefix)));
            h.Patch(AccessTools.Method(typeof(WeaponSoundPlayer), nameof(WeaponSoundPlayer.FireBullet)),
                postfix: new HarmonyMethod(typeof(ShotOverlap), nameof(FirePostfix)));
            h.Patch(AccessTools.Method(typeof(BetterAudio), nameof(BetterAudio.BorrowWeaponAudioQueue)),
                postfix: new HarmonyMethod(typeof(ShotOverlap), nameof(BorrowPostfix)));
        }

        public static void Reset() { Holders.Clear(); Taken = TakenNone = 0; _stealing = false; }

        private static bool UpdatePrefix(SuperBetterAudioQueue __instance)
        {
            if (!Plugin.ShotOverlapOn.Value) return true;
            if (__instance._nextUpdate > AudioSettings.dspTime || __instance._samples.Count < 1) return false;

            bool taken = false;
            try
            {
                SuperAudioSample sample = __instance._samples.Dequeue();
                taken = true;
                bool looping = sample.End == 0.0;
                BetterSource[] sources = __instance.AudioSources;
                if (!sources[__instance._lastSource].source1.isPlaying) __instance._lastSource = __instance._lastSource == 0 ? 1 : 0;
                var previous = (SuperSource)sources[__instance._lastSource];
                __instance._lastSource = __instance._lastSource == 0 ? 1 : 0;
                var next = (SuperSource)sources[__instance._lastSource];

                next.Loop = looping;
                if (previous.Loop) previous.SetScheduledEndTime(sample.Start); // the one change: anything but a loop rings on
                sample.PlayOn(next);
                __instance._nextUpdate = sample.Start;

                if (!looping) next.SetScheduledEndTime(sample.End);
                else if (__instance._samples.Count >= 1)
                {
                    SuperAudioSample following = __instance._samples.Peek();
                    if (following != null) next.SetScheduledEndTime(following.Start);
                }
                return false;
            }
            catch (Exception e)
            {
                DiagRunner.ReportOnce("1e shot overlap", e);
                return !taken; // nothing dequeued yet: let the game's own version run
            }
        }

        private static void FirePostfix(WeaponSoundPlayer __instance)
        {
            if (!Plugin.ShotOverlapOn.Value) return;
            try
            {
                if (QueueRef(__instance) == null) return;
                Holders.Add(__instance);
                // The game keeps a non-auto gun's voices 1 s past the recording but doesn't count the sound's flight time, so at
                // range a ringing shot could be released before it finishes.
                if (__instance.Non_auto)
                {
                    SoundBank bank = __instance.IsSilenced ? __instance.BodySilenced : __instance.Body;
                    if (bank == null) return;
                    double end = StartFireRef(__instance) + DelayRef(__instance) + bank.ClipLength + ReleaseMargin;
                    if (end > ReleaseTimeRef(__instance)) ReleaseTimeRef(__instance) = end;
                }
            }
            catch (Exception e) { DiagRunner.ReportOnce("1e shot overlap (fire)", e); }
        }

        // The pool is empty. The gun closest to finishing gives up its voices so this shot still plays.
        private static void BorrowPostfix(BetterAudio __instance, BetterAudio.AudioSourceGroupType groupType, ref SuperBetterAudioQueue __result)
        {
            if (__result != null || _stealing || groupType != BetterAudio.AudioSourceGroupType.Gunshots || !Plugin.ShotOverlapOn.Value) return;
            try
            {
                WeaponSoundPlayer victim = null;
                double soonest = double.MaxValue;
                List<WeaponSoundPlayer> dead = null;
                foreach (WeaponSoundPlayer w in Holders)
                {
                    if (w == null || QueueRef(w) == null) { (dead ?? (dead = new List<WeaponSoundPlayer>())).Add(w); continue; }
                    if (FiringRef(w)) continue;
                    BaseSoundPlayer.IObserverToPlayerBridge b = BridgeRef(w);
                    if (b == null || b.PointOfView == EPointOfView.FirstPerson) continue; // never your own gun
                    double t = ReleaseTimeRef(w);
                    if (t < soonest) { soonest = t; victim = w; }
                }
                if (dead != null) foreach (WeaponSoundPlayer w in dead) Holders.Remove(w);
                if (victim == null) { TakenNone++; return; }

                victim.Release(0.0);
                Holders.Remove(victim);
                _stealing = true;
                __result = __instance.BorrowWeaponAudioQueue(groupType);
                if (__result != null) Taken++; else TakenNone++;
            }
            catch (Exception e) { DiagRunner.ReportOnce("1e shot overlap (voices)", e); }
            finally { _stealing = false; }
        }
    }
}
