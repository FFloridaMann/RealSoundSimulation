using System;
using System.Collections.Generic;
using Audio.SpatialSystem;
using RealSoundSimulation.Acoustics;
using RealSoundSimulation.Diagnostics;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace RealSoundSimulation.Fixes
{
    // 6.1 (0.6.24): a bot's gunshot from another room comes from the opening it reaches you through, not through the wall
    // (solutions doc 2.1). Raid 36: 156 of 171 bot shots had a route; half the openings were over 33 degrees from the true
    // direction, the worst 150. The game already works out the route; it only used its cost.
    //
    // The voices are placed along the direction of the opening nearest you, at the route's real length:
    //   position = listener + direction(opening - listener) x route length
    // Stabilised: the route only changes if another is better by more than 0.05; blended in by the propagation effect (0 at
    // 0, full at 0.3), so a sound just around a door frame doesn't jump; direction and distance smoothed over 150 ms; faded
    // out when you stand within 1 m of the opening, where its direction means nothing. The game re-poses the gun's voices in
    // Update and this runs in LateUpdate, so nothing needs restoring, and the occlusion maths uses the container's own
    // position, not the voices'. Gunshots only for now. No route (same room, both outdoors, none baked) = the true position.
    [DefaultExecutionOrder(32000)]
    internal class PortalPositioning : MonoBehaviour
    {
        private const float ScanEveryS = 0.1f, RetryS = 0.02f, Hysteresis = 0.05f, FullAtEffect = 0.3f, SmoothS = 0.15f, NearFadeM = 1f;

        private static readonly AccessTools.FieldRef<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge> BridgeRef =
            AccessTools.FieldRefAccess<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge>("playersBridge");
        private static readonly AccessTools.FieldRef<WeaponSoundPlayer, SuperBetterAudioQueue> QueueRef =
            AccessTools.FieldRefAccess<WeaponSoundPlayer, SuperBetterAudioQueue>("_queue");
        private static readonly AccessTools.FieldRef<SpatialAudioSystem, Dictionary<int, SoundOcclusionProcessor>> ProcessorsRef =
            AccessTools.FieldRefAccess<SpatialAudioSystem, Dictionary<int, SoundOcclusionProcessor>>("_registeredPlayerProcessors");

        private class Gun
        {
            public WeaponSoundPlayer W;
            public int Key, Player;
            public bool HasRoute, Init;
            public Vector3 Near, CurDir;
            public float Path, CurDist, NextScan, Moved;
            public int HeldRoute = -1;
            public uint HeldPair;
        }

        private static readonly List<Gun> Guns = new List<Gun>();
        private static readonly Dictionary<int, Gun> ByKey = new Dictionary<int, Gun>();

        public static int Tracked, RouteChanges, HeldBack;
        public static long MovedFrames;
        public static double MovedSum;
        public static float MaxMoved;

        public static void Apply(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(WeaponSoundPlayer), nameof(WeaponSoundPlayer.FireBullet)),
                postfix: new HarmonyMethod(typeof(PortalPositioning), nameof(FirePostfix)));
        }

        public static void Reset()
        {
            Guns.Clear(); ByKey.Clear();
            Tracked = RouteChanges = HeldBack = 0; MovedFrames = 0; MovedSum = 0; MaxMoved = 0f;
        }

        // 0.6.37: for 6.2's log: does this gun currently reach you along a route?
        public static bool HasRoute(WeaponSoundPlayer w) => ByKey.TryGetValue(w.GetInstanceID(), out Gun g) && g.HasRoute;

        // For routes.csv: how far this gun's voices are from its true direction right now.
        public static float MovedDeg(WeaponSoundPlayer w) => ByKey.TryGetValue(w.GetInstanceID(), out Gun g) ? g.Moved : 0f;

        private static void FirePostfix(WeaponSoundPlayer __instance)
        {
            if (!Plugin.PortalPositioningOn.Value) return;
            try
            {
                int key = __instance.GetInstanceID();
                if (ByKey.ContainsKey(key) || QueueRef(__instance) == null) return;
                BaseSoundPlayer.IObserverToPlayerBridge b = BridgeRef(__instance);
                if (b == null || b.iPlayer == null || b.PointOfView == EPointOfView.FirstPerson) return;
                var g = new Gun { W = __instance, Key = key, Player = b.iPlayer.Id };
                Guns.Add(g); ByKey[key] = g; Tracked++;
            }
            catch (Exception e) { DiagRunner.ReportOnce("6.1 portal positioning (fire)", e); }
        }

        private void LateUpdate()
        {
            if (Guns.Count == 0) return;
            if (!Plugin.PortalPositioningOn.Value || !MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated) { Guns.Clear(); ByKey.Clear(); return; } // the game re-poses them next Update
            Perf.Begin();
            try
            {
                SpatialAudioSystem sas = MonoBehaviourSingleton<SpatialAudioSystem>.Instance;
                Transform lt = sas.ListenerTransform;
                if (lt == null) return;
                float now = Time.time, dt = Mathf.Max(Time.unscaledDeltaTime, 0f);
                for (int i = Guns.Count - 1; i >= 0; i--)
                {
                    Gun g = Guns[i];
                    SuperBetterAudioQueue q = g.W != null ? QueueRef(g.W) : null;
                    if (q == null)
                    {
                        ByKey.Remove(g.Key);
                        Guns[i] = Guns[Guns.Count - 1]; Guns.RemoveAt(Guns.Count - 1);
                        continue;
                    }
                    Place(sas, lt.position, g, q, now, dt);
                }
            }
            catch (Exception e) { DiagRunner.ReportOnce("6.1 portal positioning", e); }
            finally { Perf.End(Perf.Part.PortalPositioning); }
        }

        private static void Place(SpatialAudioSystem sas, Vector3 listener, Gun g, SuperBetterAudioQueue q, float now, float dt)
        {
            if (!sas.TryGetPlayerAudioContainer(g.Player, out AudioSourceContainer c) || c == null) return;
            if (now >= g.NextScan) Scan(sas, listener, g, c, now);

            Vector3 gun = g.W.WeaponPosition, toGun = gun - listener;
            float straight = toGun.magnitude;
            if (straight < 0.01f) return;
            Vector3 dirGun = toGun / straight, dirTarget = dirGun;
            float distTarget = straight;

            if (g.HasRoute)
            {
                Vector3 toNear = g.Near - listener;
                float nearM = toNear.magnitude;
                float blend = Mathf.Clamp01(c.PropagationEffect / FullAtEffect) * Mathf.Clamp01(nearM / NearFadeM) * Mathf.Clamp01(Plugin.PortalStrength.Value);
                if (blend > 0f && nearM > 0.01f)
                {
                    dirTarget = Vector3.Slerp(dirGun, toNear / nearM, blend);
                    distTarget = Mathf.Lerp(straight, Mathf.Max(g.Path, straight), blend);
                }
            }

            if (!g.Init) { g.CurDir = dirTarget; g.CurDist = distTarget; g.Init = true; }
            else
            {
                float k = 1f - Mathf.Exp(-dt / SmoothS);
                g.CurDir = Vector3.Slerp(g.CurDir, dirTarget, k);
                g.CurDist = Mathf.Lerp(g.CurDist, distTarget, k);
            }

            g.Moved = Vector3.Angle(g.CurDir, dirGun);
            if (g.Moved < 0.5f && Mathf.Abs(g.CurDist - straight) < 0.05f) return; // the game's own position stands
            q.Pose(listener + g.CurDir.normalized * g.CurDist);
            MovedFrames++; MovedSum += g.Moved;
            if (g.Moved > MaxMoved) MaxMoved = g.Moved;
        }

        private static void Scan(SpatialAudioSystem sas, Vector3 listener, Gun g, AudioSourceContainer c, float now)
        {
            g.NextScan = now + ScanEveryS;
            if (!ProcessorsRef(sas).TryGetValue(g.Player, out SoundOcclusionProcessor p) || !(p?._occluder is CombinedSpatialAudioOccluder occ)) { g.HasRoute = false; return; }
            PortalRoutes.Result r = PortalRoutes.Read(sas, c, occ, listener, g.HeldRoute, g.HeldPair, Hysteresis);
            if (r.Reason == PortalRoutes.Why.Busy || r.Reason == PortalRoutes.Why.NotComputed) { g.NextScan = now + RetryS; return; } // keep what it has
            if (r.Reason != PortalRoutes.Why.Route || !r.Found) { g.HasRoute = false; g.HeldRoute = -1; return; }
            if (r.KeptHeld) HeldBack++;
            if (g.HeldRoute >= 0 && (r.Best != g.HeldRoute || r.Pair != g.HeldPair)) RouteChanges++;
            g.HeldRoute = r.Best; g.HeldPair = r.Pair;
            g.Near = r.Near; g.Path = r.Path; g.HasRoute = true;
        }

        public static string Summary()
        {
            if (Tracked == 0) return $"PORTAL POSITIONING (6.1 {(Plugin.PortalPositioningOn.Value ? "ON" : "off")}): no gun moved\n";
            return $"PORTAL POSITIONING (6.1 {(Plugin.PortalPositioningOn.Value ? "ON" : "off")}, strength {Plugin.PortalStrength.Value:0.00}): {Tracked} guns handled, voices moved for {MovedFrames} gun-frames, on average {(MovedFrames > 0 ? MovedSum / MovedFrames : 0):0} deg from the true direction (max {MaxMoved:0}); route changed {RouteChanges} times, held back by the 0.05 margin {HeldBack} times\n";
        }
    }
}
