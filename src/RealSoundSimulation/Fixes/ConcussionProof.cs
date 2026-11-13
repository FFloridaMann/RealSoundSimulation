using System;
using System.Reflection;
using Audio.SpatialSystem;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using RealSoundSimulation.Acoustics;
using RealSoundSimulation.Diagnostics;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace RealSoundSimulation.Fixes
{
    // 6.9h (0.6.55, reworked 0.6.57, 0.6.58): hearing goes when concussion hits, the blast is still felt, and nothing is chopped. User, 9 Nov 2026: "better if the hearing cuts out with concussion when the effect is applied.
    // but we find a way to keep the feeling of the blast wave alive and no sharp cut offs for tails".
    // Bring Back Concussion takes Main, Guns, World and both ambience volumes down, by closeness, in 0.05 to 0.3 s. Explosions, echoes and tails are on Guns, so they were cut off mid-sound.
    //   cut      that mod's cut is taken over and copied: same depth, cut time, hold, fade-back (its own setting), same ringing check. World sounds still go at its speed.
    //   tail     only Guns differs: it goes down over "6.9i" s, so the bang, echoes and tails melt away.
    //   felt     inside the part of its radius where it cuts (95%), the pressure hit, thump, repeat hits and slow push go to the Master group, which no cut touches, at "6.9j". Echoes and the recording stay on Guns and fade.
    // The cut's depth, timing and tinnitus check are adapted from Bring Back Concussion by harmony (harmonyzt), MIT: see THIRD_PARTY_NOTICES.md.
    // That mod's code is found by name once a raid starts. If it's gone or changed, its own cut runs as normal. The game's own ringing (no headset) still drives Main and Guns, as that mod allows.
    //
    // 0.6.60 "6.9k": how concussive a blast is comes from its pressure, not the straight-line distance. User, 9 Nov 2026: "shouldnt it be more concussive indoors", and research "a wall may protect but the blast through a door delivers concussive effects ... room indoors but very big? very small? lots of windows no windows".
    // ShockWave.Dose works out the pressure by the way it reached you and the blast's room. That's turned into the open-air distance with the same pressure, and that mod's own explosion patch is called with the blast moved to that distance, so its radius and closeness still decide.
    // Two levels: ears (the sound cut, and ringing) reach "6.9l" dB further than the full concussion. Length and strength scale with how long the pressure lasted and how close it was. That mod's patch is called a frame late, because the game queues the blast sound and the route is only known then.
    //
    // 0.7.10 "6.9q": headsets protect your hearing. User, 10 Nov 2026: "for the time being, all headsets should remove ear ringing and most hearing loss from concussions. so they function well then later actually improved and optionally simulated."
    // Ringing already never plays with a headset (the game's, that mod's, and the ears-only ringing all check the earpiece slot). The hearing cut didn't check, so with any headset on
    // its closeness is scaled by (1 - "6.9q"): at 75% the closest blast cuts to -20 dB with a shorter hold, and light blasts don't cut. Concussion itself, the felt thump and blur are unchanged.
    //
    // 0.7.22 "6.9r": the crack before the cut. User, 11 Nov 2026, of a grenade 3.8 m away round a doorway (headset on): "maybe you should hear
    // the blast for a split second at the beginning? it just sounds like duuum". blastlevel.csv showed Guns already about -50 dB 0.17 s
    // after close blasts (also raid_20261110_212706), far deeper than our cut (-12 dB with the headset), so another writer cuts at once and
    // only the felt layers (pressure hit dulled to ~1.9 kHz, thump ~100 Hz) were left. In reality the ear's reflex and the hearing loss come
    // after the bang. So for "6.9r" ms after a blast that will cut (shorter and 6 dB quieter with a headset), Main and Guns are held at what
    // they were before the blast, whoever writes them (last in the frame); then any other writer's value is put back (their cut, a split
    // second late) unless our cut runs, and our cut starts only then. blastmix.csv: both volumes every frame for 1.5 s after a blast, to
    // find who the other writer is.
    // Found (raid_20261111_192106, no headset): the game's own ring (BetterAudio.method_8, tinnitus) sets Guns to the headset template's
    // DryVolume plus a -50 dB curve and sends Main and Guns to the tinnitus low-pass, from the frame the blast is processed, often before
    // our sound arrives. With a headset on, Guns *normally* sits at the template's DryVolume (-50/-60: a headset carries guns on its own
    // compressor path), which is what the earlier -50 dB readings were. So the window now holds Guns at DryVolume, Main at its value from
    // before the blast (never read while the game rings), and both tinnitus sends at -80, then gives the game its values back.
    //
    // 0.7.22 "6.9t": the blast heard through the cut. User (via the SEB comparison session): "grenades when you lose the hearing just feel a
    // bit dull. maybe we need to let more of the explosion noise through the whole time", then "Try it". After the crack only the felt
    // layers were left: World, Guns and Main all went to the cut's full depth (to -58 dB), and without a headset the game's ring also pulls
    // Guns down 50 dB. Real hearing after a blast goes muffled, not silent: highs first, the rumble, echoes and the world stay, roughly
    // 20-40 dB down, under the ringing. The mixer (mastermixer.bundle): Master > InGame > World > {Headphones, Guns, Main, Occlusion}, and
    // World has an exposed low-pass (WorldMixerLowpassFreq). So with 6.9t on: World goes down only to "blast heard at dB" and its low-pass
    // to "muffled to Hz" (less for a light cut); Main goes a little further, to "world heard at dB"; Guns and the ambience add nothing.
    // While the game's ring runs (no headset) its pull on Guns and Main is held at those floors, so the blast is still heard under it.
    // Also fixed: every key went to the cut's absolute depth, so with a headset (Guns normally at the template's DryVolume, -50/-60) the
    // cut *raised* dry Guns to about -13 dB. Depths are now offsets from where each key started.
    //
    // 13 Nov 2026 "mixer owner": nothing here writes the mixer any more. The cut, the crack and the ring's deafening are asked for
    // through MixerOwner, which adds them up on the game's own values and writes once, last in the frame. The bugs came from each
    // part keeping its own copy of "where to go back to": in that day's raids the crack's headset trim stayed on Main and Guns for
    // about 10 s, and the hold against the game's ring and the ring itself took turns every frame, so the user heard gun audio
    // flickering "in and out"; and PutBack restored values read at the blast, wrong after a headset change. So the copies are gone (Start, Last,
    // Calm, Normal, Theirs, PutBack): the cut is only so many dB down from whatever the game's value is now, the crack is an ask
    // that stands alone for its length and then lapses by itself, and the game's ring is run by the mod (GameRing, 6.9u) with its
    // deafening stopped at the 6.9t floors. Bring Back Concussion's own cut, when it runs instead of ours, is shown the game's values
    // first (MixerOwner.ShowNormal), because it keeps what it reads and puts it back later.
    [DefaultExecutionOrder(32002)]
    internal class ConcussionProof : MonoBehaviour
    {
        public const string MixHeader = "t,sinceBlastS,mainDb,gunsDb,worldDb,gameRinging,ourCut,window,mainBy,gunsBy"; // 13 Nov 2026: who asks, in place of the other writer's values
        private const float HeadsetCrackShare = 0.6f, HeadsetCrackDb = -6f, MinCrackS = 0.04f, MixLogS = 1.5f;
        private static float _crackUntil = -1f, _blastAt = -100f;
        private static readonly string[] HeadsetKeys = { "MainVolume", "GunsVolume", "AmbientVolume", "AmbientOccluded", "EnvironmentVolume", "OcclusionVolume", "OutEnvironmentVolume", "HeadphonesMixerVolume", "AmbientCompressorSendLevel" };
        public const string HeadsetHeader = "t,headset,tplDryDb,tplAmbientDb,mainDb,gunsDb,ambientDb,ambientOccludedDb,environmentDb,occlusionDb,outEnvironmentDb,headphonesDb,ambientCompressorSendDb";
        private static float _nextHeadsetRow;
        public static int Cracks;

        // What the cut takes off World and Main right now (0 when it isn't running). GameRing counts these towards the 6.9t floors.
        public static float CutWorldDb, CutMainDb;

        // ShockWave: a blast's sound has just started. blastmix.csv logs every frame for a while after it.
        public static void BlastEvent() => _blastAt = Time.unscaledTime;

        // ShockWave: this blast will cut your hearing; let it be heard first. playedDsp = when its sound started.
        // Main and Guns stand at the game's normal values (less 6 dB with a headset) and the ring sends stay off, whatever else asks,
        // until the window ends; then the asks lapse and the ring's deafening and the cut come through.
        public static void Crack(double playedDsp)
        {
            if (!Plugin.CrackOn.Value || Plugin.CrackMs.Value <= 0f) return;
            bool hs = HeadsetOn();
            float len = Plugin.CrackMs.Value / 1000f * (hs ? HeadsetCrackShare : 1f);
            float late = (float)Math.Max(0.0, AudioSettings.dspTime - playedDsp);
            float trim = hs ? HeadsetCrackDb : 0f, forS = Mathf.Max(len - late, MinCrackS);
            MixerOwner.Alone(MixKey.Main, MixBy.Crack, trim, forS);
            MixerOwner.Alone(MixKey.Guns, MixBy.Crack, trim, forS);
            MixerOwner.Alone(MixKey.MainRingSend, MixBy.Crack, 0f, forS); // no tinnitus low-pass yet
            MixerOwner.Alone(MixKey.GunsRingSend, MixBy.Crack, 0f, forS);
            _crackUntil = Time.unscaledTime + forS;
            Cracks++;
        }

        private static float CrackLeft() => Mathf.Max(0f, _crackUntil - Time.unscaledTime);

        // Called by the mixer owner once it has written this frame, so the rows show what is really heard.
        internal static void AfterMix(BetterAudio ba)
        {
            try
            {
                if (!DiagLog.IsOpen) return;
                AudioMixer m = ba.Master;
                float now = Time.unscaledTime;
                // 0.7.22: the headset's own mixer values every 5 s, to see whether anything moves them off the template (user: "I feel like
                // I hear footsteps worse with headsets? is this our mod mixer?")
                if (now >= _nextHeadsetRow)
                {
                    _nextHeadsetRow = now + 5f;
                    var tpl = ba.CurrentHeadphonesTemplate;
                    var sb = new System.Text.StringBuilder();
                    sb.Append(DiagLog.F(Time.time - DiagRunner.RaidStartTime, "0.0")).Append(',').Append(HeadsetOn() ? "1" : "0").Append(',')
                      .Append(DiagLog.F(tpl.DryVolume, "0.0")).Append(',').Append(DiagLog.F(tpl.AmbientVolume, "0.0"));
                    foreach (string k in HeadsetKeys) { sb.Append(','); sb.Append(m.GetFloat(k, out float hv) ? DiagLog.F(hv, "0.0") : ""); }
                    DiagLog.Row("headsetmix.csv", sb.ToString());
                }
                if (now - _blastAt < MixLogS)
                {
                    m.GetFloat(ba.AudioMixerData.MainMixerVolume, out float mv); m.GetFloat(ba.AudioMixerData.GunsMixerVolume, out float gv);
                    float wv = 0f; if (!string.IsNullOrEmpty(ba.AudioMixerData.WorldMixerVolume)) m.GetFloat(ba.AudioMixerData.WorldMixerVolume, out wv);
                    DiagLog.Row("blastmix.csv", string.Join(",", DiagLog.F(Time.time - DiagRunner.RaidStartTime, "0.000"), DiagLog.F(now - _blastAt, "0.000"),
                        DiagLog.F(mv, "0.0"), DiagLog.F(gv, "0.0"), DiagLog.F(wv, "0.0"), Tinnitus(ba) ? "1" : "0", _active ? "1" : "0", now < _crackUntil ? "1" : "0",
                        MixerOwner.By(MixKey.Main), MixerOwner.By(MixKey.Guns)));
                }
            }
            catch (Exception e) { DiagRunner.ReportOnce("6.9r mixer rows", e); }
        }
        public const string Header = "t,closeness,endDb,cutS,tailS,holdS,fadeS,tinnitus,headset"; // 0.7.10: headset
        public const string DoseHeader = "t,doseDb,equivalentM,earsEquivalentM,earsCloseness,concussed,duration,ringOnly,blastRoomDb,yourRoomDb,yourRoomM,yourRoom"; // 0.7.1: the last four, 6.9n
        private const string OtherMod = "com.harmonyzt.BringBackConcussion", CutType = "BringBackConcussion.Patches.ExplosionSoundCutoff", ProximityType = "BringBackConcussion.Patches.ExplosionProximityPatch";
        private const float NoCutAtOrBelow = 0.05f, CutsBelow = 0.95f, DefaultFadeS = 6f, PendingMaxS = 0.25f, SamePosM = 1f, RingMinS = 1.5f, RingMaxS = 4f;
        private const int GunsIndex = 1, MaxPending = 6;

        private class Pending { public IExplosiveItem Item; public Vector3 Pos; public float T; }
        private static readonly List<Pending> Waiting = new List<Pending>();
        private static MethodInfo _proximity, _ring;
        private static bool _passing, _byDose;
        private static float _dur, _closenessNow;
        public static int Concussed, RingOnly, EarsOnly, Dropped, Plain;

        private static Harmony _h;
        private static MethodInfo _trigger;
        private static bool _reentry;
        private static bool _tried, _active;
        private static ConfigEntry<float> _radius, _fadeEntry;
        private static ConfigEntry<bool> _cutEntry;
        private static FieldInfo _tinnitusEnd;
        private static readonly MixKey[] Keys = { MixKey.Main, MixKey.Guns, MixKey.World, MixKey.AmbientIn, MixKey.AmbientOut };
        private static readonly float[] Depth = new float[5]; // how far down each goes at the cut's deepest, from the game's own value
        private const int MainIndex = 0, WorldIndex = 2;
        private static bool _lpOn;
        private static float _lpStart, _lpEnd;
        private static float _t0, _end, _cut, _tail, _hold, _fade;

        public static int Soft, LeftToMod, Felt, Protected;
        private static bool _headsetNow;
        public static bool CutActive => _active;

        public static void Apply(Harmony h) => _h = h;

        public static void Reset()
        {
            Soft = LeftToMod = Felt = Protected = 0;
            Cracks = 0; _crackUntil = -1f; _nextHeadsetRow = 0f;
            Concussed = RingOnly = EarsOnly = Dropped = Plain = 0;
            Waiting.Clear();
            _active = _lpOn = false; CutWorldDb = CutMainDb = 0f;
            TryPatch();
        }

        private static void TryPatch()
        {
            if (_tried || _h == null) return;
            _tried = true;
            try
            {
                if (!Chainloader.PluginInfos.TryGetValue(OtherMod, out BepInEx.PluginInfo info) || info.Instance == null) return;
                ConfigFile cfg = info.Instance.Config;
                cfg.TryGetEntry("General", "Explosion Effect Radius", out _radius);
                cfg.TryGetEntry("Audio", "Cut Off Sound On Explosion", out _cutEntry);
                cfg.TryGetEntry("Audio", "Explosion Sound Fade In Time", out _fadeEntry);
                Type t = info.Instance.GetType().Assembly.GetType(CutType);
                _trigger = t != null ? AccessTools.Method(t, "Trigger") : null;
                if (_trigger == null) { Plugin.Log.LogWarning("6.9h: Bring Back Concussion's sound cut wasn't found, so its own cut will run"); return; }
                _h.Patch(_trigger, prefix: new HarmonyMethod(typeof(ConcussionProof), nameof(TriggerPrefix)));
                _tinnitusEnd = AccessTools.Field(typeof(BetterAudio), "float_2");
                Type p = info.Instance.GetType().Assembly.GetType(ProximityType);
                _proximity = p != null ? AccessTools.Method(p, "PatchPostfix") : null;
                _ring = p != null ? AccessTools.Method(p, "TryStartTinnitus") : null;
                if (_proximity == null) { Plugin.Log.LogWarning("6.9k: Bring Back Concussion's explosion patch wasn't found, so it will use straight-line distance"); return; }
                _h.Patch(_proximity, prefix: new HarmonyMethod(typeof(ConcussionProof), nameof(ProximityPrefix)));
                _h.Patch(AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.DoContusion)), prefix: new HarmonyMethod(typeof(ConcussionProof), nameof(ContusionPrefix)));
            }
            catch (Exception e) { _trigger = null; DiagRunner.ReportOnce("6.9h soft concussion cut (patch)", e); }
        }

        private static bool ByPressure => Plugin.ConcussionPressureOn.Value && Plugin.ShockOn.Value && _proximity != null && _radius != null;

        private static float Closeness(float equivalentM) => Mathf.Clamp01(1f - equivalentM / _radius.Value);

        // "6.9q": is your player wearing a headset (anything in the earpiece slot that's a headset, as the game's own ringing check)?
        private static bool HeadsetOn()
        {
            try
            {
                Player player = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.MainPlayer : null;
                return GunfireHearing.WearsHeadset(player); // 0.7.13: a helmet with a built-in headset counts too
            }
            catch { return false; }
        }

        // The closeness the hearing cut uses: scaled down with a headset on.
        private static float CutCloseness(float closeness)
        {
            _headsetNow = HeadsetOn();
            return _headsetNow ? closeness * (1f - Mathf.Clamp01(Plugin.HeadsetProtectionPct.Value / 100f)) : closeness;
        }

        // Will the sound be cut for this blast?
        public static bool WillCut(float distance, float doseDb)
        {
            if (!Plugin.SoftCutOn.Value || _trigger == null || _radius == null || (_cutEntry != null && !_cutEntry.Value)) return false;
            if (ByPressure) return CutCloseness(Closeness(ShockWave.EquivalentM(doseDb + Plugin.EarsReachDb.Value))) > NoCutAtOrBelow;
            return distance <= _radius.Value * CutsBelow && CutCloseness(Mathf.Clamp01(1f - distance / _radius.Value)) > NoCutAtOrBelow;
        }

        // That mod's explosion patch, held until the blast's route is known.
        private static bool ProximityPrefix(IExplosiveItem grenadeItem, Vector3 explosionPosition)
        {
            if (_passing || !ByPressure || grenadeItem == null) return true;
            if (Waiting.Count >= MaxPending) return true;
            Waiting.Add(new Pending { Item = grenadeItem, Pos = explosionPosition, T = Time.unscaledTime });
            return false;
        }

        private static void ContusionPrefix(ref float applyTime, ref float strength)
        {
            if (!_byDose) return;
            applyTime *= Mathf.Lerp(0.4f, 1.2f, _dur);
            strength = Mathf.Clamp01(strength * Mathf.Lerp(0.6f, 1.1f, _closenessNow));
        }

        private static int Find(Vector3 pos)
        {
            for (int i = 0; i < Waiting.Count; i++) if ((Waiting[i].Pos - pos).sqrMagnitude < SamePosM * SamePosM) return i;
            return -1;
        }

        // Not a blast this mod rates, or no answer in time: that mod's own distance rule.
        public static void DeliverPlain(Vector3 pos)
        {
            int i = Find(pos);
            if (i < 0) return;
            Pending p = Waiting[i];
            Waiting.RemoveAt(i);
            RunPlain(p);
        }

        private static void RunPlain(Pending p)
        {
            try
            {
                Plain++;
                _passing = true;
                _proximity.Invoke(null, new object[] { p.Item, p.Pos });
            }
            catch (Exception e) { DiagRunner.ReportOnce("6.9k concussion by pressure (plain)", e); }
            finally { _passing = false; }
        }

        public static void Deliver(Vector3 pos, float doseDb, float duration)
        {
            int i = Find(pos);
            if (i < 0) return;
            Pending p = Waiting[i];
            Waiting.RemoveAt(i);
            try
            {
                if (float.IsNegativeInfinity(doseDb)) { Dropped++; DoseRow(doseDb, float.PositiveInfinity, float.PositiveInfinity, 0f, false, duration, false); return; }
                float equiv = ShockWave.EquivalentM(doseDb), earsEquiv = ShockWave.EquivalentM(doseDb + Plugin.EarsReachDb.Value);
                float ears = Closeness(earsEquiv);
                Player player = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.MainPlayer : null;
                if (player == null) return;

                float cutEars = CutCloseness(ears);
                if (_headsetNow && ears > NoCutAtOrBelow) Protected++;
                if (cutEars > NoCutAtOrBelow && (_cutEntry == null || _cutEntry.Value))
                {
                    ShockWave.EndDipNow();
                    bool soft = Plugin.SoftCutOn.Value && TryStart(cutEars, Mathf.Lerp(0.6f, 1.5f, duration));
                    Adrenaline.Concussion(); // the hearing cut: the heart races
                    if (soft) { Soft++; }
                    else
                    {
                        LeftToMod++;
                        MixerOwner.ShowNormal(); // that mod keeps what it reads now and puts it back later
                        _reentry = true;
                        try { _trigger.Invoke(null, new object[] { cutEars }); }
                        finally { _reentry = false; }
                    }
                }

                bool concussed = equiv <= _radius.Value, ringOnly = false;
                if (concussed)
                {
                    Vector3 to = p.Pos - player.Position;
                    Vector3 moved = player.Position + (to.sqrMagnitude > 0.01f ? to.normalized : Vector3.forward) * equiv;
                    _dur = duration; _closenessNow = Closeness(equiv);
                    _passing = _byDose = true;
                    try { _proximity.Invoke(null, new object[] { p.Item, moved }); }
                    finally { _passing = _byDose = false; }
                    Concussed++;
                    Adrenaline.Concussion();
                }
                else if (ears > NoCutAtOrBelow)
                {
                    EarsOnly++;
                    if (_ring != null)
                    {
                        _ring.Invoke(null, new object[] { player, Mathf.Lerp(RingMinS, RingMaxS, ears) });
                        ringOnly = true; RingOnly++;
                        Adrenaline.Concussion();
                    }
                }
                DoseRow(doseDb, equiv, earsEquiv, ears, concussed, duration, ringOnly);
            }
            catch (Exception e) { DiagRunner.ReportOnce("6.9k concussion by pressure", e); }
        }

        private static void DoseRow(float doseDb, float equiv, float earsEquiv, float ears, bool concussed, float duration, bool ringOnly)
        {
            if (!DiagLog.IsOpen) return;
            DiagLog.Row("concdose.csv", string.Join(",", DiagLog.F(Time.time - DiagRunner.RaidStartTime, "0.00"), DiagLog.F(float.IsInfinity(doseDb) ? -999f : doseDb, "0.0"), DiagLog.F(float.IsInfinity(equiv) ? 999f : equiv, "0.0"), DiagLog.F(float.IsInfinity(earsEquiv) ? 999f : earsEquiv, "0.0"), DiagLog.F(ears, "0.00"), concussed ? "1" : "0", DiagLog.F(duration, "0.00"), ringOnly ? "1" : "0",
                DiagLog.F(ShockWave.LastBlastRoomDb, "0.0"), DiagLog.F(ShockWave.LastYourRoomDb, "0.0"), DiagLog.F(ShockWave.LastYourRoomM, "0.0"), ShockWave.LastYourRoom));
        }

        public static void FeltOn()
        {
            if (!MonoBehaviourSingleton<BetterAudio>.Instantiated) return;
            EchoVoices.OutputOverride = MonoBehaviourSingleton<BetterAudio>.Instance.MasterMixerGroup;
            EchoVoices.VolumeTrim = Mathf.Pow(10f, Plugin.FeltThroughCutDb.Value / 20f);
        }

        public static void FeltOff()
        {
            EchoVoices.OutputOverride = null;
            EchoVoices.VolumeTrim = 1f;
        }

        private static bool TriggerPrefix(ref float closeness)
        {
            if (_reentry) return true;
            if (_byDose) return false; // ears were already dealt with
            ShockWave.EndDipNow(); // the cut does that job from here
            float raw = closeness;
            closeness = CutCloseness(closeness); // "6.9q", also for that mod's own cut
            if (_headsetNow && raw > NoCutAtOrBelow) Protected++;
            if (closeness <= NoCutAtOrBelow) return !_headsetNow || raw <= NoCutAtOrBelow; // a headset took the whole cut away
            if (!Plugin.SoftCutOn.Value) { MixerOwner.ShowNormal(); return true; } // that mod keeps what it reads now and puts it back later
            try
            {
                if (TryStart(closeness, 1f)) { Soft++; Adrenaline.Concussion(); return false; }
            }
            catch (Exception e) { DiagRunner.ReportOnce("6.9h soft concussion cut", e); }
            LeftToMod++;
            Adrenaline.Concussion();
            MixerOwner.ShowNormal();
            return true;
        }

        private static bool TryStart(float closeness, float holdScale)
        {
            if (!MixerOwner.Running || !MonoBehaviourSingleton<BetterAudio>.Instantiated) return false; // no owner, no soft cut: that mod's own runs
            BetterAudio ba = MonoBehaviourSingleton<BetterAudio>.Instance;
            MixerOwner.Clear(MixBy.Cut); // a cut still running starts again from the game's values, as before
            CutWorldDb = CutMainDb = 0f;
            _end = Mathf.Lerp(0f, -80f, closeness);
            // 0.7.22: depths are offsets from the game's own values; with "6.9t" the world stays heard, muffled
            for (int i = 0; i < Depth.Length; i++) Depth[i] = 0f;
            bool heard = Plugin.HeardThroughOn.Value;
            Depth[WorldIndex] = heard ? Mathf.Max(_end, Plugin.BlastHeardDb.Value) : _end;
            if (heard) Depth[MainIndex] = Mathf.Min(0f, Mathf.Max(_end, Plugin.WorldHeardDb.Value) - Depth[WorldIndex]);
            _lpOn = heard && MixerOwner.LowpassNormal(out _lpStart);
            if (_lpOn)
            {
                float muffle = Mathf.Clamp01(closeness * 1.5f); // a light cut muffles less
                _lpEnd = Mathf.Exp(Mathf.Lerp(Mathf.Log(_lpStart), Mathf.Log(Mathf.Min(Plugin.MuffleHz.Value, _lpStart)), muffle));
            }
            _cut = Mathf.Lerp(0.3f, 0.05f, closeness);
            _hold = Mathf.Lerp(0.2f, 1.5f, closeness) * holdScale;
            _fade = _fadeEntry != null ? _fadeEntry.Value : DefaultFadeS;
            _tail = Mathf.Max(_cut, Plugin.BlastTailFadeS.Value);
            _t0 = Time.time + CrackLeft(); // 0.7.22 "6.9r": after the crack
            _active = true;
            if (DiagLog.IsOpen) DiagLog.Row("concussion.csv", string.Join(",", DiagLog.F(_t0 - DiagRunner.RaidStartTime, "0.00"), DiagLog.F(closeness, "0.00"), DiagLog.F(_end, "0.0"), DiagLog.F(_cut, "0.00"), DiagLog.F(_tail, "0.00"), DiagLog.F(_hold, "0.00"), DiagLog.F(_fade, "0.0"), Tinnitus(ba) ? "1" : "0", _headsetNow ? "1" : "0"));
            return true;
        }

        private static float Remaining(int i, float t)
        {
            float attack = i == GunsIndex ? _tail : _cut;
            if (t < attack) return t / attack;
            t -= attack;
            if (t < _hold) return 1f;
            t -= _hold;
            return _fade > 0f ? Mathf.Clamp01(1f - t / _fade) : 0f;
        }

        private static bool Tinnitus(BetterAudio ba)
        {
            try { return _tinnitusEnd != null && (float)_tinnitusEnd.GetValue(ba) > Time.time; }
            catch { return false; }
        }

        private void Update()
        {
            if (Waiting.Count > 0)
            {
                float now = Time.unscaledTime;
                for (int i = Waiting.Count - 1; i >= 0; i--)
                {
                    if (now - Waiting[i].T < PendingMaxS) continue;
                    Pending p = Waiting[i];
                    Waiting.RemoveAt(i);
                    RunPlain(p); // the sound never came: that mod's distance rule
                }
            }
            if (!_active) return;
            try
            {
                if (!MixerOwner.Running) { EndCut(); return; } // raid over, or the owner stopped: the game owns these now
                float t = Time.time - _t0;
                if (t < 0f) return; // "6.9r": the crack is still playing
                if (t >= Mathf.Max(_cut, _tail) + _hold + _fade) { EndCut(); return; }
                // Asked again every frame; the owner adds the game's ring and anything else on top and writes once.
                for (int i = 0; i < Keys.Length; i++)
                    if (Depth[i] != 0f) MixerOwner.Offset(Keys[i], MixBy.Cut, Depth[i] * Remaining(i, t));
                CutWorldDb = Depth[WorldIndex] * Remaining(WorldIndex, t);
                CutMainDb = Depth[MainIndex] * Remaining(MainIndex, t);
                if (_lpOn) MixerOwner.Ceiling(MixBy.Cut, Mathf.Exp(Mathf.Lerp(Mathf.Log(_lpStart), Mathf.Log(_lpEnd), Remaining(WorldIndex, t))));
            }
            catch (Exception e) { DiagRunner.ReportOnce("6.9h soft concussion cut (update)", e); EndCut(); }
        }

        // The owner writes the game's values back once the asks are gone.
        private static void EndCut()
        {
            _active = _lpOn = false;
            CutWorldDb = CutMainDb = 0f;
            MixerOwner.Clear(MixBy.Cut);
        }

        public static string Summary() => $"CONCUSSION SOUND CUT (6.9h {(Plugin.SoftCutOn.Value ? $"ON, blast tail {Plugin.BlastTailFadeS.Value:0.0} s, felt {Plugin.FeltThroughCutDb.Value:+0;-0;0} dB" : "off")}): that mod's cut {(_trigger != null ? "found" : "not found or not installed")}; soft cuts {Soft}, left to that mod {LeftToMod}; layers kept through the cut for {Felt} blasts; heard through the cut (6.9t {(Plugin.HeardThroughOn.Value ? $"ON blast {Plugin.BlastHeardDb.Value:0} dB, world {Plugin.WorldHeardDb.Value:0} dB, {Plugin.MuffleHz.Value:0} Hz" : "off")}); crack before the cut (6.9r {(Plugin.CrackOn.Value ? $"ON {Plugin.CrackMs.Value:0} ms" : "off")}) {Cracks}; by pressure (6.9k {(ByPressure ? $"ON, {Plugin.ConcussionPressureDb.Value:+0;-0;0} dB, ears +{Plugin.EarsReachDb.Value:0} dB" : "off or not found")}): concussed {Concussed}, ears only {EarsOnly} (with ringing {RingOnly}), softened by a headset {Protected} (6.9q {Plugin.HeadsetProtectionPct.Value:0}%), walled off {Dropped}, by distance as a fallback {Plain}; your room (6.9n {(Plugin.YourRoomOn.Value ? $"ON, up to +{Plugin.YourRoomDb.Value:0.#} dB" : "off")}) added to {ShockWave.YourRoomAdded}, same room as the blast {ShockWave.YourRoomSame}  (concussion.csv, concdose.csv, blastmix.csv)\n";
    }
}
