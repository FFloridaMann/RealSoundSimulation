using System;
using System.Collections.Generic;
using System.Text;
using Audio.SpatialSystem;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace RealSoundSimulation.Diagnostics
{
    // 0.6.22, ROADMAP 6.1 first step: measure only. Portal positioning will play a sound from the opening it reaches you
    // through. The game already works out the best route for every bot in another room (PropagationJob, one cost per baked
    // route) but only uses its cost; the route itself is thrown away. This reads that finished result each time a bot fires
    // (at most 4/s per gun, only while "Log portal routes" is on) and writes what the sound WOULD do to routes.csv.
    // Nothing is moved and no sound changes. What the raid has to show before anything is moved: are the openings sensible,
    // how far they are from the true direction, and how often the best route flips.
    //
    // Reasons a shot has no route row, mirroring ParallelSoundPropagationCalculator.OnUpdate:
    //   outdoor  shooter and you both in the common outdoor area   sameRoom  in your room (no portal involved)
    //   outOfRange past the container's range    noRoute  the map has no baked route between those rooms
    //   notComputed the calculator hasn't run for this room pair yet    busy  its job is still running this frame
    internal static class RouteLog
    {
        public const string Header = "t,clock,shooterId,reason,straightM,los,dy,sourceRoom,listenerRoom,reversed,routes,bestCost,secondCost,pathM,portals,nearX,nearY,nearZ,nearPortalM,closure,trueAz,portalAz,angleDeg,prop,occl,routeChanged,movedDeg,dullHz";

        private const float PerGunS = 0.25f, ChangedM = 1.5f, ChangedWithinS = 3f, NoCost = 1e30f;

        private static readonly AccessTools.FieldRef<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge> BridgeRef =
            AccessTools.FieldRefAccess<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge>("playersBridge");
        private static readonly AccessTools.FieldRef<SpatialAudioSystem, Dictionary<int, SoundOcclusionProcessor>> ProcessorsRef =
            AccessTools.FieldRefAccess<SpatialAudioSystem, Dictionary<int, SoundOcclusionProcessor>>("_registeredPlayerProcessors");

        private struct Seen { public Vector3 Portal; public float Time; }
        private static readonly Dictionary<int, float> LastRow = new Dictionary<int, float>();
        private static readonly Dictionary<int, Seen> LastPortal = new Dictionary<int, Seen>();

        public static int Rows, Routed, OutdoorN, SameRoomN, OutOfRangeN, NoRouteN, NotComputedN, BusyN, Changes;
        public static double AngleSum, PathRatioSum; public static int Over45;

        public static void Apply(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(WeaponSoundPlayer), nameof(WeaponSoundPlayer.FireBullet)),
                postfix: new HarmonyMethod(typeof(RouteLog), nameof(FirePostfix)));
        }

        public static void Reset()
        {
            LastRow.Clear(); LastPortal.Clear();
            Rows = Routed = OutdoorN = SameRoomN = OutOfRangeN = NoRouteN = NotComputedN = BusyN = Changes = Over45 = 0;
            AngleSum = PathRatioSum = 0;
        }

        private static void FirePostfix(WeaponSoundPlayer __instance)
        {
            if (!Plugin.RouteLogOn.Value || !DiagLog.IsOpen) return;
            try
            {
                BaseSoundPlayer.IObserverToPlayerBridge b = BridgeRef(__instance);
                if (b == null || b.iPlayer == null || b.PointOfView == EPointOfView.FirstPerson) return;
                int key = __instance.GetInstanceID();
                float now = Time.time;
                if (LastRow.TryGetValue(key, out float last) && now - last < PerGunS) return;
                LastRow[key] = now;
                if (!MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated) return;
                Log(__instance, b.iPlayer.Id, MonoBehaviourSingleton<SpatialAudioSystem>.Instance);
            }
            catch (Exception e) { DiagRunner.ReportOnce("route log", e); }
        }

        private static void Log(WeaponSoundPlayer w, int id, SpatialAudioSystem sas)
        {
            Transform lt = sas.ListenerTransform;
            if (lt == null || !sas.TryGetPlayerAudioContainer(id, out AudioSourceContainer c) || c == null) return;
            if (!ProcessorsRef(sas).TryGetValue(id, out SoundOcclusionProcessor proc) || !(proc?._occluder is CombinedSpatialAudioOccluder occ)) return;

            Vector3 listener = lt.position, gun = w.WeaponPosition;
            float straight = Vector3.Distance(listener, gun);
            ISpatialAudioRoom room = c.CurrentAudioRoom;
            Acoustics.PortalRoutes.Result rr = Acoustics.PortalRoutes.Read(sas, c, occ, listener);
            string reason = Acoustics.PortalRoutes.WhyNames[(int)rr.Reason];
            bool reversed = rr.Reversed, found = rr.Found; Vector3 near = rr.Near;
            int routes = rr.Routes, best = rr.Best, portals = rr.Portals;
            float bestCost = rr.BestCost, secondCost = rr.SecondCost, path = rr.Path, closure = rr.Closure;

            bool changed = false; float angle = float.NaN, portalAz = float.NaN;
            if (found)
            {
                Routed++;
                angle = Vector3.Angle(gun - listener, near - listener);
                AngleSum += angle; if (angle > 45f) Over45++;
                if (straight > 0.01f) PathRatioSum += path / straight;
                portalAz = Acoustics.OutdoorExposure.RelAz(near - listener);
                if (LastPortal.TryGetValue(id, out Seen p) && Time.time - p.Time < ChangedWithinS && (p.Portal - near).sqrMagnitude > ChangedM * ChangedM) { changed = true; Changes++; }
                LastPortal[id] = new Seen { Portal = near, Time = Time.time };
            }
            else Count(reason);
            Rows++;

            bool los = !Physics.Linecast(listener, gun, sas.OcclusionSettings != null ? sas.OcclusionSettings.commonSettings.occlusionLayerMask.value : 1, QueryTriggerInteraction.Ignore);
            ISpatialAudioRoom lr = sas.ListenerCurrentRoom;

            StringBuilder sb = DiagLog.Begin();
            DiagLog.AppendF(sb, Time.time - DiagRunner.RaidStartTime, 2); sb.Append(',');
            BatchWait.AppendClock(sb, DateTime.Now); sb.Append(',');
            DiagLog.AppendI(sb, id); sb.Append(',');
            sb.Append(reason); sb.Append(',');
            DiagLog.AppendF(sb, straight, 1); sb.Append(',');
            DiagLog.AppendB(sb, los); sb.Append(',');
            DiagLog.AppendF(sb, gun.y - listener.y, 1); sb.Append(',');
            if (room != null) DiagLog.AppendI(sb, room.ID); sb.Append(',');
            if (lr != null) DiagLog.AppendI(sb, lr.ID); sb.Append(',');
            DiagLog.AppendB(sb, reversed); sb.Append(',');
            DiagLog.AppendI(sb, routes); sb.Append(',');
            if (best >= 0) DiagLog.AppendF(sb, bestCost, 3); sb.Append(',');
            if (secondCost < NoCost) DiagLog.AppendF(sb, secondCost, 3); sb.Append(',');
            DiagLog.AppendF(sb, path, 1); sb.Append(',');
            DiagLog.AppendI(sb, portals); sb.Append(',');
            if (found) { DiagLog.AppendF(sb, near.x, 2); sb.Append(','); DiagLog.AppendF(sb, near.y, 2); sb.Append(','); DiagLog.AppendF(sb, near.z, 2); sb.Append(','); DiagLog.AppendF(sb, Vector3.Distance(listener, near), 1); }
            else sb.Append(",,,");
            sb.Append(',');
            DiagLog.AppendF(sb, closure, 2); sb.Append(',');
            DiagLog.AppendF(sb, Acoustics.OutdoorExposure.RelAz(gun - listener), 0); sb.Append(',');
            DiagLog.AppendF(sb, portalAz, 0); sb.Append(',');
            DiagLog.AppendF(sb, angle, 0); sb.Append(',');
            DiagLog.AppendF(sb, c.PropagationEffect, 3); sb.Append(',');
            DiagLog.AppendF(sb, c.OcclusionEffect, 3); sb.Append(',');
            DiagLog.AppendB(sb, changed); sb.Append(',');
            DiagLog.AppendF(sb, Fixes.PortalPositioning.MovedDeg(w), 0); sb.Append(','); // 0.6.24: where 6.1 actually put it
            DiagLog.AppendF(sb, Fixes.PortalPositioning.DullHz(w), 0); // 0.6.40: 6.1b's cutoff, 0 = not dulled
            DiagLog.EndRow("routes.csv");
        }

        private static void Count(string reason)
        {
            switch (reason)
            {
                case "outdoor": OutdoorN++; break;
                case "sameRoom": SameRoomN++; break;
                case "outOfRange": OutOfRangeN++; break;
                case "noRoute": NoRouteN++; break;
                case "notComputed": NotComputedN++; break;
                case "busy": BusyN++; break;
            }
        }

        public static string Summary()
        {
            if (!Plugin.RouteLogOn.Value && Rows == 0) return "PORTAL ROUTES: not logged (Log portal routes off)\n";
            var sb = new StringBuilder();
            sb.AppendLine($"PORTAL ROUTES (routes.csv, measure only; ROADMAP 6.1): {Rows} bot shots sampled, {Routed} with a route");
            sb.AppendLine($"  no route because: both outdoors {OutdoorN}, same room as you {SameRoomN}, out of range {OutOfRangeN}, none baked {NoRouteN}, not computed yet {NotComputedN}, job still running {BusyN}");
            if (Routed > 0) sb.AppendLine($"  opening vs true direction: average {AngleSum / Routed:0} deg apart, over 45 deg in {100.0 * Over45 / Routed:0}%; route path {PathRatioSum / Routed:0.00} x the straight line; best opening changed {Changes} times");
            return sb.ToString();
        }
    }
}
