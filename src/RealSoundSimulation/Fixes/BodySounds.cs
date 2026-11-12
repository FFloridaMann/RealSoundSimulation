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
    //
    // 0.7.22 "Fi"/"Fj"/"Fk": telling above from below. Raid_20261111_004027: footsteps through a floor averaged a 320 Hz cutoff from below
    // and 510 Hz from above, and a headset tells up from down only from the highs (pinna cues, roughly 4-10 kHz), so the steeper placement
    // had nothing left to work with. User: "its still hard to ID hieght is this person above me or below me".
    //   thud     "Fi": a step on the floor above shakes your ceiling: each one adds a short low thud (a synthesized 80->42 Hz knock). Steps
    //            below go into the ground and add none. A thud means above, a soft scuff means below.
    //   height   "Fj", off by default: a faint copy of each step through a floor, high-passed at 2 kHz so it keeps only the part the ears
    //            place in height, at least 60 degrees up or down, on our own HRTF-spatialized voices. Not physical: a hearing aid for height.
    //   test     "Fk": a key that plays three of your own footsteps on the floor above, then three below, the way this mod makes them.
    //   contrast "Fl": footsteps through a ceiling brighter (x2 per contrast step, heel strike and floor), from below darker (x0.5 per step,
    //            a dull rumble), so the two sound different at once. User: "so we cant change the hz to make it more obvious?"
    //   voices   contrast and the height layer also for voices, gear, searching and meds/food from another floor (both ways: through the
    //            floor and round by the stairs). User: "add all of the above for vices gear and meds/food thats probably why it sounds so
    //            disorienting". The thud stays footsteps only: a voice doesn't shake the ceiling.
    //
    // 0.7.22 rework after raid_20261111_171930 (User: "It seems like it makes no difference its really hard to tell hieght"; "Most of the
    // time in this game they will only ever be about 1-2 floors up or down"). People on another floor were ~25 m away and only ~20 degrees
    // down: the diagonal ray's wall loss (78-89 dB) pinned every step from below at the 150 Hz floor, the height layer was derived after
    // occlusion and barely played, thuds were rare (25 m reach) and pitched below many headsets. A design review (game audio: Siege, Hunt)
    // said height on headphones comes from above and below *sounding* different, plus an audible high band, not from placement. So:
    //   floors   "Fm": through-floor muffling by floors crossed (|dy| / 2.7 m), not by the ray's dB: steps above 1200 / 800 Hz (1 / 2+
    //            floors), below 450 / 320; voices and gear through the floor above 1800, below 700. Replaces the 0.7.22 contrast.
    //   tone     "Fo": the through-floor filter gets a resonance: Q 3 above (a knock at the cutoff), Q 2 below (a hollow boom).
    //   height   "Fj": level from the sound before the floor (volume x base x roll-off), -12 dB, -4 dB per extra floor; band 3-9 kHz; 75 degrees
    //            up or down on the true compass direction; 0.25 s, one per person per 0.2 s, at most 8 at once; on the gunshot group (not
    //            the occluded one). On by default.
    //   thuds    "Fi" knock from above: 110->70 Hz with a 500 Hz click, 35 m, level before the floor. "Fn" boom from below: 90->60 Hz, 15 ms
    //            attack, 0.3 s, 6 dB quieter. The attack is the cue. Both -6 dB per extra floor, at the true place.
    //   steeper  "Fg" now off by default; off = true direction, and the way round by the stairs comes from the opening itself.
    //   test     "Fk" now plays as in a raid: one floor up then down, 20 m to your right; then the same plain (no cues) to compare.
    //
    // 0.7.22 "one person, one place", after raid_20261111_190806 (Customs dorms; user: "footsteps ARE SO BAD. I cant tell if they are coming
    // from below me to the side down the haul"). body.csv: a person's footsteps and their gear or voice within 1.5 s came by different
    // ways 375 times in 654 (steps through the floor at their place, gear and voice from the stairwell), 236 same-floor sounds (~30%) were
    // moved to a doorway, and one person's sounds switched way 81 times. Every sound of a person now stays where they are (as the game
    // plays it); the ways only decide how it's muffled. The second way is off by default. Your own player's sources (step, gear, search,
    // speech, the 1.0 step layer) are never treated, in case one is shared or pooled (user: own footsteps sounded muffled, no headset).
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
        private const float ThudMaxM = 35f, KnockCutHz = 1500f, BoomCutHz = 250f, BoomBelowDb = -6f, PerFloorDb = -6f,
            HeightDeg = 75f, HeightDistM = 2.5f, HeightHpHz = 3000f, HeightLpHz = 9000f, HeightLenS = 0.25f, HeightFadeS = 0.08f, HeightEveryS = 0.2f, HeightPerFloorDb = -4f,
            TestUpM = 3f, TestAwayM = 20f, TestStepS = 0.5f, TestGapS = 1.0f, TestPartGapS = 2.0f, TestDry = 0.25f, TestOcclusion = 0.3f,
            FloorHeightM = 2.7f, ToneQAbove = 2f, ToneQBelow = 1.5f, AirMinHz = 100f; // tone: about +6 / +3.5 dB at the cutoff (was Q 3 / 2)
        private const int MaxThud = 6, HeightVoices = 20, HeightLive = 8;
        // "Fm": through-floor cutoffs, 1 floor / 2+ floors
        private const float StepAbove1Hz = 1200f, StepAbove2Hz = 800f, StepBelow1Hz = 450f, StepBelow2Hz = 320f, AirAboveHz = 1800f, AirBelowHz = 700f;
        public const string Header = "t,id,sound,distM,dyM,floor,main,second,wallDb,routeDb,cutHz,secondCutHz,secondDeltaDb,elevDeg,steepDeg,floors";

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
            public int Portals, HeldRoute = -1, Walls, Floors = 1;
            public double NextHeight;
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

        // 0.7.22: the thud, the height layer's own HRTF voices, and the clips the test plays
        private class HVoice { public AudioSource Src; public AudioLowPassFilter Lp; public AudioHighPassFilter Hp; public double Start, Until; public float Vol; public bool Short; }
        private static readonly HVoice[] HPool = new HVoice[HeightVoices];
        private static readonly double[] ThudEnds = new double[MaxThud];
        private static AudioClip _knock, _boom, _myStepClip, _anyStepClip;
        public static int BoomPlayed;
        private static AnimationCurve _flat;
        private static BetterSource _myStep;
        private static readonly HashSet<BetterSource> Mine = new HashSet<BetterSource>(); // your own player's sources this frame
        public static int MineSkipped;
        public static int ThudPlayed, ThudFull, ThudNoVoice, HeightPlayed, HeightNoVoice, TestsRun;

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
            for (int i = 0; i < MaxThud; i++) ThudEnds[i] = 0;
            ThudPlayed = ThudFull = ThudNoVoice = HeightPlayed = HeightNoVoice = TestsRun = BoomPlayed = MineSkipped = 0;
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
                if (src != null && !(_dupSrc == src && _dupFrame == Time.frameCount))
                {
                    Log(body, false, body.Air);
                    FloorCues(body, false, src, clip, volume);
                    if (Plugin.BodySecondOn.Value) Second(body, false, src, clip, volume);
                }
            }
            catch (Exception e) { DiagRunner.ReportOnce("F body sounds (hands)", e); }
        }

        private static void PlayPostfix(BetterSource __instance, AudioClip clip1, AudioClip clip2, float balance, float volume, bool oneShot)
        {
            if (!Plugin.BodySoundsOn.Value) return;
            try
            {
                if (_myStep != null && ReferenceEquals(__instance, _myStep) && (clip1 != null || clip2 != null)) _myStepClip = clip1 ?? clip2; // for the height test
                if (Owners.Count == 0) return;
                if (!Owners.TryGetValue(__instance, out Owner o) || o.B == null) return;
                Log(o.B, o.Step, o.Step ? o.B.Step : o.B.Air);
                _dupSrc = __instance; _dupFrame = Time.frameCount; // the hands hook below skips what this one handled
                AudioClip clip = clip1 != null && (clip2 == null || balance >= 0.5f) ? clip1 : clip2;
                float part = clip == clip1 ? (clip2 != null ? balance : 1f) : (clip1 != null ? 1f - balance : 1f);
                if (clip != null && (oneShot || clip1 == null)) { if (o.Step) _anyStepClip = clip; FloorCues(o.B, o.Step, __instance, clip, volume * part); }
                if (!Plugin.BodySecondOn.Value) return;
                if (!oneShot && clip1 != null) return; // a looping sound: the main voice carries it
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
                _myStep = me != null ? StepRef(me) : null;
                // 0.7.22: your own sources, so a shared or pooled one is never muffled for someone else (user: own footsteps sounded muffled)
                Mine.Clear();
                if (me != null)
                {
                    if (_myStep != null) Mine.Add(_myStep);
                    BetterSource ms = GearRef(me); if (ms != null) Mine.Add(ms);
                    ms = SearchRef(me); if (ms != null) Mine.Add(ms);
                    ms = SpeechRef(me); if (ms != null) Mine.Add(ms);
                    ms = (LayerRef(me) as Audio.AmbientSubsystem.PlayerStepLayerAudioController)?._source; if (ms != null) Mine.Add(ms);
                    foreach (BetterSource s in Mine) if (Filtered.Contains(s) || MovedFrom.ContainsKey(s)) { Neutral(s); MineSkipped++; }
                }
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
            b.Floors = Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(d.y) / FloorHeightM));
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
            bool below = b.Dy < 0f;
            if (b.Floor) StepThroughFloor(ref b.Step, Plugin.BodyFloorMuffleOn.Value ? FloorHz(true, below, b.Floors) : ImpactHz(wallDb, below), wallDb, routeOk, routeDb, routeHz, k);
            else Choose(ref b.Step, wallOk, routeOk, wallDb, routeDb, routeHz, true, k, 1f, 0f);
            float airHz = b.Floor && Plugin.BodyFloorMuffleOn.Value ? FloorHz(false, below, b.Floors) : 0f;
            Choose(ref b.Air, wallOk, routeOk, wallDb, routeDb, routeHz, false, k, b.Floor && below ? BelowAir : 1f, airHz);
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
        private static void StepThroughFloor(ref Paths p, float hz, float wallDb, bool routeOk, float routeDb, float routeHz, float k)
        {
            p.Main = Way.Wall; p.MainCut = hz; p.Second = Way.None; p.SecondDeltaDb = 0f; p.WallDb = wallDb; p.RouteDb = routeOk ? routeDb : float.NaN;
            // 0.7.20: close stairs to them carry the steps too, quieter, from the stairwell at their height
            if (routeOk && routeDb <= StepStairMaxDb) { p.Second = Way.Route; p.SecondCut = routeHz; p.SecondDeltaDb = StepStairBelowDb + 0.5f * routeDb; }
            if (p.Second != Way.None && p.SecondDeltaDb > Plugin.BodySecondWithinDb.Value) p.Second = Way.None;
            Smooth(ref p, k);
        }

        // "Fm": the through-floor cutoff by floors crossed and above/below, times the brightness setting.
        private static float FloorHz(bool step, bool below, int floors)
        {
            bool two = floors >= 2;
            float hz = step ? (below ? (two ? StepBelow2Hz : StepBelow1Hz) : (two ? StepAbove2Hz : StepAbove1Hz)) : (below ? AirBelowHz : AirAboveHz);
            return Mathf.Clamp(hz * Mathf.Max(Plugin.BodyFloorBright.Value, 0.05f), AirMinHz, OffHz);
        }

        // 0.7.19: the impact cutoff by the straight line's dB (used with Fm off)
        private static float ImpactHz(float wallDb, bool below)
        {
            float t = Mathf.InverseLerp(ImpactLossLowDb, ImpactLossHighDb, Mathf.Max(wallDb, 0f));
            float hz = Mathf.Lerp(ImpactTopHz, ImpactLowHz, t);
            return below ? Mathf.Max(hz * BelowStep, BelowStepMinHz) : hz;
        }

        // wallHzSet > 0: the wall way's cutoff is given (Fm through a floor)
        private static void Choose(ref Paths p, bool wallOk, bool routeOk, float wallDb, float routeDb, float routeHz, bool step, float k, float wallScale, float wallHzSet)
        {
            p.WallDb = wallOk ? wallDb : float.NaN; p.RouteDb = routeOk ? routeDb : float.NaN;
            float t = Mathf.InverseLerp(LossLowDb, LossHighDb, Mathf.Max(wallDb, 0f));
            float wallHz = wallHzSet > 0f ? wallHzSet
                : Mathf.Clamp((step ? Mathf.Lerp(StepWallTopHz, StepWallLowHz, t) : Mathf.Lerp(AirWallTopHz, AirWallLowHz, t)) * wallScale, AirMinHz, OffHz);
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
            if (Mine.Contains(src)) { Neutral(src); MineSkipped++; return; } // a source your own player plays on: never ours to treat
            Owners[src] = new Owner { B = b, Step = step };
            Paths p = step ? b.Step : b.Air;
            if (p.Main == Way.None) { Neutral(src); return; }

            // 0.7.22 "one person, one place": every sound of a person stays where they are; only how it's muffled changes
            Unmove(src);

            bool on = p.AppliedCut < OffHz * 0.95f;
            // "Fo": the floor tone, only on the way through a floor
            float q = p.Main == Way.Wall && b.Floor && Plugin.BodyFloorToneOn.Value ? (b.Dy > 0f ? ToneQAbove : ToneQBelow) : 1f;
            PortalPositioning.Filter(src.source1, p.AppliedCut, on, q);
            if (on) Filtered.Add(src); else Filtered.Remove(src);
        }

        private static void MoveTo(BetterSource src, Vector3 pos)
        {
            Transform t = src.transform;
            if (!MovedFrom.ContainsKey(src)) { MovedFrom[src] = t.localPosition; SourcesMoved++; }
            t.position = pos;
        }

        // "Fg": at least SteepDeg up or down from you, same compass direction and distance.
        private static Vector3 Steep(Vector3 l, Vector3 pos) => Steep(l, pos, SteepDeg);

        private static Vector3 Steep(Vector3 l, Vector3 pos, float deg)
        {
            Vector3 v = pos - l;
            float dist = v.magnitude;
            Vector3 h = new Vector3(v.x, 0f, v.z);
            float hm = h.magnitude;
            if (dist < 0.01f || Mathf.Abs(v.y) < 0.01f) return pos;
            if (Mathf.Atan2(Mathf.Abs(v.y), hm) * Mathf.Rad2Deg >= deg) return pos;
            float r = deg * Mathf.Deg2Rad;
            Vector3 flat = hm > 0.01f ? h / hm : Vector3.forward;
            Vector3 dir = flat * Mathf.Cos(r) + Vector3.up * (Mathf.Sign(v.y) * Mathf.Sin(r));
            return l + dir * dist;
        }

        // 0.7.20 "Fh": the route way's place. Same floor: towards the opening at the route's length (as 6.1). Another floor: the same compass
        // direction and distance, raised or lowered to their height (at least SteepDeg with the steeper switch on).
        private static Vector3 RoutePosFor(Vector3 l, Body b)
        {
            Vector3 rel = b.RoutePos;
            if (!b.Floor || !Plugin.BodySteepOn.Value) return l + rel; // the opening itself (0.7.22: true direction unless steeper is on)
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
            float steep = elev; // 0.7.22: played where the person is
            DiagLog.Row("body.csv", string.Join(",", DiagLog.F(Time.time - DiagRunner.RaidStartTime, "0.00"), b.Id.ToString(), step ? "step" : "air",
                DiagLog.F(b.Straight, "0.0"), DiagLog.F(b.Dy, "0.0"), b.Floor ? "1" : "0", p.Main.ToString(), p.Second.ToString(),
                float.IsNaN(p.WallDb) ? "" : DiagLog.F(p.WallDb, "0.0"), float.IsNaN(p.RouteDb) ? "" : DiagLog.F(p.RouteDb, "0.0"),
                DiagLog.F(p.AppliedCut, "0"), p.Second == Way.None ? "" : DiagLog.F(p.SecondCut, "0"), p.Second == Way.None ? "" : DiagLog.F(p.SecondDeltaDb, "0.0"),
                DiagLog.F(elev, "0"), DiagLog.F(steep, "0"), b.Floor ? b.Floors.ToString() : "0"));
        }

        // ---------------- above or below: the thuds, the height layer, the test (0.7.22) ----------------

        // A sound from another floor just started on the game's source: add the knock (footsteps from above) or the boom (from below), and
        // the height layer (footsteps through the floor; voices, gear and hands whichever way they come). Both are set against the sound
        // before the floor (volume x base x roll-off): they are cues for where the person is, not what the floor lets through.
        private static void FloorCues(Body b, bool step, BetterSource src, AudioClip clip, float volume)
        {
            if (!b.Floor) return;
            Paths p = step ? b.Step : b.Air;
            if (p.Main == Way.None || (step && p.Main != Way.Wall)) return;
            bool below = b.Dy < 0f;
            bool thud = step && b.Straight <= ThudMaxM && (below ? Plugin.BodyBoomOn.Value : Plugin.BodyThudOn.Value);
            bool height = Plugin.BodyHeightOn.Value;
            if (!thud && !height) return;
            AudioSource a = src.source1;
            float dry = Mathf.Clamp01(volume * src.BaseVolume * Rolloff(a, b.Straight));
            if (dry < 0.0005f) return;
            double now = AudioSettings.dspTime;
            Vector3 at = step ? b.P.Position : b.Head;
            int extra = Mathf.Max(b.Floors - 1, 0);
            if (thud) Thud(src, below, at, dry * Db(PerFloorDb * extra), now);
            if (height && now >= b.NextHeight
                && Height(clip, HeightPos(_listener, at), dry * Db(Plugin.BodyHeightDb.Value + HeightPerFloorDb * extra), a != null && a.pitch > 0f ? a.pitch : 1f, now, true))
                b.NextHeight = now + HeightEveryS;
        }

        // "Fi" knock from above (a sharp start with a 500 Hz click) and "Fn" boom from below (a slow start, deeper, longer, quieter).
        private static bool Thud(object owner, bool below, Vector3 pos, float level, double start)
        {
            if (_knock == null)
            {
                float ph = 0f;
                _knock = ShockWave.MakeClip("rss_body_knock", 0.22f, t =>
                {
                    ph += 2f * Mathf.PI * (70f + 40f * Mathf.Exp(-t / 0.03f)) / 48000f;
                    return Mathf.Clamp01(t / 0.002f) * (Mathf.Exp(-t / 0.06f) * Mathf.Sin(ph) + 0.6f * Mathf.Exp(-t / 0.008f) * Mathf.Sin(2f * Mathf.PI * 500f * t));
                });
                float pb = 0f;
                _boom = ShockWave.MakeClip("rss_body_boom", 0.3f, t =>
                {
                    pb += 2f * Mathf.PI * (60f + 30f * Mathf.Exp(-t / 0.05f)) / 48000f;
                    return Mathf.Clamp01(t / 0.015f) * Mathf.Exp(-t / 0.1f) * Mathf.Sin(pb);
                });
            }
            AudioClip c = below ? _boom : _knock;
            double now = AudioSettings.dspTime;
            int slot = -1;
            for (int i = 0; i < MaxThud; i++) if (ThudEnds[i] <= now) { slot = i; break; }
            if (slot < 0) { ThudFull++; return false; }
            float v = Mathf.Clamp01(level * Db(Plugin.BodyThudDb.Value + (below ? BoomBelowDb : 0f)));
            EchoVoices.Play(owner, c, pos, start, c.length, false, v, 1f, below ? BoomCutHz : KnockCutHz, 0f, minCutoff: 20f, cls: EchoClass.Bot, tier: 1);
            if (EchoVoices.LastDropped) { ThudNoVoice++; return false; }
            ThudEnds[slot] = start + c.length;
            if (below) BoomPlayed++; else ThudPlayed++;
            return true;
        }

        // Where the height layer plays: HeightDeg up or down on the true compass direction, close by (its level is set, not rolled off).
        private static Vector3 HeightPos(Vector3 l, Vector3 pos)
        {
            Vector3 d = Steep(l, pos, HeightDeg) - l;
            return d.sqrMagnitude > 1e-4f ? l + d.normalized * HeightDistM : pos;
        }

        // On our own HRTF voices (the echo voices are panned without height).
        //   cue = the height layer: 3-9 kHz, 0.25 s with a fade, on the gunshot group (not the occluded one), at most HeightLive at once.
        //   else = a whole sound muffled to lpHz with resonance q (the F8 test's footsteps), on the given group.
        private static bool Height(AudioClip clip, Vector3 pos, float vol, float pitch, double start, bool cue, float lpHz = OffHz, float q = 1f,
                                   UnityEngine.Audio.AudioMixerGroup group = null)
        {
            if (clip == null || vol < 0.0003f) return false;
            double now = AudioSettings.dspTime;
            if (cue)
            {
                int live = 0;
                foreach (HVoice h in HPool) if (h != null && h.Short && h.Until > now) live++;
                if (live >= HeightLive) { HeightNoVoice++; return false; }
                BetterAudio ba = Singleton<BetterAudio>.Instantiated ? Singleton<BetterAudio>.Instance : null;
                group = ba != null ? ba.GunshotMixerGroup : null;
            }
            HVoice v = FreeVoice(now);
            if (v == null) { if (cue) HeightNoVoice++; return false; }
            float len = clip.length / pitch;
            v.Src.Stop();
            v.Src.transform.position = pos;
            v.Src.outputAudioMixerGroup = group;
            v.Src.clip = clip;
            v.Src.pitch = pitch;
            v.Vol = Mathf.Clamp01(vol);
            v.Src.volume = v.Vol;
            v.Hp.enabled = cue; v.Hp.cutoffFrequency = HeightHpHz;
            float lp = cue ? HeightLpHz : lpHz;
            v.Lp.enabled = lp < OffHz * 0.95f; v.Lp.cutoffFrequency = lp; v.Lp.lowpassResonanceQ = cue ? 1f : q;
            v.Short = cue;
            v.Start = start;
            v.Src.PlayScheduled(start);
            if (cue && len > HeightLenS) { v.Src.SetScheduledEndTime(start + HeightLenS); len = HeightLenS; }
            v.Until = start + len + 0.05;
            if (cue) HeightPlayed++;
            return true;
        }

        private static HVoice FreeVoice(double now)
        {
            if (HPool[0] == null)
            {
                _flat = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 1f));
                for (int i = 0; i < HeightVoices; i++)
                {
                    var go = new GameObject("RealSoundSimulation height " + i);
                    DontDestroyOnLoad(go);
                    var s = go.AddComponent<AudioSource>();
                    s.playOnAwake = false; s.spatialBlend = 1f; s.spatialize = true; s.dopplerLevel = 0f; s.spread = 0f;
                    s.reverbZoneMix = 0f; s.bypassReverbZones = true; s.priority = 200;
                    s.rolloffMode = AudioRolloffMode.Custom; s.maxDistance = 100000f;
                    s.SetCustomCurve(AudioSourceCurveType.CustomRolloff, _flat);
                    var lp = go.AddComponent<AudioLowPassFilter>(); lp.lowpassResonanceQ = 1f; lp.enabled = false;
                    var hp = go.AddComponent<AudioHighPassFilter>(); hp.highpassResonanceQ = 1f; hp.enabled = false;
                    HPool[i] = new HVoice { Src = s, Lp = lp, Hp = hp };
                }
            }
            foreach (HVoice v in HPool) if (v.Until <= now) return v;
            return null;
        }

        private static float Db(float db) => Mathf.Pow(10f, db / 20f);

        private void Update()
        {
            if (Plugin.BodyHeightTestKey.Value.IsDown()) Plugin.Safe("height test", HeightTest);
            if (HPool[0] == null) return;
            // the height layer's 0.25 s ends in a short fade, not a click
            double now = AudioSettings.dspTime;
            foreach (HVoice v in HPool)
            {
                if (!v.Short || v.Until <= now || now < v.Start) continue;
                float left = (float)(v.Start + HeightLenS - now);
                v.Src.volume = v.Vol * Mathf.Clamp01(left / HeightFadeS);
            }
        }

        // "Fk": as in a raid: one floor up, then one floor down, 20 m to your right, with the cues by their switches; then the same plain
        // (the 0.7.19 muffling for a typical diagonal through concrete, no tone, no thuds, no height layer) to compare.
        private static void HeightTest()
        {
            if (!MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated || !Singleton<GameWorld>.Instantiated || Singleton<GameWorld>.Instance is HideoutGameWorld) return;
            Transform lt = MonoBehaviourSingleton<SpatialAudioSystem>.Instance.ListenerTransform;
            if (lt == null) return;
            AudioClip clip = _myStepClip ?? _anyStepClip;
            if (clip == null) { Plugin.Log.LogInfo("Fk height test: take a step first (it plays your own footstep)"); return; }
            Vector3 l = lt.position;
            Vector3 fwd = Vector3.ProjectOnPlane(lt.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
            AudioSource own = _myStep != null ? _myStep.source1 : null;
            UnityEngine.Audio.AudioMixerGroup group = own != null ? own.outputAudioMixerGroup : null;
            float dist = Mathf.Sqrt(TestAwayM * TestAwayM + TestUpM * TestUpM);
            // a fixed level for "a footstep ~20 m away": your own step source is set up for close range and rolls off to ~0 by 20 m
            float dry = TestDry, heard = dry * TestOcclusion;
            double t0 = AudioSettings.dspTime + 0.1, part = 6 * TestStepS + TestGapS + TestPartGapS;
            int played = 0;
            for (int c = 0; c < 2; c++)
            {
                bool cued = c == 0;
                for (int f = 0; f < 2; f++)
                {
                    bool below = f == 1;
                    Vector3 truePos = l + right * TestAwayM + Vector3.up * (below ? -TestUpM : TestUpM);
                    Vector3 at = Plugin.BodySteepOn.Value ? Steep(l, truePos) : truePos;
                    float hz = cued && Plugin.BodyFloorMuffleOn.Value ? FloorHz(true, below, 1) : ImpactHz(80f, below);
                    float q = cued && Plugin.BodyFloorToneOn.Value ? (below ? ToneQBelow : ToneQAbove) : 1f;
                    for (int i = 0; i < 3; i++)
                    {
                        double start = t0 + c * part + f * (3 * TestStepS + TestGapS) + i * TestStepS;
                        if (Height(clip, at, heard, 1f, start, false, hz, q, group)) played++;
                        if (!cued) continue;
                        if (below ? Plugin.BodyBoomOn.Value : Plugin.BodyThudOn.Value) Thud(clip, below, truePos, dry, start);
                        if (Plugin.BodyHeightOn.Value) Height(clip, HeightPos(l, truePos), dry * Db(Plugin.BodyHeightDb.Value), 1f, start, true);
                    }
                }
            }
            TestsRun++;
            Plugin.Log.LogInfo($"Fk height test: {played} steps: above, below, then the same plain (floors {(Plugin.BodyFloorMuffleOn.Value ? "on" : "off")}, tone {(Plugin.BodyFloorToneOn.Value ? "on" : "off")}, knock {(Plugin.BodyThudOn.Value ? "on" : "off")}, boom {(Plugin.BodyBoomOn.Value ? "on" : "off")}, height layer {(Plugin.BodyHeightOn.Value ? "on" : "off")}, steeper {(Plugin.BodySteepOn.Value ? "on" : "off")})");
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
                   $"lowest footstep cutoff {LowestStepHz:0} Hz, lowest airborne {LowestAirHz:0} Hz; second way played {SecondPlayed}, too quiet or none {SecondTooQuiet}, too far {SecondFar}, {MaxSecond} already playing {SecondFull}, no echo voice free {SecondNoVoice}; " +
                   $"muffling by floors (Fm {(Plugin.BodyFloorMuffleOn.Value ? $"ON x{Plugin.BodyFloorBright.Value:0.00}" : "off")}), floor tone (Fo {(Plugin.BodyFloorToneOn.Value ? "ON" : "off")}), steeper (Fg {(Plugin.BodySteepOn.Value ? "ON" : "off")}); " +
                   $"knock from above (Fi {(Plugin.BodyThudOn.Value ? "ON" : "off")}) played {ThudPlayed}, boom from below (Fn {(Plugin.BodyBoomOn.Value ? "ON" : "off")}) played {BoomPlayed}, level {Plugin.BodyThudDb.Value:+0;-0;0} dB, {MaxThud} already playing {ThudFull}, no echo voice free {ThudNoVoice}; " +
                   $"height layer (Fj {(Plugin.BodyHeightOn.Value ? $"ON {Plugin.BodyHeightDb.Value:+0;-0;0} dB" : "off")}) played {HeightPlayed}, {HeightLive} already playing or no voice {HeightNoVoice}; height tests (Fk) {TestsRun}; your own sources left alone {MineSkipped}\n";
        }
    }
}
