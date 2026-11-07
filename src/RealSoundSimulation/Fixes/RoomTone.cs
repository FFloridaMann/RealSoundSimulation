using System;
using System.Collections.Generic;
using Audio.AmbientSubsystem;
using Audio.SpatialSystem;
using RealSoundSimulation.Acoustics;
using RealSoundSimulation.Diagnostics;
using HarmonyLib;
using UnityEngine;

namespace RealSoundSimulation.Fixes
{
    // 0.6.9, setting "8g Indoor wind follows openings" (raid 28, last tag: wind heard four floors down a stairwell).
    // The game plays one room tone per baked room (RoomToneController -> SpatialAudioRoom -> RoomAmbientSoundPlayer, a
    // Nonspatial BetterSource), and the indoor-layer fade (fix 8/9) moves it with "how enclosed you are" - so a draught
    // recording plays at full deep inside. Room tones whose clip name contains "wind" are treated as draughts: their level
    // follows how much of the outside reaches you (probe v2's outdoor level, else the exposure value). Other room tones
    // are untouched. The level goes through the source's occlusion volume factor (BetterSource.UpdateSourceVolume, every
    // frame), remembering what the game set so nothing compounds - the same way as 9k. Every room tone is logged
    // (roomtone.csv), so the clip names can be checked.
    internal static class RoomTone
    {
        public const string Header = "t,room,clip,roomToneVolume,wind,gain";

        private static readonly AccessTools.FieldRef<SpatialAudioRoom, ISoundPlayer> PlayerRef =
            AccessTools.FieldRefAccess<SpatialAudioRoom, ISoundPlayer>("_roomAmbientSoundPlayer");

        private static SpatialAudioRoom _room;
        private static BetterSource _cur;
        private static bool _wind;
        private static string _clip = "";
        private static float _raw = 1f, _written = -1f, _gain = 1f;
        private static readonly SortedDictionary<string, bool> Heard = new SortedDictionary<string, bool>();

        public static void Apply(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(RoomToneController), nameof(RoomToneController.ChangeRoomTone)),
                postfix: new HarmonyMethod(typeof(RoomTone), nameof(ChangedPostfix)));
            h.Patch(AccessTools.Method(typeof(BetterSource), nameof(BetterSource.UpdateSourceVolume)),
                prefix: new HarmonyMethod(typeof(RoomTone), nameof(VolumePrefix)));
        }

        public static void Reset()
        {
            Release();
            _room = null; _clip = ""; _wind = false; _gain = 1f;
        }

        // 0.6.14: the list of room tones heard is kept until the next raid starts (the end-of-raid reset ran before the
        // summary was written, so it said "none")
        public static void ClearReport() => Heard.Clear();

        private static void ChangedPostfix(RoomToneController __instance)
        {
            try
            {
                var room = __instance._prevEnteredRoom as SpatialAudioRoom;
                if (room == _room) return;
                Release();
                _room = room;
                AudioClip c = room != null && room.AmbientData != null ? room.AmbientData.RoomTone : null;
                _clip = c != null ? c.name : "";
                _wind = _clip.IndexOf("wind", StringComparison.OrdinalIgnoreCase) >= 0;
                if (_clip.Length > 0) Heard[_clip] = _wind;
                if (DiagLog.IsOpen && room != null)
                    DiagLog.Row("roomtone.csv", string.Join(",", DiagLog.F(Time.time - DiagRunner.RaidStartTime, "0.00"), DiagLog.Csv(room.name),
                        DiagLog.Csv(_clip), DiagLog.F(room.AmbientData != null ? room.AmbientData.RoomToneVolume : float.NaN), _wind ? "1" : "0", DiagLog.F(_gain)));
            }
            catch (Exception e) { DiagRunner.ReportOnce("8g room tone", e); }
        }

        // Called by AmbienceMix every frame while fix 8 is on.
        public static void Tick(float dt)
        {
            BetterSource s = null;
            if (_room != null && PlayerRef(_room) is RoomAmbientSoundPlayer p) s = p._source;
            if (s != _cur) { Release(); _cur = s; }
            float target = 1f;
            if (_wind && Plugin.IndoorWindFollowsOpenings.Value)
            {
                // full while at least -10 dB of the outside reaches you, 1 dB down per dB below that, at most -40 dB
                float outDb;
                if (AmbienceMix.V2Active && !float.IsNaN(AmbienceMix.V2Level)) outDb = 10f * Mathf.Log10(Mathf.Max(AmbienceMix.V2Level, 1e-8f));
                else if (!float.IsNaN(AmbienceMix.ExposureApplied)) outDb = 20f * Mathf.Log10(Mathf.Max(AmbienceMix.ExposureApplied, 1e-4f));
                else outDb = 0f;
                target = Mathf.Pow(10f, Mathf.Clamp(outDb + 10f, -40f, 0f) / 20f);
            }
            _gain += (target - _gain) * (1f - Mathf.Exp(-dt / AmbienceMix.Smooth));
            if (target >= 1f && _gain > 0.999f) _gain = 1f;
        }

        // Off (fix 8 off or out of raid): hand the level back.
        public static void Off() { _gain = 1f; Release(); }

        private static void VolumePrefix(BetterSource __instance)
        {
            if (_cur == null || !ReferenceEquals(__instance, _cur)) return;
            float cur = __instance.OcclusionVolumeFactor;
            if (Mathf.Abs(cur - _written) > 1e-5f) _raw = cur; // the game set a new factor
            _written = Mathf.Clamp01(_raw * _gain);
            __instance.OcclusionVolumeFactor = _written;
        }

        private static void Release()
        {
            if (_cur != null && Mathf.Abs(_cur.OcclusionVolumeFactor - _written) < 1e-5f) _cur.OcclusionVolumeFactor = _raw;
            _cur = null; _raw = 1f; _written = -1f;
        }

        public static string Summary()
        {
            var parts = new List<string>();
            foreach (var kv in Heard) parts.Add(kv.Key + (kv.Value ? " (wind)" : ""));
            return $"  room tones heard (8g {(Plugin.IndoorWindFollowsOpenings.Value ? "ON" : "off")}): {(parts.Count > 0 ? string.Join(", ", parts) : "none")}\n";
        }
    }
}
