using System.Collections.Generic;
using Audio.AmbientSubsystem.Data;
using Audio.Data;
using EFT;
using UnityEngine;
using UnityEngine.Audio;

namespace RealSoundSimulation.Fixes
{
    // 0.9.3: outdoor ambience on maps without the game's ambience blender (Icebreaker, Manimal's custom map). There probe v2's
    // outdoor level, muffling and indoor layer were measured but never reached the mixer: outdoor ambience and the room hums
    // stayed at full volume all raid, deep inside the ship too (raid_20261114_124117: AmbientOutVolume and AmbientInVolume at
    // 0 dB). One rule for every map: with a blender, fix 3 goes through it (unchanged); without one, fix 3 writes the same values
    // the blender path writes on retail maps straight to the same controls of the game's master mixer (one mixer, every map).
    // The control names come from the game's mixer-name files (AmbientOut/InMixerBlendParameters) when they are loaded, else
    // from the game's known names (Streets raid_20261114_120507). Icebreaker never loads those files, so on a fresh launch the
    // first version found nothing (raid_20261114_133032). Every name is confirmed on the mixer before use, and without both
    // volume controls nothing is written. Limits are the retail blenders' (low-pass 500-22000 Hz, no high-pass, sends capped at
    // -2 / -5.4 dB). The values found at raid start are put back when the raid ends or fix 3 stops.
    internal static class NoBlenderAmbience
    {
        private const float MinLowpass = 500f, MaxLowpass = 22000f, NoHighpass = 10f, MaxReverbDb = -2f, MaxDelayDb = -5.4f;

        // the game's names (retail blenders' AmbientOutMixerBlendParameters / AmbientInMixerBlendParameters)
        private static readonly string[] KnownOutVol = { "AmbientOutVolume" }, KnownOutLp = { "AmbientOutLowpass", "AmbientEffectsLowpass" },
            KnownOutHp = { "AmbientOutHighpass" }, KnownOutRev = { "AmbientOutReverbSend" }, KnownOutDel = { "AmbientOutDelaySend" },
            KnownInVol = { "AmbientInVolume" };

        private static bool _checked, _ready;
        private static string[] _outVol, _outLp, _outHp, _outRev, _outDel, _inVol;
        private static readonly Dictionary<string, float> Original = new Dictionary<string, float>();
        private static float _lastDb = 999f, _lastCut = -1f, _lastIn = -1f;
        public static bool Active;
        public static IEnumerable<string> ControlNames => Original.Keys; // for the mixer log (MixerNames)

        public static void Reset()
        {
            _checked = _ready = false;
            _outVol = _outLp = _outHp = _outRev = _outDel = _inVol = null;
            Original.Clear();
            _lastDb = 999f; _lastCut = -1f; _lastIn = -1f;
            Active = false;
        }

        // Called by fix 3 each frame once the map is known to have no blender.
        public static void Apply()
        {
            if (!_checked) Prepare();
            if (!_ready || !AmbienceMix.V2Active || float.IsNaN(AmbienceMix.V2Level)) return;
            AudioMixer m = Master();
            if (m == null) return;

            float lv = AmbienceMix.V2Level, cut = AmbienceMix.V2Cutoff, indoor = AmbienceMix.V2Indoor;
            AmbienceMix.ApplyFloor(ref lv, ref cut);
            float volDb = Mathf.Clamp(10f * Mathf.Log10(Mathf.Max(lv, 1e-8f)), -80f, 0f); // as the blender path (DoorwayBlend)
            cut = Mathf.Clamp(cut, MinLowpass, MaxLowpass);
            if (Mathf.Abs(volDb - _lastDb) > 0.05f || Mathf.Abs(Mathf.Log(cut / Mathf.Max(_lastCut, 1f))) > 0.01f)
            {
                Set(m, _outVol, volDb);
                Set(m, _outLp, cut);
                Set(m, _outHp, NoHighpass);
                Set(m, _outRev, Mathf.Min(volDb, MaxReverbDb));
                Set(m, _outDel, Mathf.Min(volDb, MaxDelayDb));
                _lastDb = volDb; _lastCut = cut;
            }
            if (Mathf.Abs(indoor - _lastIn) > 0.003f)
            {
                Set(m, _inVol, AudioUtils.ConvertNormalizedVolumeToDB(indoor)); // as AmbientSoundBlender.SetIndoorMixerParams
                _lastIn = indoor;
                DoorwayBlend.IndoorApplied = indoor;
            }
            Active = true;
        }

        // Puts back what the mixer had before this raid's writes.
        public static void Restore()
        {
            if (!Active) return;
            Active = false;
            AudioMixer m = Master();
            if (m != null) foreach (KeyValuePair<string, float> kv in Original) m.SetFloat(kv.Key, kv.Value);
            _lastDb = 999f; _lastCut = -1f; _lastIn = -1f;
        }

        private static void Prepare()
        {
            _checked = true;
            AudioMixer m = Master();
            if (m == null) return;
            AudioMixerParametersData outData = null, inData = null;
            foreach (AudioMixerParametersData d in Resources.FindObjectsOfTypeAll<AudioMixerParametersData>())
            {
                if (d == null) continue;
                if (d.name == "AmbientOutMixerBlendParameters") outData = d;
                else if (d.name == "AmbientInMixerBlendParameters") inData = d;
            }
            string source = outData != null && inData != null ? "the game's mixer-name files" : "the game's known names";
            var missing = new List<string>();
            _outVol = Confirmed(m, outData, EMixerParameterType.Volume, KnownOutVol, missing);
            _outLp = Confirmed(m, outData, EMixerParameterType.LowPassFreq, KnownOutLp, missing);
            _outHp = Confirmed(m, outData, EMixerParameterType.HighPassFreq, KnownOutHp, missing);
            _outRev = Confirmed(m, outData, EMixerParameterType.ReverbSend, KnownOutRev, missing);
            _outDel = Confirmed(m, outData, EMixerParameterType.DelaySend, KnownOutDel, missing);
            _inVol = Confirmed(m, inData, EMixerParameterType.Volume, KnownInVol, missing);
            string miss = missing.Count > 0 ? "; not on the mixer: " + string.Join(" ", missing) : "";
            if (_outVol.Length == 0 || _inVol.Length == 0)
            {
                Plugin.Log.LogWarning($"fix 3: no ambience blender on this map, and the mixer's ambience volume controls weren't found ({source}{miss}); the map's own ambience is left as it is");
                return;
            }
            foreach (string[] set in new[] { _outVol, _outLp, _outHp, _outRev, _outDel, _inVol })
                foreach (string n in set)
                    if (!Original.ContainsKey(n) && m.GetFloat(n, out float v)) Original[n] = v;
            _ready = true;
            Plugin.Log.LogInfo($"fix 3: no ambience blender on this map; probe v2's outdoor level and indoor layer go straight to {Original.Count} mixer controls (names from {source}{miss})");
        }

        // The names for one control: from the loaded file if there is one, else the known names; only those the mixer has.
        private static string[] Confirmed(AudioMixer m, AudioMixerParametersData d, EMixerParameterType t, string[] known, List<string> missing)
        {
            var list = new List<string>();
            if (d != null && d.TryGetParameters(t, out AudioMixerParamsContainer c) && c != null)
                for (int i = 0; i < c.Count; i++) if (!string.IsNullOrEmpty(c[i])) list.Add(c[i]);
            if (list.Count == 0) list.AddRange(known);
            var ok = new List<string>();
            foreach (string n in list)
            {
                if (m.GetFloat(n, out float _)) ok.Add(n);
                else missing.Add(n);
            }
            return ok.ToArray();
        }

        private static void Set(AudioMixer m, string[] names, float v)
        {
            if (names == null) return;
            for (int i = 0; i < names.Length; i++) m.SetFloat(names[i], v);
        }

        private static AudioMixer Master() =>
            MonoBehaviourSingleton<BetterAudio>.Instantiated ? MonoBehaviourSingleton<BetterAudio>.Instance.Master : null;
    }
}
