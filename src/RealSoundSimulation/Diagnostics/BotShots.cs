using System;
using System.Collections.Generic;
using System.Text;
using Audio.SpatialSystem;
using EFT;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Audio;

namespace RealSoundSimulation.Diagnostics
{
    // 0.6.21: what happens to each bot gunshot (raid 34, mark 1: "a lot of shots were getting cut off" in a distant fight).
    // From the game's code a remote shot can be lost or shortened three ways:
    //   cut    a gun has two voices that alternate; SuperBetterAudioQueue.Update ends the older one at the exact start of the
    //          next sound (AudioSource.SetScheduledEndTime, no fade), so a semi-auto gun's next shot chops off the last
    //          one's decay. Measured here: when a sound is queued before the same gun's previous sound would have ended.
    //   muted  FireBullet plays nothing if the voice's occlusion volume factor is already 0
    //   noqueue BorrowWeaponAudioQueue found no free voices, so FireBullet plays nothing
    // One row per sound queued or lost (botshots.csv), totals in the summary. Full-auto rounds while the loop runs queue
    // nothing, so they add no rows. Nothing here changes a sound.
    internal static class BotShots
    {
        public const string Header = "t,clock,shooterId,event,clip,lengthS,delayS,dist,occl,occludedMixer,occlVolume,firstBurstSeeded,cutPrevS,cutPrevShare";

        private static readonly AccessTools.FieldRef<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge> BridgeRef =
            AccessTools.FieldRefAccess<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge>("playersBridge");
        private static readonly AccessTools.FieldRef<WeaponSoundPlayer, SuperBetterAudioQueue> QueueRef =
            AccessTools.FieldRefAccess<WeaponSoundPlayer, SuperBetterAudioQueue>("_queue");
        private static readonly AccessTools.FieldRef<WeaponSoundPlayer, AudioMixerGroup> MixerRef =
            AccessTools.FieldRefAccess<WeaponSoundPlayer, AudioMixerGroup>("_currentMixerGroup");
        private static readonly AccessTools.FieldRef<WeaponSoundPlayer, bool> FiringRef =
            AccessTools.FieldRefAccess<WeaponSoundPlayer, bool>("_isFiring");

        private struct Prev { public SuperBetterAudioQueue Queue; public double Start, End, Length; }
        private static readonly Dictionary<int, Prev> Last = new Dictionary<int, Prev>();
        private static readonly Dictionary<int, Prev> Older = new Dictionary<int, Prev>(); // 0.6.23: with 1e on, the sound that can actually be cut is the one before last

        // set for the length of one FireBullet / StopFiringLoop call on a remote gun
        private static WeaponSoundPlayer _cur;
        private static bool _wasFiring, _inStopLoop;
        private static int _queued;

        public static int Bodies, Tails, Muted, NoQueue, Cut;
        public static double CutSum, CutShareSum;

        public static void Apply(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(WeaponSoundPlayer), nameof(WeaponSoundPlayer.FireBullet)),
                prefix: new HarmonyMethod(typeof(BotShots), nameof(FirePrefix)), postfix: new HarmonyMethod(typeof(BotShots), nameof(FirePostfix)));
            h.Patch(AccessTools.Method(typeof(WeaponSoundPlayer), nameof(WeaponSoundPlayer.StopFiringLoop)),
                prefix: new HarmonyMethod(typeof(BotShots), nameof(StopPrefix)), postfix: new HarmonyMethod(typeof(BotShots), nameof(StopPostfix)));
            h.Patch(AccessTools.Method(typeof(SuperBetterAudioQueue), nameof(SuperBetterAudioQueue.Enqueue)),
                postfix: new HarmonyMethod(typeof(BotShots), nameof(EnqueuePostfix)));
        }

        public static void Reset()
        {
            Last.Clear(); Older.Clear(); _cur = null; _queued = 0; _inStopLoop = false;
            Bodies = Tails = Muted = NoQueue = Cut = 0; CutSum = CutShareSum = 0;
        }

        private static bool Remote(WeaponSoundPlayer w)
        {
            BaseSoundPlayer.IObserverToPlayerBridge b = BridgeRef(w);
            return b != null && b.PointOfView != EPointOfView.FirstPerson;
        }

        private static void FirePrefix(WeaponSoundPlayer __instance)
        {
            _cur = null;
            if (!DiagLog.IsOpen) return;
            try { if (Remote(__instance)) { _cur = __instance; _wasFiring = FiringRef(__instance); _queued = 0; _inStopLoop = false; } }
            catch (Exception e) { DiagRunner.ReportOnce("bot shots (fire)", e); }
        }

        private static void FirePostfix(WeaponSoundPlayer __instance)
        {
            WeaponSoundPlayer w = _cur; _cur = null;
            if (w == null || _wasFiring || _queued > 0) return; // a running loop queues nothing; a sound that was queued has its row
            try
            {
                if (__instance.Body == null) return;
                SuperBetterAudioQueue q = QueueRef(__instance);
                if (q == null) { NoQueue++; Row(__instance, "noqueue", null, null, 0f, 0f, -1f, 0f); }
                else if (q.AudioSources[0] != null && q.AudioSources[0].OcclusionVolumeFactor <= 0f) { Muted++; Row(__instance, "muted", null, null, 0f, 0f, -1f, 0f); }
            }
            catch (Exception e) { DiagRunner.ReportOnce("bot shots (fire end)", e); }
        }

        private static void StopPrefix(WeaponSoundPlayer __instance)
        {
            _cur = null;
            if (!DiagLog.IsOpen) return;
            try { if (Remote(__instance)) { _cur = __instance; _queued = 0; _inStopLoop = true; } }
            catch (Exception e) { DiagRunner.ReportOnce("bot shots (stop)", e); }
        }

        private static void StopPostfix() { _cur = null; _inStopLoop = false; }

        private static void EnqueuePostfix(SuperBetterAudioQueue __instance, AudioClip clip1, AudioClip clip2, double startTime, float clipTime, float pitch)
        {
            WeaponSoundPlayer w = _cur;
            if (w == null) return;
            try
            {
                bool tail = _inStopLoop || _queued > 0;
                _queued++;
                if (tail) Tails++; else Bodies++;

                float rawLen = Mathf.Max(clip1 != null ? clip1.length : 0f, clip2 != null ? clip2.length : 0f);
                float len = rawLen / Mathf.Max(pitch, 0.01f);
                int key = w.GetInstanceID();
                float cut = -1f, share = 0f;
                Prev p;
                bool have = Plugin.ShotOverlapOn.Value ? Older.TryGetValue(key, out p) : Last.TryGetValue(key, out p);
                if (have && p.Queue == __instance && startTime >= p.Start && startTime < p.End - 0.01)
                {
                    cut = (float)(p.End - startTime);
                    share = p.Length > 0 ? (float)(cut / p.Length) : 0f;
                    Cut++; CutSum += cut; CutShareSum += share;
                }
                // clipTime 0 = a firing loop: the game ends it on purpose when the tail starts, so it never counts as cut
                if (Last.TryGetValue(key, out Prev recent)) Older[key] = recent; else Older.Remove(key);
                if (clipTime > 0f) Last[key] = new Prev { Queue = __instance, Start = startTime, End = startTime + clipTime, Length = clipTime };
                else Last.Remove(key);

                Row(w, tail ? "tail" : "body", clip1, clip2, len, (float)(startTime - AudioSettings.dspTime), cut, share);
            }
            catch (Exception e) { DiagRunner.ReportOnce("bot shots (queue)", e); }
        }

        private static void Row(WeaponSoundPlayer w, string ev, AudioClip clip1, AudioClip clip2, float length, float delay, float cut, float share)
        {
            if (!DiagLog.IsOpen) return;
            BaseSoundPlayer.IObserverToPlayerBridge b = BridgeRef(w);
            int id = b != null && b.iPlayer != null ? b.iPlayer.Id : -1;
            float occl = float.NaN;
            if (id >= 0 && MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated
                && MonoBehaviourSingleton<SpatialAudioSystem>.Instance.TryGetPlayerAudioContainer(id, out AudioSourceContainer c) && c != null) occl = c.OcclusionEffect;
            AudioMixerGroup mixer = MixerRef(w);
            bool occluded = mixer != null && MonoBehaviourSingleton<BetterAudio>.Instantiated && mixer == MonoBehaviourSingleton<BetterAudio>.Instance.GunshotOccludedMixerGroup;
            SuperBetterAudioQueue q = QueueRef(w);
            float vol = q != null && q.AudioSources[0] != null ? q.AudioSources[0].OcclusionVolumeFactor : float.NaN;
            bool seeded = Fixes.FirstBurstOcclusion.TakeSeeded(w.GetInstanceID());
            AudioClip clip = clip1 != null ? clip1 : clip2;

            StringBuilder sb = DiagLog.Begin();
            DiagLog.AppendF(sb, Time.time - DiagRunner.RaidStartTime, 2); sb.Append(',');
            BatchWait.AppendClock(sb, DateTime.Now); sb.Append(',');
            DiagLog.AppendI(sb, id); sb.Append(',');
            sb.Append(ev); sb.Append(',');
            if (clip != null) DiagLog.AppendS(sb, clip.name); sb.Append(',');
            DiagLog.AppendF(sb, length, 2); sb.Append(',');
            DiagLog.AppendF(sb, delay, 3); sb.Append(',');
            DiagLog.AppendF(sb, w.Distance, 1); sb.Append(',');
            DiagLog.AppendF(sb, occl, 3); sb.Append(',');
            DiagLog.AppendB(sb, occluded); sb.Append(',');
            DiagLog.AppendF(sb, vol, 3); sb.Append(',');
            DiagLog.AppendB(sb, seeded); sb.Append(',');
            if (cut >= 0f) { DiagLog.AppendF(sb, cut, 3); sb.Append(','); DiagLog.AppendF(sb, share, 2); } else sb.Append(',');
            DiagLog.EndRow("botshots.csv");
        }

        public static string Summary()
        {
            int played = Bodies + Tails;
            var sb = new StringBuilder();
            sb.AppendLine("BOT GUNSHOTS (botshots.csv; one row per sound a bot's gun queued or lost)");
            sb.AppendLine($"  sounds queued {played} (shots and loops {Bodies}, tails {Tails}); not played at all: occlusion volume 0 {Muted}, no free voices {NoQueue}");
            sb.AppendLine(Cut > 0
                ? $"  cut off by the same gun's next sound{(Plugin.ShotOverlapOn.Value ? " (1e on: only a third sound taking back the oldest voice)" : "")}: {Cut} ({100.0 * Cut / Math.Max(played, 1):0}% of queued), average {CutSum / Cut:0.00} s lost, {100.0 * CutShareSum / Cut:0}% of the sound"
                : "  cut off by the same gun's next sound: 0");
            sb.AppendLine($"  gun voices taken from the gun closest to finishing because the pool was empty (1e): {Fixes.ShotOverlap.Taken}; pool empty and no gun could give any: {Fixes.ShotOverlap.TakenNone}");
            sb.AppendLine($"  first bursts started muffled (1d): {Fixes.FirstBurstOcclusion.Seeded}; shooter in clear line of sight, left alone: {Fixes.FirstBurstOcclusion.ClearLine}");
            return sb.ToString();
        }
    }
}
