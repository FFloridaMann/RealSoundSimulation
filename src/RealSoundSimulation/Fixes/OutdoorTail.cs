using System;
using System.Collections.Generic;
using Audio.SpatialSystem;
using RealSoundSimulation.Acoustics;
using RealSoundSimulation.Diagnostics;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace RealSoundSimulation.Fixes
{
    // 0.6.11, "3m Outdoor tail from the opening" (raid 29 tag 1: barrel out of a window, the whole shot sounded indoor;
    // user: "inside shot, outside tail"). Adds, never replaces: fix 3c's indoor/outdoor body mix stays as it is, and the
    // gun's OUTDOOR tail recording also plays from the opening the shot goes out of, a beat later.
    //
    //   when      your own single shots (the shot's own recording) and the tail at the end of a burst - every trigger
    //             pull of a gun that can fire full-auto is a loop + tail, so those are covered through the tail sample.
    //             Never on the loop. Only while 3c mixes the shot mostly indoor (weight after 3j > 0.5) and the aim cone
    //             is not fully closed.
    //   clip      the gun's Tail bank (TailSilenced when suppressed; read at fire time, so setting 0's swap applies),
    //             outdoor set, close tier. Guns without a Tail bank get none (logged).
    //   from      the aim cone, forward ray first: the first ray that leaves cover (0.6.8 exit test) before the wall it
    //             hits. Glass or bars on the way -> the pane/grate it passes (through glass: 9d loss, 1.5 kHz; bars:
    //             -1 dB, 12 kHz); else the exit point. None found but the cone is open -> 3 m along the aim.
    //   level     shot volume x open share (1 - aim closure) x indoor weight x through-loss x "3l".
    //   delay     distance to that point / 343 m/s.
    // One per trigger pull, on a voice from fix 6's pool (does not count toward 6b). Logged on the ownshots.csv row.
    internal static class OutdoorTail
    {
        private static readonly AccessTools.FieldRef<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge> BridgeRef =
            AccessTools.FieldRefAccess<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge>("playersBridge");

        [ThreadStatic] private static bool _own;
        private static bool _bankIsTail;
        private static WeaponSoundPlayer _wsp;
        private static Vector3 _origin, _aim;
        private static bool _haveOpening;
        private static Vector3 _openPos;
        private static string _openThrough = "";
        private static int _glassMask = -1;
        private static float _lastFind = -100f;

        public static bool PlayedThisCall;
        public static float LastLevelDb = float.NaN, LastDelayMs = float.NaN, LastAz = float.NaN;
        public static string LastThrough = "", LastSkip = "";
        public static int Played, Fallbacks;
        private static float _lvlSum, _delaySum;
        private static readonly Dictionary<string, int> Skips = new Dictionary<string, int>();

        private const float GlassCutHz = 1500f, BarsCutHz = 12000f, BarsDb = -1f, FallbackM = 3f;

        public static void Apply(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(WeaponSoundPlayer), nameof(WeaponSoundPlayer.FireBullet)),
                prefix: new HarmonyMethod(typeof(OutdoorTail), nameof(FirePrefix)) { priority = Priority.Low },
                finalizer: new HarmonyMethod(typeof(OutdoorTail), nameof(ClearFinalizer)));
            h.Patch(AccessTools.Method(typeof(WeaponSoundPlayer), nameof(WeaponSoundPlayer.StopFiringLoop)),
                prefix: new HarmonyMethod(typeof(OutdoorTail), nameof(TailPrefix)),
                finalizer: new HarmonyMethod(typeof(OutdoorTail), nameof(ClearFinalizer)));
            h.Patch(AccessTools.Method(typeof(SoundBank), nameof(SoundBank.PickClipsByDistance)),
                postfix: new HarmonyMethod(typeof(OutdoorTail), nameof(PickPostfix)));
            h.Patch(AccessTools.Method(typeof(SuperBetterAudioQueue), nameof(SuperBetterAudioQueue.Enqueue)),
                postfix: new HarmonyMethod(typeof(OutdoorTail), nameof(EnqueuePostfix)));
        }

        public static void Reset()
        {
            Played = 0; Fallbacks = 0; _lvlSum = _delaySum = 0f; Skips.Clear(); _glassMask = -1; _haveOpening = false; _wsp = null; _lastFind = -100f;
        }

        private static bool IsOwn(WeaponSoundPlayer w)
        {
            BaseSoundPlayer.IObserverToPlayerBridge b = BridgeRef(w);
            return b != null && b.PointOfView == EPointOfView.FirstPerson;
        }

        private static void FirePrefix(WeaponSoundPlayer __instance, Vector3 shotPosition, Vector3 shotDirection)
        {
            _own = false; _bankIsTail = false; PlayedThisCall = false; LastSkip = "";
            try
            {
                if (!Plugin.OutdoorTailOn.Value || !IsOwn(__instance)) return;
                _own = true; _wsp = __instance;
                _origin = shotPosition;
                if (shotDirection.sqrMagnitude > 1e-6f) _aim = shotDirection.normalized;
                // every round of a burst comes through here: look for the opening at most 5 times a second (<= ~90 short
                // casts each); the burst's tail uses the latest
                if (Time.time - _lastFind >= 0.2f) { FindOpening(); _lastFind = Time.time; }
            }
            catch (Exception e) { _own = false; DiagRunner.ReportOnce("3m outdoor tail (fire)", e); }
        }

        // the tail at the end of a burst: keeps the opening found at the burst's last shot
        private static void TailPrefix(WeaponSoundPlayer __instance)
        {
            _own = false; _bankIsTail = true; PlayedThisCall = false; LastSkip = "";
            try
            {
                if (!Plugin.OutdoorTailOn.Value || !IsOwn(__instance)) return;
                _own = true; _wsp = __instance;
            }
            catch (Exception e) { _own = false; DiagRunner.ReportOnce("3m outdoor tail (tail)", e); }
        }

        private static Exception ClearFinalizer(Exception __exception) { _own = false; return __exception; }

        private static void PickPostfix(SoundBank __instance)
        {
            if (!_own || _wsp == null) return;
            _bankIsTail = __instance == _wsp.Tail || __instance == _wsp.TailSilenced;
        }

        private static void FindOpening()
        {
            _haveOpening = false; _openThrough = "";
            int mask = EnclosureProbe.CurrentMask();
            if (!EnclosureProbe.MeasureAim(_origin, _aim, mask, out _, out _)) return;
            if (_glassMask == -1) _glassMask = LayerMask.GetMask("LowPolyCollider", "TransparentCollider") & ~mask;
            for (int i = 0; i < EnclosureProbe.AimCone; i++)
            {
                Vector3 d = EnclosureProbe.AimDirs[i];
                float maxD = float.IsInfinity(EnclosureProbe.AimHitDist[i]) ? EnclosureProbe.MaxRange : EnclosureProbe.AimHitDist[i];
                if (!AcousticProbe.ExitAlong(_origin, d, maxD, out Vector3 ex, out float t)) continue;
                _openPos = ex; _openThrough = "open";
                if (_glassMask != 0 && CountedRay.Raycast(_origin, d, out RaycastHit gh, t + 0.5f, _glassMask, QueryTriggerInteraction.Ignore))
                {
                    OutdoorExposure.Path p = OutdoorExposure.Classify(gh.collider);
                    if (p == OutdoorExposure.Path.Glass) { _openPos = gh.point; _openThrough = "glass"; }
                    else if (p == OutdoorExposure.Path.Bars) { _openPos = gh.point; _openThrough = "bars"; }
                }
                _haveOpening = true;
                return;
            }
        }

        private static void EnqueuePostfix(SuperBetterAudioQueue __instance, double startTime, float clipTime, float volume, float pitch)
        {
            if (!_own || PlayedThisCall || _wsp == null) return;
            if (clipTime <= 0f) return; // the loop: never
            try
            {
                if (!_bankIsTail && !_wsp.Non_auto) return; // a full-auto gun's body: its tail sample gets it
                if (!Plugin.EchoOn.Value) { Skip("fix 6 off (its voices)"); return; }
                float w = GunEnvironmentBlend.LastWeightUsed;
                if (float.IsNaN(w)) { Skip("3c not deciding"); return; }
                if (w <= 0.5f) { Skip("mostly outdoor already"); return; }
                float open = 1f - EnclosureProbe.AimClosure;
                if (float.IsNaN(open) || open <= 0.001f) { Skip("aim cone closed"); return; }
                SoundBank bank = _wsp.IsSilenced && _wsp.TailSilenced != null ? _wsp.TailSilenced : _wsp.Tail;
                if (bank == null) { Skip("no tail bank"); return; }
                AudioClip clip = bank.PickSingleClip(0); // outdoor set, close tier
                if (clip == null) { Skip("no outdoor tail clip"); return; }

                Vector3 pos; string through;
                if (_haveOpening) { pos = _openPos; through = _openThrough; }
                else { pos = _origin + _aim * FallbackM; through = "open (fallback)"; }
                float gainDb = Plugin.OutdoorTailDb.Value, cut = 22000f;
                if (through == "glass") { gainDb -= Plugin.GlassLossDb.Value; cut = GlassCutHz; }
                else if (through == "bars") { gainDb += BarsDb; cut = BarsCutHz; }
                float vol = volume * Mathf.Clamp01(open) * Mathf.Clamp01(w) * Mathf.Pow(10f, gainDb / 20f);

                Transform lt = MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated ? MonoBehaviourSingleton<SpatialAudioSystem>.Instance.ListenerTransform : null;
                Vector3 l = lt != null ? lt.position : _origin;
                float dist = Vector3.Distance(l, pos);
                // 0.6.14: distance loss like the echoes' (-20 log10(path / 2 m)); fix 6's voices don't fade with distance,
                // so a window 10 m away played as loud as one 1 m away
                vol *= 2f / Mathf.Max(dist, 2f);
                float delay = dist / OwnEchoes.SpeedOfSound;
                float p = pitch > 0f ? pitch : 1f;
                EchoVoices.Play(__instance, clip, pos, startTime + delay, clip.length / p, false, vol, p, cut, delay);
                if (EchoVoices.LastDropped) { Skip("headroom cap"); return; } // 0.6.16

                PlayedThisCall = true; Played++;
                if (!_haveOpening) Fallbacks++;
                LastLevelDb = 20f * Mathf.Log10(Mathf.Max(vol / Mathf.Max(volume, 1e-4f), 1e-5f));
                LastDelayMs = delay * 1000f;
                LastAz = lt != null && dist > 0.05f ? AcousticProbe.RelAz(lt, (pos - l) / dist) : float.NaN;
                LastThrough = through;
                _lvlSum += LastLevelDb; _delaySum += LastDelayMs;
            }
            catch (Exception e) { DiagRunner.ReportOnce("3m outdoor tail", e); }
        }

        private static void Skip(string why)
        {
            LastSkip = why;
            Skips.TryGetValue(why, out int n); Skips[why] = n + 1;
        }

        public static string Summary()
        {
            var parts = new List<string>();
            foreach (var kv in Skips) parts.Add($"{kv.Key} {kv.Value}");
            string lobe = GunEnvironmentBlend.LobeDiffN > 0 ? $"{GunEnvironmentBlend.LobeDiffSum / GunEnvironmentBlend.LobeDiffN:+0.00;-0.00} over {GunEnvironmentBlend.LobeDiffN} shots" : "no shots";
            return $"OUTDOOR TAIL FROM THE OPENING (3m {(Plugin.OutdoorTailOn.Value ? "ON" : "off")}): played {Played}" +
                   (Played > 0 ? $", mean level {_lvlSum / Played:0.0} dB, mean delay {_delaySum / Played:0} ms, {Fallbacks} from the 3 m fallback (cone open but no exit point found)" : "") +
                   $"; not played: {(parts.Count > 0 ? string.Join(", ", parts) : "-")}\n" +
                   $"MUZZLE LOBE (3k {Plugin.MuzzleLobe.Value:0.0}): muzzle room sphere minus lobe, mean {lobe}\n";
        }
    }
}
