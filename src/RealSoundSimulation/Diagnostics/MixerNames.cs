using System;
using System.Collections.Generic;
using System.Text;
using Audio.AmbientSubsystem;
using Audio.AmbientSubsystem.Data;
using Audio.Data;
using UnityEngine;
using UnityEngine.Audio;

namespace RealSoundSimulation.Diagnostics
{
    // Logging only, with bug report logs on: which mixer controls the doorway ambience fade (fix 3/8/9) moves, so it can later
    // be driven on maps where the game's ambience blender is missing (Icebreaker) or replaced (Manimal's Interchange).
    // About 10 s into the raid, mixernames.txt lists each blender's outdoor and indoor control names with its limits, and
    // every loaded AudioMixerParametersData asset (the files the blenders take their names from) with its names. After that,
    // once a second, each of those controls is read from the master mixer and every change is logged (mixerparams.csv), to
    // see who moves which: the game, this mod, or another mod.
    internal static class MixerNames
    {
        public const string Header = "t,param,value";
        private const float ListAfterS = 10f;

        private static readonly List<string> Names = new List<string>();
        private static readonly Dictionary<string, float> Last = new Dictionary<string, float>();
        private static float _raidStart, _nextSample;
        private static bool _listed;
        private static string _summary = "";
        private static int _changes;

        public static void Reset(float raidStart)
        {
            Names.Clear(); Last.Clear();
            _raidStart = raidStart; _nextSample = 0f; _listed = false; _summary = ""; _changes = 0;
        }

        // Called from DiagRunner's per-frame tick (not during the spawn back-off).
        public static void Tick(float now)
        {
            if (!DiagLog.IsOpen) return;
            if (!_listed)
            {
                if (now - _raidStart < ListAfterS) return;
                _listed = true;
                List();
            }
            if (now < _nextSample || Names.Count == 0 || !MonoBehaviourSingleton<BetterAudio>.Instantiated) return;
            _nextSample = now + 1f;
            AudioMixer m = MonoBehaviourSingleton<BetterAudio>.Instance.Master;
            if (m == null) return;
            foreach (string n in Names)
            {
                if (!m.GetFloat(n, out float v)) continue;
                if (Last.TryGetValue(n, out float old) && Mathf.Abs(old - v) < 0.01f) continue;
                Last[n] = v;
                _changes++;
                DiagLog.Row("mixerparams.csv", string.Join(",", DiagLog.F(now - _raidStart, "0.0"), DiagLog.Csv(n), DiagLog.F(v, "0.##")));
            }
        }

        private static void List()
        {
            var sb = new StringBuilder();
            var names = new SortedSet<string>(StringComparer.Ordinal);
            sb.AppendLine("AMBIENCE MIXER CONTROLS (logged about 10 s into the raid)");

            int blenders = 0;
            EnvironmentSoundBlendSystem system = Fixes.BlendSystem.Get();
            if (system == null || system._ambientSoundBlenders == null) sb.AppendLine("game ambience blender: none on this map");
            else
            {
                foreach (AmbientSoundBlender b in system._ambientSoundBlenders)
                {
                    if (b == null) continue;
                    blenders++;
                    sb.AppendLine($"blender '{b.name}': low-pass {b._minLowpassFreq:0}-{b._maxLowpassFreq:0} Hz, high-pass {b._minHighPassFreq:0}-{b._maxHighPassFreq:0} Hz, "
                        + $"max reverb send {b._maxOutdoorReverbSendDb:0.0} dB, max delay send {b._maxOutdoorDelaySendDb:0.0} dB");
                    Describe("  outdoor", b._outdoorMixerParams, sb, names);
                    Describe("  indoor", b._indoorMixerParams, sb, names);
                }
            }

            AudioMixerParametersData[] assets = Resources.FindObjectsOfTypeAll<AudioMixerParametersData>();
            sb.AppendLine($"loaded mixer-name files (AudioMixerParametersData): {assets.Length}");
            foreach (AudioMixerParametersData d in assets) Describe("  '" + (d != null ? d.name : "?") + "'", d, sb, names);

            AudioMixer m = MonoBehaviourSingleton<BetterAudio>.Instantiated ? MonoBehaviourSingleton<BetterAudio>.Instance.Master : null;
            var onMaster = new List<string>(); var missing = new List<string>();
            foreach (string n in names)
            {
                if (m != null && m.GetFloat(n, out float v)) { onMaster.Add($"{n}={v:0.##}"); Names.Add(n); }
                else missing.Add(n);
            }
            sb.AppendLine($"on the master mixer now ({onMaster.Count}): {string.Join(" ", onMaster)}");
            sb.AppendLine($"not on the master mixer ({missing.Count}): {string.Join(" ", missing)}");
            DiagLog.Text("mixernames.txt", sb.ToString());

            _summary = $"blenders {blenders}, mixer-name files {assets.Length}, controls on the master {onMaster.Count}, not on it {missing.Count}";
            Plugin.Log.LogInfo("Ambience mixer controls: " + _summary + " (mixernames.txt)");
        }

        private static void Describe(string label, AudioMixerParametersData d, StringBuilder sb, SortedSet<string> names)
        {
            if (d == null) { sb.AppendLine(label + ": none"); return; }
            var parts = new List<string>();
            foreach (EMixerParameterType t in Enum.GetValues(typeof(EMixerParameterType)))
            {
                if (!d.TryGetParameters(t, out AudioMixerParamsContainer c) || c == null || c.Count == 0) continue;
                var list = new List<string>();
                for (int i = 0; i < c.Count; i++) { list.Add(c[i]); if (!string.IsNullOrEmpty(c[i])) names.Add(c[i]); }
                parts.Add(t + ": " + string.Join(" ", list));
            }
            sb.AppendLine(label + (parts.Count == 0 ? ": (empty)" : " | " + string.Join(" | ", parts)));
        }

        public static string Summary()
        {
            if (!_listed) return "AMBIENCE MIXER CONTROLS: not listed (raid under 10 s or logs off)\n";
            return $"AMBIENCE MIXER CONTROLS (mixernames.txt): {_summary}; changes logged {_changes} (mixerparams.csv)\n";
        }
    }
}
