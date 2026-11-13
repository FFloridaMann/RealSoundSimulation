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
    //
    // 13 Nov 2026: no more crackle. Raid_20261113_111558 (user): with the tail off "def sounds better ... Crackling gone"; with it on,
    // "crackling at the very end of the echo but only after multiple shots ... 1 shot no crackle two quick shots crackle", and after a
    // quiet spell a first shot whose "full audio didnt play". Two causes in the handling: a fourth quick shot stopped the oldest source
    // dead while its reverb still rang (Restarted, 10 that raid), and every shot rewrote its source's reverb settings even while the
    // previous shot's tail was still ringing in it. Now nothing ringing is ever stopped or retuned:
    //   share    a shot whose room matches a ringing source (reverb time within 15%, hardness within 0.15) is added to it unchanged
    //   fresh    else a source that has fully rung out is tuned to this room
    //   nearest  else the ringing source closest in settings takes it unchanged; if every source already has MaxDry dry samples
    //            sounding, the shot has no room tail (the ones ringing cover it), never a cut
    // A source's pitch only changes when no dry sample is playing on it, and sources stay alive QuietStopS after the last shot and
    // until their tail has rung out, so a fight doesn't start each burst on a cold reverb.
    internal static class RoomTail
    {
        [ThreadStatic] private static bool _own;
        private const int MaxTails = 3;
        private sealed class Tail { public GameObject Go; public AudioSource Src; public AudioReverbFilter Rev; public double DryEnd, DryEnd2, RingEnd; public float LastShot = -100f, Rt = -1f, Hard = -1f; public bool Alive; }
        private const int MaxDry = 2;
        private const float SameRt = 0.15f, SameHard = 0.15f;
        private static readonly Tail[] Tails = new Tail[MaxTails];
        private static bool _built;
        private static AudioClip _silence;
        private const float QuietStopS = 20f, BaseGainDb = -6f;
        public static int Played, Shared, Skipped; private static float _sumRt, _sumAmount;

        // 0.7.6: real voices held right now (keep-alive voices plus dry samples still sounding), for the echo headroom
        public static int Voices
        {
            get
            {
                if (!_built) return 0;
                double now = AudioSettings.dspTime; int n = 0;
                foreach (Tail t in Tails) { if (t.Alive) n++; n += DryCount(t, now); }
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

        public static void Reset() { Played = Shared = Skipped = 0; _sumRt = _sumAmount = 0f; }

        private static int DryCount(Tail t, double now) => (t.DryEnd > now ? 1 : 0) + (t.DryEnd2 > now ? 1 : 0);

        private static float Distance(Tail t, float rt, float hard) => t.Rt <= 0f ? 99f : Mathf.Abs(Mathf.Log(t.Rt / rt)) + Mathf.Abs(t.Hard - hard);

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

                // 13 Nov 2026: share a matching ringing source, else a rung-out one (tuned to this room), else the nearest ringing one
                // as it is; never stop or retune a source that is still ringing
                double now = AudioSettings.dspTime;
                Tail tl = null; bool fresh = false;
                foreach (Tail t in Tails)
                    if (t.Alive && t.RingEnd > now && DryCount(t, now) < MaxDry && Mathf.Abs(t.Rt - rt) <= SameRt * rt && Mathf.Abs(t.Hard - hard) <= SameHard) { tl = t; break; }
                if (tl == null) foreach (Tail t in Tails) if (t.RingEnd <= now && DryCount(t, now) == 0) { tl = t; fresh = true; break; }
                if (tl == null) foreach (Tail t in Tails) if (DryCount(t, now) < MaxDry && (tl == null || Distance(t, rt, hard) < Distance(tl, rt, hard))) tl = t;
                if (tl == null) { Skipped++; return; }
                if (!fresh) Shared++;
                AudioSource src = tl.Src; AudioReverbFilter rev = tl.Rev;

                if (fresh)
                {
                    rev.decayTime = rt;
                    rev.decayHFRatio = Mathf.Lerp(0.45f, 0.95f, hard);        // soft rooms lose their highs faster
                    rev.roomHF = Mathf.Lerp(-1400f, -250f, hard);
                    rev.reverbDelay = Mathf.Clamp(1.5f * mfp / 343f, 0.005f, 0.08f); // bigger rooms: the dense tail builds later
                    rev.reflectionsDelay = Mathf.Clamp(mfp / 343f, 0.003f, 0.1f);
                    tl.Rt = rt; tl.Hard = hard;
                }

                var ba = MonoBehaviourSingleton<BetterAudio>.Instantiated ? MonoBehaviourSingleton<BetterAudio>.Instance : null;
                if (ba != null) src.outputAudioMixerGroup = ba.GunshotMixerGroup;
                if (!src.isPlaying) { src.clip = _silence; src.loop = true; src.Play(); }
                tl.Alive = true;
                float p = pitch > 0f ? pitch : 1f;
                if (DryCount(tl, now) == 0) src.pitch = p; else p = src.pitch; // no pitch jump under a dry sample still playing
                float gain = Mathf.Pow(10f, (BaseGainDb + Plugin.RoomTailDb.Value) / 20f);
                src.PlayOneShot(dry, Mathf.Clamp01(volume * amount * gain));
                double end = now + dry.length / p;
                if (tl.DryEnd <= now) tl.DryEnd = end; else tl.DryEnd2 = end;
                tl.RingEnd = System.Math.Max(tl.RingEnd, end + tl.Rt + 0.2);
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
                if (!t.Alive || (Plugin.RoomTailOn.Value && (Time.time - t.LastShot <= QuietStopS || AudioSettings.dspTime < t.RingEnd))) continue;
                if (t.Src != null) t.Src.Stop(); // rung out by now: nothing audible is cut
                t.Alive = false; t.DryEnd = t.DryEnd2 = t.RingEnd = 0; t.Rt = t.Hard = -1f;
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
            return $"ROOM TAIL ({state}): shots with a tail {Played}{avg}; added to a ringing tail {Shared}, no tail (all {MaxTails} busy) {Skipped}\n";
        }
    }
}
