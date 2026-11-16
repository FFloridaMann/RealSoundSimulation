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
    //
    // 0.9.3 "small open spaces have no room hum" (raid_20261115_130407 tag 12: a thin wood shack with empty windows, baked as a room
    // with the map's residential hum, sounded like the inside of a factory; you'd only hear the rain there). Where the space is small
    // (probe v2's mean free path, averaged over 2 s: small below 3 m, no longer small above 3.5 m) and the outside comes in loudly, the
    // room's hum fades on its own source: full while the outside is 15 dB down or more, gone once it is only 8 dB down. In the map's
    // outdoor zone (a gas station canopy, a container in a yard) the same applies at any size. Containers inside a building (outside
    // about -22 dB) keep the building's hum. Draught tones (8g) follow the outside already.
    // Not on the game's AmbientIn level: raid_20261115_143702 showed that group also carries the roof rain (the indoor rain player's
    // PrecipitationAmbientIn sits under it), so muting it silenced the rain in the shack. Logged to humshell.csv when it moves.
    // The map's indoor tone loop gets the same gain (raid_20261115_151409 mark 3, the shack: playing.csv showed
    // "amb_scary_tone_indoor_loop" at 0.46 on CommonAmbInBypass, the drone the game plays wherever its zone says indoor). Found by
    // its mixer group (CommonAmbInBypass, looping, not 3D), so any map that uses the game's ambient tones is covered whatever the clip.
    // Its volume is set on the source; what the game set is remembered and given back.
    internal static class RoomTone
    {
        public const string Header = "t,room,clip,roomToneVolume,wind,gain";
        public const string ShellHeader = "t,outsideDb,meanFreePathAvgM,target,shell,small,outdoorZone";
        private const float ShellMaxM = 3f, ShellLeaveM = 3.5f, ShellFullDb = -15f, ShellGoneDb = -8f, ShellSmoothS = 1f, SizeAvgS = 2f;
        private static float _shell = 1f, _shellLogged = 1f, _mfpAvg = float.NaN;
        private static bool _small, _bigDoorway;
        private static float _rawSmallSince = -1f;
        private const float SmallAttackS = 0.3f, ShellFallS = 0.25f, ShellRiseS = 1f, BigFullDb = -8f, BigGoneDb = -2f;
        private const string ToneGroup = "CommonAmbInBypass";
        private static readonly List<AudioSource> Tones = new List<AudioSource>();
        private static readonly List<float> ToneRaw = new List<float>(), ToneWritten = new List<float>();
        private static bool _tonesSearched;
        // 0.9.3: raid_20261115_154828 found none on the loading screen (the game sets its ambient tones up after you spawn) and the
        // drone stayed at 0.46 all raid. While none are found, search again 3, 10, 30, 60 and 120 s after spawn.
        private static readonly float[] ToneRetryS = { 3f, 10f, 30f, 60f, 120f };
        private static int _toneRetry;
        public static int TonesFound => Tones.Count + TonePlayers.Count;

        // 0.9.3 (from a code read of the game's ambient system): every ambient sound on a map is a BaseAmbientSoundPlayer, started by
        // its Play() during the deploy countdown (AmbientAudioSystem.Init -> AmbientSoundPlayerGroup.Play). Each then waits its own random
        // start delay, and some only set "loop" on their first frame of playing, which is why the scene search (loops only) missed the
        // drone until 45 s. A postfix on Play() hands over each player as it starts: 2D players on the indoor tone group are kept, loops
        // and one-shots alike, with no search. The player writes Source.volume = Fader.Volume every frame, so the gain goes on the
        // fader's target (Fader.EndVolume), which the game's own fades then carry; what the game set is remembered and given back.
        // The scene search stays as a fallback while no player has been caught.
        private static readonly List<BaseAmbientSoundPlayer> TonePlayers = new List<BaseAmbientSoundPlayer>(), TonePending = new List<BaseAmbientSoundPlayer>();
        private static readonly List<float> TpRaw = new List<float>(), TpWritten = new List<float>();
        private static bool _tonePlayerLogged;

        internal static void ApplyToneHook(Harmony h) =>
            h.Patch(AccessTools.Method(typeof(BaseAmbientSoundPlayer), nameof(BaseAmbientSoundPlayer.Play)),
                postfix: new HarmonyMethod(typeof(RoomTone), nameof(PlayPostfix)));

        private static void PlayPostfix(BaseAmbientSoundPlayer __instance)
        {
            try { Consider(__instance); }
            catch (Exception e) { DiagRunner.ReportOnce("indoor tone players (play)", e); }
        }

        private static void Consider(BaseAmbientSoundPlayer p)
        {
            if (p == null || p._spatialBlend > 0.5f || TonePlayers.Contains(p)) return;
            AudioSource src = p.Source;
            UnityEngine.Audio.AudioMixerGroup g = src != null && src.outputAudioMixerGroup != null ? src.outputAudioMixerGroup : p._mixerGroup;
            if (g == null) { if (!TonePending.Contains(p)) TonePending.Add(p); return; } // its group sets it later this frame
            TonePending.Remove(p);
            if (g.name != ToneGroup) return;
            TonePlayers.Add(p); TpRaw.Add(p.Fader != null ? p.Fader.EndVolume : 1f); TpWritten.Add(-1f);
            if (!_tonePlayerLogged)
            {
                _tonePlayerLogged = true;
                Transform par = p.transform.parent;
                Plugin.Log.LogInfo($"small open spaces: indoor tone player caught as it started: {p.GetType().Name} '{p.name}' under '{(par != null ? par.name : "")}' on {g.name}");
            }
        }

        private static void ApplyTonePlayers()
        {
            for (int i = TonePending.Count - 1; i >= 0; i--)
            {
                BaseAmbientSoundPlayer q = TonePending[i];
                if (q == null) { TonePending.RemoveAt(i); continue; }
                Consider(q);
            }
            for (int i = TonePlayers.Count - 1; i >= 0; i--)
            {
                BaseAmbientSoundPlayer p = TonePlayers[i];
                if (p == null) { TonePlayers.RemoveAt(i); TpRaw.RemoveAt(i); TpWritten.RemoveAt(i); continue; }
                Audio.AuxiliaryAudioUtils.AudioFader f = p.Fader;
                if (f == null) continue;
                float cur = f.EndVolume;
                if (Mathf.Abs(cur - TpWritten[i]) > 1e-5f) TpRaw[i] = cur; // the game set a new target (fade in, stop, a new play)
                float w = TpRaw[i] * _shell;
                if (Mathf.Abs(w - cur) > 1e-5f) f.EndVolume = w;
                TpWritten[i] = w;
            }
        }

        private static void RestoreTonePlayers()
        {
            for (int i = 0; i < TonePlayers.Count; i++)
            {
                BaseAmbientSoundPlayer p = TonePlayers[i];
                if (p != null && p.Fader != null && Mathf.Abs(p.Fader.EndVolume - TpWritten[i]) < 1e-5f) p.Fader.EndVolume = TpRaw[i];
                TpWritten[i] = -1f;
            }
        }

        // Raid.Prepare: a new raid's players come through Play() after this.
        internal static void ForgetTonePlayers()
        {
            RestoreTonePlayers();
            TonePlayers.Clear(); TonePending.Clear(); TpRaw.Clear(); TpWritten.Clear(); _tonePlayerLogged = false;
        }

        // Raid.Prepare (loading screen), or the first tick after spawn if that didn't run: one scene search.
        internal static void FindToneLoops()
        {
            RestoreTones();
            Tones.Clear(); ToneRaw.Clear(); ToneWritten.Clear();
            _tonesSearched = true;
            foreach (AudioSource a in UnityEngine.Object.FindObjectsOfType<AudioSource>(true))
            {
                if (a == null || a.spatialBlend > 0.5f || a.outputAudioMixerGroup == null || a.outputAudioMixerGroup.name != ToneGroup) continue; // loops and one-shots
                Tones.Add(a); ToneRaw.Add(a.volume); ToneWritten.Add(-1f);
            }
            if (Tones.Count > 0) Plugin.Log.LogInfo($"small open spaces: {Tones.Count} indoor tone source(s) found by the fallback search ({ToneGroup})");
        }

        internal static void ForgetToneLoops() { RestoreTones(); Tones.Clear(); ToneRaw.Clear(); ToneWritten.Clear(); _tonesSearched = false; }

        private static void ApplyTones()
        {
            for (int i = 0; i < Tones.Count; i++)
            {
                AudioSource a = Tones[i];
                if (a == null) continue;
                float cur = a.volume;
                if (Mathf.Abs(cur - ToneWritten[i]) > 1e-5f) ToneRaw[i] = cur; // the game set a new volume
                float w = ToneRaw[i] * _shell;
                if (Mathf.Abs(w - cur) > 1e-5f) a.volume = w;
                ToneWritten[i] = w;
            }
        }

        private static void RestoreTones()
        {
            for (int i = 0; i < Tones.Count; i++)
            {
                AudioSource a = Tones[i];
                if (a != null && Mathf.Abs(a.volume - ToneWritten[i]) < 1e-5f) a.volume = ToneRaw[i];
                ToneWritten[i] = -1f;
            }
        }
        public static float Shell => _shell;
        public static long ShellFrames, ShellMutedFrames;
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
            _shell = _shellLogged = 1f; ShellFrames = ShellMutedFrames = 0; _mfpAvg = float.NaN; _small = false; _rawSmallSince = -1f; _bigDoorway = false;
            RestoreTones(); RestoreTonePlayers(); // the lists are kept: they were filled for this raid before spawn
            _toneRetry = 0;
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
            ShellTick(dt);

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
            float g = blend * (IsWind(room) && Plugin.IndoorWindFollowsOpenings.Value ? _gain : _shell);
            int i = IndexOf(s);
            if (i < 0)
            {
                if (_n >= MaxSources) return;
                i = _n++; Src[i] = s; Raw[i] = 1f; Written[i] = -1f;
            }
            Gain[i] = g; Seen[i] = true;
        }

        private static void ShellTick(float dt)
        {
            float target = 1f, outDb = float.NaN, mfp = AcousticProbe.MeanFreePath;
            // 0.9.3: the size averaged over 2 s, with a gap between "small" and "not small" (the raw reading jumped 2.2 -> 6.6 m for a
            // couple of seconds in the shack, raid_20261115_143702 at 276 s, and the hum came back in between)
            if (!float.IsNaN(mfp)) _mfpAvg = float.IsNaN(_mfpAvg) ? mfp : _mfpAvg + (mfp - _mfpAvg) * (1f - Mathf.Exp(-dt / SizeAvgS));
            // 0.9.3: becoming small is fast (the raw reading under 3 m for 0.3 s), stopping slow (the average over 3.5 m). Walking into the
            // shack the map turned its zone indoor at the doorway while the 2 s average still said 7 m, so for 2-3 s the drone came back
            // up to about 90% before fading (raid_20261115_162925 at 184 and 193 s, "full drone when I walk in, then it fades").
            if (!float.IsNaN(mfp) && mfp <= ShellMaxM) { if (_rawSmallSince < 0f) _rawSmallSince = Time.time; }
            else _rawSmallSince = -1f;
            if (!_small && (_rawSmallSince >= 0f && Time.time - _rawSmallSince >= SmallAttackS || (!float.IsNaN(_mfpAvg) && _mfpAvg <= ShellMaxM))) _small = true;
            else if (_small && !float.IsNaN(_mfpAvg) && _mfpAvg > ShellLeaveM && _rawSmallSince < 0f) _small = false;
            bool outdoorZone = Diagnostics.EnvProbe.GameSaysOutdoor; // the map's own zone: a covered spot it calls outdoor
            _bigDoorway = false;
            if (Plugin.SmallShellNoHum.Value && AmbienceMix.V2Active && !float.IsNaN(AmbienceMix.V2Level))
            {
                outDb = 10f * Mathf.Log10(Mathf.Max(AmbienceMix.V2Level, 1e-8f));
                if (_small || outdoorZone) target = Mathf.InverseLerp(ShellGoneDb, ShellFullDb, outDb);
                else
                {
                    // 0.9.3: a big space only loses its hum right at the edge of outside (a warehouse doorway at -6.5 dB played the full
                    // drone, raid_20261115_162925 mark 8): full at -8 dB, gone at -2 dB. Dry run: 6-9.5% of indoor time on Streets, 0 on Labs.
                    target = Mathf.InverseLerp(BigGoneDb, BigFullDb, outDb);
                    _bigDoorway = target < 1f;
                }
            }
            // falls within about half a second, comes back over 2 s, so a brief misreading can't bring the hum back to full
            _shell += (target - _shell) * (1f - Mathf.Exp(-dt / (target < _shell ? ShellFallS : ShellRiseS)));
            if (TonePlayers.Count == 0)
            {
                if (!_tonesSearched) StartupTiming.Run("first use", "indoor tone loops", FindToneLoops); // the loading-screen step didn't run
                else if (Tones.Count == 0 && _toneRetry < ToneRetryS.Length && Time.time - Raid.StartTime >= ToneRetryS[_toneRetry])
                {
                    _toneRetry++;
                    StartupTiming.Run("first use", $"indoor tone loops (retry {_toneRetry})", FindToneLoops);
                }
            }
            else if (Tones.Count > 0) { RestoreTones(); Tones.Clear(); ToneRaw.Clear(); ToneWritten.Clear(); } // caught by the hook: the search's list isn't needed
            if (target >= 1f && _shell > 0.999f) _shell = 1f;
            if (target <= 0f && _shell < 0.001f) _shell = 0f;
            ShellFrames++; if (_shell < 0.05f) ShellMutedFrames++;
            ApplyTones();
            ApplyTonePlayers();
            if (Mathf.Abs(_shell - _shellLogged) >= 0.1f || (_shell != _shellLogged && (_shell == 0f || _shell == 1f)))
            {
                _shellLogged = _shell;
                if (DiagLog.IsOpen)
                    DiagLog.Row("humshell.csv", string.Join(",", DiagLog.F(Time.time - DiagRunner.RaidStartTime, "0.00"), DiagLog.F(outDb, "0.0"),
                        DiagLog.F(_mfpAvg, "0.0"), DiagLog.F(target, "0.00"), DiagLog.F(_shell, "0.00"), _small ? "1" : (_bigDoorway ? "big" : "0"), outdoorZone ? "1" : "0"));
            }
        }

        private static int IndexOf(BetterSource s)
        {
            for (int i = 0; i < _n; i++) if (ReferenceEquals(Src[i], s)) return i;
            return -1;
        }

        // Off (fix 8 off or out of raid): hand the level back.
        public static void Off() { _gain = 1f; _shell = 1f; Release(); RestoreTones(); RestoreTonePlayers(); }

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
            string shell = ShellFrames > 0 ? $", small open spaces muted the hum in {100.0 * ShellMutedFrames / ShellFrames:0.0}% of frames (humshell.csv), indoor tone players {TonePlayers.Count} (hook), sources {Tones.Count} (search)" : "";
            return $"  room tones heard (8g {(Plugin.IndoorWindFollowsOpenings.Value ? "ON" : "off")}): {(parts.Count > 0 ? string.Join(", ", parts) : "none")}{shell}\n"
                + RoomToneBlend.Summary();
        }
    }
}
