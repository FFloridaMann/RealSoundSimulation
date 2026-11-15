using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Audio.Data;
using BepInEx.Bootstrap;
using EFT;
using RealSoundSimulation.Diagnostics;
using UnityEngine;

namespace RealSoundSimulation.Fixes
{
    // 0.9.3: Icebreaker (Manimal's map, 1.2.4) plays its outdoor ambience on its own wind bed (Amb_OutdoorWind), straight to
    // the output, not through the game's ambience mixer, and every frame sets that bed and its four flat indoor/outdoor beds
    // to full or off (the wind to 5.4%) by the game's indoor check, over 1.5 s (IcebreakerAcoustics.TickAmbientBlend and
    // IcebreakerAmbientAudio.OutdoorDuck). So the doorway fade never reached what you hear: 2 m inside a door probe v2 said
    // -8.5 dB, Icebreaker had already dropped the wind to -25 dB (raid_20261115_110423 tag 3).
    // Same rule as every other map: while fix 3 runs without a blender and probe v2 is on, these beds follow probe v2's
    // outdoor level and muffling (outdoor beds) and its indoor layer (indoor beds), with the values the blender path writes
    // on retail maps. Written in LateUpdate, after Icebreaker's own Update, so nothing of Icebreaker's is patched or skipped;
    // when this stops, Icebreaker's switch carries on from the last level. A bed routed onto the game's AmbientOut/AmbientIn
    // mixer while fix 3 drives that mixer (NoBlenderAmbience) gets its full level here, so it isn't lowered twice. Beds that
    // skip the mixer get a low-pass filter for the muffling. Icebreaker's fields are read by name; if a later version renames
    // them, this logs once and leaves Icebreaker's switch as it is.
    internal static class IcebreakerBeds
    {
        public const string Header = "t,icebreakerWind,windVolume,windBase,outGain,cutoffHz,indoorGain,beds,bedsOnMixer";
        private const string Guid = "com.manimal.icebreaker";
        private const float MinLowpass = 500f, MaxLowpass = 22000f, LogEvery = 0.25f;

        private struct Bed { public AudioSource Src; public float Base; public bool Indoor, OnMixer; public AudioLowPassFilter Lp; }

        private static bool _looked, _usable;
        private static FieldInfo _windF, _baseVolF, _liveF, _entriesF, _srcF, _baseF, _indoorF;
        private static object _live; private static int _entryCount = -1;
        private static readonly List<Bed> Beds = new List<Bed>();
        private static AudioSource _wind; private static AudioLowPassFilter _windLp; private static bool _windOnMixer;
        private static bool _driving, _announced;
        private static float _nextLog;
        private static int _frames;
        private static string _routes = "";

        public static void Reset()
        {
            _live = null; _entryCount = -1; Beds.Clear();
            _wind = null; _windLp = null; _windOnMixer = false;
            _driving = false; _announced = false; _nextLog = 0f; _frames = 0; _routes = "";
        }

        // Called by fix 3 each frame on a map without a blender, after NoBlenderAmbience.Apply.
        public static void Apply()
        {
            if (!Find()) return;
            if (!AmbienceMix.V2Active || float.IsNaN(AmbienceMix.V2Level)) { Release(); return; }
            Refresh();
            if (_wind == null && Beds.Count == 0) return; // not an Icebreaker raid, or its ambience isn't up yet

            float lv = AmbienceMix.V2Level, cut = AmbienceMix.V2Cutoff;
            AmbienceMix.ApplyFloor(ref lv, ref cut);
            float outGain = Mathf.Pow(10f, Mathf.Clamp(10f * Mathf.Log10(Mathf.Max(lv, 1e-8f)), -80f, 0f) / 20f); // as NoBlenderAmbience
            cut = Mathf.Clamp(cut, MinLowpass, MaxLowpass);
            float inGain = Mathf.Pow(10f, AudioUtils.ConvertNormalizedVolumeToDB(Mathf.Clamp01(AmbienceMix.V2Indoor)) / 20f);
            bool mixer = NoBlenderAmbience.Active;

            float theirs = float.NaN, windBase = float.NaN;
            if (_wind != null)
            {
                theirs = _wind.volume;
                windBase = WindBase();
                bool viaMixer = mixer && _windOnMixer;
                _wind.volume = windBase * (viaMixer ? 1f : outGain);
                if (!viaMixer) _windLp = Lowpass(_wind, _windLp, cut);
            }
            for (int i = 0; i < Beds.Count; i++)
            {
                Bed b = Beds[i];
                if (b.Src == null || b.Src.mute) continue; // the game's own outdoor bed: Icebreaker keeps it muted
                bool viaMixer = mixer && b.OnMixer;
                b.Src.volume = b.Base * (viaMixer ? 1f : b.Indoor ? inGain : outGain);
                if (!b.Indoor && !viaMixer) { b.Lp = Lowpass(b.Src, b.Lp, cut); Beds[i] = b; }
            }
            _driving = true;
            _frames++;
            Announce();
            Log(theirs, windBase, outGain, cut, inGain);
        }

        // Fix 3 stopped (switched off, raid over) or probe v2 is off: Icebreaker's own switch takes over again from here.
        public static void Release()
        {
            if (!_driving) return;
            _driving = false;
            if (_windLp != null) _windLp.enabled = false;
            foreach (Bed b in Beds) if (b.Lp != null) b.Lp.enabled = false;
        }

        private static AudioLowPassFilter Lowpass(AudioSource s, AudioLowPassFilter lp, float hz)
        {
            if (lp == null)
            {
                lp = s.GetComponent<AudioLowPassFilter>();
                if (lp == null) lp = s.gameObject.AddComponent<AudioLowPassFilter>();
                lp.lowpassResonanceQ = 1f;
            }
            if (!lp.enabled) lp.enabled = true;
            if (Mathf.Abs(lp.cutoffFrequency - hz) > 1f) lp.cutoffFrequency = hz;
            return lp;
        }

        private static float WindBase()
        {
            if (_baseVolF?.GetValue(null) is Dictionary<AudioSource, float> d && d.TryGetValue(_wind, out float v)) return v;
            return 1f; // Icebreaker's own default (BaseVol)
        }

        // Icebreaker's wind bed and flat beds, re-read when Icebreaker makes new ones (each raid).
        private static void Refresh()
        {
            AudioSource w = _windF.GetValue(null) as AudioSource;
            if (w != _wind) { _wind = w; _windLp = null; _windOnMixer = w != null && OnMixer(w, false); }

            object live = _liveF.GetValue(null);
            IList entries = live != null ? _entriesF.GetValue(live) as IList : null;
            int n = entries?.Count ?? -1;
            if (live == _live && n == _entryCount) return;
            _live = live; _entryCount = n;
            Beds.Clear();
            if (entries != null)
                foreach (object e in entries)
                {
                    if (!(_srcF.GetValue(e) is AudioSource s) || s == null) continue;
                    bool indoor = (bool)_indoorF.GetValue(e);
                    Beds.Add(new Bed { Src = s, Base = (float)_baseF.GetValue(e), Indoor = indoor, OnMixer = OnMixer(s, indoor) });
                }
            var parts = new List<string>();
            if (_wind != null) parts.Add($"{_wind.name} (outdoor) -> {Group(_wind)}");
            foreach (Bed b in Beds) parts.Add($"{(b.Src != null ? b.Src.name : "?")} ({(b.Indoor ? "indoor" : "outdoor")}{(b.Src != null && b.Src.mute ? ", muted" : "")}) -> {Group(b.Src)}");
            _routes = string.Join("; ", parts);
        }

        // On the game's ambience channel that fix 3 drives without a blender (AmbientOut... / AmbientIn...).
        private static bool OnMixer(AudioSource s, bool indoor)
        {
            string g = s != null && s.outputAudioMixerGroup != null ? s.outputAudioMixerGroup.name : "";
            return g.StartsWith(indoor ? "AmbientIn" : "AmbientOut");
        }

        private static string Group(AudioSource s) => s != null && s.outputAudioMixerGroup != null ? "'" + s.outputAudioMixerGroup.name + "'" : "direct";

        // Once per game: is Icebreaker installed, and are its fields where this expects them.
        private static bool Find()
        {
            if (_looked) return _usable;
            _looked = true;
            if (!Chainloader.PluginInfos.TryGetValue(Guid, out BepInEx.PluginInfo pi) || pi?.Instance == null) return false;
            Assembly asm = pi.Instance.GetType().Assembly;
            System.Type acoustics = asm.GetType("Manimal.Icebreaker.IcebreakerAcoustics");
            System.Type ambient = asm.GetType("Manimal.Icebreaker.IcebreakerAmbientAudio");
            System.Type duck = ambient?.GetNestedType("OutdoorDuck", BindingFlags.NonPublic | BindingFlags.Public);
            System.Type entry = duck?.GetNestedType("Entry", BindingFlags.NonPublic | BindingFlags.Public);
            const BindingFlags S = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
            const BindingFlags I = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            _windF = acoustics?.GetField("_windSrc", S);
            _baseVolF = acoustics?.GetField("_ambientBaseVol", S);
            _liveF = duck?.GetField("_live", S);
            _entriesF = duck?.GetField("_entries", I);
            _srcF = entry?.GetField("Src", I);
            _baseF = entry?.GetField("Base", I);
            _indoorF = entry?.GetField("Indoor", I);
            _usable = _windF != null && _liveF != null && _entriesF != null && _srcF != null && _baseF != null && _indoorF != null;
            if (!_usable)
                Plugin.Log.LogWarning($"fix 3: Icebreaker {pi.Metadata.Version} is installed but its ambience beds weren't found where this version of the mod looks for them; Icebreaker's own indoor/outdoor switch is left as it is");
            return _usable;
        }

        private static void Announce()
        {
            if (_announced) return;
            _announced = true;
            Plugin.Log.LogInfo($"fix 3: Icebreaker's own indoor/outdoor switch found ({(_wind != null ? "wind bed + " : "")}{Beds.Count} flat beds); they follow probe v2 while fix 3 is on: {_routes}");
        }

        private static void Log(float theirs, float windBase, float outGain, float cut, float inGain)
        {
            if (!DiagLog.IsOpen || Time.time < _nextLog) return;
            _nextLog = Time.time + LogEvery;
            int onMixer = 0;
            foreach (Bed b in Beds) if (b.OnMixer) onMixer++;
            DiagLog.Row("icebreakerbeds.csv", string.Join(",", DiagLog.F(Time.time - DiagRunner.RaidStartTime, "0.00"),
                DiagLog.F(theirs), DiagLog.F(_wind != null ? _wind.volume : float.NaN), DiagLog.F(windBase), DiagLog.F(outGain),
                DiagLog.F(cut, "0"), DiagLog.F(inGain), Beds.Count.ToString(), onMixer.ToString()));
        }

        public static string Summary()
        {
            if (!_looked || !_usable) return "";
            if (_frames == 0) return "ICEBREAKER BEDS (fix 3): Icebreaker installed; not driven this raid (not Icebreaker, or fix 3 / probe v2 off)\n";
            return $"ICEBREAKER BEDS (fix 3): driven {_frames} frames; {_routes} (icebreakerbeds.csv)\n";
        }
    }
}
