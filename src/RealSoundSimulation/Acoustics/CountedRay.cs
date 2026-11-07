using Audio.SpatialSystem;
using UnityEngine;

namespace RealSoundSimulation.Acoustics
{
    // Physics.Raycast and room lookups that count themselves, for the COST section of the summary (0.5.3).
    internal static class CountedRay
    {
        public static long Rays, Lookups;

        public static bool Raycast(Vector3 o, Vector3 d, out RaycastHit hit, float maxDistance, int mask, QueryTriggerInteraction q)
        {
            Rays++;
            return Physics.Raycast(o, d, out hit, maxDistance, mask, q);
        }

        public static bool Raycast(Vector3 o, Vector3 d, float maxDistance, int mask, QueryTriggerInteraction q)
        {
            Rays++;
            return Physics.Raycast(o, d, maxDistance, mask, q);
        }

        public static ISpatialAudioRoom Room(AudioRoomStorage storage, ISpatialAudioRoom known, Vector3 p)
        {
            Lookups++;
            return storage.FindActualCurrentRoom(known, p);
        }
    }
}
