using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Random = UnityEngine.Random;

namespace RealSoundSimulation.Fixes
{
    // 0.7.3: shot-to-shot variation. The game plays every shot of a gun at the same pitch and volume (pitch only moves with the
    // gun's state, volume never), so every echo and through-wall layer built from it is identical too. Each shot now gets a small
    // random pitch and volume change where the game queues it, before anything else sees it, so the echoes, the outdoor tail and
    // the thump all come from that same varied shot. Brought back from the old Gun Audio mod (pitch up to 2% either way, up to
    // about 1 dB quieter), which the 1e port left out.
    //
    // One value per shot: a sample queued in the same frame as the last one on this gun (a shot and its tail) or right after a
    // firing loop (the burst's tail) keeps that value; anything else draws a new one. A loop keeps its value for the whole burst.
    internal static class ShotVariation
    {
        private sealed class Last { public int Frame; public bool WasLoop; public float PitchMul, VolumeMul; }
        private static readonly Dictionary<SuperBetterAudioQueue, Last> Queues = new Dictionary<SuperBetterAudioQueue, Last>();
        public static int Varied, Shared;
        private static float _sumAbsPitch, _sumDb;

        public static void Apply(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(SuperBetterAudioQueue), nameof(SuperBetterAudioQueue.Enqueue)),
                prefix: new HarmonyMethod(typeof(ShotVariation), nameof(EnqueuePrefix)) { priority = Priority.First });
        }

        public static void Reset() { Queues.Clear(); Varied = Shared = 0; _sumAbsPitch = _sumDb = 0f; }

        private static void EnqueuePrefix(SuperBetterAudioQueue __instance, ref float clipTime, ref float volume, ref float pitch)
        {
            if (!Plugin.ShotVariationOn.Value || __instance == null) return;
            try
            {
                int frame = Time.frameCount;
                if (!Queues.TryGetValue(__instance, out Last last))
                {
                    if (Queues.Count > 512) Queues.Clear(); // guns come and go; the cache only needs the last value per gun
                    last = new Last { Frame = -1 };
                    Queues[__instance] = last;
                }

                bool sameShot = last.Frame == frame || last.WasLoop;
                if (!sameShot)
                {
                    float p = Plugin.ShotPitchPct.Value / 100f;
                    float db = Plugin.ShotVolumeDb.Value;
                    last.PitchMul = 1f + Random.Range(-p, p);
                    float drop = Random.Range(0f, db);
                    last.VolumeMul = Mathf.Pow(10f, -drop / 20f);
                    Varied++; _sumAbsPitch += Mathf.Abs(last.PitchMul - 1f); _sumDb += drop;
                }
                else Shared++;

                last.Frame = frame;
                last.WasLoop = clipTime <= 0f; // the game passes 0 for a firing loop; its tail comes next and shares the value

                float before = pitch > 0f ? pitch : 1f;
                pitch = before * last.PitchMul;
                volume *= last.VolumeMul;
                if (clipTime > 0f) clipTime *= before / pitch; // a faster or slower sample ends sooner or later
            }
            catch (System.Exception e) { Diagnostics.DiagRunner.ReportOnce("shot variation", e); }
        }

        public static string Summary()
        {
            string state = Plugin.ShotVariationOn.Value ? $"ON, pitch up to {Plugin.ShotPitchPct.Value:0.0}% either way, volume up to {Plugin.ShotVolumeDb.Value:0.0} dB quieter" : "off";
            string avg = Varied > 0 ? $", mean pitch change {100f * _sumAbsPitch / Varied:0.00}%, mean volume drop {_sumDb / Varied:0.00} dB" : "";
            return $"SHOT VARIATION ({state}): shots varied {Varied}, tails and loops sharing their shot's value {Shared}{avg}\n";
        }
    }
}
