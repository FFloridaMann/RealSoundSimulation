using System;
using System.Text;
using Audio.SpatialSystem;
using Comfort.Common;
using EFT;
using RealSoundSimulation.Diagnostics;
using UnityEngine;
using UnityEngine.Audio;

namespace RealSoundSimulation.Fixes
{
    // The mixer controls the mod moves. The names are read from the game's own list each frame.
    internal enum MixKey { Main, Guns, World, AmbientIn, AmbientOut, MainRingSend, GunsRingSend, WorldLowpass }

    // Who is asking: the soft hearing cut (6.9h/6.9t), the crack before the cut (6.9r), the game's ring run by the mod (6.9u),
    // the shock wave's dip (6.9c), ear ringing from gunfire (5r).
    internal enum MixBy { Cut, Crack, Ring, Dip, Gunfire }

    // 13 Nov 2026 "mixer owner": one place writes the game's mixer volumes. Before this, the soft hearing cut, the crack before the cut,
    // the hold against the game's ring, the shock wave's dip and the gunfire dulling each read a control, kept their own copy of where it
    // "should" go back to, and wrote it themselves; the game's ring wrote the same controls every frame as well. They fought. From the
    // raids of 13 Nov 2026: Main and Guns stayed 6 dB down for about 10 s after a blast (the crack's headset trim was never taken off),
    // and with no headset the user heard gun audio flickering "in and out", then voices and guns going "in and out" until extraction.
    //   ask      a feature asks for a control by name and by who it is: so many dB up or down, or for the world's low-pass a ceiling in
    //            Hz. It asks again every frame while it wants it, or once with a length. An ask nobody renews lapses after a quarter of
    //            a second, so a feature that stops (an error, a raid ending) can never leave a control stuck.
    //   write    last in the frame, for each control somebody asks for: the game's own value plus every ask added up (the lowest
    //            ceiling for the low-pass). "Alone" asks (the crack) sit on the game's normal value and the others wait under them.
    //   normal   the game's own value: Guns is the headset's DryVolume, read from the headset each frame (so it follows a headset
    //            change at once); World is 0; the two ring sends are -80; Main is what the game last set while nobody asked (the game's
    //            own ring not counted); the ambience volumes and the low-pass are read when the first ask arrives.
    //   others   if something else writes a control while the mod holds it (the game's fades, the game's own ring when the mod isn't
    //            running it, Bring Back Concussion's own cut), its value becomes the ground the asks stand on, so two writers add up
    //            instead of taking turns. A control the game has muted (-60 dB or lower) is left alone.
    //   end      when the last ask on a control ends, its normal value is written once and the control isn't touched again.
    //   raids    only in a raid. In menus and the hideout, and at a raid's end, every ask is dropped and nothing is written: the game
    //            owns its controls there.
    // The ambience blender (AmbienceMix) and the doorway snapshots (DoorwayBlend) use other controls and aren't part of this.
    internal static class MixerOwner
    {
        public const string Header = "t,ring,headset,tplDryDb,mainDb,mainBy,gunsDb,gunsBy,worldDb,worldBy,lowpassHz,lowpassBy,mainRingSendDb,mainRingSendBy,gunsRingSendDb,gunsRingSendBy,ambientInDb,ambientInBy,ambientOutDb,ambientOutBy";
        public const float SendOffDb = -80f;

        private const int KeyCount = 8, OwnerCount = 5, Main = (int)MixKey.Main, Guns = (int)MixKey.Guns, World = (int)MixKey.World, Lowpass = (int)MixKey.WorldLowpass;
        private const float LeaseS = 0.25f, MovedByOther = 0.05f, MovedByOtherHz = 5f, TooQuietDb = -60f, StepDb = 0.01f, StepHz = 1f, FloorDb = -80f, SaneLowpassHz = 100f, LogEveryS = 0.5f;
        private static readonly string[] OwnerName = { "cut", "crack", "ring", "dip", "gunfire" };
        private static readonly int[] LogOrder = { Main, Guns, World, Lowpass, (int)MixKey.MainRingSend, (int)MixKey.GunsRingSend, (int)MixKey.AmbientIn, (int)MixKey.AmbientOut };

        private struct Ask { public bool On, Alone; public float Value, Until; }
        private struct Line { public string Name; public bool Held, Foreign; public float Normal, Theirs, Last; }

        private static readonly Ask[,] Asks = new Ask[KeyCount, OwnerCount];
        private static readonly Line[] Lines = new Line[KeyCount];
        private static readonly int[] Started = new int[OwnerCount];
        private static readonly StringBuilder Sb = new StringBuilder(256);
        private static float _mainIdle = float.NaN, _nextRow;
        private static bool _inRaid, _broken;

        public static int FramesWritten, FramesFollowed;

        // In a raid and working: asks are taken. Out of a raid they'd be dropped anyway.
        public static bool Running => _inRaid && !_broken;

        public static void Reset()
        {
            Drop();
            _broken = false; _nextRow = 0f;
            FramesWritten = FramesFollowed = 0;
            for (int o = 0; o < OwnerCount; o++) Started[o] = 0;
        }

        // ---------------- asking ----------------

        // So many dB on top of the game's value (negative = quieter). Renew it every frame, or give it a length.
        public static void Offset(MixKey key, MixBy by, float db, float forS = LeaseS) => Put(key, by, db, forS, false);

        // The control sits at the game's normal value plus this, and every other ask on it waits. For the crack before the cut.
        public static void Alone(MixKey key, MixBy by, float db, float forS) => Put(key, by, db, forS, true);

        // The world's low-pass comes down to at most this.
        public static void Ceiling(MixBy by, float hz, float forS = LeaseS) => Put(MixKey.WorldLowpass, by, hz, forS, false);

        public static void Clear(MixKey key, MixBy by) => Asks[(int)key, (int)by].On = false;

        public static void Clear(MixBy by)
        {
            for (int k = 0; k < KeyCount; k++) Asks[k, (int)by].On = false;
        }

        private static void Put(MixKey key, MixBy by, float value, float forS, bool alone)
        {
            if (!Running || float.IsNaN(value) || float.IsInfinity(value)) return;
            ref Ask a = ref Asks[(int)key, (int)by];
            if (!a.On) Started[(int)by]++;
            a.On = true; a.Alone = alone; a.Value = value; a.Until = Time.unscaledTime + forS;
        }

        // Where the world's low-pass normally sits, to work out how far to bring it down.
        public static bool LowpassNormal(out float hz)
        {
            hz = 0f;
            if (!Running) return false;
            ref Line l = ref Lines[Lowpass];
            if (l.Held) { hz = l.Foreign ? l.Theirs : l.Normal; return hz > SaneLowpassHz; }
            try
            {
                BetterAudio ba = MonoBehaviourSingleton<BetterAudio>.Instance;
                string name = ba.AudioMixerData.WorldMixerLowpassFreq;
                return !string.IsNullOrEmpty(name) && ba.Master.GetFloat(name, out hz) && hz > SaneLowpassHz;
            }
            catch { return false; }
        }

        // Something outside the mod is about to read the controls and keep what it reads as "where to go back to" (Bring Back
        // Concussion's own cut does). Show it the game's values without the mod's asks, or it would put the mod's dip back later.
        public static void ShowNormal()
        {
            if (!Running) return;
            try
            {
                BetterAudio ba = MonoBehaviourSingleton<BetterAudio>.Instance;
                AudioMixer m = ba.Master;
                float dry = Dry(ba);
                for (int k = 0; k < KeyCount; k++)
                {
                    ref Line l = ref Lines[k];
                    if (!l.Held || string.IsNullOrEmpty(l.Name)) continue;
                    if (!m.GetFloat(l.Name, out float cur) || Mathf.Abs(cur - l.Last) > MovedByOther) continue; // someone else's value is already there
                    float plain = l.Foreign ? l.Theirs : NormalOf(k, dry, ref l);
                    if (Follows(k) && plain <= TooQuietDb) continue;
                    m.SetFloat(l.Name, plain);
                    l.Last = m.GetFloat(l.Name, out float kept) ? kept : plain;
                }
            }
            catch (Exception e) { DiagRunner.ReportOnce("mixer owner (show normal)", e); }
        }

        // ---------------- every frame, last ----------------

        // The same checks as the features use: a raid's sound system is up, the world isn't the menu's or the hideout's, and you exist.
        private static bool InRaid(out BetterAudio ba)
        {
            ba = null;
            if (!MonoBehaviourSingleton<BetterAudio>.Instantiated || !SpatialAudioSystem.Initialized) return false;
            if (!Singleton<GameWorld>.Instantiated) return false;
            GameWorld world = Singleton<GameWorld>.Instance;
            if (world == null || world is HideoutGameWorld || world.MainPlayer == null) return false;
            ba = MonoBehaviourSingleton<BetterAudio>.Instance;
            return ba != null && ba.Master != null && ba.AudioMixerData != null;
        }

        internal static void Frame()
        {
            BetterAudio ba;
            try
            {
                if (!InRaid(out ba))
                {
                    if (_inRaid) { Drop(); GameRing.Quiet(); }
                    _inRaid = false;
                    return;
                }
                _inRaid = true;
            }
            catch (Exception e) { DiagRunner.ReportOnce("mixer owner (raid check)", e); return; }

            if (!_broken)
            {
                try
                {
                    GameRing.Tick(ba);
                    Write(ba);
                }
                catch (Exception e)
                {
                    // The owner can't be trusted this raid: every control goes back to normal, the game runs its own ring again, and
                    // the features' asks are ignored until the next raid.
                    DiagRunner.ReportOnce("mixer owner", e);
                    _broken = true;
                    try { LetGo(ba); } catch { }
                    try { GameRing.HandBack(ba); } catch { }
                    Drop();
                }
            }
            try { Log(ba); }
            catch (Exception e) { DiagRunner.ReportOnce("mixer owner (log)", e); }
            ConcussionProof.AfterMix(ba);
        }

        internal static void Off()
        {
            Drop();
            GameRing.Quiet();
            _inRaid = false;
        }

        // Forget everything without writing: the game owns its controls now.
        private static void Drop()
        {
            for (int k = 0; k < KeyCount; k++)
            {
                for (int o = 0; o < OwnerCount; o++) Asks[k, o].On = false;
                Lines[k].Held = Lines[k].Foreign = false;
            }
            _mainIdle = float.NaN;
        }

        private static float Dry(BetterAudio ba)
        {
            try { return ba.CurrentHeadphonesTemplate.DryVolume; }
            catch { return 0f; }
        }

        // Main and the ambience volumes are moved by the game itself (fades, mutes), so a muted one is left alone.
        private static bool Follows(int k) => k == Main || k == (int)MixKey.AmbientIn || k == (int)MixKey.AmbientOut;

        private static float NormalOf(int k, float dry, ref Line l)
        {
            if (k == Guns) return dry;
            if (k == World) return 0f;
            if (k == (int)MixKey.MainRingSend || k == (int)MixKey.GunsRingSend) return SendOffDb;
            return l.Normal; // Main, the ambience volumes and the low-pass: read from the game
        }

        private static void Names(BetterAudio ba)
        {
            var d = ba.AudioMixerData;
            Lines[Main].Name = d.MainMixerVolume;
            Lines[Guns].Name = d.GunsMixerVolume;
            Lines[World].Name = d.WorldMixerVolume;
            Lines[(int)MixKey.AmbientIn].Name = d.AmbientInMixerVolume;
            Lines[(int)MixKey.AmbientOut].Name = d.AmbientOutMixerVolume;
            Lines[(int)MixKey.MainRingSend].Name = d.MainMixerTinnitusSendLevel;
            Lines[(int)MixKey.GunsRingSend].Name = d.GunsMixerTinnitusSendLevel;
            Lines[Lowpass].Name = d.WorldMixerLowpassFreq;
        }

        private static void Write(BetterAudio ba)
        {
            AudioMixer m = ba.Master;
            Names(ba);
            float now = Time.unscaledTime, dry = Dry(ba);
            bool wrote = false, followed = false;
            for (int k = 0; k < KeyCount; k++)
            {
                bool any = false, alone = false;
                float sum = 0f, aloneSum = 0f, ceiling = float.PositiveInfinity;
                for (int o = 0; o < OwnerCount; o++)
                {
                    ref Ask a = ref Asks[k, o];
                    if (!a.On) continue;
                    if (now >= a.Until) { a.On = false; continue; }
                    any = true;
                    if (k == Lowpass) ceiling = Mathf.Min(ceiling, a.Value);
                    else if (a.Alone) { alone = true; aloneSum += a.Value; }
                    else sum += a.Value;
                }

                ref Line l = ref Lines[k];
                if (string.IsNullOrEmpty(l.Name)) { l.Held = false; continue; }
                if (!any)
                {
                    if (l.Held)
                    {
                        // The last ask has ended: back to normal once, unless someone else has just set it.
                        l.Held = false;
                        if (m.GetFloat(l.Name, out float at) && Mathf.Abs(at - l.Last) <= MovedByOther)
                        {
                            float back = (Follows(k) || k == Lowpass) && l.Foreign ? l.Theirs : NormalOf(k, dry, ref l);
                            if (Mathf.Abs(back - at) > 0.0001f) { m.SetFloat(l.Name, back); wrote = true; }
                        }
                        if (k == Main && !GameRing.GameRuns(ba) && m.GetFloat(l.Name, out float after)) _mainIdle = after;
                    }
                    else if (k == Main && !GameRing.GameRuns(ba) && m.GetFloat(l.Name, out float idle)) _mainIdle = idle; // what the game set, while nobody asks
                    continue;
                }

                if (!m.GetFloat(l.Name, out float cur)) continue;
                if (!l.Held)
                {
                    if (k == Lowpass && cur <= SaneLowpassHz) continue; // not a low-pass we understand
                    if (k == Main) l.Normal = float.IsNaN(_mainIdle) ? cur : _mainIdle;
                    else if (k != Guns && k != World) l.Normal = cur; // ambience and low-pass: as they are now (unused for the sends)
                    l.Held = true; l.Foreign = false;
                    l.Last = NormalOf(k, dry, ref l); // what should be there; anything else was put there by another writer
                }
                float normal = NormalOf(k, dry, ref l);
                if (Mathf.Abs(cur - l.Last) > (k == Lowpass ? MovedByOtherHz : MovedByOther)) { l.Theirs = cur; l.Foreign = true; followed = true; }
                float ground = l.Foreign ? l.Theirs : normal;

                float value;
                if (k == Lowpass) value = Mathf.Min(ground, ceiling);
                else
                {
                    if (Follows(k) && ground <= TooQuietDb) { l.Last = cur; continue; } // muted by the game (raid start, death, menus): not ours to move
                    value = Mathf.Max(alone ? normal + aloneSum : ground + sum, FloorDb);
                }
                if (Mathf.Abs(value - cur) > (k == Lowpass ? StepHz : StepDb))
                {
                    // Read back what the mixer kept (it may clamp), so its own rounding is never taken for another writer.
                    m.SetFloat(l.Name, value);
                    l.Last = m.GetFloat(l.Name, out float kept) ? kept : value;
                    wrote = true;
                }
                else l.Last = cur;
            }
            if (wrote) FramesWritten++;
            if (followed) FramesFollowed++;
        }

        // Every held control back to normal, now. Only when the owner gives up.
        private static void LetGo(BetterAudio ba)
        {
            AudioMixer m = ba.Master;
            float dry = Dry(ba);
            for (int k = 0; k < KeyCount; k++)
            {
                ref Line l = ref Lines[k];
                if (!l.Held || string.IsNullOrEmpty(l.Name)) continue;
                if (m.GetFloat(l.Name, out float at) && Mathf.Abs(at - l.Last) <= MovedByOther)
                    m.SetFloat(l.Name, (Follows(k) || k == Lowpass) && l.Foreign ? l.Theirs : NormalOf(k, dry, ref l));
            }
        }

        // ---------------- logging ----------------

        // Who is asking for this control now, with how much: "cut:-10.0+ring:-35.2"; an ask that stands alone is marked "!".
        public static string By(MixKey key)
        {
            var sb = new StringBuilder();
            AppendBy(sb, (int)key);
            return sb.ToString();
        }

        private static void AppendBy(StringBuilder sb, int k)
        {
            float now = Time.unscaledTime;
            bool first = true;
            for (int o = 0; o < OwnerCount; o++)
            {
                Ask a = Asks[k, o];
                if (!a.On || now >= a.Until) continue;
                if (!first) sb.Append('+');
                first = false;
                sb.Append(OwnerName[o]);
                if (a.Alone) sb.Append('!');
                sb.Append(':').Append(DiagLog.F(a.Value, k == Lowpass ? "0" : "0.0"));
            }
        }

        // mixer.csv, every half second: where each control ended up this frame and who asked for it.
        private static void Log(BetterAudio ba)
        {
            if (!DiagLog.IsOpen) return;
            float now = Time.unscaledTime;
            if (now < _nextRow) return;
            _nextRow = now + LogEveryS;
            AudioMixer m = ba.Master;
            if (string.IsNullOrEmpty(Lines[Main].Name)) Names(ba);
            Player player = Singleton<GameWorld>.Instance.MainPlayer;
            Sb.Clear();
            Sb.Append(DiagLog.F(Time.time - DiagRunner.RaidStartTime, "0.0")).Append(',')
              .Append(GameRing.Ours ? "mod" : GameRing.GameRuns(ba) ? "game" : "").Append(',')
              .Append(GunfireHearing.WearsHeadset(player) ? '1' : '0').Append(',')
              .Append(DiagLog.F(Dry(ba), "0.0"));
            foreach (int k in LogOrder)
            {
                Sb.Append(',');
                if (!string.IsNullOrEmpty(Lines[k].Name) && m.GetFloat(Lines[k].Name, out float v)) Sb.Append(DiagLog.F(v, k == Lowpass ? "0" : "0.0"));
                Sb.Append(',');
                AppendBy(Sb, k);
            }
            DiagLog.Row("mixer.csv", Sb.ToString());
        }

        public static string Summary()
        {
            var asks = new StringBuilder();
            for (int o = 0; o < OwnerCount; o++) asks.Append(o > 0 ? ", " : "").Append(OwnerName[o]).Append(' ').Append(Started[o]);
            return $"MIXER OWNER ({(_broken ? "STOPPED after an error, see errors.txt" : "on")}; the game's ring run by the mod, 6.9u {(Plugin.RingTakeoverOn.Value ? "ON" : "off")}{(GameRing.Failed ? ", stopped after an error" : "")}): " +
                   $"asks started by owner: {asks}; frames written {FramesWritten}; frames on top of another writer {FramesFollowed}; " +
                   $"rings taken over {GameRing.TakenOver} (started again by a new blast {GameRing.Restarted}, left to the game {GameRing.LeftToGame}, handed back after an error {GameRing.HandedBack}); " +
                   $"ring held by gunfire {GameRing.HeldFrames + GunfireHearing.Holds} frames, ring extensions capped {GameRing.Capped + GunfireHearing.HoldsCapped}  (mixer.csv)\n";
        }
    }

    // Runs after everything that asks: ShockWave (32000) and ConcussionProof (32002) come before it in the frame.
    [DefaultExecutionOrder(32010)]
    internal class MixerOwnerRunner : MonoBehaviour
    {
        private void LateUpdate() => MixerOwner.Frame();

        private void OnDestroy() => MixerOwner.Off();
    }
}
