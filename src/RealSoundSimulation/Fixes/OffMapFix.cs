using Audio.SpatialSystem;
using HarmonyLib;
using UnityEngine;

namespace RealSoundSimulation.Fixes
{
    // Fix 4: sounds placed off the edge of the baked sound map.
    // A sound outside every baked room has an "invalid" room, so the propagation search finds no room pair and returns
    // 1.0 (fully blocked). Raid 1: all 87 of SPTBattleAmbience's distant battles (450-600 m) were fully muffled, wherever
    // you stood. Here such a sound is treated as outdoors: no propagation loss while you are outside, and while you are
    // inside the loss follows the doorway value (none at the doorway, full deep inside).
    internal static class OffMapFix
    {
        public static int Applied;

        public static void Apply(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(ParallelSoundPropagationCalculator), nameof(ParallelSoundPropagationCalculator.OnUpdate)),
                postfix: new HarmonyMethod(typeof(OffMapFix), nameof(Postfix)));
        }

        public static void Reset() => Applied = 0;

        private static void Postfix(ParallelSoundPropagationCalculator __instance, AudioSourceContainer sourceContainer)
        {
            if (!Plugin.OffMapOn.Value || sourceContainer == null) return;
            try
            {
                // 0.6.8 "9k": sounds we've moved through the openings keep the game's loss at 0 (we set their level),
                // even if their moved position lands inside the map
                if (DistantThroughOpenings.Handles(sourceContainer))
                {
                    __instance._propagationScore = 0f; __instance._earlyOutThisFrame = true; Applied++;
                    DistantThroughOpenings.Register(sourceContainer);
                    return;
                }
                ISpatialAudioRoom room = sourceContainer.CurrentAudioRoom;
                if (room != null && room.IsValid) return;
                DistantThroughOpenings.Register(sourceContainer);
                if (DistantThroughOpenings.Handles(sourceContainer))
                {
                    __instance._propagationScore = 0f; __instance._earlyOutThisFrame = true; Applied++;
                    return;
                }

                float o = DoorwayBlend.TargetOutdoorness;
                float score;
                if (float.IsNaN(o)) score = 0f;          // no doorway information: assume outdoors
                else if (o >= 0.5f) score = 0f;           // listener outside
                else score = 1f - 2f * o;                 // inside: 0 at the doorway, 1 deep inside
                __instance._propagationScore = Mathf.Clamp01(score);
                __instance._earlyOutThisFrame = true;      // stop OnLateUpdate overwriting it with a job result
                Applied++;
            }
            catch (System.Exception e)
            {
                Diagnostics.DiagRunner.ReportOnce("fix 4 off-map sounds", e);
            }
        }
    }
}
