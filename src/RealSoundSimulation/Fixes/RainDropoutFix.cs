using System;
using Audio.AmbientSubsystem;
using Audio.AmbientSubsystem.Data;
using RealSoundSimulation.Diagnostics;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace RealSoundSimulation.Fixes
{
    // Fix 7: indoor rain cutting out between rooms (roadmap step 4b, 0.4.0). Details: docs/AMBIENCE.md section 4.
    //
    // The game's rain player (PrecipitationAmbientBlender) crossfades to a new clip whenever you change room or its
    // volume target moves. SetClip picks that clip, but its TryGetClip returns "false" when the room's clip is the SAME
    // clip as last time, and SetClip then sets NO clip: the crossfade fades the rain to silence. The next crossfade
    // brings it back. So walking between rooms that share a rain clip, or up/down in rooms whose roof rain follows your
    // height, flips the rain on and off.
    //
    // This replaces SetClip with the intended logic: always pick the room's clip (or the default for this environment)
    // while it's raining, even if it's the same one. Every call is logged to rain.csv; "prevented" = the game would
    // have silenced the rain here.
    // 0.5.1: crossfades are never interrupted. The game (on room changes) and fix 8 (roof rain) call StartCrossfader;
    // if one is still running, StartCrossfader stopped it and restarted the incoming loop at a random point - an audible
    // jump/click (raid 15, the Nakatani hall: restarts every 0.5 s). Now a request during a crossfade waits and runs as
    // soon as the current one finishes.
    internal static class RainDropoutFix
    {
        private static readonly System.Collections.Generic.Dictionary<PrecipitationAmbientBlender, Vector2> Pending =
            new System.Collections.Generic.Dictionary<PrecipitationAmbientBlender, Vector2>();
        private static readonly System.Collections.Generic.List<PrecipitationAmbientBlender> Ready =
            new System.Collections.Generic.List<PrecipitationAmbientBlender>();
        public static int Deferred;
        private static bool _releasing;
        private static readonly AccessTools.FieldRef<PrecipitationAmbientBlender, IAudioCrossfader> CrossfaderRef =
            AccessTools.FieldRefAccess<PrecipitationAmbientBlender, IAudioCrossfader>("_crossfader");
        private static readonly AccessTools.FieldRef<PrecipitationAmbientBlender, RoomAmbientData> DataRef =
            AccessTools.FieldRefAccess<PrecipitationAmbientBlender, RoomAmbientData>("_currentAmbientData");
        private static readonly AccessTools.FieldRef<PrecipitationAmbientBlender, ESeasonStatus> SeasonRef =
            AccessTools.FieldRefAccess<PrecipitationAmbientBlender, ESeasonStatus>("_currentSeason");
        private static readonly AccessTools.FieldRef<PrecipitationAmbientBlender, SeasonAmbientSoundDataSO> SoundDataRef =
            AccessTools.FieldRefAccess<PrecipitationAmbientBlender, SeasonAmbientSoundDataSO>("_ambientSoundData");
        private static readonly AccessTools.FieldRef<PrecipitationAmbientBlender, int> LastHashRef =
            AccessTools.FieldRefAccess<PrecipitationAmbientBlender, int>("_lastClipHash");

        public static int Calls, WouldDrop, Prevented, VolumeOnly;

        // 0.6.9: a crossfade to the SAME clip is replaced by a volume ramp on the playing source. Raid 28 (tag 2): six room
        // changes in 3 s each crossfaded the clip into a second copy of itself at another loop position; the sum dipped
        // about 6 dB in the middle (to 0.07) and flammed.
        private struct Ramp { public AudioSource Src; public float From, To, T0, Dur; }
        private static readonly System.Collections.Generic.Dictionary<PrecipitationAmbientBlender, Ramp> Ramps =
            new System.Collections.Generic.Dictionary<PrecipitationAmbientBlender, Ramp>();
        private static readonly System.Collections.Generic.List<PrecipitationAmbientBlender> RampDone =
            new System.Collections.Generic.List<PrecipitationAmbientBlender>();
        private static readonly System.Reflection.MethodInfo UpdateGlobalsM =
            AccessTools.Method(typeof(PrecipitationAmbientBlender), "UpdateGlobalsVolumeMult");
        private static readonly AccessTools.FieldRef<PrecipitationAmbientBlender, float> GlobalsMultRef =
            AccessTools.FieldRefAccess<PrecipitationAmbientBlender, float>("_globalsVolumeMult");

        public static void Apply(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(PrecipitationAmbientBlender), nameof(PrecipitationAmbientBlender.SetClip)),
                prefix: new HarmonyMethod(typeof(RainDropoutFix), nameof(SetClipPrefix)));
            h.Patch(AccessTools.Method(typeof(PrecipitationAmbientBlender), nameof(PrecipitationAmbientBlender.StartCrossfader)),
                prefix: new HarmonyMethod(typeof(RainDropoutFix), nameof(StartCrossfaderPrefix)));
        }

        public static void Reset() { Calls = WouldDrop = Prevented = Deferred = VolumeOnly = 0; Pending.Clear(); Ramps.Clear(); }

        // The clip SetClip would pick now (the room's, else the default for this environment); null if not raining.
        private static AudioClip ChooseClip(PrecipitationAmbientBlender b, out bool roomClip)
        {
            AudioClip clip = null; roomClip = false;
            RainController.ERainIntensity intensity = b.CurrentPrecipitationIntensity;
            if (intensity == RainController.ERainIntensity.None) return null;
            RoomAmbientData data = DataRef(b);
            ESeasonStatus season = SeasonRef(b);
            int hash = data != null ? data.GetPrecipitationClip(season, intensity, out clip) : -1;
            roomClip = hash != -1;
            if (!roomClip) SoundDataRef(b)?.TryGetPrecipitationClip(season, intensity, b._environment, out clip);
            return clip;
        }

        private static bool TryVolumeOnly(PrecipitationAmbientBlender b, IAudioCrossfader cf, float duration)
        {
            AudioSource cur = cf.CurrentSource;
            if (cur == null || !cur.isPlaying || cur.clip == null || UpdateGlobalsM == null) return false;
            if (ChooseClip(b, out _) != cur.clip) return false;
            UpdateGlobalsM.Invoke(b, null);
            float to = b.CurrentTargetVolume * GlobalsMultRef(b);
            float from = b._environment == EnvironmentType.Indoor ? AmbienceMix.RainGameVolume(cur) : cur.volume;
            Ramps[b] = new Ramp { Src = cur, From = from, To = to, T0 = Time.time, Dur = Mathf.Max(0.05f, duration) };
            VolumeOnly++;
            Log(b, b.CurrentPrecipitationIntensity, cur.clip, false, true);
            return true;
        }

        private static bool StartCrossfaderPrefix(PrecipitationAmbientBlender __instance, float duration, float speed)
        {
            try
            {
                if (!Plugin.RainFixOn.Value) return true;
                IAudioCrossfader cf = CrossfaderRef(__instance);
                if (cf == null) return true;
                if (!_releasing && cf.IsCrossfadeStarted)
                {
                    Pending[__instance] = new Vector2(duration, speed); // latest request wins; it reads the current target when it runs
                    Deferred++;
                    return false;
                }
                if (!cf.IsCrossfadeStarted && TryVolumeOnly(__instance, cf, duration)) return false;
                Ramps.Remove(__instance); // a real crossfade (new clip) takes over
                return true;
            }
            catch (Exception e) { DiagRunner.ReportOnce("fix 7 crossfade wait", e); return true; }
        }

        // Called every frame: start waiting crossfades whose previous one has finished.
        public static void Tick()
        {
            if (Ramps.Count > 0)
            {
                RampDone.Clear();
                foreach (var kv in Ramps)
                {
                    Ramp r = kv.Value;
                    if (kv.Key == null || r.Src == null) { RampDone.Add(kv.Key); continue; }
                    float k = Mathf.Clamp01((Time.time - r.T0) / r.Dur);
                    float v = Mathf.Lerp(r.From, r.To, k * k * (3f - 2f * k));
                    // the indoor player's volume is the game's base; fix 8 multiplies its roof factor on top this frame
                    r.Src.volume = v;
                    if (k >= 1f) RampDone.Add(kv.Key);
                }
                foreach (PrecipitationAmbientBlender b in RampDone) Ramps.Remove(b);
            }
            if (Pending.Count == 0) return;
            Ready.Clear();
            foreach (var kv in Pending)
            {
                if (kv.Key == null) { Ready.Add(kv.Key); continue; }
                IAudioCrossfader cf = CrossfaderRef(kv.Key);
                if (cf == null || !cf.IsCrossfadeStarted) Ready.Add(kv.Key);
            }
            foreach (PrecipitationAmbientBlender b in Ready)
            {
                if (b == null || !Pending.TryGetValue(b, out Vector2 req)) { Pending.Remove(b); continue; }
                Pending.Remove(b);
                _releasing = true;
                try { if (b.isActiveAndEnabled) b.StartCrossfader(req.x, req.y); }
                finally { _releasing = false; }
            }
        }

        private static bool SetClipPrefix(PrecipitationAmbientBlender __instance)
        {
            try
            {
                IAudioCrossfader cf = CrossfaderRef(__instance);
                if (cf == null) return true;
                RainController.ERainIntensity intensity = __instance.CurrentPrecipitationIntensity;
                AudioClip clip = null;
                bool roomClip = false;
                if (intensity != RainController.ERainIntensity.None)
                {
                    RoomAmbientData data = DataRef(__instance);
                    ESeasonStatus season = SeasonRef(__instance);
                    int hash = data != null ? data.GetPrecipitationClip(season, intensity, out clip) : -1;
                    roomClip = hash != -1;
                    if (!roomClip) SoundDataRef(__instance)?.TryGetPrecipitationClip(season, intensity, __instance._environment, out clip);
                }
                // what the game would do: a room clip identical to the last one -> no clip (silence)
                bool wouldDrop = roomClip && clip != null && clip.GetHashCode() == LastHashRef(__instance);
                Calls++;
                if (wouldDrop) WouldDrop++;
                bool fix = Plugin.RainFixOn.Value;
                Log(__instance, intensity, clip, wouldDrop, fix);
                if (!fix) return true;
                if (wouldDrop) Prevented++;
                cf.MixSource.clip = clip;
                LastHashRef(__instance) = clip != null ? clip.GetHashCode() : -1;
                return false;
            }
            catch (Exception e)
            {
                DiagRunner.ReportOnce("fix 7 rain dropout", e);
                return true;
            }
        }

        private static void Log(PrecipitationAmbientBlender b, RainController.ERainIntensity intensity, AudioClip clip, bool wouldDrop, bool fixOn)
        {
            if (!DiagLog.IsOpen) return;
            DiagLog.Row("rain.csv", string.Join(",", DiagLog.F(Time.time - DiagRunner.RaidStartTime, "0.00"), b._environment.ToString(),
                intensity.ToString(), clip != null ? clip.name.Replace(',', ' ') : "none", DiagLog.F(b.CurrentTargetVolume),
                wouldDrop ? "1" : "0", fixOn ? "1" : "0"));
        }
    }
}
