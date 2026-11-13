using System;
using System.Collections;
using System.Reflection;
using Comfort.Common;
using EFT;
using HarmonyLib;
using RealSoundSimulation.Diagnostics;
using UnityEngine;

namespace RealSoundSimulation.Fixes
{
    // 13 Nov 2026 "6.9u": the game's ear ring after a blast or a head hit, run by the mod. The game's own ring
    // (BetterAudio.StartTinnitusEffect, which starts the coroutine method_8) lasts at least 15 s and writes Guns, Main and both ring
    // sends every frame. The mod's hearing cut wrote the same controls back every frame, and gunfire kept pushing the ring's end later.
    // The user, with no headset, heard gun audio flickering "in and out", then voices and guns going "in and out" until extraction.
    //   start    the game's call is taken over in a raid. The ring's length and end time are set exactly as the game sets them (float_1
    //            and float_2), so everything that asks "is the game ringing" still gets the right answer: this mod, and Bring Back
    //            Concussion. The game's coroutine isn't started.
    //   sound    the same ring recording, not placed in the world, on the Master group, at the game's own loudness curve
    //            (EFTHardSettings.TinnitusSound over the ring's length).
    //   deaf     the game's own curves (MainChannelLevel for Main and Guns, TinnitusLowpas for the two ring sends, both at the game's
    //            speed multiplier), asked for through the mixer owner, so they add to the mod's cut instead of fighting it.
    //   heard    with "blast heard through the cut" (6.9t) on, the deafening stops where the blast is still heard at "blast heard at
    //            dB" and the world at "world heard at dB", counting what the mod's own cut already takes off. Off = as the game.
    //   held     firing with gunfire ear ringing (5r) on keeps the ring's last 2.5 s going, as before, but only the sound, and for at
    //            most 10 s in all. The deafening always runs its own course. It used to be pushed along with the sound without limit.
    //   again    a new blast while it rings starts both again from the top, as in the game.
    //   pause    the game's TogglePauseTinnitusEffect only sets a flag nothing reads and runs an action nothing sets, so there is
    //            nothing to follow there.
    //   fallback the switch off, the mixer owner not running, no raid (the hideout), or any error: the game runs its own ring. A ring
    //            already running here when something fails is handed to the game's coroutine, which carries on from the same point.
    internal static class GameRing
    {
        private const float MinLengthS = 15f, HoldWhenLeftS = 2.5f;
        internal const float MaxHeldS = 10f;

        private static AccessTools.FieldRef<BetterAudio, float> _ringEnd, _ringLength;
        private static MethodInfo _gameRoutine;
        private static AudioSource _source;
        private static AudioClip _clip;
        private static bool _ours, _capCounted;
        private static float _end, _length, _held;

        public static bool Failed;
        public static int TakenOver, Restarted, LeftToGame, HandedBack, HeldFrames, Capped;

        // The mod is running the ring now.
        public static bool Ours => _ours;

        // The game's own coroutine is running the ring and writing the mixer: the mod isn't.
        public static bool GameRuns(BetterAudio ba) => !_ours && _ringEnd != null && ba != null && _ringEnd(ba) > Time.time;

        public static void Apply(Harmony h)
        {
            _ringLength = AccessTools.FieldRefAccess<BetterAudio, float>("float_1");
            _ringEnd = AccessTools.FieldRefAccess<BetterAudio, float>("float_2");
            _gameRoutine = AccessTools.Method(typeof(BetterAudio), "method_8", new[] { typeof(AudioClip) });
            h.Patch(AccessTools.Method(typeof(BetterAudio), nameof(BetterAudio.StartTinnitusEffect)),
                prefix: new HarmonyMethod(typeof(GameRing), nameof(StartPrefix)));
        }

        public static void Reset()
        {
            Quiet();
            Failed = false;
            TakenOver = Restarted = LeftToGame = HandedBack = HeldFrames = Capped = 0;
        }

        // The game is about to start, or lengthen, its ring. False = the mod runs it instead.
        private static bool StartPrefix(BetterAudio __instance, float time, AudioClip clip)
        {
            if (Failed || _ringEnd == null || !MixerOwner.Running) return true;
            bool wasOurs = _ours, set = false;
            float wasEnd = 0f, wasLength = 0f;
            try
            {
                wasEnd = _ringEnd(__instance); wasLength = _ringLength(__instance);
                if (!_ours && wasEnd > Time.time) { LeftToGame++; return true; } // the game's coroutine has this ring already: it carries on
                AudioClip use = clip != null ? clip : GunfireHearing.ClipOf(Singleton<GameWorld>.Instance.MainPlayer);
                if (use == null) { LeftToGame++; return true; }
                AudioSource src = Source();
                src.outputAudioMixerGroup = __instance.MasterMixerGroup; // where the game's own ring plays; follows the overall volume

                float length = Mathf.Max(MinLengthS, time * 2f); // as the game
                _ringLength(__instance) = length;
                _ringEnd(__instance) = length + Time.time;
                set = true;
                _length = length; _end = length + Time.time; _held = 0f; _capCounted = false;
                _clip = use;
                if (_ours) Restarted++; else TakenOver++;
                _ours = true;
                src.volume = EFTHardSettings.Instance.TinnitusSound.Evaluate(0f);
                if (src.clip != use || !src.isPlaying) { src.clip = use; src.Play(); }
                return false;
            }
            catch (Exception e)
            {
                // Leave the game's two numbers so that its own call, which runs next, starts its coroutine.
                Fail("6.9u game ring (start)", e);
                Silence();
                if (set || wasOurs)
                {
                    try { _ringLength(__instance) = wasLength; _ringEnd(__instance) = wasOurs ? 0f : wasEnd; } catch { }
                }
                return true;
            }
        }

        // Every frame, from the mixer owner, before it writes.
        internal static void Tick(BetterAudio ba)
        {
            if (!_ours) return;
            try
            {
                float now = Time.time, end = _end + _held;
                if (now >= end) { Silence(); return; }
                EFTHardSettings s = EFTHardSettings.Instance;

                // The deafening, on the ring's own length, never held.
                if (now < _end)
                {
                    float x = s.TinnitusEffectSpeedMultiplier * (1f - (_end - now) / _length);
                    float send = s.TinnitusLowpas.Evaluate(x), deaf = s.MainChannelLevel.Evaluate(x);
                    float guns = deaf, main = deaf;
                    if (Plugin.HeardThroughOn.Value)
                    {
                        // Guns sit under World, and Main under World too: what the mod's cut takes off already counts towards the floor.
                        float gunsFloor = Mathf.Min(0f, Plugin.BlastHeardDb.Value - ConcussionProof.CutWorldDb);
                        float mainFloor = Mathf.Min(0f, Plugin.WorldHeardDb.Value - ConcussionProof.CutWorldDb - ConcussionProof.CutMainDb);
                        if (guns < gunsFloor) guns = gunsFloor;
                        if (main < mainFloor) main = mainFloor;
                    }
                    MixerOwner.Offset(MixKey.Main, MixBy.Ring, main);
                    MixerOwner.Offset(MixKey.Guns, MixBy.Ring, guns);
                    MixerOwner.Offset(MixKey.MainRingSend, MixBy.Ring, send - MixerOwner.SendOffDb);
                    MixerOwner.Offset(MixKey.GunsRingSend, MixBy.Ring, send - MixerOwner.SendOffDb);
                }
                else MixerOwner.Clear(MixBy.Ring);

                // The sound: the same curve, standing still while gunfire holds it.
                AudioSource src = Source();
                src.volume = s.TinnitusSound.Evaluate(1f - (end - now) / _length);
                if (!src.isPlaying && _clip != null) { src.clip = _clip; src.Play(); }
            }
            catch (Exception e)
            {
                Fail("6.9u game ring", e);
                HandBack(ba);
            }
        }

        // Gunfire ear ringing (5r): you're firing while the ring plays. True = the mod runs this ring and has dealt with it.
        // Only the ring's last seconds are held, as before, and only for so long in all.
        internal static bool Hold(BetterAudio ba)
        {
            if (!_ours) return false;
            if (_end + _held - Time.time >= HoldWhenLeftS) return true;
            if (_held >= MaxHeldS)
            {
                if (!_capCounted) { _capCounted = true; Capped++; }
                return true;
            }
            _held = Mathf.Min(_held + Time.deltaTime, MaxHeldS);
            _ringEnd(ba) = _end + _held; // "is the game ringing" stays true while the ring is heard
            HeldFrames++;
            return true;
        }

        // Something failed while the mod ran the ring: the game's coroutine takes it from here, from the same point on its curves.
        internal static void HandBack(BetterAudio ba)
        {
            if (!_ours) return;
            AudioClip clip = _clip;
            float end = _end;
            Silence();
            try
            {
                if (ba == null || _gameRoutine == null || end <= Time.time) return;
                _ringEnd(ba) = end; // without what gunfire held
                MixerOwner.ShowNormal(); // the coroutine keeps the Main it reads now as its own starting point
                ba.StartCoroutine((IEnumerator)_gameRoutine.Invoke(ba, new object[] { clip }));
                HandedBack++;
            }
            catch (Exception e) { DiagRunner.ReportOnce("6.9u game ring (hand back)", e); }
        }

        // The raid is over or the mod is switching off: stop the sound, write nothing, and don't leave "ringing" set with no ring.
        internal static void Quiet()
        {
            bool was = _ours;
            Silence();
            if (!was || _ringEnd == null) return;
            try
            {
                if (!MonoBehaviourSingleton<BetterAudio>.Instantiated) return;
                BetterAudio ba = MonoBehaviourSingleton<BetterAudio>.Instance;
                if (_ringEnd(ba) > Time.time) _ringEnd(ba) = Time.time;
            }
            catch { }
        }

        private static void Silence()
        {
            _ours = false;
            MixerOwner.Clear(MixBy.Ring);
            try { if (_source != null) _source.Stop(); } catch { }
        }

        private static void Fail(string what, Exception e)
        {
            Failed = true; // the game's own ring until the next raid
            DiagRunner.ReportOnce(what, e);
        }

        private static AudioSource Source()
        {
            if (_source != null) return _source;
            var go = new GameObject("RealSoundSimulation game ring");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _source = go.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _source.loop = true;
            _source.priority = 0;
            return _source;
        }
    }
}
