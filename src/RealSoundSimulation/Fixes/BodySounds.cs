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
    //   sounds   footsteps (NestedStepSoundSource, and EFT 1.0's extra step layer outdoors), gear rustle, searching and looting, voices, and
    //            everything their hands play (meds, food and drink, reloads, handling: BaseSoundPlayer's clip source). Other people only,
    //            within "Fa" m.
    //
    // 0.7.17 "Fc"/"Fd": both ways at once. User: "does it come through walls (like thin wood glass doors etc)", then "why not both the wall
    // and path?". Real sound reaches you both through what's in between and round through the openings; the louder way sets the direction.
    //   wall     anything on the straight line (a wall, a door, glass, a floor): its loss by material and thickness (ThroughWall's measure,
    //            125 Hz) x "Fb". The sound stays where they really are, so the spatializer keeps the direction and height, low-passed by
    //            that loss. Footsteps through a floor count 10 dB louder: a step is felt through the structure.
    //   route    round through the openings (PortalPositioning's routes): the extra length in dB + 4 dB per opening + 3 dB per 90 degrees
    //            of turn. The sound comes from the opening, at the route's length, dulled by 6.1b's rule.
    //   main     the game's own voice for the sound follows whichever loses less.
    //   second   the other way plays on a voice from the echo pool, at its own place, muffling and level, but only when it's within "Fd"
    //            dB of the main one (quieter isn't heard), within "Fe" m, at most MaxSecond at once, nearest first. It counts as a bot
    //            echo, so it never takes voices your gun needs.
    //   else     same room or the open: unchanged.
    // Our own low-pass on each source, on top of the game's occlusion. Moved sources are put back when they no longer need it.
    //
    // 0.7.19 "Ff"/"Fg": above and below. Raid_20261110_224746 (switch on): people on another floor were heard from an opening about 7 times
    // as often as through the floor, because 0.7.17 let footsteps take the louder way and a concrete floor's airborne loss (~40-50 dB)
    // always lost to the stairwell; the same person came "from the floor" one moment and from across the room the next. User: "still
    // struggling with footsteps heard through the floor and in general it being confusing if a player is above or below".
    //   footsteps on another floor always come through the floor at the person's place, no second way: a step is an impact the floor itself
    //            carries into the room below, not airborne sound, so the airborne loss doesn't apply. Above you: a ceiling thud, brighter on a
    //            light floor (1500 Hz) and deeper on concrete (350 Hz). Below you: duller (x0.6, down to 200 Hz). Airborne sounds from below
    //            through the floor are duller too (x0.7). Voices and gear still take the louder way, and both ways when close.
    //   steeper  "Fg": a sound coming through a floor is placed at least 45 degrees above or below you, keeping its compass direction and
    //            distance, so the spatializer has an angle it can render as up or down (headphones carry height poorly; someone upstairs 10 m
    //            to the side is only ~15 degrees up). An exaggeration, on its own switch.
    //   log      body.csv: each body sound from another floor or room, with the way it took and why.
    //
    // 0.7.20 "Fh": the way round keeps its height. User: "they can maybe come from the path but on the same level? below or above?". The
    // route way used to come from the opening nearest you (the foot of the stairs on your floor), so someone upstairs heard that way was
    // level with you. Now a route way from another floor comes from the stairwell's compass direction but at their height (at least
    // 45 degrees with the steeper switch on). Footsteps on another floor keep the thud through the floor and, when the stairs to them are
    // close (route at most StepStairMaxDb), also come that way as a second voice, StepStairBelowDb + half the route loss quieter.
    [DefaultExecutionOrder(32001)]
    internal class BodySounds : MonoBehaviour
    {
        private const float ScanEveryS = 0.1f, WallsEveryS = 0.5f, FloorDyM = 1.8f, FloorNormalY = 0.7f, HeadUpM = 1.0f, SmoothS = 0.15f,
            FullAtEffect = 0.3f, NearFadeM = 1f, OffHz = 22000f, LossLowDb = 5f, LossHighDb = 50f, StepThroughFloorDb = 10f,
            StepWallTopHz = 1500f, StepWallLowHz = 250f, AirWallTopHz = 3000f, AirWallLowHz = 150f,
            RouteDbPerOpening = 4f, RouteDbPer90 = 3f, SpeedOfSound = 343f, SecondMinCutHz = 100f;
        private const int MaxHits = 16, MaxSecond = 6;
        private const float ImpactTopHz = 1500f, ImpactLowHz = 350f, ImpactLossLowDb = 15f, ImpactLossHighDb = 50f, BelowStep = 0.6f, BelowStepMinHz = 200f,
            BelowAir = 0.7f, SteepDeg = 45f, StepStairMaxDb = 12f, StepStairBelowDb = 6f;
        public const string Header = "t,id,sound,distM,dyM,floor,main,second,wallDb,routeDb,cutHz,secondCutHz,secondDeltaDb,elevDeg,steepDeg";

        private static readonly AccessTools.FieldRef<Player, BetterSource> StepRef = AccessTools.FieldRefAccess<Player, BetterSource>("NestedStepSoundSource");
        private static readonly AccessTools.FieldRef<Player, BetterSource> GearRef = AccessTools.FieldRefAccess<Player, BetterSource>("_gearSource");
        private static readonly AccessTools.FieldRef<Player, BetterSource> SearchRef = AccessTools.FieldRefAccess<Player, BetterSource>("_searchSource");
        private static readonly AccessTools.FieldRef<Player, BetterSource> SpeechRef = AccessTools.FieldRefAccess<Player, BetterSource>("_speechSource");
        // EFT 1.0's extra footstep layer (surface detail, only while the person is outdoors) plays on a source of its own
        private static readonly AccessTools.FieldRef<Player, Audio.AudioWeatherSystem.IPlayerStepAudioController> LayerRef =
            AccessTools.FieldRefAccess<Player, Audio.AudioWeatherSystem.IPlayerStepAudioController>("_specificStepAudioController");
        private static readonly AccessTools.FieldRef<BaseSoundPlayer, BetterSource> ClipsRef = AccessTools.FieldRefAccess<BaseSoundPlayer, BetterSource>("_clipsSource");
        private static readonly AccessTools.FieldRef<BaseSoundPlayer, bool> ReleasedRef = AccessTools.FieldRefAccess<BaseSoundPlayer, bool>("_isClipSourceReleased");
        private static readonly AccessTools.FieldRef<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge> BridgeRef =
            AccessTools.FieldRefAccess<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge>("playersBridge");
        private static readonly AccessTools.FieldRef<SpatialAudioSystem, Dictionary<int, SoundOcclusionProcessor>> ProcessorsRef =
            AccessTools.FieldRefAccess<SpatialAudioSystem, Dictionary<int, SoundOcclusionProcessor>>("_registeredPlayerProcessors");

        private enum Way : byte { None, Wall, Route }

        // One sound's two ways for this person right now (footsteps and airborne sounds differ).
        private struct Paths
        {
            public Way Main, Second;
            public float MainCut, SecondCut, SecondDeltaDb, AppliedCut, WallDb, RouteDb;
            public Way LastMain;
        }

        private class Body
        {
            public Player P;
            public int Id;
            public bool HasRoute, Blocked, Floor, Init;
            public Vector3 Near, CurDir, Head;
            public float Path, Straight, CurDist, LossDb, NextScan, NextWalls, Blend, Dy;
            public int Portals, HeldRoute = -1, Walls;
            public uint HeldPair;
            public Paths Step, Air;
            public readonly List<BaseSoundPlayer> Hands = new List<BaseSoundPlayer>();
            public readonly Dictionary<BaseSoundPlayer, BetterSource> HandSrc = new Dictionary<BaseSoundPlayer, BetterSource>(); // the source each was last seen playing on
            public BetterSource Layer; // the 1.0 step layer's source last seen
            public Vector3 RoutePos => CurDir.normalized * CurDist;   // relative to the listener
        }

        private struct Owner { public Body B; public bool Step; }

        private static readonly Dictionary<int, Body> Bodies = new Dictionary<int, Body>();
        private static readonly Dictionary<BetterSource, Vector3> MovedFrom = new Dictionary<BetterSource, Vector3>(); // original local position
        private static readonly HashSet<BetterSource> Filtered = new HashSet<BetterSource>();
        private static readonly HashSet<BetterSource> TouchedNow = new HashSet<BetterSource>();
        private static readonly Dictionary<BetterSource, Owner> Owners = new Dictionary<BetterSource, Owner>(); // whose sound a source plays, from the last frame
        private static readonly Dictionary<AudioSource, AnimationCurve> Rolloffs = new Dictionary<AudioSource, AnimationCurve>();
        private static readonly double[] SecondEnds = new double[MaxSecond];
        private static readonly RaycastHit[] Hits = new RaycastHit[MaxHits];
        private static readonly List<int> Gone = new List<int>();
        private static BetterSource _dupSrc; private static int _dupFrame = -1;
        private static Vector3 _listener;

        public static int Handled, HandsSeen, SecondPlayed, SecondTooQuiet, SecondFull, SecondFar, SecondNoVoice;
        public static long FloorFrames, RouteFrames, WallFrames, SourcesMoved;
        public static float LowestStepHz = OffHz, LowestAirHz = OffHz;

        public static void Apply(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(BaseSoundPlayer), nameof(BaseSoundPlayer.PlayClipInternal)),
                postfix: new HarmonyMethod(typeof(BodySounds), nameof(HandsPostfix)));
            // every body sound starts here (the source types call it first); the second way is copied from it
            h.Patch(AccessTools.Method(typeof(BetterSource), nameof(BetterSource.Play)),
                postfix: new HarmonyMethod(typeof(BodySounds), nameof(PlayPostfix)));
        }

        public static void Reset()
        {
            NeutralAll();
            Bodies.Clear(); MovedFrom.Clear(); Filtered.Clear(); Owners.Clear(); Rolloffs.Clear();
            for (int i = 0; i < MaxSecond; i++) SecondEnds[i] = 0;
            Handled = HandsSeen = SecondPlayed = SecondTooQuiet = SecondFull = SecondFar = SecondNoVoice = 0;
            FloorFrames = RouteFrames = WallFrames = SourcesMoved = 0; LowestStepHz = LowestAirHz = OffHz;
        }

        // Someone's hands played a sound (meds, food, a reload): remember the sound player so its source is treated while it plays.
        private static void HandsPostfix(BaseSoundPlayer __instance, AudioClip clip, float volume)
        {
            if (!Plugin.BodySoundsOn.Value) return;
            try
            {
                BaseSoundPlayer.IObserverToPlayerBridge b = BridgeRef(__instance);
                if (b == null || b.PointOfView == EPointOfView.FirstPerson || !(b.iPlayer is Player p)) return;
                Body body = Get(p);
                if (body == null) return;
                if (!body.Hands.Contains(__instance)) { body.Hands.Add(__instance); HandsSeen++; }
                // the very first sound of a newly seen hands source isn't in Owners yet: copy its second way from here
                BetterSource src = ReleasedRef(__instance) ? null : ClipsRef(__instance);
                if (src != null && !(_dupSrc == src && _dupFrame == Time.frameCount)) { Log(body, false, body.Air); if (Plugin.BodySecondOn.Value) Second(body, false, src, clip, volume); }
            }
            catch (Exception e) { DiagRunner.ReportOnce("F body sounds (hands)", e); }
        }

        private static void PlayPostfix(BetterSource __instance, AudioClip clip1, AudioClip clip2, float balance, float volume, bool oneShot)
        {
            if (!Plugin.BodySoundsOn.Value || Owners.Count == 0) return;
            try
            {
                if (!Owners.TryGetValue(__instance, out Owner o) || o.B == null) return;
                Log(o.B, o.Step, o.Step ? o.B.Step : o.B.Air);
                if (!Plugin.BodySecondOn.Value) return;
                if (!oneShot && clip1 != null) return; // a looping sound: the main voice carries it
                AudioClip clip = clip1 != null && (clip2 == null || balance >= 0.5f) ? clip1 : clip2;
                float part = clip == clip1 ? (clip2 != null ? balance : 1f) : (clip1 != null ? 1f - balance : 1f);
                Second(o.B, o.Step, __instance, clip, volume * part);
                _dupSrc = __instance; _dupFrame = Time.frameCount;
            }
            catch (Exception e) { DiagRunner.ReportOnce("F body sounds (second way)", e); }
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
                Owners.Clear();
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
                _listener = l;
                float now = Time.time, dt = Mathf.Max(Time.unscaledDeltaTime, 0f), range = Plugin.BodySoundsRangeM.Value;
                Owners.Clear();

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

        // Where this person is to you: what's on the straight line (and whether it's a floor), and the route through openings.
        private static void Scan(SpatialAudioSystem sas, Vector3 l, Body b, float now)
        {
            b.NextScan = now + ScanEveryS;
            Vector3 s = b.P.Position + Vector3.up * HeadUpM, d = s - l;
            float len = d.magnitude;
            b.Head = s; b.Straight = Mathf.Max(len, 0.5f); b.Dy = d.y;
            int mask = sas.OcclusionSettings != null ? sas.OcclusionSettings.commonSettings.occlusionLayerMask.value : 1;
            bool wasBlocked = b.Blocked, floorHit = false;
            b.Blocked = false;
            if (len > 0.01f)
            {
                CountedRay.Rays++;
                int n = Physics.RaycastNonAlloc(l, d / len, Hits, len, mask, QueryTriggerInteraction.Ignore);
                b.Blocked = n > 0;
                for (int i = 0; i < n; i++) if (Mathf.Abs(Hits[i].normal.y) >= FloorNormalY) { floorHit = true; break; }
            }
            b.Floor = floorHit && Mathf.Abs(d.y) > FloorDyM;
            if (!b.Blocked) b.LossDb = 0f;
            else if (now >= b.NextWalls || !wasBlocked)
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

        private static void Decide(Vector3 l, Body b, float dt)
        {
            float k = 1f - Mathf.Exp(-dt / SmoothS);
            Vector3 toS = b.Head - l;
            bool wallOk = b.Blocked;
            bool routeOk = b.Blocked && b.HasRoute && b.Blend > 0f;
            float wallDb = b.LossDb * Plugin.BodySoundsFloorStrength.Value;
            float routeDb = 0f, routeHz = OffHz;
            if (routeOk)
            {
                Vector3 toNear = b.Near - l;
                float turn = Vector3.Angle(toS, toNear) * Mathf.Clamp01(toNear.magnitude / NearFadeM);
                routeDb = 20f * Mathf.Log10(Mathf.Max(b.Path, b.Straight) / b.Straight) + RouteDbPerOpening * Mathf.Max(b.Portals, 1) + RouteDbPer90 * turn / 90f;
                routeHz = PortalPositioning.DullHzFor(toS, toNear, true, b.Portals, Plugin.RouteDullStrength.Value);
            }
            if (b.Floor) StepThroughFloor(ref b.Step, wallDb, routeOk, routeDb, routeHz, b.Dy < 0f, k);
            else Choose(ref b.Step, wallOk, routeOk, wallDb, routeDb, routeHz, true, k, 1f);
            Choose(ref b.Air, wallOk, routeOk, wallDb, routeDb, routeHz, false, k, b.Floor && b.Dy < 0f ? BelowAir : 1f);
            if (b.Floor && (b.Step.Main == Way.Wall || b.Air.Main == Way.Wall)) FloorFrames++;
            else if (b.Step.Main == Way.Wall || b.Air.Main == Way.Wall) WallFrames++;
            if (b.Step.Main == Way.Route || b.Air.Main == Way.Route) RouteFrames++;
            if (b.Step.Main != Way.None && b.Step.AppliedCut < LowestStepHz) LowestStepHz = b.Step.AppliedCut;
            if (b.Air.Main != Way.None && b.Air.AppliedCut < LowestAirHz) LowestAirHz = b.Air.AppliedCut;

            // where the route way comes from: towards the opening, at the route's length (as 6.1), smoothed
            float straight = toS.magnitude;
            if (straight < 0.01f) return;
            Vector3 dirS = toS / straight, dirT = dirS; float distT = straight;
            if (routeOk)
            {
                Vector3 toNear = b.Near - l; float nearM = toNear.magnitude;
                float blend = b.Blend * Mathf.Clamp01(nearM / NearFadeM) * Mathf.Clamp01(Plugin.PortalStrength.Value);
                if (blend > 0f && nearM > 0.01f) { dirT = Vector3.Slerp(dirS, toNear / nearM, blend); distT = Mathf.Lerp(straight, Mathf.Max(b.Path, straight), blend); }
            }
            if (!b.Init) { b.CurDir = dirT; b.CurDist = distT; b.Init = true; }
            else { b.CurDir = Vector3.Slerp(b.CurDir, dirT, k); b.CurDist = Mathf.Lerp(b.CurDist, distT, k); }
        }

        // The main voice follows the way that loses less; the other is the second way if it's close enough to be heard.
        // 0.7.19: a footstep on another floor: always through the floor, an impact thud, duller from below.
        private static void StepThroughFloor(ref Paths p, float wallDb, bool routeOk, float routeDb, float routeHz, bool below, float k)
        {
            float t = Mathf.InverseLerp(ImpactLossLowDb, ImpactLossHighDb, Mathf.Max(wallDb, 0f));
            float hz = Mathf.Lerp(ImpactTopHz, ImpactLowHz, t);
            if (below) hz = Mathf.Max(hz * BelowStep, BelowStepMinHz);
            p.Main = Way.Wall; p.MainCut = hz; p.Second = Way.None; p.SecondDeltaDb = 0f; p.WallDb = wallDb; p.RouteDb = routeOk ? routeDb : float.NaN;
            // 0.7.20: close stairs to them carry the steps too, quieter, from the stairwell at their height
            if (routeOk && routeDb <= StepStairMaxDb) { p.Second = Way.Route; p.SecondCut = routeHz; p.SecondDeltaDb = StepStairBelowDb + 0.5f * routeDb; }
            if (p.Second != Way.None && p.SecondDeltaDb > Plugin.BodySecondWithinDb.Value) p.Second = Way.None;
            Smooth(ref p, k);
        }

        private static void Choose(ref Paths p, bool wallOk, bool routeOk, float wallDb, float routeDb, float routeHz, bool step, float k, float wallScale)
        {
            p.WallDb = wallOk ? wallDb : float.NaN; p.RouteDb = routeOk ? routeDb : float.NaN;
            float t = Mathf.InverseLerp(LossLowDb, LossHighDb, Mathf.Max(wallDb, 0f));
            float wallHz = (step ? Mathf.Lerp(StepWallTopHz, StepWallLowHz, t) : Mathf.Lerp(AirWallTopHz, AirWallLowHz, t)) * wallScale;
            p.Second = Way.None; p.SecondDeltaDb = 0f;
            if (wallOk && (!routeOk || wallDb <= routeDb))
            {
                p.Main = Way.Wall; p.MainCut = wallHz;
                if (routeOk) { p.Second = Way.Route; p.SecondCut = routeHz; p.SecondDeltaDb = routeDb - wallDb; }
            }
            else if (routeOk)
            {
                p.Main = Way.Route; p.MainCut = routeHz;
                if (wallOk) { p.Second = Way.Wall; p.SecondCut = wallHz; p.SecondDeltaDb = wallDb - routeDb; }
            }
            else { p.Main = Way.None; p.MainCut = OffHz; }
            if (p.Second != Way.None && p.SecondDeltaDb > Plugin.BodySecondWithinDb.Value) p.Second = Way.None;
            Smooth(ref p, k);
        }

        private static void Smooth(ref Paths p, float k)
        {
            float target = p.Main == Way.None ? OffHz : p.MainCut;
            p.AppliedCut = p.Main != p.LastMain || p.AppliedCut <= 0f ? target : Mathf.Exp(Mathf.Lerp(Mathf.Log(p.AppliedCut), Mathf.Log(target), k)); // a new way starts as it is
            p.LastMain = p.Main;
        }

        private static void Treat(BetterSource src, Body b, bool step, Vector3 l)
        {
            if (src == null || !TouchedNow.Add(src)) return;
            Owners[src] = new Owner { B = b, Step = step };
            Paths p = step ? b.Step : b.Air;
            if (p.Main == Way.None) { Neutral(src); return; }

            if (p.Main == Way.Route && Plugin.PortalPositioningOn.Value) MoveTo(src, RoutePosFor(l, b));
            else if (p.Main == Way.Wall && b.Floor && Plugin.BodySteepOn.Value) MoveTo(src, Steep(l, step ? b.P.Position : b.Head));
            else Unmove(src);

            bool on = p.AppliedCut < OffHz * 0.95f;
            PortalPositioning.Filter(src.source1, p.AppliedCut, on);
            if (on) Filtered.Add(src); else Filtered.Remove(src);
        }

        private static void MoveTo(BetterSource src, Vector3 pos)
        {
            Transform t = src.transform;
            if (!MovedFrom.ContainsKey(src)) { MovedFrom[src] = t.localPosition; SourcesMoved++; }
            t.position = pos;
        }

        // "Fg": at least SteepDeg up or down from you, same compass direction and distance.
        private static Vector3 Steep(Vector3 l, Vector3 pos)
        {
            Vector3 v = pos - l;
            float dist = v.magnitude;
            Vector3 h = new Vector3(v.x, 0f, v.z);
            float hm = h.magnitude;
            if (dist < 0.01f || Mathf.Abs(v.y) < 0.01f) return pos;
            if (Mathf.Atan2(Mathf.Abs(v.y), hm) * Mathf.Rad2Deg >= SteepDeg) return pos;
            float r = SteepDeg * Mathf.Deg2Rad;
            Vector3 flat = hm > 0.01f ? h / hm : Vector3.forward;
            Vector3 dir = flat * Mathf.Cos(r) + Vector3.up * (Mathf.Sign(v.y) * Mathf.Sin(r));
            return l + dir * dist;
        }

        // 0.7.20 "Fh": the route way's place. Same floor: towards the opening at the route's length (as 6.1). Another floor: the same compass
        // direction and distance, raised or lowered to their height (at least SteepDeg with the steeper switch on).
        private static Vector3 RoutePosFor(Vector3 l, Body b)
        {
            Vector3 rel = b.RoutePos;
            if (!b.Floor) return l + rel;
            float dist = rel.magnitude;
            Vector3 h = new Vector3(rel.x, 0f, rel.z);
            float hm = h.magnitude;
            if (dist < 0.01f) return l + rel;
            Vector3 toThem = b.Head - l;
            float elev = Mathf.Atan2(Mathf.Abs(b.Dy), new Vector2(toThem.x, toThem.z).magnitude) * Mathf.Rad2Deg;
            if (Plugin.BodySteepOn.Value) elev = Mathf.Max(elev, SteepDeg);
            float r = elev * Mathf.Deg2Rad;
            Vector3 flat = hm > 0.01f ? h / hm : Vector3.forward;
            return l + (flat * Mathf.Cos(r) + Vector3.up * (Mathf.Sign(b.Dy) * Mathf.Sin(r))) * dist;
        }

        private static float Elev(Vector3 l, Vector3 pos)
        {
            Vector3 v = pos - l;
            return Mathf.Atan2(v.y, new Vector2(v.x, v.z).magnitude) * Mathf.Rad2Deg;
        }

        // body.csv: one row per body sound from another floor or room (not the same room).
        private static void Log(Body b, bool step, Paths p)
        {
            if (!DiagLog.IsOpen || p.Main == Way.None) return;
            Vector3 truePos = step ? b.P.Position : b.Head;
            float elev = Elev(_listener, truePos);
            float steep = p.Main == Way.Wall && b.Floor && Plugin.BodySteepOn.Value ? Elev(_listener, Steep(_listener, truePos))
                : p.Main == Way.Route ? Elev(_listener, RoutePosFor(_listener, b)) : elev;
            DiagLog.Row("body.csv", string.Join(",", DiagLog.F(Time.time - DiagRunner.RaidStartTime, "0.00"), b.Id.ToString(), step ? "step" : "air",
                DiagLog.F(b.Straight, "0.0"), DiagLog.F(b.Dy, "0.0"), b.Floor ? "1" : "0", p.Main.ToString(), p.Second.ToString(),
                float.IsNaN(p.WallDb) ? "" : DiagLog.F(p.WallDb, "0.0"), float.IsNaN(p.RouteDb) ? "" : DiagLog.F(p.RouteDb, "0.0"),
                DiagLog.F(p.AppliedCut, "0"), p.Second == Way.None ? "" : DiagLog.F(p.SecondCut, "0"), p.Second == Way.None ? "" : DiagLog.F(p.SecondDeltaDb, "0.0"),
                DiagLog.F(elev, "0"), DiagLog.F(steep, "0")));
        }

        // ---------------- the second way ----------------

        private static void Second(Body b, bool step, BetterSource src, AudioClip clip, float volume)
        {
            if (clip == null || !Plugin.BodySecondOn.Value) return;
            Paths p = step ? b.Step : b.Air;
            if (p.Main == Way.None) return;
            if (p.Second == Way.None) { if (p.Main != Way.None) SecondTooQuiet++; return; }
            if (b.Straight > Plugin.BodySecondRangeM.Value) { SecondFar++; return; }
            double now = AudioSettings.dspTime;
            int slot = -1;
            for (int i = 0; i < MaxSecond; i++) if (SecondEnds[i] <= now) { slot = i; break; }
            if (slot < 0) { SecondFull++; return; }

            AudioSource a = src.source1;
            float dist = b.Straight;
            float level = Mathf.Clamp01(volume * src.BaseVolume * src.OcclusionVolumeFactor * Rolloff(a, dist)) * Mathf.Pow(10f, -p.SecondDeltaDb / 20f);
            if (level < 0.001f) { SecondTooQuiet++; return; }
            float pitch = a != null && a.pitch > 0f ? a.pitch : 1f;
            Vector3 pos; float delay;
            if (p.Second == Way.Route) { pos = RoutePosFor(_listener, b); delay = Mathf.Max(0f, (Mathf.Max(b.Path, b.Straight) - b.Straight) / SpeedOfSound); }
            else { pos = b.Head; delay = 0f; }
            double len = clip.length / pitch;
            EchoVoices.Play(src, clip, pos, now + delay, len, false, level, pitch, p.SecondCut, delay, 0f, 0.0, SecondMinCutHz, false, 0f, EchoClass.Bot, 1);
            if (EchoVoices.LastDropped) { SecondNoVoice++; return; }
            SecondEnds[slot] = now + delay + len;
            SecondPlayed++;
        }

        // How loud the game's own voice is at this distance, by its roll-off curve (read once per source).
        private static float Rolloff(AudioSource a, float dist)
        {
            if (a == null) return 1f;
            float max = Mathf.Max(a.maxDistance, 0.01f);
            if (a.rolloffMode != AudioRolloffMode.Custom) return Mathf.Clamp01(a.minDistance / Mathf.Max(dist, a.minDistance));
            if (!Rolloffs.TryGetValue(a, out AnimationCurve c)) Rolloffs[a] = c = a.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
            return c != null ? Mathf.Clamp01(c.Evaluate(Mathf.Clamp01(dist / max))) : 1f;
        }

        // ---------------- putting sources back ----------------

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
            return $"BODY SOUNDS (F {(Plugin.BodySoundsOn.Value ? "ON" : "off")}, range {Plugin.BodySoundsRangeM.Value:0} m, walls and floors x{Plugin.BodySoundsFloorStrength.Value:0.00}; second way {(Plugin.BodySecondOn.Value ? $"ON within {Plugin.BodySecondWithinDb.Value:0} dB, {Plugin.BodySecondRangeM.Value:0} m" : "off")}): " +
                   $"people handled {Handled}, hands sound players seen {HandsSeen}; person-frames through a floor {FloorFrames}, through a wall {WallFrames}, from an opening {RouteFrames}; sources moved {SourcesMoved}; " +
                   $"lowest footstep cutoff {LowestStepHz:0} Hz, lowest airborne {LowestAirHz:0} Hz; second way played {SecondPlayed}, too quiet or none {SecondTooQuiet}, too far {SecondFar}, {MaxSecond} already playing {SecondFull}, no echo voice free {SecondNoVoice}\n";
        }
    }
}
