using System;
using System.Collections.Generic;
using RealSoundSimulation.Acoustics;
using RealSoundSimulation.Diagnostics;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace RealSoundSimulation.Fixes
{
    // 3o (0.6.30): the ring of your own shot follows you when you turn. Your gun is a stereo recording locked to your head, so at a
    // half-open spot, firing then turning around left the indoor side where you now face and the outdoor side behind you (raid 40 tag 1).
    // Fix 3c plays the indoor recording on a voice's first source and the outdoor one on its second, so the two can be moved apart:
    // the open side is remembered when you fire, and until the sound ends the change in your facing since then shifts them. Turning
    // sideways pans them across your ears; whichever one has swung behind you is dulled, as your head would. Firing without turning
    // changes nothing. A stereo recording can't truly be placed behind you, so front and back is a cue rather than a position.
    // Only runs on your own gun, on a shot with both recordings in it, for the few seconds it rings.
    internal class OwnShotTurn : MonoBehaviour
    {
        private const float PanAmount = 0.6f, BehindCutoffHz = 3500f, OpenCutoffHz = 22000f, FocusFull = 0.5f;

        private static readonly AccessTools.FieldRef<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge> BridgeRef =
            AccessTools.FieldRefAccess<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge>("playersBridge");
        private static readonly AccessTools.FieldRef<WeaponSoundPlayer, SuperBetterAudioQueue> QueueRef =
            AccessTools.FieldRefAccess<WeaponSoundPlayer, SuperBetterAudioQueue>("_queue");

        private static WeaponSoundPlayer _gun;
        private static SuperBetterAudioQueue _queue;
        private static Vector3 _open;
        private static float _r0, _f0, _strength;
        private static bool _active;

        private static readonly Dictionary<AudioSource, AudioLowPassFilter> Filters = new Dictionary<AudioSource, AudioLowPassFilter>();

        public static int Armed;

        public static void Apply(Harmony h)
        {
            var post = new HarmonyMethod(typeof(OwnShotTurn), nameof(ShotPostfix));
            h.Patch(AccessTools.Method(typeof(WeaponSoundPlayer), nameof(WeaponSoundPlayer.FireBullet)), postfix: post);
            h.Patch(AccessTools.Method(typeof(WeaponSoundPlayer), nameof(WeaponSoundPlayer.StopFiringLoop)), postfix: post);
        }

        public static void Reset() { Clear(); Filters.Clear(); Armed = 0; }

        private static void ShotPostfix(WeaponSoundPlayer __instance)
        {
            if (Plugin.ShotRingFollowsTurn.Value <= 0f || !GunEnvironmentBlend.LastMixed) return;
            try
            {
                BaseSoundPlayer.IObserverToPlayerBridge b = BridgeRef(__instance);
                if (b == null || b.PointOfView != EPointOfView.FirstPerson) return;
                SuperBetterAudioQueue q = QueueRef(__instance);
                if (q == null || !AcousticProbe.Valid || !MonoBehaviourSingleton<Audio.SpatialSystem.SpatialAudioSystem>.Instantiated) return;
                Transform lt = MonoBehaviourSingleton<Audio.SpatialSystem.SpatialAudioSystem>.Instance.ListenerTransform;
                if (lt == null) return;
                Vector3 open = AcousticProbe.Arrival; open.y = 0f;
                if (open.sqrMagnitude < 1e-4f) return;
                if (_active && _queue != q) Clear();
                _open = open.normalized;
                _r0 = Vector3.Dot(_open, lt.right);
                _f0 = Vector3.Dot(_open, lt.forward);
                _strength = Mathf.Clamp01(AcousticProbe.Focus / FocusFull); // no clear open side: nothing to move
                _gun = __instance; _queue = q; _active = true;
                Armed++;
            }
            catch (Exception e) { DiagRunner.ReportOnce("3o shot ring turn (fire)", e); }
        }

        private void LateUpdate()
        {
            if (!_active) return;
            try
            {
                if (_gun == null || QueueRef(_gun) != _queue || !Playing(_queue) || !MonoBehaviourSingleton<Audio.SpatialSystem.SpatialAudioSystem>.Instantiated) { Clear(); return; }
                Transform lt = MonoBehaviourSingleton<Audio.SpatialSystem.SpatialAudioSystem>.Instance.ListenerTransform;
                if (lt == null) return;
                float s = _strength * Mathf.Clamp01(Plugin.ShotRingFollowsTurn.Value);
                float r = Vector3.Dot(_open, lt.right), f = Vector3.Dot(_open, lt.forward);
                float panOutdoor = Mathf.Clamp((r - _r0) * PanAmount * s, -1f, 1f);
                float outdoorBehind = Mathf.Clamp01((_f0 - f) * 0.5f) * s, indoorBehind = Mathf.Clamp01((f - _f0) * 0.5f) * s;
                foreach (BetterSource v in _queue.AudioSources)
                {
                    if (!(v is SuperSource ss)) continue;
                    Set(ss.source1, -panOutdoor, indoorBehind);   // slot 1 = the indoor recording
                    Set(ss.source2, panOutdoor, outdoorBehind);   // slot 2 = the outdoor recording
                }
            }
            catch (Exception e) { DiagRunner.ReportOnce("3o shot ring turn", e); Clear(); }
        }

        private static bool Playing(SuperBetterAudioQueue q)
        {
            foreach (BetterSource v in q.AudioSources)
                if (v is SuperSource ss && ((ss.source1 != null && ss.source1.isPlaying) || (ss.source2 != null && ss.source2.isPlaying))) return true;
            return false;
        }

        private static void Set(AudioSource a, float pan, float behind)
        {
            if (a == null) return;
            a.panStereo = pan;
            AudioLowPassFilter lp = Filter(a, behind > 0.001f);
            if (lp != null) lp.cutoffFrequency = Mathf.Exp(Mathf.Lerp(Mathf.Log(OpenCutoffHz), Mathf.Log(BehindCutoffHz), behind));
        }

        private static AudioLowPassFilter Filter(AudioSource a, bool on)
        {
            if (!Filters.TryGetValue(a, out AudioLowPassFilter lp))
            {
                if (!on) return null;
                lp = a.gameObject.AddComponent<AudioLowPassFilter>(); // our own, separate from the game's occlusion filters
                if (lp != null) lp.lowpassResonanceQ = 1f;
                Filters[a] = lp;
            }
            if (lp != null && lp.enabled != on) lp.enabled = on;
            return lp;
        }

        // Back to neutral: these voices return to the game's pool and other guns use them.
        private static void Clear()
        {
            if (_queue != null && _queue.AudioSources != null)
            {
                foreach (BetterSource v in _queue.AudioSources)
                {
                    if (!(v is SuperSource ss)) continue;
                    if (ss.source1 != null) ss.source1.panStereo = 0f;
                    if (ss.source2 != null) ss.source2.panStereo = 0f;
                }
            }
            foreach (var kv in Filters) if (kv.Value != null && kv.Value.enabled) kv.Value.enabled = false;
            _active = false; _gun = null; _queue = null;
        }
    }
}
