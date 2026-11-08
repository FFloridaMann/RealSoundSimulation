using Audio.SpatialSystem;
using Unity.Collections;
using UnityEngine;

namespace RealSoundSimulation.Acoustics
{
    // 0.6.24: reads the game's finished route result for a shooter in another room (PropagationJob: one cost per baked route;
    // the game uses only the cost and throws the route away). Shared by routes.csv and 6.1 so the log and the move agree.
    // Reasons there is no route mirror ParallelSoundPropagationCalculator.OnUpdate.
    internal static class PortalRoutes
    {
        public enum Why { Route, Outdoor, SameRoom, OutOfRange, NoRoute, NotComputed, Busy }
        public static readonly string[] WhyNames = { "route", "outdoor", "sameRoom", "outOfRange", "noRoute", "notComputed", "busy" };

        private const float NoCost = 1e30f;

        public struct Result
        {
            public Why Reason;
            public bool Found, Reversed, KeptHeld;
            public int Routes, Best, Portals;
            public uint Pair;
            public float BestCost, SecondCost, Path, Closure;
            public Vector3 Near; // the opening nearest the listener
        }

        // heldRoute/heldPair/hysteresis: keep the route already in use unless the game's best beats it by more than hysteresis.
        public static Result Read(SpatialAudioSystem sas, AudioSourceContainer c, CombinedSpatialAudioOccluder occ, Vector3 listener, int heldRoute = -1, uint heldPair = 0, float hysteresis = 0f)
        {
            var r = new Result { Reason = Why.NoRoute, Best = -1, BestCost = float.MaxValue, SecondCost = float.MaxValue, Path = float.NaN, Closure = float.NaN };
            ISpatialAudioRoom room = c.CurrentAudioRoom;
            var calc = occ._propagationCalculator as ParallelSoundPropagationCalculator;
            SoundPropagationJobScheduler s = calc?._propagationJobScheduler;
            RoomPair pair = null;

            if (room == null || !room.IsValid) return r;
            if (sas.IsSourceAndListenerInCommonOutdoor(room)) { r.Reason = Why.Outdoor; return r; }
            if (sas.IsSourceInListenerRoom(c)) { r.Reason = Why.SameRoom; return r; }
            if ((listener - c.CurrentPosition).sqrMagnitude > c.SqrMaxDistance) { r.Reason = Why.OutOfRange; return r; }
            if (!sas.TryGetMatchingRoomPair(room, out pair, out r.Reversed) || pair == null || pair.Routes.Length == 0) return r;
            if (s == null || s.DataDisposed || !s._routesSettingsData.IsCreated || !s._routeNodesData.IsCreated || calc._prevRoomPairID != pair.ID) { r.Reason = Why.NotComputed; return r; }
            if (!s.LastHandle.IsCompleted) { r.Reason = Why.Busy; return r; }

            r.Reason = Why.Route;
            r.Pair = pair.ID;
            NativeArray<float> costs = s._lowestCost, dists = s._lowestDistance;
            r.Routes = Mathf.Min(s._routesSettingsData.Length, costs.Length);
            float bestDist = float.MaxValue;
            for (int i = 0; i < r.Routes; i++) // the game's own rule (PropagationEffectCalculationJob): within 0.01 of the best cost, the shorter wins
            {
                float cost = costs[i], dist = dists[i];
                if (cost >= NoCost) continue; // not evaluated this update (quality limit)
                if (cost < r.BestCost - 0.01f) { if (r.BestCost < NoCost) r.SecondCost = r.BestCost; r.BestCost = cost; bestDist = dist; r.Best = i; }
                else if (Mathf.Abs(cost - r.BestCost) <= 0.01f && dist < bestDist) { bestDist = dist; r.Best = i; }
                else if (cost < r.SecondCost) r.SecondCost = cost;
            }
            if (r.Best < 0) { r.Reason = Why.NotComputed; return r; }

            if (hysteresis > 0f && heldRoute >= 0 && heldRoute < r.Routes && heldPair == r.Pair && heldRoute != r.Best
                && costs[heldRoute] < NoCost && r.BestCost > costs[heldRoute] - hysteresis)
            {
                r.Best = heldRoute; r.BestCost = costs[heldRoute]; bestDist = dists[heldRoute]; r.KeptHeld = true;
            }

            r.Path = bestDist;
            AudioRouteData route = s._routesSettingsData[r.Best];
            int start = route.NodeStartIndex + 1, first = -1, last = -1;
            for (int n = start; n < s._routeNodesData.Length && n < start + route.PortalDataLength; n++)
            {
                if (s._routeNodesData[n].Type != AudioNode.ENodeType.Portal) break;
                if (first < 0) first = n;
                last = n; r.Portals++;
            }
            // the job runs listener-to-shooter when the room pair is stored the other way round
            int nearIdx = r.Reversed ? first : last;
            if (nearIdx >= 0)
            {
                AudioNode node = s._routeNodesData[nearIdx];
                r.Near = new Vector3(node.Position.x, node.Position.y, node.Position.z);
                r.Closure = node.PortalClosureLevel;
                r.Found = true;
            }
            return r;
        }
    }
}
