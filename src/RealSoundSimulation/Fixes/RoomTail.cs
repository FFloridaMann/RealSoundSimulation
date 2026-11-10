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
    //
    // 0.7.6: three of them, used in turn, and counted. With one source every shot's dry sample was one more real voice for
    // its whole length (PlayOneShot starts a new voice each time and none can be ended alone), so ten quick shots stacked
    // ten voices that the echo headroom (EchoVoices.Allowed) never saw. Now each shot takes the source whose dry sample ends
    // soonest; if that one is still sounding (a fourth shot inside one sample's length) it is restarted, which ends only that
    // oldest sample, by then under three newer shots. So at most MaxTails dry samples sound at once, and with the three
    // silent keep-alive voices that is at most 6 real voices, all counted in Voices. Each source has its own reverb with the
    // room as it was for its shot (a later shot no longer changes the reverb or the pitch of a tail that is still ringing).
    internal static class RoomTail
    {
        [ThreadStatic] private static bool _own;
        private const int MaxTails = 3;
        private sealed class Tail { public GameObject Go; public AudioSource Src; public AudioReverbFilter Rev; public double DryEnd; public float LastShot = -100f; public bool Alive; }
        private static readonly Tail[] Tails = new Tail[MaxTails];
        private static bool _built;
        private static AudioClip _silence;
        private const float QuietStopS = 8f, BaseGainDb = -6f;
        public static int Played, Restarted; private static float _sumRt, _sumAmount;

        // 0.7.6: real voices held right now (keep-alive voices plus dry samples still sounding), for the echo headroom
        public static int Voices
        {
            get
            {
                if (!_built) return 0;
                double now = AudioSettings.dspTime; int n = 0;
                foreach (Tail t in Tails) { if (t.Alive) n++; if (t.DryEnd > now) n++; }
                return n;
            }
        }

        public static void Apply(Harmony h)
        {
            var pre = new HarmonyMethod(typeof(RoomTail), nameof(OwnPrefix));
            var fin = new HarmonyMethod(typeof(RoomTail), nameof(ClearFinalizer));
            h.Patch(AccessTools.Method(typeof(WeaponSoundPlayer), nameof(WeaponSoundPlayer.FireBullet)), prefix: pre, finalizer: fin);
            h.Patch(AccessTools.Method(typeof(WeaponSoundPlayer), nameof(WeaponSoundPlayer.StopFiringLoop)), prefix: pre, finalizer: fin);
            h.Patch(AccessTools.Method(typeof(SuperBetterAudioQueue), nameof(SuperBetterAudioQueue.Enqueue)),
                postfix: new HarmonyMethod(typeof(RoomTail), nameof(EnqueuePostfix)) { priority = Priority.Last });
        }

        public static void Reset() { Played = Restarted = 0; _sumRt = _sumAmount = 0f; }

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

                // 0.7.6: the source whose dry sample ends soonest; still sounding = the fourth stacked shot, so end that sample
                double now = AudioSettings.dspTime;
                Tail tl = Tails[0];
                for (int i = 1; i < MaxTails; i++) if (Tails[i].DryEnd < tl.DryEnd) tl = Tails[i];
                AudioSource src = tl.Src; AudioReverbFilter rev = tl.Rev;
                if (tl.DryEnd > now) { src.Stop(); Restarted++; }

                rev.decayTime = rt;
                rev.decayHFRatio = Mathf.Lerp(0.45f, 0.95f, hard);        // soft rooms lose their highs faster
                rev.roomHF = Mathf.Lerp(-1400f, -250f, hard);
                rev.reverbDelay = Mathf.Clamp(1.5f * mfp / 343f, 0.005f, 0.08f); // bigger rooms: the dense tail builds later
                rev.reflectionsDelay = Mathf.Clamp(mfp / 343f, 0.003f, 0.1f);

                var ba = MonoBehaviourSingleton<BetterAudio>.Instantiated ? MonoBehaviourSingleton<BetterAudio>.Instance : null;
                if (ba != null) src.outputAudioMixerGroup = ba.GunshotMixerGroup;
                if (!src.isPlaying) { src.clip = _silence; src.loop = true; src.Play(); }
                tl.Alive = true;
                float p = pitch > 0f ? pitch : 1f;
                src.pitch = p;
                float gain = Mathf.Pow(10f, (BaseGainDb + Plugin.RoomTailDb.Value) / 20f);
                src.PlayOneShot(dry, Mathf.Clamp01(volume * amount * gain));
                tl.DryEnd = now + dry.length / p;
                tl.LastShot = Time.time;
                Played++; _sumRt += rt; _sumAmount += amount;
            }
            catch (Exception e) { DiagRunner.ReportOnce("room tail (play)", e); }
        }

        // Called every frame by DiagRunner: lets the voice go after a few quiet seconds, by which time any tail has died.
        public static void Tick()
        {
            if (!_built) return;
            foreach (Tail t in Tails)
            {
                if (!t.Alive || (Time.time - t.LastShot <= QuietStopS && Plugin.RoomTailOn.Value)) continue;
                if (t.Src != null) t.Src.Stop();
                t.Alive = false; t.DryEnd = 0;
            }
        }

        private static bool Build()
        {
            if (_built) return true;
            _silence = AudioClip.Create("RealSoundSimulation silence", 4800, 1, 48000, false);
            for (int i = 0; i < MaxTails; i++)
            {
                var go = new GameObject("RealSoundSimulation room tail " + i);
                UnityEngine.Object.DontDestroyOnLoad(go);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;           // a room's tail surrounds you
                src.spatialize = false;
                src.dopplerLevel = 0f;
                src.bypassReverbZones = true;
                src.reverbZoneMix = 0f;
                src.priority = 64;
                var rev = go.AddComponent<AudioReverbFilter>();
                rev.reverbPreset = AudioReverbPreset.User;
                rev.dryLevel = -10000f;          // tail only: the shot itself is the game's
                rev.room = 0f;
                rev.roomLF = 0f;
                rev.reflectionsLevel = -10000f;  // the early reflections are the echoes' job
                rev.reverbLevel = 0f;
                rev.diffusion = 100f;
                rev.density = 100f;
                rev.hfReference = 5000f;
                rev.lfReference = 250f;
                Tails[i] = new Tail { Go = go, Src = src, Rev = rev };
            }
            _built = true;
            return true;
        }

        public static string Summary()
        {
            string state = Plugin.RoomTailOn.Value ? $"ON, volume {Plugin.RoomTailDb.Value:+0;-0;0} dB, length x{Plugin.RoomTailLength.Value:0.00}, game reverb {Plugin.GameReverbUnderTailDb.Value:+0;-0;0} dB" : "off";
            string avg = Played > 0 ? $", mean reverb time {_sumRt / Played:0.00} s, mean indoor amount {_sumAmount / Played:0.00}" : "";
            return $"ROOM TAIL ({state}): shots with a tail {Played}{avg}; oldest of {MaxTails} ended early for a new shot {Restarted}\n";
        }
    }
}
