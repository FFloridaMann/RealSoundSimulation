using System;
using System.Collections.Generic;
using RealSoundSimulation.Diagnostics;
using Audio.SpatialSystem;
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
    //
    // 0.7.12 "1f": a ringing tail rings out on a spare voice. A gun still has only two voices, and an automatic gun fired in single shots
    // or short bursts uses both for every shot (the bang and its tail), so the next shot took back a voice whose tail was still ringing
    // (Fika test raid, tag 1: an AK's 2 s tail cut after 0.23 s; 23-64% of bot sounds in earlier raids). Now, for anyone else's gun,
    // the ringing voice is taken out of the gun's pair and left to finish where it is, and a spare voice from the game's own gun pool
    // takes its place. The tail keeps everything the game gave it: its position, occlusion, mixer group and our route muffling.
    // 0.7.18: no reserve. 0.7.12 left at least 6 spare voices for new guns and so ran once in a raid (raid_20261110_221827: 1 handed off,
    // 42 skipped, 51% of bot sounds still cut). The game's pool never runs dry: BorrowSource makes a new voice when it's empty, as it
    // does in any big fight. Instead at most MaxRingingOut tails ring on their own at once; past that a tail is cut as before.
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
        private const double MinLeftS = 0.15, FreeAfterS = 0.1;
        private const int MaxRingingOut = 8;

        private struct Ringing { public BetterSource Src; public double Until; }
        private static readonly Dictionary<SuperBetterAudioQueue, WeaponSoundPlayer> OwnerOf = new Dictionary<SuperBetterAudioQueue, WeaponSoundPlayer>();
        private static readonly Dictionary<BetterSource, double> EndOf = new Dictionary<BetterSource, double>();
        private static readonly List<Ringing> RingingOut = new List<Ringing>();
        public static int HandedOff, NoSpare, MaxRinging, PoolEmpty, PoolMin = int.MaxValue;
        public static long PoolSum;

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

        public static void Reset()
        {
            Holders.Clear(); Taken = TakenNone = 0; _stealing = false;
            foreach (Ringing r in RingingOut) Free(r.Src);
            RingingOut.Clear(); OwnerOf.Clear(); EndOf.Clear();
            HandedOff = NoSpare = MaxRinging = PoolEmpty = 0; PoolMin = int.MaxValue; PoolSum = 0;
        }

        // Called every frame in raid: a tail that has rung out gives its voice back to the game.
        public static void Tick()
        {
            if (RingingOut.Count == 0) return;
            double now = AudioSettings.dspTime;
            for (int i = RingingOut.Count - 1; i >= 0; i--)
            {
                if (now < RingingOut[i].Until) continue;
                Free(RingingOut[i].Src);
                RingingOut.RemoveAt(i);
            }
        }

        private static void Free(BetterSource s)
        {
            try
            {
                if (s == null) return; // destroyed with the raid
                PortalPositioning.FilterOff(s);
                s.Release();
            }
            catch (Exception e) { DiagRunner.ReportOnce("1f tail hand-off (free)", e); }
        }

        // "1f": the voice about to be reused is still ringing a tail with time left. Swap a spare voice in for it and let it finish.
        private static SuperSource HandOff(SuperBetterAudioQueue q, int idx, SuperSource ringing, double newStart)
        {
            if (!EndOf.TryGetValue(ringing, out double end)) return null;
            if (end - Math.Max(newStart, AudioSettings.dspTime) < MinLeftS) return null;
            if (!OwnerOf.TryGetValue(q, out WeaponSoundPlayer w) || w == null) return null;
            BaseSoundPlayer.IObserverToPlayerBridge b = BridgeRef(w);
            if (b == null || b.PointOfView == EPointOfView.FirstPerson) return null; // your own gun keeps its two voices
            if (!MonoBehaviourSingleton<BetterAudio>.Instantiated) return null;
            BetterAudio ba = MonoBehaviourSingleton<BetterAudio>.Instance;
            var pool = ba.SourceGroups != null ? ba.SourceGroups[(int)BetterAudio.AudioSourceGroupType.Gunshots] as BetterAudio.NonspatialAudioMixer<Audio.ReverbSubsystem.ReverbSuperSource> : null;
            if (pool == null || RingingOut.Count >= MaxRingingOut) { NoSpare++; return null; }
            int spare0 = pool.stack_0.Count; // for the summary: how many spare voices the game had at this moment
            PoolSum += spare0; if (spare0 < PoolMin) PoolMin = spare0; if (spare0 == 0) PoolEmpty++;
            var spare = pool.BorrowSource() as SuperSource;
            if (spare == null) return null;
            spare.transform.position = ringing.transform.position;
            spare.SetRolloff(ringing.MaxDistance);
            spare.SetPriority(ringing.source1.priority);
            spare.SetMixerGroup(ringing.source1.outputAudioMixerGroup);
            spare.EnableStereo(false);
            spare.IncludeInOcclusionProcess(true);
            if (MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated && b.iPlayer != null)
                MonoBehaviourSingleton<SpatialAudioSystem>.Instance.ProcessSourceOcclusion(b.iPlayer, spare);
            q.AudioSources[idx] = spare;
            EndOf.Remove(ringing);
            RingingOut.Add(new Ringing { Src = ringing, Until = end + FreeAfterS });
            if (RingingOut.Count > MaxRinging) MaxRinging = RingingOut.Count;
            HandedOff++;
            return spare;
        }

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
                if (Plugin.TailHandoffOn.Value && !next.Loop && next.source1 != null && next.source1.isPlaying)
                {
                    SuperSource spare = HandOff(__instance, __instance._lastSource, next, sample.Start);
                    if (spare != null) next = spare;
                }

                next.Loop = looping;
                if (previous.Loop) previous.SetScheduledEndTime(sample.Start); // the one change: anything but a loop rings on
                sample.PlayOn(next);
                __instance._nextUpdate = sample.Start;

                if (!looping) { next.SetScheduledEndTime(sample.End); EndOf[next] = sample.End; }
                else EndOf.Remove(next);
                if (looping && __instance._samples.Count >= 1)
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
                SuperBetterAudioQueue q = QueueRef(__instance);
                if (q == null) return;
                Holders.Add(__instance);
                OwnerOf[q] = __instance; // 1f: which gun a queue belongs to (queues are pooled and change hands)
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
