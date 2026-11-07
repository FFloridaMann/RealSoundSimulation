using System;
using Audio.ReverbSubsystem;
using RealSoundSimulation.Acoustics;
using RealSoundSimulation.Diagnostics;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace RealSoundSimulation.Fixes
{
    // Fix 3c: your own gunshots, blended between the indoor and outdoor recordings; plus the own-gun room-reverb boost (5).
    //
    // Each gun has two sets of recordings, Outdoor (index 0) and Indoor (index 1), for the shot and for the tail at the end
    // of a burst. The game picks one set from the indoor trigger volumes (playersBridge.Environment), so your own gun swaps
    // recordings the instant you cross a threshold, and never knows which way you're aiming.
    //
    // The game's gun player already plays two clips at once with a balance between them (slot 1 at `balance`, slot 2 at
    // `1 - balance`; normally used to crossfade near/far recordings for remote shots). Your own shots are at distance 0 and
    // only ever use one clip, so the second slot is free: this puts the indoor recording in slot 1 and the outdoor one in
    // slot 2, with slot 1's volume = "indoor weight". Where that weight comes from (setting "3d Own gunshot blend source"):
    //   Doorway    1 - doorway value from fix 3 (0.2.1 behaviour). Only acts near doorways (weight 0.02..0.98).
    //   Enclosure  from the enclosure probe (0.2.2): how enclosed it is around you, mixed with how enclosed the direction
    //              you're firing in is (setting "3e Aim weight"). Acts everywhere, so a spot covered on three sides gets
    //              part of the indoor recording, and firing into a room leans indoor while firing out leans outdoor.
    // Only your own (first-person) shots are touched; remote shooters keep their own environment.
    //
    // 5 Own gun room reverb boost: the game also plays a hidden reverb-only copy of your own shot through Meta's room
    // acoustics (ReverbSuperSource._spatializerA/B, server "stereo" sends: Gunshots 0 dB reverb, 0 dB early reflections).
    // This adds the boost to both sends on your gun's sources before each shot. 0 = the game's values.
    internal enum OwnShotBlendSource { Doorway, Enclosure }

    // 0.5.4: how the small-space rule (size factor) applies. Current = 0.3.1 rule (down to 50% indoor share when walls are
    // ~2 m away; hard rooms exempt). Soft = at least 85%. Off = room size never reduces the indoor share. Raid 18: small
    // interior rooms of the Capital building got only 65-76% indoor although they read fully enclosed.
    internal enum SmallSpaceRule { Current, Soft, Off }

    internal static class GunEnvironmentBlend
    {
        [ThreadStatic] private static bool _ownShot;
        [ThreadStatic] private static bool _isTail;
        private static float _indoorWeight = float.NaN; // for the current own shot/tail (NaN = leave the game's pick)
        private static float _lastAimEnc = float.NaN, _lastAimDist = float.NaN, _lastAimClosure = float.NaN;

        private static readonly AccessTools.FieldRef<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge> BridgeRef =
            AccessTools.FieldRefAccess<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge>("playersBridge");
        private static readonly AccessTools.FieldRef<WeaponSoundPlayer, SuperBetterAudioQueue> QueueRef =
            AccessTools.FieldRefAccess<WeaponSoundPlayer, SuperBetterAudioQueue>("_queue");
        private static readonly AccessTools.FieldRef<BetterSource, AudioGroupPreset> PresetRef =
            AccessTools.FieldRefAccess<BetterSource, AudioGroupPreset>("Preset");

        private const int Outdoor = 0, Indoor = 1;
        private const float PureBelow = 0.02f, PureAbove = 0.98f;
        // Probe room measure -> indoor weight (0.3.1). Room (walls x roof) below RoomOpen counts as fully open, above
        // RoomClosed as fully indoor. Aim closure maps 0.25..0.85. See EnclosureProbe for Room and SizeFactor.
        public const float RoomOpen = 0.15f, RoomClosed = 0.65f;
        public const float OpenBelow = 0.25f, ClosedAbove = 0.85f;

        public static int Blended; // shots/tails blended this raid (summary)

        public static void Apply(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(WeaponSoundPlayer), nameof(WeaponSoundPlayer.FireBullet)),
                prefix: new HarmonyMethod(typeof(GunEnvironmentBlend), nameof(FirePrefix)),
                postfix: new HarmonyMethod(typeof(GunEnvironmentBlend), nameof(FirePostfix)),
                finalizer: new HarmonyMethod(typeof(GunEnvironmentBlend), nameof(ClearFinalizer)));
            h.Patch(AccessTools.Method(typeof(WeaponSoundPlayer), nameof(WeaponSoundPlayer.StopFiringLoop)),
                prefix: new HarmonyMethod(typeof(GunEnvironmentBlend), nameof(TailPrefix)),
                finalizer: new HarmonyMethod(typeof(GunEnvironmentBlend), nameof(ClearFinalizer)));
            h.Patch(AccessTools.Method(typeof(SoundBank), nameof(SoundBank.PickClipsByDistance)),
                postfix: new HarmonyMethod(typeof(GunEnvironmentBlend), nameof(PickPostfix)));
        }

        public static void Reset() { Blended = 0; _lastAimEnc = _lastAimDist = _lastAimClosure = float.NaN; }

        private static bool IsOwn(WeaponSoundPlayer wsp)
        {
            BaseSoundPlayer.IObserverToPlayerBridge b = BridgeRef(wsp);
            return b != null && b.PointOfView == EPointOfView.FirstPerson;
        }

        private static void FirePrefix(WeaponSoundPlayer __instance, Vector3 shotPosition, Vector3 shotDirection)
        {
            _ownShot = false; _isTail = false; _indoorWeight = float.NaN;
            try
            {
                if (!IsOwn(__instance) || !Plugin.GunEnvBlendOn.Value) return;
                _ownShot = true;
                if (Plugin.OwnShotSource.Value == OwnShotBlendSource.Enclosure && EnclosureProbe.Valid)
                {
                    EnclosureProbe.MeasureAim(shotPosition, shotDirection, EnclosureProbe.CurrentMask(), out _lastAimEnc, out _lastAimDist);
                    _lastAimClosure = EnclosureProbe.AimClosure;
                    if (!EnclosureProbe.MeasureRoomAt(shotPosition, EnclosureProbe.CurrentMask())) EnclosureProbe.MuzzleRoom = float.NaN;
                    // 0.6.0: probe v2 measures the muzzle too (fix 9a)
                    if (V2 && !AcousticProbe.MeasureAt(shotPosition)) AcousticProbe.MuzzleRoom = float.NaN;
                    _lastMuzzleDist = Vector3.Distance(shotPosition, EnclosureProbe.LastOrigin);
                    _indoorWeight = EnclosureWeight(_lastAimClosure);
                }
                else _indoorWeight = DoorwayWeight();
            }
            catch (Exception e) { _ownShot = false; DiagRunner.ReportOnce("fix 3c own gunshot (fire)", e); }
        }

        // The reverb boost goes after the game's FireBullet: the gun's sources are only borrowed inside it on the first shot,
        // and the reverb copies play later (scheduled), so setting the sends here still reaches this shot.
        private static void FirePostfix(WeaponSoundPlayer __instance)
        {
            try { if (IsOwn(__instance)) ApplyReverbBoost(__instance); }
            catch (Exception e) { DiagRunner.ReportOnce("5 own gun reverb boost", e); }
        }

        // The tail at the end of a burst: same weight as the burst's last shot, re-read for doorway mode.
        private static void TailPrefix(WeaponSoundPlayer __instance)
        {
            _ownShot = false; _isTail = true; _indoorWeight = float.NaN;
            try
            {
                if (!Plugin.GunEnvBlendOn.Value || !IsOwn(__instance)) return;
                _ownShot = true;
                _indoorWeight = Plugin.OwnShotSource.Value == OwnShotBlendSource.Enclosure && EnclosureProbe.Valid
                    ? EnclosureWeight(_lastAimClosure)
                    : DoorwayWeight();
            }
            catch (Exception e) { _ownShot = false; DiagRunner.ReportOnce("fix 3c own gunshot (tail)", e); }
        }

        private static float DoorwayWeight()
        {
            float o = DoorwayBlend.Outdoorness;
            if (float.IsNaN(o)) o = DoorwayBlend.TargetOutdoorness;
            return float.IsNaN(o) ? float.NaN : 1f - o;
        }

        // The room around you and where you're aiming -> indoor weight (0 open air .. 1 inside a room). 0.3.1:
        //   a    = room measure (walls x roof) mapped RoomOpen..RoomClosed -> 0..1
        //   f    = aim closure mapped 0.25..0.85 -> 0..1
        //   w    = lerp(a, f, AimWeight x RoofClosure)   (aim only counts under a roof: firing at a wall in the open is
        //          the echoes' job, not a reason to play the indoor recording)
        //   w   *= SizeFactor                             (tiny spaces: less of the long indoor tail)
        // 0.3.3: room, roof and size are each a mix of the head and the muzzle readings, weighted by "3g Muzzle weight"
        // (0.7), so on a threshold the side the muzzle is on decides most.
        public static float EnclosureWeight(float aimClosure)
        {
            float mw = Mathf.Clamp01(Plugin.MuzzleWeight.Value);
            bool v2 = V2; // 0.6.0, fix 9a: room, roof and size from probe v2
            float room = v2 ? Mix(AcousticProbe.Room, AcousticProbe.MuzzleRoom, mw) : Mix(EnclosureProbe.Room, EnclosureProbe.MuzzleRoom, mw);
            if (float.IsNaN(room)) return float.NaN;
            float a = Mathf.InverseLerp(RoomOpen, RoomClosed, room);
            float w = a;
            if (!float.IsNaN(aimClosure))
            {
                float f = Mathf.InverseLerp(OpenBelow, ClosedAbove, aimClosure);
                float over = v2 ? Mix(AcousticProbe.Coverage, AcousticProbe.MuzzleCoverage, mw) : Mix(EnclosureProbe.RoofClosure, EnclosureProbe.MuzzleRoof, mw);
                if (float.IsNaN(over)) over = 0f;
                w = Mathf.Lerp(a, f, Mathf.Clamp01(Plugin.OwnShotAimWeight.Value) * over);
            }
            float size = v2 ? Mix(AcousticProbe.Size, AcousticProbe.MuzzleSize, mw) : Mix(EnclosureProbe.SizeFactor, EnclosureProbe.MuzzleSize, mw);
            if (float.IsNaN(size)) size = 1f;
            switch (Plugin.SmallSpace.Value)
            {
                case SmallSpaceRule.Off: size = 1f; break;
                case SmallSpaceRule.Soft: size = 1f - (1f - size) * 0.3f; break; // 0.5 -> 0.85
            }
            _lastRoomUsed = room;
            return Mathf.Clamp01(w * size);
        }

        // 0.6.0: fix 9a - gunshots use probe v2 (when it's running)
        public static bool V2 => Plugin.GunV2On.Value && Plugin.AcousticProbeOn.Value && AcousticProbe.Valid;

        // how hard and closed the room is, for 5a (reverb) and 6e (echoes): from probe v2 with fix 9a on
        public static float HardRoomNow() => V2 ? AcousticProbe.HardRoom : EnclosureProbe.HardRoom;

        // head/muzzle mix; falls back to whichever exists
        private static float Mix(float head, float muzzle, float muzzleWeight)
        {
            if (float.IsNaN(muzzle)) return head;
            if (float.IsNaN(head)) return muzzle;
            return Mathf.Lerp(head, muzzle, muzzleWeight);
        }

        private static float _lastMuzzleDist = float.NaN, _lastRoomUsed = float.NaN;
        public static float LastReverbBoost;

        private static Exception ClearFinalizer(Exception __exception)
        {
            _ownShot = false; _isTail = false;
            return __exception;
        }

        private static void PickPostfix(SoundBank __instance, ref AudioClip clip1, ref AudioClip clip2, ref float proportions, int environment)
        {
            if (!_ownShot || clip1 == null || clip2 != null) return; // only single-clip picks (own shots at distance 0)
            float w = Steep(_indoorWeight);
            _usedWeight = w;
            bool applied = false;
            try
            {
                bool enclosureMode = Plugin.OwnShotSource.Value == OwnShotBlendSource.Enclosure && EnclosureProbe.Valid;
                // Doorway mode only acts near doorways; enclosure mode decides everywhere.
                if (!float.IsNaN(w) && (enclosureMode || (w > PureBelow && w < PureAbove))
                    && __instance.HasEnvironment && __instance.Environments != null && __instance.Environments.Length >= 2)
                {
                    // Which distance tier the game picked (0 close, 1 mid, 2 far), so both recordings come from the same tier.
                    EnvironmentVariety picked = __instance.Environments[environment == Indoor ? Indoor : Outdoor];
                    int tier = -1;
                    for (int k = 0; k < 3 && tier < 0; k++)
                    {
                        DistanceVarity dv = picked?[k];
                        if (dv?.Clips != null && Array.IndexOf(dv.Clips, clip1) >= 0) tier = k;
                    }
                    if (tier >= 0)
                    {
                        AudioClip indoorClip = null, outdoorClip = null;
                        if (w > PureBelow) __instance.SelectRandomClip(__instance.Environments[Indoor][tier], ref indoorClip);
                        if (w < PureAbove) __instance.SelectRandomClip(__instance.Environments[Outdoor][tier], ref outdoorClip);

                        if (w <= PureBelow && outdoorClip != null) { clip1 = outdoorClip; proportions = 1f; applied = true; }      // fully outdoor
                        else if (w >= PureAbove && indoorClip != null) { clip1 = indoorClip; proportions = 1f; applied = true; }   // fully indoor
                        else if (indoorClip != null && outdoorClip != null)
                        {
                            clip1 = indoorClip;
                            clip2 = outdoorClip;
                            proportions = w; // slot 1 (indoor) volume; slot 2 (outdoor) gets 1 - w
                            applied = true;
                        }
                    }
                }
                if (applied) Blended++;
            }
            catch (Exception e)
            {
                DiagRunner.ReportOnce("fix 3c own gunshot blend", e);
            }
            Log(environment, _indoorWeight, applied); // the measured weight; weightUsed (last column) is after 3j
        }

        // Own-gun room reverb boost: add the setting to the stereo reverb/early-reflection sends of your gun's hidden
        // reverb copies. Always (re)applied, so setting it back to 0 restores the game's values.
        private static void ApplyReverbBoost(WeaponSoundPlayer wsp)
        {
            SuperBetterAudioQueue q = QueueRef(wsp);
            if (q?.AudioSources == null) return;
            // 0.3.3: plus "5a Hard room reverb boost dB" scaled by how much this is a hard closed room (stairwell).
            float hr = HardRoomNow();
            float hardRoom = float.IsNaN(hr) ? 0f : hr;
            float boost = Plugin.OwnReverbBoostDb.Value + Plugin.HardRoomReverbDb.Value * hardRoom;
            LastReverbBoost = boost;
            foreach (BetterSource bs in q.AudioSources)
            {
                if (!(bs is ReverbSuperSource rs)) continue;
                AudioGroupPreset p = PresetRef(bs);
                if (p?.acousticSettings?.stereo == null) continue;
                float rev = p.acousticSettings.stereo.reverbSendDb + boost, er = p.acousticSettings.stereo.earlyReflectionsSendDb + boost;
                SetSends(rs._spatializerA, rev, er);
                SetSends(rs._spatializerB, rev, er);
            }
        }

        private static void SetSends(Audio.SpatialSystem.BaseSpatialAudioSource sp, float rev, float er)
        {
            if (sp == null) return;
            if (Mathf.Abs(sp.ReverbSendDB - rev) < 0.01f && Mathf.Abs(sp.EarlyReflectionsSendDB - er) < 0.01f) return;
            sp.ReverbSendDB = rev;
            sp.EarlyReflectionsSendDB = er;
            sp.UpdateParameters();
        }

        // 0.6.9 "3j": steeper around 0.5, same crossover. Raid 28: 132 of 202 samples mixed the two recordings between 0.25
        // and 0.75; two different recordings of one shot at similar levels can flam. x = 2w - 1 -> sign(x) |x|^p,
        // p = 1 / (1 + 2 x steepness): steepness 1 turns 0.6 into 0.79 and 0.4 into 0.21; 0 leaves it as it was.
        private static float _usedWeight = float.NaN;
        private static float Steep(float w)
        {
            if (float.IsNaN(w)) return w;
            float k = Mathf.Clamp01(Plugin.BlendSteepness.Value);
            if (k <= 0f) return w;
            float x = 2f * Mathf.Clamp01(w) - 1f;
            float y = Mathf.Sign(x) * Mathf.Pow(Mathf.Abs(x), 1f / (1f + 2f * k));
            return 0.5f + 0.5f * y;
        }

        // One row per own shot / burst tail: what each measurement said and what was played.
        public static float LastShotTime = float.NaN, LastIndoorWeight = float.NaN; // for the mark key

        private static void Log(int environment, float w, bool applied)
        {
            LastShotTime = Time.time; LastIndoorWeight = w;
            if (!DiagLog.IsOpen) return;
            DiagLog.Row("ownshots.csv", string.Join(",",
                DiagLog.F(Time.time - DiagRunner.RaidStartTime, "0.00"), _isTail ? "tail" : "shot", Plugin.OwnShotSource.Value.ToString(),
                environment == Indoor ? "indoor" : "outdoor",
                DiagLog.F(DoorwayBlend.TargetOutdoorness), DiagLog.F(EnclosureProbe.Enclosure), DiagLog.F(EnclosureProbe.Horizontal),
                DiagLog.F(EnclosureProbe.Overhead), DiagLog.F(_lastAimEnc), DiagLog.F(_lastAimDist, "0.0"), DiagLog.F(w),
                applied ? "1" : "0", DiagLog.F(LastReverbBoost, "0.0"),
                DiagLog.F(EnclosureProbe.HorizClosure), DiagLog.F(EnclosureProbe.OverClosure), DiagLog.F(EnclosureProbe.Room),
                DiagLog.F(EnclosureProbe.SizeFactor), DiagLog.F(EnclosureProbe.ThinShare), DiagLog.F(_lastAimClosure),
                DiagLog.F(EnclosureProbe.ZenithClosure), DiagLog.F(EnclosureProbe.MuzzleRoom), DiagLog.F(EnclosureProbe.MuzzleRoof),
                DiagLog.F(EnclosureProbe.MuzzleSize), DiagLog.F(_lastMuzzleDist, "0.00"), DiagLog.F(_lastRoomUsed),
                DiagLog.F(EnclosureProbe.HardShare), DiagLog.F(EnclosureProbe.HardRoom), DiagLog.F(EnclosureProbe.GlassShare),
                V2 ? "v2" : "v1", DiagLog.F(AcousticProbe.Room), DiagLog.F(AcousticProbe.MuzzleRoom), DiagLog.F(AcousticProbe.Size),
                DiagLog.F(AcousticProbe.HardRoom), DiagLog.F(AcousticProbe.Rt60, "0.00"), DiagLog.F(_usedWeight)));
            _usedWeight = float.NaN;
        }
    }
}
