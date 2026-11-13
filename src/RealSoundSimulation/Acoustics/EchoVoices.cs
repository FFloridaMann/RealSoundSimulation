using System;
using System.Collections.Generic;
using Audio.SpatialSystem;
using EFT;
using UnityEngine;
using UnityEngine.Audio;

namespace RealSoundSimulation.Acoustics
{
    // The mod's own voice pool, shared by everything the mod adds: your echoes, bot echoes, explosion echoes, the through-wall
    // thump, the shock wave, pass-bys, the outdoor tail and room echoes. It stays under a headroom cap (Allowed) so the game's
    // own sounds are never pushed out, and only bot echoes can lose a voice to another sound. Each Play says where it goes:
    // the game's gunshot group by default, or another group (the felt layers of a blast go to Master, past the concussion cut).
    // Turning one feature off must only stop that feature's class of voices (StopClass), never the pool.

    // Who owns a voice. Only bot echoes can ever lose one. Blast is a close blast's shock wave and the through-wall thump:
    // they may go up to BlastReserve voices past the cap (still well inside its margin), so a blast is never silent in a full pool.
    internal enum EchoClass : byte { Other, Own, Bot, Blast }

    // The echo voice pool: plain AudioSources (2D panning by position, no distance roll-off; level and filter are set per
    // echo) routed to the game's gunshot mixer group.
    internal class EchoVoices : MonoBehaviour
    {
        // 0.6.9: 48 (was 24). Raid 28: every own sample was a full-auto loop plus a tail, 6 echoes each; the pool was full
        // and 274 voices were stolen (256 by tail echoes), each cut off mid-sound - heard as a doubled or stuttering shot.
        // 0.6.14: 96 (raid 30: 48/48 busy, 57 cuts with no bots). Unity has ~196 real voices; the game peaked at 40 playing
        // sources with 16 bot shooters (raid 25). Not higher than 96.
        public const int PoolSize = 96;
        // 0.6.61: voices that are only ever used while a replaced echo fades out, so the new echo can start at once; never counted against the cap
        private const int HandoverVoices = 8;
        private const float ReleaseS = 0.04f, ReplaceMarginDb = 1f, FadeOverClipDb = 30f;
        private const double LoopSafetyS = 15.0, FadeEarlyS = 0.04;
        private const float MinFadeS = 0.05f;

        private class Voice
        {
            public GameObject Go;
            public AudioSource Src;
            public AudioLowPassFilter Lp;
            public double Start, BusyUntil, LoopStart, FadeStart, FadeEnd;
            public float FadeBase;
            public bool OpenLoop, Fading;
            public object Owner;
            public float Delay;
            public EchoClass Cls;
            public int Tier;
            public float StartDb, Dur;
            public bool Releasing;
        }

        private static readonly Voice[] Pool = new Voice[PoolSize + HandoverVoices];
        private static bool _built;
        private static AnimationCurve _flat;

        public static int Busy
        {
            get
            {
                if (!_built) return 0;
                double now = AudioSettings.dspTime; int n = 0;
                foreach (Voice v in Pool) if (v.BusyUntil > now && !v.Releasing) n++;
                return n;
            }
        }

        private static void Build()
        {
            if (_built) return;
            _flat = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 1f));
            for (int i = 0; i < Pool.Length; i++)
            {
                var go = new GameObject("RealSoundSimulation echo " + i);
                DontDestroyOnLoad(go);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 1f;
                src.spatialize = false;
                src.dopplerLevel = 0f;
                src.spread = 0f;
                src.reverbZoneMix = 0f;
                src.bypassReverbZones = true;
                src.priority = 220; // 0.6.15 (was 40): when Unity runs short of real voices, game sounds keep theirs and ours go virtual first
                src.rolloffMode = AudioRolloffMode.Custom;
                src.maxDistance = 100000f;
                src.SetCustomCurve(AudioSourceCurveType.CustomRolloff, _flat);
                var lp = go.AddComponent<AudioLowPassFilter>();
                lp.lowpassResonanceQ = 1f;
                lp.cutoffFrequency = 22000f;
                Pool[i] = new Voice { Go = go, Src = src, Lp = lp };
            }
            _built = true;
        }

        // Returns true if a busy voice had to be stopped to make room.
        public static bool LastDropped, LastReplaced; public static int CapDropped;
        public static int ReplacedForOwn, ReplacedForOther, ReplacedForBot, OwnDropped, OwnDroppedWithBots, MaxBot, BlastOverCap, BlastDropped;
        public static void ResetStats() { CapDropped = ReplacedForOwn = ReplacedForOther = ReplacedForBot = OwnDropped = OwnDroppedWithBots = MaxBot = BlastOverCap = BlastDropped = 0; }
        public const int BlastReserve = 8; // a close blast uses up to 5 (hit, crack, push, thump and its second voice)
        private static int _realVoices = -1;
        private const int ReserveVoices = 32; // 0.6.37 (voice budget for step 6, was 16); the mod's own opening sounds are counted too
        public static int Allowed()
        {
            if (_realVoices < 0) _realVoices = AudioSettings.GetConfiguration().numRealVoices;
            // 0.7.6: the room tail's voices (at most 6) are counted too
            return Mathf.Clamp(_realVoices - Diagnostics.DiagRunner.LastPlayingInContainers - Fixes.OpeningSounds.Voices - Fixes.RoomTail.Voices - ReserveVoices, 16, PoolSize);
        }

        public static bool Play(object owner, AudioClip clip, Vector3 pos, double start, double duration, bool loop, float volume, float pitch, float cutoff, float delay,
                                float clipOffset = 0f, double loopStart = 0.0, float minCutoff = 400f, float fadeS = 0f, EchoClass cls = EchoClass.Other, int tier = 0,
                                AudioMixerGroup output = null, float trim = 1f)
        {
            Build();
            double now = AudioSettings.dspTime;
            // 0.6.16: headroom cap. Our busy voices stay under what Unity has left after the game's own playing sources
            // (counted every 0.5 s) and a margin of 16, so the game's sounds are never virtualised for our echoes. At the
            // cap the new echo is dropped (callers offer a shot's echoes loudest first). Quiet raids never reach it.
            LastDropped = false; LastReplaced = false;
            float startDb = volume > 1e-5f ? 20f * Mathf.Log10(volume) : -100f;
            Count(now, out int busy, out int own, out int bot);
            if (!HasRoom(cls, busy, own, bot))
            {
                Voice victim = Plugin.EchoReplaceOn.Value ? Victim(now, cls, tier, startDb) : null;
                if (victim != null)
                {
                    Release(victim, now);
                    LastReplaced = true; bot--;
                    if (cls == EchoClass.Own) ReplacedForOwn++; else if (cls == EchoClass.Bot) ReplacedForBot++; else ReplacedForOther++;
                }
                else if (cls == EchoClass.Blast && CountOf(EchoClass.Blast, now) < BlastReserve) BlastOverCap++;
                else
                {
                    LastDropped = true; CapDropped++;
                    if (cls == EchoClass.Own) { OwnDropped++; if (bot > 0) OwnDroppedWithBots++; }
                    if (cls == EchoClass.Blast) BlastDropped++;
                    return false;
                }
            }
            if (cls == EchoClass.Bot && bot + 1 > MaxBot) MaxBot = bot + 1;
            Voice pick = null; bool stole = false;
            foreach (Voice v in Pool) if (v.BusyUntil <= now) { pick = v; break; }
            if (pick == null)
            {
                // 0.6.9: take the voice that ends soonest (least left to cut), not the one that started first - that was
                // often a loop echo still sounding, or the previous burst's tail in full swing.
                foreach (Voice v in Pool) if (v.Cls == EchoClass.Bot && (pick == null || v.BusyUntil < pick.BusyUntil)) pick = v; // 0.6.61: a bot echo goes first
                if (pick == null) foreach (Voice v in Pool) if (pick == null || v.BusyUntil < pick.BusyUntil) pick = v;
                pick.Src.Stop();
                stole = true;
            }
            var ba = MonoBehaviourSingleton<BetterAudio>.Instantiated ? MonoBehaviourSingleton<BetterAudio>.Instance : null;
            if (ba != null) pick.Src.outputAudioMixerGroup = output != null ? output : ba.GunshotMixerGroup; // "6.9j": felt layers go elsewhere
            pick.Go.transform.position = pos;
            pick.Src.clip = clip;
            pick.Src.loop = loop;
            pick.Src.volume = Mathf.Clamp01(volume * (output != null ? trim : 1f));
            pick.Src.pitch = pitch > 0f ? pitch : 1f;
            pick.Lp.cutoffFrequency = Mathf.Clamp(cutoff, minCutoff, 22000f);
            pick.Src.time = clipOffset > 0f && clip != null && clip.length > 0f ? Mathf.Repeat(clipOffset, clip.length) : 0f; // 0.6.14: late loop echoes
            pick.Src.PlayScheduled(start);
            pick.Start = start; pick.Owner = owner; pick.Delay = delay; pick.OpenLoop = loop; pick.LoopStart = loopStart > 0.0 ? loopStart : start - delay;
            pick.Cls = cls; pick.Tier = tier; pick.StartDb = startDb; pick.Dur = loop ? 0f : (float)duration; pick.Releasing = false;
            pick.Fading = !loop && fadeS > 0f;
            if (pick.Fading)
            {
                // 0.6.44: done a little before the scheduled end, so the last volume step of a frame-rate fade is already silent
                pick.FadeEnd = start + duration - FadeEarlyS;
                pick.FadeStart = pick.FadeEnd - Mathf.Max(fadeS - (float)FadeEarlyS, MinFadeS);
                pick.FadeBase = pick.Src.volume;
            }
            if (loop)
            {
                pick.BusyUntil = start + LoopSafetyS;
                pick.Src.SetScheduledEndTime(start + LoopSafetyS);
            }
            else
            {
                double end = start + duration;
                pick.Src.SetScheduledEndTime(end);
                pick.BusyUntil = end + 0.02;
            }
            return stole;
        }

        private static int CountOf(EchoClass cls, double now)
        {
            int n = 0;
            foreach (Voice v in Pool) if (v.Cls == cls && v.BusyUntil > now && !v.Releasing) n++;
            return n;
        }

        private static void Count(double now, out int busy, out int own, out int bot)
        {
            busy = own = bot = 0;
            foreach (Voice v in Pool)
            {
                if (v.BusyUntil <= now || v.Releasing) continue;
                busy++;
                if (v.Cls == EchoClass.Own) own++; else if (v.Cls == EchoClass.Bot) bot++;
            }
        }

        public static void Snapshot(out int busy, out int own, out int bot)
        {
            if (!_built) { busy = own = bot = 0; return; }
            Count(AudioSettings.dspTime, out busy, out own, out bot);
        }

        // 0.6.61 (6.3): how many voices bot echoes may hold right now. Other echoes and the voices held free for your gun come off the top.
        private static int BotLimit(int busy, int own, int bot)
        {
            int allowed = Allowed(), held = Mathf.Max(0, Mathf.Min(Plugin.OwnEchoShare.Value, allowed / 2) - own);
            return Mathf.Min(Plugin.BotEchoCeiling.Value, allowed - held - (busy - bot));
        }

        private static bool HasRoom(EchoClass cls, int busy, int own, int bot) => cls == EchoClass.Bot ? bot < BotLimit(busy, own, bot) : busy < Allowed();

        // Bot voices still free to take right now.
        public static int BotHeadroom()
        {
            Build();
            Count(AudioSettings.dspTime, out int busy, out int own, out int bot);
            return BotLimit(busy, own, bot) - bot;
        }

        // How loud an echo is at this moment: a recording's ring dies away, so it counts for less the further through its length it is.
        private static float LevelNow(Voice v, double now)
        {
            if (v.Dur <= 0f || now <= v.Start) return v.StartDb;
            return v.StartDb - FadeOverClipDb * Mathf.Clamp01((float)((now - v.Start) / v.Dur));
        }

        // The bot echo to give up: a later echo of its shooter's set first (a shooter's first echo matters more than another's third), then the quietest now.
        // Your gun and every other sound can take any of them; a bot echo only one that ranks below it. Nothing but bot echoes is ever chosen.
        private static Voice Victim(double now, EchoClass cls, int tier, float startDb)
        {
            Voice worst = null; float worstDb = 0f;
            foreach (Voice v in Pool)
            {
                if (v.Cls != EchoClass.Bot || v.Releasing || v.BusyUntil <= now) continue;
                float db = LevelNow(v, now);
                if (worst == null || v.Tier > worst.Tier || (v.Tier == worst.Tier && db < worstDb)) { worst = v; worstDb = db; }
            }
            if (worst == null || cls != EchoClass.Bot) return worst;
            return worst.Tier > tier || (worst.Tier == tier && worstDb < startDb - ReplaceMarginDb) ? worst : null;
        }

        // Not started yet: nothing to hear, free it. Playing: a short fade, on a voice no longer counted, so the new echo starts on another.
        private static void Release(Voice v, double now)
        {
            v.OpenLoop = false; v.Owner = null;
            if (v.Start > now) { v.Src.Stop(); v.BusyUntil = 0; v.Fading = false; return; }
            v.Releasing = true; v.Fading = true;
            v.FadeBase = v.Src.volume; v.FadeStart = now; v.FadeEnd = now + ReleaseS;
            v.BusyUntil = v.FadeEnd + 0.02;
            v.Src.SetScheduledEndTime(v.FadeEnd + 0.01);
        }

        // The owner queue scheduled a new sample at `nextStart`: its looping echoes end there, each shifted by its delay.
        public static void CloseLoops(object owner, double nextStart)
        {
            if (!_built) return;
            foreach (Voice v in Pool)
            {
                if (!v.OpenLoop || !ReferenceEquals(v.Owner, owner)) continue;
                if (nextStart - v.LoopStart < OwnEchoes.LoopMinS)
                {
                    // 0.6.14: one round - free it at once (normally held back and never scheduled; this is the safety net)
                    v.Src.Stop(); v.BusyUntil = 0; v.OpenLoop = false; v.Owner = null;
                    continue;
                }
                double end = nextStart + v.Delay;
                v.Src.SetScheduledEndTime(end);
                v.BusyUntil = end + 0.02;
                v.OpenLoop = false;
            }
        }

        private static bool _ownStopped;
        private static void StopClass(EchoClass cls, double now)
        {
            foreach (Voice v in Pool)
            {
                if (v.Cls != cls || v.BusyUntil <= now || v.Src == null) continue;
                v.Src.Stop();
                v.BusyUntil = 0; v.OpenLoop = false; v.Owner = null; v.Fading = false; v.Releasing = false;
            }
        }

        public static void StopAll()
        {
            if (!_built) return;
            foreach (Voice v in Pool)
            {
                if (v.Src != null) v.Src.Stop();
                v.BusyUntil = 0; v.OpenLoop = false; v.Owner = null; v.Fading = false; v.Releasing = false;
            }
        }

        private void Update()
        {
            if (!_built) return;
            double now = AudioSettings.dspTime;
            // 0.7.21: this pool also carries the through-wall thump, explosion and bot echoes, the shock wave, fragment pass-bys and body
            // sounds' second way. Your gun's Echoes - on switch used to stop every voice every frame (and skip the fades), so turning it off
            // silently killed all of those. Now it stops only your gun's echoes, once.
            if (!Plugin.EchoOn.Value) { if (!_ownStopped) { StopClass(EchoClass.Own, now); _ownStopped = true; } }
            else _ownStopped = false;
            OwnEchoes.TickPending(now);
            foreach (Voice v in Pool)
            {
                if (v.OpenLoop && now > v.Start + LoopSafetyS - 0.1) { v.OpenLoop = false; v.Owner = null; }
                if (!v.Fading) continue;
                if (now >= v.BusyUntil) { v.Fading = false; v.Releasing = false; continue; }
                if (now <= v.FadeStart) continue;
                float left = Mathf.Clamp01((float)((v.FadeEnd - now) / (v.FadeEnd - v.FadeStart)));
                v.Src.volume = v.FadeBase * left * left;
            }
        }
    }
}
