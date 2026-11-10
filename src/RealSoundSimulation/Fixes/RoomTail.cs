using System;
using HarmonyLib;
using RealSoundSimulation.Acoustics;
using RealSoundSimulation.Diagnostics;
using UnityEngine;

namespace RealSoundSimulation.Fixes
{
    // 0.7.4: your own shot's reverb tail from the room the mod measures. The game's reverb copy of your shot (Meta XR) takes its
    // length from Meta's coarse baked map, and the mod could only change its volume. This plays the dry (outdoor) recording of
    // each shot into one Unity reverb whose decay is the room's measured reverb time (probe v2), whose high end dies faster in
    // soft rooms than in hard ones, and whose loudness follows how indoor the shot is (after the open-air pull). Overlapping
    // shots share it, like one real room. The game's own reverb copy is turned down under it ("Game reverb under the tail").
    //
    // One voice, kept playing silence while you shoot so the tail isn't cut when a shot's sample ends (a stopped source stops
    // its effects too), and stopped again after a few quiet seconds.
    internal static class RoomTail
    {
        [ThreadStatic] private static bool _own;
        private static GameObject _go;
        private static AudioSource _src;
        private static AudioReverbFilter _rev;
        private static AudioClip _silence;
        private static float _lastShot = -100f;
        private const float QuietStopS = 8f, BaseGainDb = -6f;
        public static int Played; private static float _sumRt, _sumAmount;

        public static void Apply(Harmony h)
        {
            var pre = new HarmonyMethod(typeof(RoomTail), nameof(OwnPrefix));
            var fin = new HarmonyMethod(typeof(RoomTail), nameof(ClearFinalizer));
            h.Patch(AccessTools.Method(typeof(WeaponSoundPlayer), nameof(WeaponSoundPlayer.FireBullet)), prefix: pre, finalizer: fin);
            h.Patch(AccessTools.Method(typeof(WeaponSoundPlayer), nameof(WeaponSoundPlayer.StopFiringLoop)), prefix: pre, finalizer: fin);
            h.Patch(AccessTools.Method(typeof(SuperBetterAudioQueue), nameof(SuperBetterAudioQueue.Enqueue)),
                postfix: new HarmonyMethod(typeof(RoomTail), nameof(EnqueuePostfix)) { priority = Priority.Last });
        }

        public static void Reset() { Played = 0; _sumRt = _sumAmount = 0f; }

        private static void OwnPrefix(WeaponSoundPlayer __instance)
        {
            try { _own = Plugin.RoomTailOn.Value && GunEnvironmentBlend.IsOwn(__instance); }
            catch (Exception e) { _own = false; DiagRunner.ReportOnce("room tail (own)", e); }
        }

        private static Exception ClearFinalizer(Exception __exception) { _own = false; return __exception; }

        private static void EnqueuePostfix(AudioClip clip1, AudioClip clip2, float clipTime, float volume, float pitch)
        {
            if (!_own || clipTime <= 0f) return; // single shots and burst tails; a firing loop gets its tail from the burst's tail sample
            try
            {
                float amount = GunEnvironmentBlend.LastWeightUsed;
                if (float.IsNaN(amount) || amount < 0.05f) return;
                AudioClip dry = OwnEchoes.DryClip != null ? OwnEchoes.DryClip : (clip2 != null ? clip2 : clip1);
                if (dry == null) return;
                if (!Build()) return;

                // Room: measured reverb time, hardness and size (probe v2), each with a sane fallback
                float rt = AcousticProbe.Valid && !float.IsNaN(AcousticProbe.Rt60) ? AcousticProbe.Rt60 : 0.8f;
                rt = Mathf.Clamp(rt * Plugin.RoomTailLength.Value, 0.2f, 4f);
                float hardNow = GunEnvironmentBlend.HardRoomNow();
                float hard = float.IsNaN(hardNow) ? 0.5f : Mathf.Clamp01(hardNow);
                float mfp = AcousticProbe.Valid && !float.IsNaN(AcousticProbe.MeanFreePath) ? AcousticProbe.MeanFreePath : 4f;

                _rev.decayTime = rt;
                _rev.decayHFRatio = Mathf.Lerp(0.45f, 0.95f, hard);        // soft rooms lose their highs faster
                _rev.roomHF = Mathf.Lerp(-1400f, -250f, hard);
                _rev.reverbDelay = Mathf.Clamp(1.5f * mfp / 343f, 0.005f, 0.08f); // bigger rooms: the dense tail builds later
                _rev.reflectionsDelay = Mathf.Clamp(mfp / 343f, 0.003f, 0.1f);

                var ba = MonoBehaviourSingleton<BetterAudio>.Instantiated ? MonoBehaviourSingleton<BetterAudio>.Instance : null;
                if (ba != null) _src.outputAudioMixerGroup = ba.GunshotMixerGroup;
                if (!_src.isPlaying) { _src.clip = _silence; _src.loop = true; _src.Play(); }
                _src.pitch = pitch > 0f ? pitch : 1f;
                float gain = Mathf.Pow(10f, (BaseGainDb + Plugin.RoomTailDb.Value) / 20f);
                _src.PlayOneShot(dry, Mathf.Clamp01(volume * amount * gain));
                _lastShot = Time.time;
                Played++; _sumRt += rt; _sumAmount += amount;
            }
            catch (Exception e) { DiagRunner.ReportOnce("room tail (play)", e); }
        }

        // Called every frame by DiagRunner: lets the voice go after a few quiet seconds, by which time any tail has died.
        public static void Tick()
        {
            if (_src != null && _src.isPlaying && (Time.time - _lastShot > QuietStopS || !Plugin.RoomTailOn.Value)) _src.Stop();
        }

        private static bool Build()
        {
            if (_src != null) return true;
            _go = new GameObject("RealSoundSimulation room tail");
            UnityEngine.Object.DontDestroyOnLoad(_go);
            _src = _go.AddComponent<AudioSource>();
            _src.playOnAwake = false;
            _src.spatialBlend = 0f;           // a room's tail surrounds you
            _src.spatialize = false;
            _src.dopplerLevel = 0f;
            _src.bypassReverbZones = true;
            _src.reverbZoneMix = 0f;
            _src.priority = 64;
            _rev = _go.AddComponent<AudioReverbFilter>();
            _rev.reverbPreset = AudioReverbPreset.User;
            _rev.dryLevel = -10000f;          // tail only: the shot itself is the game's
            _rev.room = 0f;
            _rev.roomLF = 0f;
            _rev.reflectionsLevel = -10000f;  // the early reflections are the echoes' job
            _rev.reverbLevel = 0f;
            _rev.diffusion = 100f;
            _rev.density = 100f;
            _rev.hfReference = 5000f;
            _rev.lfReference = 250f;
            _silence = AudioClip.Create("RealSoundSimulation silence", 4800, 1, 48000, false);
            return true;
        }

        public static string Summary()
        {
            string state = Plugin.RoomTailOn.Value ? $"ON, volume {Plugin.RoomTailDb.Value:+0;-0;0} dB, length x{Plugin.RoomTailLength.Value:0.00}, game reverb {Plugin.GameReverbUnderTailDb.Value:+0;-0;0} dB" : "off";
            string avg = Played > 0 ? $", mean reverb time {_sumRt / Played:0.00} s, mean indoor amount {_sumAmount / Played:0.00}" : "";
            return $"ROOM TAIL ({state}): shots with a tail {Played}{avg}\n";
        }
    }
}
