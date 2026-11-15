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
    // 0.9.3: with the room tone blend (8h, RoomToneBlend) up to two rooms' tones play at once, each with its own blend
    // level; this class applies blend level x draught level to each of their sources. Without 8h it handles the game's
    // current room tone as before.
    internal static class RoomTone
    {
        public const string Header = "t,room,clip,roomToneVolume,wind,gain";
        private const int MaxSources = 4;

        private static readonly AccessTools.FieldRef<SpatialAudioRoom, ISoundPlayer> PlayerRef =
            AccessTools.FieldRefAccess<SpatialAudioRoom, ISoundPlayer>("_roomAmbientSoundPlayer");

        private static SpatialAudioRoom _room;
        private static bool _wind;
        private static string _clip = "";
        private static float _gain = 1f; // the draught level (8g), shared by every wind room tone
        private static readonly SortedDictionary<string, bool> Heard = new SortedDictionary<string, bool>();

        // the sources whose level is set here: what the game last set (Raw), what was written (Written), the level (Gain)
        private static readonly BetterSource[] Src = new BetterSource[MaxSources];
        private static readonly float[] Raw = new float[MaxSources], Written = new float[MaxSources], Gain = new float[MaxSources];
        private static readonly bool[] Seen = new bool[MaxSources];
        private static int _n;

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

        internal static RoomAmbientSoundPlayer Player(SpatialAudioRoom r) => r != null ? PlayerRef(r) as RoomAmbientSoundPlayer : null;

        internal static bool IsWind(SpatialAudioRoom r)
        {
            AudioClip c = r != null && r.AmbientData != null ? r.AmbientData.RoomTone : null;
            return c != null && c.name.IndexOf("wind", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void ChangedPostfix(RoomToneController __instance)
        {
            try
            {
                if (RoomToneBlend.Active) return; // 8h logs its own room changes
                var room = __instance._prevEnteredRoom as SpatialAudioRoom;
                if (room == _room) return;
                Release();
                Note(room);
            }
            catch (Exception e) { DiagRunner.ReportOnce("8g room tone", e); }
        }

        // The room whose tone you now hear: remembered for the summary and logged (roomtone.csv).
        internal static void Note(SpatialAudioRoom room)
        {
            _room = room;
            AudioClip c = room != null && room.AmbientData != null ? room.AmbientData.RoomTone : null;
            _clip = c != null ? c.name : "";
            _wind = IsWind(room);
            if (_clip.Length > 0) Heard[_clip] = _wind;
            if (DiagLog.IsOpen && room != null)
                DiagLog.Row("roomtone.csv", string.Join(",", DiagLog.F(Time.time - DiagRunner.RaidStartTime, "0.00"), DiagLog.Csv(room.name),
                    DiagLog.Csv(_clip), DiagLog.F(room.AmbientData != null ? room.AmbientData.RoomToneVolume : float.NaN), _wind ? "1" : "0", DiagLog.F(_wind ? _gain : 1f)));
        }

        // Called by AmbienceMix every frame while fix 8 is on (after RoomToneBlend.Tick).
        public static void Tick(float dt)
        {
            float target = 1f;
            if (Plugin.IndoorWindFollowsOpenings.Value)
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

            for (int i = 0; i < _n; i++) Seen[i] = false;
            if (RoomToneBlend.Active)
                for (int i = 0; i < RoomToneBlend.Count; i++) Track(RoomToneBlend.RoomAt(i), RoomToneBlend.GainAt(i));
            else if (_room != null) Track(_room, 1f);
            for (int i = _n - 1; i >= 0; i--) if (!Seen[i]) ReleaseAt(i);
        }

        private static void Track(SpatialAudioRoom room, float blend)
        {
            BetterSource s = Player(room)?._source;
            if (s == null) return;
            float g = blend * (IsWind(room) && Plugin.IndoorWindFollowsOpenings.Value ? _gain : 1f);
            int i = IndexOf(s);
            if (i < 0)
            {
                if (_n >= MaxSources) return;
                i = _n++; Src[i] = s; Raw[i] = 1f; Written[i] = -1f;
            }
            Gain[i] = g; Seen[i] = true;
        }

        private static int IndexOf(BetterSource s)
        {
            for (int i = 0; i < _n; i++) if (ReferenceEquals(Src[i], s)) return i;
            return -1;
        }

        // Off (fix 8 off or out of raid): hand the level back.
        public static void Off() { _gain = 1f; Release(); }

        private static void VolumePrefix(BetterSource __instance)
        {
            if (_n == 0) return;
            int i = IndexOf(__instance);
            if (i < 0) return;
            float cur = __instance.OcclusionVolumeFactor;
            if (Mathf.Abs(cur - Written[i]) > 1e-5f) Raw[i] = cur; // the game set a new factor
            Written[i] = Mathf.Clamp01(Raw[i] * Gain[i]);
            __instance.OcclusionVolumeFactor = Written[i];
        }

        private static void ReleaseAt(int i)
        {
            BetterSource s = Src[i];
            if (s != null && Mathf.Abs(s.OcclusionVolumeFactor - Written[i]) < 1e-5f) s.OcclusionVolumeFactor = Raw[i];
            _n--;
            Src[i] = Src[_n]; Raw[i] = Raw[_n]; Written[i] = Written[_n]; Gain[i] = Gain[_n]; Seen[i] = Seen[_n];
            Src[_n] = null;
        }

        private static void Release()
        {
            for (int i = _n - 1; i >= 0; i--) ReleaseAt(i);
        }

        public static string Summary()
        {
            var parts = new List<string>();
            foreach (var kv in Heard) parts.Add(kv.Key + (kv.Value ? " (wind)" : ""));
            return $"  room tones heard (8g {(Plugin.IndoorWindFollowsOpenings.Value ? "ON" : "off")}): {(parts.Count > 0 ? string.Join(", ", parts) : "none")}\n"
                + RoomToneBlend.Summary();
        }
    }
}
