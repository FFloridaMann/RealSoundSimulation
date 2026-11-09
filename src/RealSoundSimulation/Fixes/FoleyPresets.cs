using System;
using System.Collections.Generic;
using RealSoundSimulation.Diagnostics;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using UnityEngine;

namespace RealSoundSimulation.Fixes
{
    // 10a (0.6.35): footsteps, gear and weapon handling played with EFT 1.0's settings for those two sound groups. Raid on Sound Engine
    // Backport (8 Nov 2026): footsteps and gun inspections sounded better than ours, and none of its files are footsteps, so it comes from these
    // settings or its 1.0 mixer. This takes only the settings: distance curve, stereo spread, occlusion radius, and 3D ear processing (HRTF)
    // kept on at any distance where SPT switches it off by distance. Weapon handling also fades HRTF by distance, angle and height, close and
    // in front of you it is plain. Levels stay SPT's (SEB only changes them together with its mixer). Curves and logic transcribed from
    // Sound Engine Backport by red_the_fox, MIT licence: see THIRD_PARTY_NOTICES.md.
    // The game reads the curves when it builds its voices, so the switch needs a game restart.
    internal static class FoleyPresets
    {
        private sealed class Group
        {
            public float Radius, Hrtf, MinDistance, MinHeight, MinAngle, Smooth;
            public bool Dynamic;
            public AnimationCurve Spread, Rolloff;
        }

        private static readonly AccessTools.FieldRef<BetterSource, bool> ForceStereoRef = AccessTools.FieldRefAccess<BetterSource, bool>("_forceStereo");
        private static readonly AccessTools.FieldRef<BetterSource, AudioGroupPreset> PresetRef = AccessTools.FieldRefAccess<BetterSource, AudioGroupPreset>("Preset");
        private static readonly Dictionary<BetterAudio.AudioSourceGroupType, Group> Dynamic = new Dictionary<BetterAudio.AudioSourceGroupType, Group>();
        private static readonly HashSet<string> Reported = new HashSet<string>();
        private static string _was = "";
        public static long DynamicUpdates;

        public static string State => Plugin.FoleyPresetsOn.Value ? $"ON ({(_was.Length > 0 ? "SPT had: " + _was : "not loaded yet")}, handling updates {DynamicUpdates})" : "off";

        public static void Apply(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(BetterAudio), nameof(BetterAudio.LoadGroupPreset)), postfix: new HarmonyMethod(typeof(FoleyPresets), nameof(LoadPostfix)));
            h.Patch(AccessTools.Method(typeof(BetterSource), nameof(BetterSource.CheckBinauralAllowed)), prefix: new HarmonyMethod(typeof(FoleyPresets), nameof(BinauralPrefix)));
        }

        private static void LoadPostfix(string groupName, AudioGroupPreset __result)
        {
            try
            {
                if (__result == null || !Plugin.FoleyPresetsOn.Value || !Groups.TryGetValue(groupName, out Group g)) return;
                if (Reported.Add(groupName))
                {
                    string entry = $"{groupName} HRTF {__result.HrtfIntensity:0.00} off by distance {__result.DisableBinauralByDist} radius {__result.OcclusionSourceRadius:0.00} volume {__result.OverallVolume:0.00}";
                    _was = _was.Length > 0 ? _was + "; " + entry : entry;
                    Plugin.Log.LogInfo("10a footsteps and handling: " + entry + " -> EFT 1.0 settings");
                }
                __result.SoundRolloff = g.Rolloff;
                __result.SpreadCurve = g.Spread;
                __result.OcclusionSourceRadius = g.Radius;
                __result.HrtfIntensity = g.Hrtf;
                __result.DisableBinauralByDist = false;
                if (g.Dynamic) Dynamic[__result.Type] = g;
            }
            catch (Exception e) { DiagRunner.ReportOnce("10a footsteps and handling (load)", e); }
        }

        // false = this voice's 3D processing is handled here, and the game's distance rule is skipped
        private static bool BinauralPrefix(BetterSource __instance)
        {
            if (Dynamic.Count == 0) return true;
            try
            {
                AudioGroupPreset p = PresetRef(__instance);
                if (p == null || !Dynamic.TryGetValue(p.Type, out Group g) || ForceStereoRef(__instance) || !p.DirectBinaural || !p.SteamSpatialize) return true;
                CameraManager cam = CameraManager.Instance;
                if (cam == null || cam.Camera == null) return true;
                Transform t = cam.Camera.transform;
                Vector3 f = t.forward, v = __instance.Position - t.position;
                float angle = Vector2.SignedAngle(new Vector2(f.x, f.z), new Vector2(v.x, v.z));
                float want = Target(cam.SqrDistance(__instance.Position), Mathf.Abs(angle), Mathf.Abs(v.y), g.MinDistance * g.MinDistance, g.MinAngle, g.MinHeight, g.Hrtf);
                __instance.RefreshSpatialization(true);
                __instance.HrtfIntensity = Mathf.Lerp(__instance.HrtfIntensity, want, Time.deltaTime * g.Smooth);
                __instance.UpdateParameters();
                DynamicUpdates++;
                return false;
            }
            catch (Exception e) { DiagRunner.ReportOnce("10a footsteps and handling (HRTF)", e); return true; }
        }

        private static float Target(float sqrDist, float angle, float height, float sqrFull, float minAngle, float minHeight, float intensity)
        {
            if (sqrDist < 2.25f) return 0f;
            float d = !Mathf.Approximately(sqrFull, 2.25f) ? Mathf.Clamp01((sqrDist - 2.25f) / (sqrFull - 2.25f)) : 0f;
            float a = !Mathf.Approximately(minAngle, 180f) ? Mathf.Clamp01((angle - minAngle) / (180f - minAngle)) : 0f;
            float h = minHeight > 0f ? Mathf.Clamp01(height / minHeight) : 1f;
            float x = 1f - (1f - h) * (1f - d) * (1f - a);
            return Mathf.Clamp01(x * x * (3f - 2f * x) * intensity);
        }

        private static readonly Dictionary<string, Group> Groups = new Dictionary<string, Group>
        {
            ["Character"] = new Group
            {
                Radius = 0.7f, Hrtf = 1f, Dynamic = false,
                Spread = new AnimationCurve(new Keyframe(0f, 0.216602f, -1.38822f, -1.38822f), new Keyframe(0.0991626f, 0f, -0.0093874f, 0f), new Keyframe(1f, 0f, -0f, 0f)),
                Rolloff = new AnimationCurve(new Keyframe(0f, 1f, -9.57461f, -9.57461f), new Keyframe(0.150689f, 0.205432f, -0.819359f, -0.819359f), new Keyframe(1.00146f, 0f, -0.039257f, -0.039257f))
            },
            ["Weaponary"] = new Group
            {
                Radius = 1f, Hrtf = 0.5f, Dynamic = true, MinDistance = 10f, MinHeight = 2f, MinAngle = 12f, Smooth = 1f,
                Spread = new AnimationCurve(new Keyframe(0f, 0.216602f, -1.38822f, -1.38822f), new Keyframe(0.145f, 0f, -0.0093874f, 0f), new Keyframe(1f, 0f, -0f, 0f)),
                Rolloff = new AnimationCurve(new Keyframe(0f, 1f, -8.59794f, -8.59794f), new Keyframe(0.0557053f, 0.506997f, -3.80001f, -3.80001f), new Keyframe(0.226786f, 0.203754f, -0.658843f, -0.658843f), new Keyframe(1f, 0f, 0.0188551f, 0.0188551f))
            }
        };
    }
}
