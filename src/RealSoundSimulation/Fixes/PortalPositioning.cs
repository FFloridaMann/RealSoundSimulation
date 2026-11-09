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
    //
    // 6.1b (0.6.40): the same voices are dulled by the way the sound got here. User, 9 Nov 2026: "the sound should be muffled (less distinct) regardless".
    // Raid 20261109_001706: a bot 39 m away with 3-5 walls on the straight line read occlusion 0.17, because one opening connects the rooms; the game
    // only muffles above 0.60, so 2 of 96 bot shots under 60 m were muffled. So this uses the geometry, not that number:
    //   steps    1 if a wall is on the straight line + the turn at the nearest opening / 90 deg (at most 135, less within 1 m of it) + 0.5 per extra opening
    //   cutoff   16 kHz x 2^(-1.5 x steps x "6.1c"), never below "6.1d"; smoothed over 150 ms, set at once on the first shot
    // Sound bending round an edge really does lose its highs first, but real rooms are gentler than a filter and hard walls bounce some highs along, so
    // these are starting values. Steps aside on the game's muffled channel. Our own filter: back to neutral when the gun goes quiet or gives up its voices.
    [DefaultExecutionOrder(32000)]
    internal class PortalPositioning : MonoBehaviour
    {
        private const float ScanEveryS = 0.1f, RetryS = 0.02f, Hysteresis = 0.05f, FullAtEffect = 0.3f, SmoothS = 0.15f, NearFadeM = 1f,
            DullTopHz = 16000f, DullOffHz = 22000f, DullOctavesPerStep = 1.5f, DullBendFullDeg = 90f, DullBendMaxDeg = 135f, DullExtraOpening = 0.5f, DullHoldS = 6f;

        private static readonly AccessTools.FieldRef<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge> BridgeRef =
            AccessTools.FieldRefAccess<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge>("playersBridge");
        private static readonly AccessTools.FieldRef<WeaponSoundPlayer, SuperBetterAudioQueue> QueueRef =
            AccessTools.FieldRefAccess<WeaponSoundPlayer, SuperBetterAudioQueue>("_queue");
        private static readonly AccessTools.FieldRef<SpatialAudioSystem, Dictionary<int, SoundOcclusionProcessor>> ProcessorsRef =
            AccessTools.FieldRefAccess<SpatialAudioSystem, Dictionary<int, SoundOcclusionProcessor>>("_registeredPlayerProcessors");
        private static readonly AccessTools.FieldRef<WeaponSoundPlayer, UnityEngine.Audio.AudioMixerGroup> MixerRef =
            AccessTools.FieldRefAccess<WeaponSoundPlayer, UnityEngine.Audio.AudioMixerGroup>("_currentMixerGroup");

        private class Gun
        {
            public WeaponSoundPlayer W;
            public SuperBetterAudioQueue Q;
            public int Key, Player, Portals;
            public bool HasRoute, Init, Blocked, FilterOn;
            public Vector3 Near, CurDir;
            public float Path, CurDist, NextScan, Moved, LastFire, CurCut = DullOffHz;
            public int HeldRoute = -1;
            public uint HeldPair;
        }

        private static readonly List<Gun> Guns = new List<Gun>();
        private static readonly Dictionary<int, Gun> ByKey = new Dictionary<int, Gun>();
        private static readonly Dictionary<GameObject, AudioLowPassFilter> Filters = new Dictionary<GameObject, AudioLowPassFilter>(); // by object: a filter acts on every source on it

        public static int Tracked, RouteChanges, HeldBack;
        public static long MovedFrames, DullFrames, StepAsideFrames;
        public static double MovedSum, DullLogSum;
        public static float MaxMoved, MinCut = DullOffHz;

        private static bool Wanted => Plugin.PortalPositioningOn.Value || Plugin.RouteDullOn.Value;

        public static void Apply(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(WeaponSoundPlayer), nameof(WeaponSoundPlayer.FireBullet)),
                postfix: new HarmonyMethod(typeof(PortalPositioning), nameof(FirePostfix)));
        }

        public static void Reset()
        {
            NeutralAll();
            Filters.Clear();
            Guns.Clear(); ByKey.Clear();
            Tracked = RouteChanges = HeldBack = 0; MovedFrames = DullFrames = StepAsideFrames = 0; MovedSum = DullLogSum = 0; MaxMoved = 0f; MinCut = DullOffHz;
        }

        private static void NeutralAll()
        {
            foreach (var kv in Filters) if (kv.Value != null && kv.Value.enabled) kv.Value.enabled = false;
        }

        // For routes.csv: this gun's cutoff right now, 0 = not dulled. The first row of a burst can read 0: it's written before the voices are set that frame.
        public static float DullHz(WeaponSoundPlayer w) => ByKey.TryGetValue(w.GetInstanceID(), out Gun g) && g.FilterOn ? g.CurCut : 0f;

        // 0.6.37: for 6.2's log: does this gun currently reach you along a route?
        public static bool HasRoute(WeaponSoundPlayer w) => ByKey.TryGetValue(w.GetInstanceID(), out Gun g) && g.HasRoute;

        // For routes.csv: how far this gun's voices are from its true direction right now.
        public static float MovedDeg(WeaponSoundPlayer w) => ByKey.TryGetValue(w.GetInstanceID(), out Gun g) ? g.Moved : 0f;

        private static void FirePostfix(WeaponSoundPlayer __instance)
        {
            if (!Wanted) return;
            try
            {
                int key = __instance.GetInstanceID();
                float now = Time.time;
                if (ByKey.TryGetValue(key, out Gun known))
                {
                    if (now - known.LastFire >= DullHoldS) known.NextScan = 0f; // first shot after a pause: the line may have changed
                    known.LastFire = now;
                    return;
                }
                if (QueueRef(__instance) == null) return;
                BaseSoundPlayer.IObserverToPlayerBridge b = BridgeRef(__instance);
                if (b == null || b.iPlayer == null || b.PointOfView == EPointOfView.FirstPerson) return;
                var g = new Gun { W = __instance, Key = key, Player = b.iPlayer.Id, LastFire = now };
                Guns.Add(g); ByKey[key] = g; Tracked++;
            }
            catch (Exception e) { DiagRunner.ReportOnce("6.1 portal positioning (fire)", e); }
        }

        private void LateUpdate()
        {
            if (Guns.Count == 0) return;
            if (!Wanted || !MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated) { NeutralAll(); Guns.Clear(); ByKey.Clear(); return; } // the game re-poses them next Update
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
                    if (q != g.Q) { SetVoices(g.Q, DullOffHz, false); g.FilterOn = false; g.CurCut = DullOffHz; g.Q = q; } // gave its voices back: others use them now
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
            Dull(listener, g, q, now, dt);
            if (!Plugin.PortalPositioningOn.Value) { g.Init = false; g.Moved = 0f; return; }

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
            g.Near = r.Near; g.Path = r.Path; g.Portals = r.Portals; g.HasRoute = true;

            if (Plugin.RouteDullOn.Value && now - g.LastFire < DullHoldS)
            {
                Vector3 toGun = g.W.WeaponPosition - listener;
                float len = toGun.magnitude;
                int mask = sas.OcclusionSettings != null ? sas.OcclusionSettings.commonSettings.occlusionLayerMask.value : 1;
                g.Blocked = len > 0.01f && CountedRay.Raycast(listener, toGun / len, out RaycastHit _, len, mask, QueryTriggerInteraction.Ignore);
            }
        }

        private static void Dull(Vector3 listener, Gun g, SuperBetterAudioQueue q, float now, float dt)
        {
            float target = now - g.LastFire < DullHoldS ? DullTarget(listener, g) : DullOffHz;
            if (!g.FilterOn && target < DullOffHz) g.CurCut = target; // the shot starts already dulled
            else g.CurCut = Mathf.Exp(Mathf.Lerp(Mathf.Log(g.CurCut), Mathf.Log(target), 1f - Mathf.Exp(-dt / SmoothS)));

            bool on = target < DullOffHz || g.CurCut < DullOffHz * 0.95f;
            if (!on && !g.FilterOn) return;
            if (!on) g.CurCut = DullOffHz;
            SetVoices(q, g.CurCut, on);
            g.FilterOn = on;
            if (on)
            {
                DullFrames++; DullLogSum += Mathf.Log(g.CurCut);
                if (g.CurCut < MinCut) MinCut = g.CurCut;
            }
        }

        private static float DullTarget(Vector3 listener, Gun g)
        {
            if (!Plugin.RouteDullOn.Value || !g.HasRoute) return DullOffHz;
            if (MonoBehaviourSingleton<BetterAudio>.Instantiated && MixerRef(g.W) == MonoBehaviourSingleton<BetterAudio>.Instance.GunshotOccludedMixerGroup)
            {
                StepAsideFrames++;
                return DullOffHz; // the game has already muffled this shot
            }
            return DullHzFor(g.W.WeaponPosition - listener, g.Near - listener, g.Blocked, g.Portals);
        }

        // The rule for how dull a route makes a sound; 6.4 uses it for explosions too.
        internal static float DullHzFor(Vector3 toSource, Vector3 toNear, bool blocked, int openings)
        {
            float bend = Mathf.Min(Vector3.Angle(toSource, toNear), DullBendMaxDeg) / DullBendFullDeg * Mathf.Clamp01(toNear.magnitude / NearFadeM);
            float steps = (blocked ? 1f : 0f) + bend + DullExtraOpening * Mathf.Max(openings - 1, 0);
            return Mathf.Clamp(DullTopHz * Mathf.Pow(2f, -DullOctavesPerStep * steps * Plugin.RouteDullStrength.Value), Plugin.RouteDullMinHz.Value, DullTopHz);
        }

        private static void SetVoices(SuperBetterAudioQueue q, float cutoff, bool on)
        {
            if (q == null || q.AudioSources == null) return;
            foreach (BetterSource v in q.AudioSources)
            {
                if (!(v is SuperSource ss)) continue;
                SetFilter(ss.source1, cutoff, on);
                SetFilter(ss.source2, cutoff, on);
            }
        }

        private static void SetFilter(AudioSource a, float cutoff, bool on)
        {
            if (a == null) return;
            GameObject go = a.gameObject;
            if (!Filters.TryGetValue(go, out AudioLowPassFilter lp))
            {
                if (!on) return;
                lp = go.AddComponent<AudioLowPassFilter>(); // our own, separate from the game's occlusion filters
                if (lp != null) lp.lowpassResonanceQ = 1f;
                Filters[go] = lp;
            }
            if (lp == null) return;
            if (lp.enabled != on) lp.enabled = on;
            if (on) lp.cutoffFrequency = cutoff;
        }

        public static string Summary() => PositionSummary() + DullSummary();

        private static string DullSummary()
        {
            string state = Plugin.RouteDullOn.Value ? $"ON, strength {Plugin.RouteDullStrength.Value:0.00}, floor {Plugin.RouteDullMinHz.Value:0} Hz" : "off";
            if (DullFrames == 0) return $"ROUTE DULLING (6.1b {state}): no gun dulled; on the game's muffled channel for {StepAsideFrames} gun-frames (routes.csv dullHz)\n";
            return $"ROUTE DULLING (6.1b {state}): dulled for {DullFrames} gun-frames, typical cutoff {Mathf.Exp((float)(DullLogSum / DullFrames)):0} Hz, lowest {MinCut:0} Hz, voices with a filter {Filters.Count}; on the game's muffled channel for {StepAsideFrames} gun-frames (routes.csv dullHz)\n";
        }

        private static string PositionSummary()
        {
            if (Tracked == 0) return $"PORTAL POSITIONING (6.1 {(Plugin.PortalPositioningOn.Value ? "ON" : "off")}): no gun moved\n";
            return $"PORTAL POSITIONING (6.1 {(Plugin.PortalPositioningOn.Value ? "ON" : "off")}, strength {Plugin.PortalStrength.Value:0.00}): {Tracked} guns handled, voices moved for {MovedFrames} gun-frames, on average {(MovedFrames > 0 ? MovedSum / MovedFrames : 0):0} deg from the true direction (max {MaxMoved:0}); route changed {RouteChanges} times, held back by the 0.05 margin {HeldBack} times\n";
        }
    }
}
