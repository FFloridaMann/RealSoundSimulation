using System;
using System.Collections.Generic;
using RealSoundSimulation.Diagnostics;
using Audio.SpatialSystem;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace RealSoundSimulation.Fixes
{
    // 1e and 1f: a gun's shots ring out instead of chopping each other. History and raid evidence: docs/dev/FIXES.md (1e, 1f).
    //   1e   SuperBetterAudioQueue.Update with one change: only a firing loop is ended when the next sound starts. If the game's gun pool
    //        is empty, the gun closest to finishing (not firing, never yours) gives up its voices so the new shot still plays.
    //   1f   a voice whose tail is still ringing is taken out of the gun's pair and left to finish where it is, and a spare voice
    //        from the game's gun pool takes its place (BorrowGunVoice sets it up like the ringing one). Your own gun too ("Tails - your
    //        shots ring out"): its spare is set up the first-person way, and a tail ringing out keeps following the gun. At most
    //        MaxRingingOut of other guns' tails and MaxOwnRinging of yours ring on their own. Past that, or with under MinLeftS
    //        left, the tail is still moved off the pair but fades out over CutFadeS instead of being cut dead (tailcuts.csv).
    // A gun's queue must keep exactly two voices: game code assumes it. Giving your own gun four (13d4965) dropped shots and was reverted.
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
        private const int MaxRingingOut = 8, MaxOwnRinging = 12;
        private const double CutFadeS = 0.06;
        private struct Fading { public SuperSource Src; public double Start; public float Base1, Base2; }
        private static readonly List<Fading> FadingOut = new List<Fading>();
        public static int Faded, OwnFaded, OwnFadedEnding;
        public const string CutsHeader = "t,gun,what,ringingBefore,leftS";

        private struct Ringing { public BetterSource Src; public double Until; public WeaponSoundPlayer OwnGun; }
        private static readonly Dictionary<SuperBetterAudioQueue, WeaponSoundPlayer> OwnerOf = new Dictionary<SuperBetterAudioQueue, WeaponSoundPlayer>();
        private static readonly Dictionary<BetterSource, double> EndOf = new Dictionary<BetterSource, double>();
        private static readonly List<Ringing> RingingOut = new List<Ringing>();
        public static int HandedOff, NoSpare, MaxRinging, PoolEmpty, PoolMin = int.MaxValue, OwnHandedOff, OwnNoSpare;
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
            HandedOff = NoSpare = MaxRinging = PoolEmpty = OwnHandedOff = OwnNoSpare = Faded = OwnFaded = OwnFadedEnding = 0; PoolMin = int.MaxValue; PoolSum = 0;
            foreach (Fading f in FadingOut) StopAndFree(f.Src);
            FadingOut.Clear();
        }

        // Called every frame in raid: a tail that has rung out gives its voice back to the game.
        public static void Tick()
        {
            if (FadingOut.Count > 0) TickFades();
            if (RingingOut.Count == 0) return;
            double now = AudioSettings.dspTime;
            for (int i = RingingOut.Count - 1; i >= 0; i--)
            {
                if (now < RingingOut[i].Until)
                {
                    // your gun's tail goes where your gun goes, as the voices still in its pair do (the game poses those every frame)
                    WeaponSoundPlayer g = RingingOut[i].OwnGun;
                    SuperBetterAudioQueue oq = g != null ? QueueRef(g) : null; // null once your gun has given its voices back
                    if (oq != null && RingingOut[i].Src != null && oq.AudioSources != null && oq.AudioSources[0] != null)
                        RingingOut[i].Src.transform.position = oq.AudioSources[0].transform.position;
                    continue;
                }
                Free(RingingOut[i].Src);
                RingingOut.RemoveAt(i);
            }
        }

        // A tail moved off past a limit: quiet over CutFadeS (a curve, so it sounds like a short release, not a step), then stopped and given back.
        private static void TickFades()
        {
            double now = AudioSettings.dspTime;
            for (int i = FadingOut.Count - 1; i >= 0; i--)
            {
                Fading f = FadingOut[i];
                float left = (float)(1.0 - (now - f.Start) / CutFadeS);
                if (left > 0f && f.Src != null && f.Src.source1 != null)
                {
                    float k = left * left;
                    f.Src.source1.volume = f.Base1 * k;
                    if (f.Src.source2 != null) f.Src.source2.volume = f.Base2 * k;
                    continue;
                }
                StopAndFree(f.Src);
                FadingOut.RemoveAt(i);
            }
        }

        private static void StopAndFree(SuperSource s)
        {
            if (s == null) return;
            try { if (s.source1 != null) s.source1.Stop(); if (s.source2 != null) s.source2.Stop(); }
            catch (Exception e) { DiagRunner.ReportOnce("1f tail fade (stop)", e); }
            Free(s);
        }

        private static int RingingCount(bool own)
        {
            int n = 0;
            foreach (Ringing r in RingingOut) if ((r.OwnGun != null) == own) n++;
            return n;
        }

        private static void CutRow(bool own, string what, int before, double left)
        {
            if (!DiagLog.IsOpen) return;
            DiagLog.Row("tailcuts.csv", string.Join(",", DiagLog.F(Time.time - DiagRunner.RaidStartTime, "0.00"), own ? "yours" : "other", what,
                before.ToString(), DiagLog.F((float)left, "0.00")));
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
            double left = end - Math.Max(newStart, AudioSettings.dspTime);
            if (left <= 0.0) return null; // already finished: nothing to cut
            if (!OwnerOf.TryGetValue(q, out WeaponSoundPlayer w) || w == null) return null;
            BaseSoundPlayer.IObserverToPlayerBridge b = BridgeRef(w);
            if (b == null) return null;
            bool own = b.PointOfView == EPointOfView.FirstPerson;
            if (own && !Plugin.OwnRingOutOn.Value) return null;
            if (!MonoBehaviourSingleton<BetterAudio>.Instantiated) return null;
            BetterAudio ba = MonoBehaviourSingleton<BetterAudio>.Instance;
            var pool = ba.SourceGroups != null ? ba.SourceGroups[(int)BetterAudio.AudioSourceGroupType.Gunshots] as BetterAudio.NonspatialAudioMixer<Audio.ReverbSubsystem.ReverbSuperSource> : null;
            if (pool == null) { if (own) OwnNoSpare++; else NoSpare++; return null; }
            int before = RingingCount(own);
            bool ending = left < MinLeftS, full = before >= (own ? MaxOwnRinging : MaxRingingOut);
            int spare0 = pool.stack_0.Count; // for the summary: how many spare voices the game had at this moment
            PoolSum += spare0; if (spare0 < PoolMin) PoolMin = spare0; if (spare0 == 0) PoolEmpty++;
            SuperSource spare = BorrowGunVoice(pool, ringing, own, b);
            if (spare == null) return null;
            q.AudioSources[idx] = spare;
            EndOf.Remove(ringing);
            if (ending || full)
            {
                FadingOut.Add(new Fading { Src = ringing, Start = AudioSettings.dspTime, Base1 = ringing.source1 != null ? ringing.source1.volume : 0f,
                                           Base2 = ringing.source2 != null ? ringing.source2.volume : 0f });
                if (own) { if (full) OwnFaded++; else OwnFadedEnding++; } else Faded++;
                CutRow(own, full ? "faded at the limit" : "faded as it ended", before, left);
                return spare;
            }
            RingingOut.Add(new Ringing { Src = ringing, Until = end + FreeAfterS, OwnGun = own ? w : null });
            if (own) CutRow(true, "rang out", before, left);
            if (RingingOut.Count > MaxRinging) MaxRinging = RingingOut.Count;
            if (own) OwnHandedOff++; else HandedOff++;
            return spare;
        }

        // A spare voice from the game's gun pool, set up as the ringing voice it replaces in a gun's pair. The game sets the rest
        // on the next shot (FireBullet: mixer group, stereo, rolloff; the sample: clips, volumes, pitch). Two ways, as the game does:
        //   others   placed in the world and in the occlusion process (WeaponSoundPlayer.RegisterSourceForOcclusionProcess)
        //   yours    first person: stereo, never occluded, and the room reverb sends your shot already had (3c, 5)
        private static SuperSource BorrowGunVoice(BetterAudio.NonspatialAudioMixer<Audio.ReverbSubsystem.ReverbSuperSource> pool, SuperSource like, bool own,
                                                  BaseSoundPlayer.IObserverToPlayerBridge b)
        {
            var spare = pool.BorrowSource() as SuperSource;
            if (spare == null) return null;
            spare.transform.position = like.transform.position;
            spare.SetRolloff(like.MaxDistance);
            spare.SetPriority(like.source1.priority);
            spare.SetMixerGroup(like.source1.outputAudioMixerGroup);
            if (own)
            {
                spare.EnableStereo(true);
                spare.IncludeInOcclusionProcess(false);
                spare.ResetOcclusion();
                if (like is Audio.ReverbSubsystem.ReverbSuperSource from && spare is Audio.ReverbSubsystem.ReverbSuperSource to)
                {
                    CopySends(from._spatializerA, to._spatializerA);
                    CopySends(from._spatializerB, to._spatializerB);
                }
                return spare;
            }
            spare.EnableStereo(false);
            spare.IncludeInOcclusionProcess(true);
            if (MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated && b.iPlayer != null)
                MonoBehaviourSingleton<SpatialAudioSystem>.Instance.ProcessSourceOcclusion(b.iPlayer, spare);
            return spare;
        }

        private static void CopySends(Audio.SpatialSystem.BaseSpatialAudioSource from, Audio.SpatialSystem.BaseSpatialAudioSource to)
        {
            if (from == null || to == null) return;
            to.ReverbSendDB = from.ReverbSendDB;
            to.EarlyReflectionsSendDB = from.EarlyReflectionsSendDB;
            to.UpdateParameters();
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
