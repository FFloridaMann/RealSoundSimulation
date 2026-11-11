using System;
using System.Collections.Generic;
using Audio.SpatialSystem;
using RealSoundSimulation.Acoustics;
using RealSoundSimulation.Diagnostics;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace RealSoundSimulation.Fixes
{
    // 0.7.15 "F": other people's footsteps and body sounds reach you the way gunshots do. User, 10 Nov 2026: "it still is at times hard to
    // tell if someone is above or below you". The game plays these at the person's real position and leaves the height to the spatializer,
    // which headphones barely carry; what tells you someone is upstairs is the way the sound gets to you.
    //   sounds   footsteps (NestedStepSoundSource, and from 0.7.16 EFT 1.0's extra step layer outdoors), gear rustle, searching and looting, voices, and everything their hands play
    //            (meds, food and drink, reloads, handling: BaseSoundPlayer's clip source). Other people only, within "F range" m.
    //   floor    something above or below you (more than 1.8 m up or down, with a floor or ceiling on the straight line between): the sound
    //            stays where they really are, so the spatializer keeps the height, and goes through the floor by its material and thickness
    //            (ThroughWall's measure). Footsteps keep their low thud; voices, zippers and meds come through very muffled.
    //   route    a voice, zipper or med from another floor with a stairwell or open route comes from that opening, muffled by the route,
    //            like gunshots (PortalPositioning's routes and rule). Footsteps stay the thud through the floor. Same floor, another room:
    //            everything comes from the doorway.
    //   else     same room or the open: unchanged.
    // Our own low-pass on each source, on top of the game's occlusion. Moved sources are put back when they no longer need it.
    [DefaultExecutionOrder(32001)]
    internal class BodySounds : MonoBehaviour
    {
        private const float ScanEveryS = 0.1f, WallsEveryS = 0.5f, FloorDyM = 1.8f, FloorNormalY = 0.7f, HeadUpM = 1.0f, SmoothS = 0.15f,
            FullAtEffect = 0.3f, NearFadeM = 1f, OffHz = 22000f, LossLowDb = 15f, LossHighDb = 50f,
            StepFloorTopHz = 900f, StepFloorLowHz = 250f, AirFloorTopHz = 500f, AirFloorLowHz = 150f;
        private const int MaxHits = 16;

        private static readonly AccessTools.FieldRef<Player, BetterSource> StepRef = AccessTools.FieldRefAccess<Player, BetterSource>("NestedStepSoundSource");
        private static readonly AccessTools.FieldRef<Player, BetterSource> GearRef = AccessTools.FieldRefAccess<Player, BetterSource>("_gearSource");
        private static readonly AccessTools.FieldRef<Player, BetterSource> SearchRef = AccessTools.FieldRefAccess<Player, BetterSource>("_searchSource");
        private static readonly AccessTools.FieldRef<Player, BetterSource> SpeechRef = AccessTools.FieldRefAccess<Player, BetterSource>("_speechSource");
        // 0.7.16: EFT 1.0's extra footstep layer (surface detail, only while the person is outdoors) plays on a source of its own
        private static readonly AccessTools.FieldRef<Player, Audio.AudioWeatherSystem.IPlayerStepAudioController> LayerRef = AccessTools.FieldRefAccess<Player, Audio.AudioWeatherSystem.IPlayerStepAudioController>("_specificStepAudioController");
        private static readonly AccessTools.FieldRef<BaseSoundPlayer, BetterSource> ClipsRef = AccessTools.FieldRefAccess<BaseSoundPlayer, BetterSource>("_clipsSource");
        private static readonly AccessTools.FieldRef<BaseSoundPlayer, bool> ReleasedRef = AccessTools.FieldRefAccess<BaseSoundPlayer, bool>("_isClipSourceReleased");
        private static readonly AccessTools.FieldRef<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge> BridgeRef =
            AccessTools.FieldRefAccess<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge>("playersBridge");
        private static readonly AccessTools.FieldRef<SpatialAudioSystem, Dictionary<int, SoundOcclusionProcessor>> ProcessorsRef =
            AccessTools.FieldRefAccess<SpatialAudioSystem, Dictionary<int, SoundOcclusionProcessor>>("_registeredPlayerProcessors");

        private enum Mode : byte { None, Route, Floor }

        private class Body
        {
            public Player P;
            public int Id;
            public bool HasRoute, Blocked, Floor, Init;
            public Vector3 Near, CurDir;
            public float Path, CurDist, LossDb, NextScan, NextWalls, StepCut = OffHz, AirCut = OffHz, Blend;
            public int Portals, HeldRoute = -1, Walls;
            public uint HeldPair;
            public Mode StepMode, AirMode, LastStepMode, LastAirMode;
            public readonly List<BaseSoundPlayer> Hands = new List<BaseSoundPlayer>();
            public BetterSource Layer; // the 1.0 step layer's source last seen
            public readonly Dictionary<BaseSoundPlayer, BetterSource> HandSrc = new Dictionary<BaseSoundPlayer, BetterSource>(); // the source each was last seen playing on
        }

        private static readonly Dictionary<int, Body> Bodies = new Dictionary<int, Body>();
        private static readonly Dictionary<BetterSource, Vector3> MovedFrom = new Dictionary<BetterSource, Vector3>(); // original local position
        private static readonly HashSet<BetterSource> Filtered = new HashSet<BetterSource>();
        private static readonly HashSet<BetterSource> TouchedNow = new HashSet<BetterSource>();
        private static readonly RaycastHit[] Hits = new RaycastHit[MaxHits];
        private static readonly List<int> Gone = new List<int>();

        public static int Handled, HandsSeen;
        public static long FloorFrames, RouteFrames, SourcesMoved;
        public static float LowestStepHz = OffHz, LowestAirHz = OffHz;

        public static void Apply(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(BaseSoundPlayer), nameof(BaseSoundPlayer.PlayClipInternal)),
                postfix: new HarmonyMethod(typeof(BodySounds), nameof(HandsPostfix)));
        }

        public static void Reset()
        {
            NeutralAll();
            Bodies.Clear(); MovedFrom.Clear(); Filtered.Clear();
            Handled = HandsSeen = 0; FloorFrames = RouteFrames = SourcesMoved = 0; LowestStepHz = LowestAirHz = OffHz;
        }

        // Someone's hands played a sound (meds, food, a reload): remember the sound player so its source is treated while it plays.
        private static void HandsPostfix(BaseSoundPlayer __instance)
        {
            if (!Plugin.BodySoundsOn.Value) return;
            try
            {
                BaseSoundPlayer.IObserverToPlayerBridge b = BridgeRef(__instance);
                if (b == null || b.PointOfView == EPointOfView.FirstPerson || !(b.iPlayer is Player p)) return;
                Body body = Get(p);
                if (body != null && !body.Hands.Contains(__instance)) { body.Hands.Add(__instance); HandsSeen++; }
            }
            catch (Exception e) { DiagRunner.ReportOnce("F body sounds (hands)", e); }
        }

        private static Body Get(Player p)
        {
            if (p == null) return null;
            if (Bodies.TryGetValue(p.Id, out Body b)) return b;
            b = new Body { P = p, Id = p.Id };
            Bodies[p.Id] = b; Handled++;
            return b;
        }

        private void LateUpdate()
        {
            if (!Plugin.BodySoundsOn.Value || !MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated || !Singleton<GameWorld>.Instantiated)
            {
                if (MovedFrom.Count > 0 || Filtered.Count > 0) NeutralAll();
                if (Bodies.Count > 0) Bodies.Clear();
                return;
            }
            try
            {
                SpatialAudioSystem sas = MonoBehaviourSingleton<SpatialAudioSystem>.Instance;
                Transform lt = sas.ListenerTransform;
                GameWorld gw = Singleton<GameWorld>.Instance;
                if (lt == null || gw is HideoutGameWorld || gw.AllAlivePlayersList == null) return;
                Player me = gw.MainPlayer;
                Vector3 l = lt.position;
                float now = Time.time, dt = Mathf.Max(Time.unscaledDeltaTime, 0f), range = Plugin.BodySoundsRangeM.Value;

                foreach (Player p in gw.AllAlivePlayersList)
                {
                    if (p == null || p == me || p.PointOfView == EPointOfView.FirstPerson) continue;
                    if ((p.Position - l).sqrMagnitude > range * range) { if (Bodies.ContainsKey(p.Id)) { Gone.Add(p.Id); } continue; }
                    Body b = Get(p);
                    if (now >= b.NextScan) Scan(sas, l, b, now);
                    Decide(l, b, dt);
                    TouchedNow.Clear();
                    Treat(StepRef(p), b, true, l);
                    BetterSource layer = (LayerRef(p) as Audio.AmbientSubsystem.PlayerStepLayerAudioController)?._source;
                    if (b.Layer != null && b.Layer != layer) Neutral(b.Layer); // it gave its source back (went indoors): leave that one as the game made it
                    b.Layer = layer;
                    Treat(layer, b, true, l);
                    Treat(GearRef(p), b, false, l);
                    Treat(SearchRef(p), b, false, l);
                    Treat(SpeechRef(p), b, false, l);
                    for (int i = b.Hands.Count - 1; i >= 0; i--)
                    {
                        BaseSoundPlayer h = b.Hands[i];
                        b.HandSrc.TryGetValue(h, out BetterSource last);
                        BetterSource cur = h != null && !ReleasedRef(h) ? ClipsRef(h) : null;
                        if (last != null && last != cur) Neutral(last); // gone back to the game's pool: leave it as the game made it
                        if (h == null) { b.HandSrc.Remove(h); b.Hands.RemoveAt(i); continue; }
                        if (cur == null) { b.HandSrc.Remove(h); continue; }
                        b.HandSrc[h] = cur;
                        Treat(cur, b, false, l);
                    }
                }
                foreach (var kv in Bodies) if (kv.Value.P == null || kv.Value.P.HealthController == null || !kv.Value.P.HealthController.IsAlive) Gone.Add(kv.Key);
                foreach (int id in Gone)
                {
                    if (!Bodies.TryGetValue(id, out Body b)) continue;
                    if (b.P != null) { Neutral(StepRef(b.P)); Neutral(GearRef(b.P)); Neutral(SearchRef(b.P)); Neutral(SpeechRef(b.P)); }
                    foreach (BetterSource hs in b.HandSrc.Values) Neutral(hs);
                    Neutral(b.Layer);
                    Bodies.Remove(id);
                }
                Gone.Clear();
            }
            catch (Exception e) { DiagRunner.ReportOnce("F body sounds", e); }
        }

        // Where this person is to you: a floor between you, a route through openings, or neither.
        private static void Scan(SpatialAudioSystem sas, Vector3 l, Body b, float now)
        {
            b.NextScan = now + ScanEveryS;
            Vector3 s = b.P.Position + Vector3.up * HeadUpM, d = s - l;
            float len = d.magnitude;
            int mask = sas.OcclusionSettings != null ? sas.OcclusionSettings.commonSettings.occlusionLayerMask.value : 1;
            b.Blocked = false; bool floorHit = false;
            if (len > 0.01f)
            {
                CountedRay.Rays++;
                int n = Physics.RaycastNonAlloc(l, d / len, Hits, len, mask, QueryTriggerInteraction.Ignore);
                b.Blocked = n > 0;
                for (int i = 0; i < n; i++) if (Mathf.Abs(Hits[i].normal.y) >= FloorNormalY) { floorHit = true; break; }
            }
            bool wasFloor = b.Floor;
            b.Floor = floorHit && Mathf.Abs(d.y) > FloorDyM;
            if (b.Floor && (now >= b.NextWalls || !wasFloor))
            {
                b.LossDb = ThroughWall.LossDb(l, s, out b.Walls);
                b.NextWalls = now + WallsEveryS;
            }

            b.HasRoute = false;
            if (!sas.TryGetPlayerAudioContainer(b.Id, out AudioSourceContainer c) || c == null) return;
            if (!ProcessorsRef(sas).TryGetValue(b.Id, out SoundOcclusionProcessor proc) || !(proc?._occluder is CombinedSpatialAudioOccluder occ)) return;
            PortalRoutes.Result r = PortalRoutes.Read(sas, c, occ, l, b.HeldRoute, b.HeldPair, 0.05f);
            if (r.Reason == PortalRoutes.Why.Busy || r.Reason == PortalRoutes.Why.NotComputed) { b.NextScan = now + 0.02f; return; }
            if (r.Reason != PortalRoutes.Why.Route || !r.Found) { b.HeldRoute = -1; return; }
            b.HeldRoute = r.Best; b.HeldPair = r.Pair;
            b.Near = r.Near; b.Path = r.Path; b.Portals = r.Portals; b.HasRoute = true;
            b.Blend = Mathf.Clamp01(c.PropagationEffect / FullAtEffect);
        }

        // Footsteps: through the floor when there is one, else from the doorway. Everything airborne: from the opening when there's a route, else through the floor.
        private static void Decide(Vector3 l, Body b, float dt)
        {
            bool route = b.HasRoute && b.Blocked && b.Blend > 0f;
            b.StepMode = b.Floor ? Mode.Floor : route ? Mode.Route : Mode.None;
            b.AirMode = route ? Mode.Route : b.Floor ? Mode.Floor : Mode.None;

            Vector3 s = b.P.Position + Vector3.up * HeadUpM;
            float routeHz = route ? PortalPositioning.DullHzFor(s - l, b.Near - l, b.Blocked, b.Portals, Plugin.RouteDullStrength.Value) : OffHz;
            float t = Mathf.InverseLerp(LossLowDb, LossHighDb, b.LossDb * Plugin.BodySoundsFloorStrength.Value);
            float stepTarget = b.StepMode == Mode.Floor ? Mathf.Lerp(StepFloorTopHz, StepFloorLowHz, t) : b.StepMode == Mode.Route ? routeHz : OffHz;
            float airTarget = b.AirMode == Mode.Floor ? Mathf.Lerp(AirFloorTopHz, AirFloorLowHz, t) : b.AirMode == Mode.Route ? routeHz : OffHz;
            float k = 1f - Mathf.Exp(-dt / SmoothS);
            b.StepCut = b.StepMode != b.LastStepMode && b.LastStepMode == Mode.None ? stepTarget : Mathf.Exp(Mathf.Lerp(Mathf.Log(b.StepCut), Mathf.Log(stepTarget), k));
            b.AirCut = b.AirMode != b.LastAirMode && b.LastAirMode == Mode.None ? airTarget : Mathf.Exp(Mathf.Lerp(Mathf.Log(b.AirCut), Mathf.Log(airTarget), k));
            b.LastStepMode = b.StepMode; b.LastAirMode = b.AirMode;
            if (b.StepMode == Mode.Floor || b.AirMode == Mode.Floor) FloorFrames++;
            if (b.StepMode == Mode.Route || b.AirMode == Mode.Route) RouteFrames++;
            if (b.StepMode != Mode.None && b.StepCut < LowestStepHz) LowestStepHz = b.StepCut;
            if (b.AirMode != Mode.None && b.AirCut < LowestAirHz) LowestAirHz = b.AirCut;

            // where a routed sound comes from: towards the opening, at the route's length (as 6.1), smoothed
            Vector3 toS = s - l; float straight = toS.magnitude;
            if (straight < 0.01f) return;
            Vector3 dirS = toS / straight, dirT = dirS; float distT = straight;
            if (route)
            {
                Vector3 toNear = b.Near - l; float nearM = toNear.magnitude;
                float blend = b.Blend * Mathf.Clamp01(nearM / NearFadeM) * Mathf.Clamp01(Plugin.PortalStrength.Value);
                if (blend > 0f && nearM > 0.01f) { dirT = Vector3.Slerp(dirS, toNear / nearM, blend); distT = Mathf.Lerp(straight, Mathf.Max(b.Path, straight), blend); }
            }
            if (!b.Init) { b.CurDir = dirT; b.CurDist = distT; b.Init = true; }
            else { b.CurDir = Vector3.Slerp(b.CurDir, dirT, k); b.CurDist = Mathf.Lerp(b.CurDist, distT, k); }
        }

        private static void Treat(BetterSource src, Body b, bool step, Vector3 l)
        {
            if (src == null || !TouchedNow.Add(src)) return;
            Mode mode = step ? b.StepMode : b.AirMode;
            float cut = step ? b.StepCut : b.AirCut;
            if (mode == Mode.None) { Neutral(src); return; }

            if (mode == Mode.Route && Plugin.PortalPositioningOn.Value)
            {
                Transform t = src.transform;
                if (!MovedFrom.ContainsKey(src)) { MovedFrom[src] = t.localPosition; SourcesMoved++; }
                t.position = l + b.CurDir.normalized * b.CurDist;
            }
            else Unmove(src);

            bool on = cut < OffHz * 0.95f;
            PortalPositioning.Filter(src.source1, cut, on);
            if (on) Filtered.Add(src); else Filtered.Remove(src);
        }

        private static void Unmove(BetterSource src)
        {
            if (!MovedFrom.TryGetValue(src, out Vector3 local)) return;
            if (src != null) src.transform.localPosition = local;
            MovedFrom.Remove(src);
        }

        private static void Neutral(BetterSource src)
        {
            if (src == null) return;
            Unmove(src);
            if (Filtered.Remove(src)) PortalPositioning.Filter(src.source1, OffHz, false);
        }

        private static void NeutralAll()
        {
            foreach (var kv in MovedFrom) if (kv.Key != null) kv.Key.transform.localPosition = kv.Value;
            MovedFrom.Clear();
            foreach (BetterSource s in Filtered) if (s != null) PortalPositioning.Filter(s.source1, OffHz, false);
            Filtered.Clear();
        }

        public static string Summary()
        {
            if (!Plugin.BodySoundsOn.Value && Handled == 0) return "BODY SOUNDS (F off)\n";
            return $"BODY SOUNDS (F {(Plugin.BodySoundsOn.Value ? "ON" : "off")}, range {Plugin.BodySoundsRangeM.Value:0} m, floor x{Plugin.BodySoundsFloorStrength.Value:0.00}): people handled {Handled}, hands sound players seen {HandsSeen}; " +
                   $"person-frames through a floor {FloorFrames}, from an opening {RouteFrames}; sources moved {SourcesMoved}; lowest footstep cutoff {LowestStepHz:0} Hz, lowest airborne {LowestAirHz:0} Hz\n";
        }
    }
}
