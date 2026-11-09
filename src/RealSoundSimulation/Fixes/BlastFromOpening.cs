using System;
using System.Collections.Generic;
using System.Linq;
using Audio.SpatialSystem;
using RealSoundSimulation.Acoustics;
using RealSoundSimulation.Diagnostics;
using EFT;
using EFT.DataProviding;
using EFT.Interactive;
using HarmonyLib;
using UnityEngine;

namespace RealSoundSimulation.Fixes
{
    // 6.4 (0.6.41): an explosion in another room comes from the opening it reaches you through, dulled by the way it got there. The low end still comes
    // through the walls from the true spot: that's 6.2's thump, with its own level "6.2h". User, 9 Nov 2026, Ground Zero: "i sort of wish grenades sounded
    // more from the entrance and (felt (thud from teh source)". Raid 20261109_083938, 949 s: a grenade 15 m away through two walls played from its true spot.
    //
    //   when     the game has just set up the blast's one-shot occlusion, so its own room lookup for the blast is done
    //   route    of the game's baked routes between that room and yours, the shortest; a shut door counts as 15 m. Guns read the game's finished route
    //            (6.1), but for a one-shot that answer only arrives after the sound has started.
    //   where    listener + direction(opening nearest you) x route length, as 6.1, by "6.4a". Fades out within 1 m of the opening. Stays there while it rings.
    //   occlusion the game's maths follows the voice's transform, so the container gets an offset back to the true spot (the game's own SetPositionOffset).
    //            The game doesn't clear that offset when a container is reused, so a postfix on Clear does.
    //   tone     the same rule as 6.1b ("6.4b", floor from 6.1d), on our own filter, back to neutral when the sound ends. 0.6.46: blasts have their own strength
    //            "6.4c" (0.5). Raid 20261109_100114: the gun rule took four blasts 10 m away to 1.5 kHz; user: not "loud enough or concussive enough especially indoors"
    // Same room, both outdoors or no baked route: left at the true spot. No raid numbers behind the 15 m or the levels yet: blast.csv records each blast.
    //
    // 0.6.42: the grenade's bounces too. User, 9 Nov 2026: "grenades bouncing on the ground noise still coming from true direction". Each grenade has its own
    // voice that the game keeps on the grenade every frame (Update), so it's re-placed here in LateUpdate, like 6.1, with the route re-read every 0.1 s while
    // the grenade moves or the voice plays. The game's occlusion for it follows the grenade's own transform, not the voice, so it needs no offset.
    //
    // 0.6.49 "6.7": a window breaking in another room. User, 9 Nov 2026: a grenade in the hallway broke glass "a few walls and turns" away and "it sounded a bit loud".
    // The game plays glass at the pane, and only if the window isn't part of your room does it run the one-shot occlusion, route loss only. That pass is the hook
    // here, so a window of your own room is never touched. Same route, placement and dulling rule as a blast (strength "6.7a"), plus "6.7b" dB per opening, 16 at most.
    [DefaultExecutionOrder(32000)]
    internal class BlastFromOpening : MonoBehaviour
    {
        private const float ClosedDoorM = 15f, NearFadeM = 1f, ExtraHoldS = 0.25f, BounceScanS = 0.1f, BounceSmoothS = 0.15f, BounceHoldS = 0.5f, MovingSqr = 0.01f;

        private static readonly AccessTools.FieldRef<SpatialAudioSystem, Dictionary<int, AudioSourceContainer>> ContainersRef =
            AccessTools.FieldRefAccess<SpatialAudioSystem, Dictionary<int, AudioSourceContainer>>("_allRegisteredSoundContainers");
        private static readonly AccessTools.FieldRef<SpatialAudioSystem, Dictionary<int, AudioSourceContainer>> GoContainersRef =
            AccessTools.FieldRefAccess<SpatialAudioSystem, Dictionary<int, AudioSourceContainer>>("_registeredGameObjectContainers");
        private static readonly AccessTools.FieldRef<SuperSourceDistant, AudioSource> Source2Ref =
            AccessTools.FieldRefAccess<SuperSourceDistant, AudioSource>("_source2");
        private static readonly AccessTools.FieldRef<Grenade, BetterSource> CollisionSourceRef =
            AccessTools.FieldRefAccess<Grenade, BetterSource>("_collisionSource");

        private class Voice { public AudioLowPassFilter Lp; public double End; }
        private class Bouncer
        {
            public Grenade G; public BetterSource S; public Rigidbody Rb;
            public bool HasRoute, Init, Counted;
            public Vector3 Near, CurDir;
            public float Path, CurDist, Cut, NextScan;
        }

        private static readonly Dictionary<GameObject, Voice> Filters = new Dictionary<GameObject, Voice>(); // by object: a filter acts on every source on it
        private static readonly Dictionary<string, int> Skips = new Dictionary<string, int>();
        private static readonly List<Bouncer> Bouncers = new List<Bouncer>();
        private static bool _pending, _anyOn;
        private static Vector3 _blast;

        public static int Moved, Dulled, BounceGrenades;
        public static long BounceFrames;
        private static double _movedSum, _cutLogSum, _bounceMovedSum;
        private static bool Wanted => Plugin.BlastOpeningOn.Value || Plugin.BlastDullOn.Value;

        private const float RoomBaseDb = 3f, RoomBounceDb = 2f, RoomMaxDb = -3f, RoomDullBy = 0.8f; // +6 for an explosion, -3 for what gets out of the room
        private static readonly float[] RoomExtraM = { 6f, 12f, 20f, 32f, 46f, 62f };
        private static SoundBank _bank;
        public static int RoomPlayed, RoomDropped;
        private static double _roomLevelSum;

        public const string Header = "t,moved,reason,distM,pathM,openings,turnDeg,movedDeg,lineBlocked,cutoffHz,closure";

        public static void Apply(Harmony h)
        {
            var pre = new HarmonyMethod(typeof(BlastFromOpening), nameof(PlayPrefix));
            var fin = new HarmonyMethod(typeof(BlastFromOpening), nameof(PlayFinalizer));
            h.Patch(typeof(BetterAudio).GetMethods().First(m => m.Name == "PlayAtPoint" && m.GetParameters().Length > 2 && m.GetParameters()[1].ParameterType == typeof(SoundBank) && m.GetParameters()[2].ParameterType == typeof(int)), prefix: pre, finalizer: fin);
            h.Patch(AccessTools.Method(typeof(BetterAudio), nameof(BetterAudio.PlayAtPointDistant)), prefix: pre, finalizer: fin);
            h.Patch(AccessTools.Method(typeof(SpatialAudioSystem), nameof(SpatialAudioSystem.RunOneShotPropagationOcclusionTest)), postfix: new HarmonyMethod(typeof(BlastFromOpening), nameof(OcclusionPostfix)));
            h.Patch(AccessTools.Method(typeof(AudioSourceContainer), nameof(AudioSourceContainer.Clear)), postfix: new HarmonyMethod(typeof(BlastFromOpening), nameof(ClearPostfix)));
            h.Patch(AccessTools.Method(typeof(Grenade), nameof(Grenade.InitAudioSource)), postfix: new HarmonyMethod(typeof(BlastFromOpening), nameof(GrenadeInitPostfix)));
            h.Patch(AccessTools.Method(typeof(Grenade), nameof(Grenade.ReleaseAudioSource)), prefix: new HarmonyMethod(typeof(BlastFromOpening), nameof(GrenadeReleasePrefix)));
            var glassPre = new HarmonyMethod(typeof(BlastFromOpening), nameof(GlassPrefix));
            var glassFin = new HarmonyMethod(typeof(BlastFromOpening), nameof(GlassFinalizer));
            h.Patch(AccessTools.Method(typeof(WindowBreaker), nameof(WindowBreaker.PlayHitSound)), prefix: glassPre, finalizer: glassFin);
            h.Patch(AccessTools.Method(typeof(WindowBreaker), nameof(WindowBreaker.PlayPieceSound)), prefix: glassPre, finalizer: glassFin);
        }

        public static void Reset()
        {
            foreach (var kv in Filters) if (kv.Value.Lp != null && kv.Value.Lp.enabled) kv.Value.Lp.enabled = false;
            Filters.Clear(); Skips.Clear(); Bouncers.Clear();
            _pending = _anyOn = false;
            _glass = false; GlassMoved = GlassLeft = GlassNoPass = 0; GlassSkips.Clear(); _glassCutLogSum = _glassLossSum = 0;
            Moved = Dulled = BounceGrenades = RoomPlayed = RoomDropped = 0; BounceFrames = 0; _movedSum = _cutLogSum = _bounceMovedSum = _roomLevelSum = 0;
        }

        private static void GrenadeInitPostfix(Grenade __instance)
        {
            if (!Wanted) return;
            try
            {
                BetterSource s = CollisionSourceRef(__instance);
                if (s == null) return;
                for (int i = 0; i < Bouncers.Count; i++)
                    if (Bouncers[i].G == __instance) { Bouncers[i].S = s; return; }
                Bouncers.Add(new Bouncer { G = __instance, S = s, Rb = __instance.GetComponent<Rigidbody>() });
            }
            catch (Exception e) { DiagRunner.ReportOnce("6.4 grenade bounce (init)", e); }
        }

        // The voice goes back to the game's pool: stop touching it now.
        private static void GrenadeReleasePrefix(BetterSource source)
        {
            if (source == null) return;
            try
            {
                for (int i = Bouncers.Count - 1; i >= 0; i--)
                {
                    if (Bouncers[i].S != source) continue;
                    FilterOff(source);
                    Bouncers.RemoveAt(i);
                }
            }
            catch (Exception e) { DiagRunner.ReportOnce("6.4 grenade bounce (release)", e); }
        }

        private static void PlayPrefix(Vector3 position, SoundBank bank)
        {
            _pending = bank != null && bank.SourceType == BetterAudio.AudioSourceGroupType.Grenades;
            _blast = position;
            _bank = bank;
        }

        private static Exception PlayFinalizer(Exception __exception)
        {
            _pending = false;
            return __exception;
        }

        private static void ClearPostfix(AudioSourceContainer __instance) => __instance._positionOffset = Vector3.zero;

        private static void OcclusionPostfix(SpatialAudioSystem __instance, BetterSource source, int __result)
        {
            if (_glass)
            {
                _glass = false;
                if (!Plugin.GlassRouteOn.Value) return;
                Perf.Begin();
                try { HandleGlass(__instance, source, __result); }
                catch (Exception e) { DiagRunner.ReportOnce("6.7 glass by route", e); }
                finally { Perf.End(Perf.Part.BlastFromOpening); }
                return;
            }
            if (!_pending) return;
            _pending = false;
            if (!Wanted && !Plugin.RoomEchoOn.Value) return;
            Perf.Begin();
            try { Handle(__instance, source, __result); }
            catch (Exception e) { DiagRunner.ReportOnce("6.4 blast from the opening", e); }
            finally { Perf.End(Perf.Part.BlastFromOpening); }
        }

        private static void Handle(SpatialAudioSystem sas, BetterSource source, int containerId)
        {
            Transform lt = sas.ListenerTransform;
            if (lt == null || source == null) return;
            Vector3 listener = lt.position, blast = _blast, toBlast = blast - listener;
            float straight = toBlast.magnitude;

            if (!ContainersRef(sas).TryGetValue(containerId, out AudioSourceContainer c) || c == null) { Skip("no container", straight); return; }
            if (!FindRoute(sas, c, blast, listener, out Vector3 near, out float path, out int openings, out float closure, out string why)) { Skip(why, straight); return; }

            Vector3 toNear = near - listener;
            float nearM = toNear.magnitude, movedDeg = 0f, cutoff = 0f;
            bool moved = false, blocked = false;

            if (Plugin.BlastOpeningOn.Value && nearM > 0.01f && straight > 0.01f)
            {
                float blend = Mathf.Clamp01(nearM / NearFadeM) * Mathf.Clamp01(Plugin.BlastOpeningStrength.Value);
                if (blend > 0f)
                {
                    Vector3 dir = Vector3.Slerp(toBlast / straight, toNear / nearM, blend).normalized;
                    Vector3 placed = listener + dir * Mathf.Lerp(straight, Mathf.Max(path, straight), blend);
                    source.Position = placed;
                    c.SetPositionOffset(blast - placed); // the game's occlusion stays at the true spot
                    movedDeg = Vector3.Angle(toBlast, dir);
                    moved = true;
                    if (!BlastRecording.InLayer) { Moved++; _movedSum += movedDeg; }
                }
            }

            bool dull = Plugin.BlastDullOn.Value, room = Plugin.RoomEchoOn.Value && !BlastRecording.InLayer;
            if ((dull || room) && straight > 0.01f)
            {
                int mask = sas.OcclusionSettings != null ? sas.OcclusionSettings.commonSettings.occlusionLayerMask.value : 1;
                blocked = CountedRay.Raycast(listener, toBlast / straight, out RaycastHit _, straight, mask, QueryTriggerInteraction.Ignore);
                float cut = PortalPositioning.DullHzFor(toBlast, toNear, blocked, openings, Plugin.BlastDullStrength.Value);
                if (dull)
                {
                    cutoff = cut;
                    FilterVoice(source, cutoff);
                    if (!BlastRecording.InLayer) { Dulled++; _cutLogSum += Mathf.Log(cutoff); }
                }
                if (room)
                {
                    Vector3 dir = nearM > 0.01f ? toNear / nearM : toBlast / straight;
                    PlayRoomEchoes(listener, lt.forward, listener + dir * Mathf.Max(nearM, 1f), path, cut);
                }
            }

            Row(moved ? "1" : "0", "", straight, path, openings, Vector3.Angle(toBlast, toNear), movedDeg, blocked ? "1" : "0", cutoff, closure);
        }

        // 0.6.48 "6.3e": a blast fills its room and that pours out of the door, which 6.3's single bounce can't do. Raid 20261109_102037: 8 of 11 blasts had no echo at all.
        private static void PlayRoomEchoes(Vector3 listener, Vector3 forward, Vector3 pos, float path, float cut)
        {
            AudioClip clip = _bank != null ? _bank.PickSingleClip(0) : null;
            if (clip == null) return;
            float trim = Plugin.RoomEchoDb.Value, length = Mathf.Min(clip.length, Plugin.ExplosionEchoLengthS.Value);
            int count = Mathf.Clamp(Plugin.RoomEchoCount.Value, 1, RoomExtraM.Length);
            double now = AudioSettings.dspTime;
            float shadow = OwnEchoes.HeadShadow(listener, forward, pos);
            for (int i = 0; i < count; i++)
            {
                float p = path + RoomExtraM[i], delay = p / OwnEchoes.SpeedOfSound;
                float lvl = Mathf.Min(RoomBaseDb - 20f * Mathf.Log10(p / 2f) - RoomBounceDb * i + trim, RoomMaxDb + Mathf.Max(0f, trim));
                float c = Mathf.Min(cut * RoomDullBy * Mathf.Pow(RoomDullBy, i), OwnEchoes.AirCutoff(p)) * shadow;
                bool stole = EchoVoices.Play(_bank, clip, pos, now + delay, length, false, Mathf.Pow(10f, lvl / 20f), 1f, c, delay, fadeS: length * 0.5f);
                if (EchoVoices.LastDropped) { RoomDropped++; continue; }
                RoomPlayed++; _roomLevelSum += lvl;
                if (DiagLog.IsOpen)
                    DiagLog.Row("echoes.csv", string.Join(",", DiagLog.F(Time.time - DiagRunner.RaidStartTime, "0.00"), "explosion", count.ToString(), i.ToString(), "Room", "", DiagLog.F(p, "0.0"), DiagLog.F(delay * 1000f, "0"), DiagLog.F(lvl, "0.0"), "0", DiagLog.F(c, "0"), "", "", stole ? "1" : "0"));
            }
        }

        // ---- 6.7: glass (0.6.49)
        private const float GlassMaxLossDb = 16f;
        public const string GlassHeader = "t,x,y,z,moved,reason,distM,pathM,openings,lineBlocked,cutoffHz,lossDb,clip";
        private static readonly AccessTools.FieldRef<BetterSource, float> PreVolumeRef = AccessTools.FieldRefAccess<BetterSource, float>("PreOcclusionVolume");
        private static readonly AccessTools.FieldRef<SuperSource, float> SecondPreRef = AccessTools.FieldRefAccess<SuperSource, float>("_secondPreEffectsVolume");
        private static readonly Dictionary<string, int> GlassSkips = new Dictionary<string, int>();
        private static bool _glass;
        private static Vector3 _glassAt;
        public static int GlassMoved, GlassLeft, GlassNoPass;
        private static double _glassCutLogSum, _glassLossSum;

        private static void GlassPrefix(ref Vector3 hitPoint)
        {
            _glass = true;
            _glassAt = hitPoint;
        }

        // Still set: the game ran no occlusion for this one, so it's a window of your own room or the sound didn't play (out of range, too many at once).
        private static Exception GlassFinalizer(Exception __exception)
        {
            if (_glass)
            {
                _glass = false;
                GlassNoPass++;
                GlassRow("0", "no occlusion pass", _glassAt, 0f, 0f, 0, "", 0f, 0f, "");
            }
            return __exception;
        }

        private static void HandleGlass(SpatialAudioSystem sas, BetterSource source, int containerId)
        {
            Transform lt = sas.ListenerTransform;
            if (lt == null || source == null) return;
            Vector3 listener = lt.position, at = _glassAt, toGlass = at - listener;
            float straight = toGlass.magnitude;
            string clip = source.source1 != null && source.source1.clip != null ? source.source1.clip.name : "";
            if (straight < 0.01f) return;
            if (!ContainersRef(sas).TryGetValue(containerId, out AudioSourceContainer c) || c == null) { GlassSkip("no container", at, straight, clip); return; }
            if (!FindRoute(sas, c, at, listener, out Vector3 near, out float path, out int openings, out float _, out string why)) { GlassSkip(why, at, straight, clip); return; }

            Vector3 toNear = near - listener;
            float nearM = toNear.magnitude;
            if (nearM > 0.01f)
            {
                float blend = Mathf.Clamp01(nearM / NearFadeM);
                Vector3 dir = Vector3.Slerp(toGlass / straight, toNear / nearM, blend).normalized;
                Vector3 placed = listener + dir * Mathf.Lerp(straight, Mathf.Max(path, straight), blend);
                source.Position = placed;
                c.SetPositionOffset(c._positionOffset + (at - placed)); // keeps the game's own 1 m shift into the room
            }

            int mask = sas.OcclusionSettings != null ? sas.OcclusionSettings.commonSettings.occlusionLayerMask.value : 1;
            bool blocked = CountedRay.Raycast(listener, toGlass / straight, out RaycastHit _, straight, mask, QueryTriggerInteraction.Ignore);
            float cut = PortalPositioning.DullHzFor(toGlass, toNear, blocked, openings, Plugin.GlassDullStrength.Value);
            FilterVoice(source, cut);

            float lossDb = Mathf.Min(Plugin.GlassLossDb2.Value * openings, GlassMaxLossDb);
            float gain = Mathf.Pow(10f, -lossDb / 20f);
            PreVolumeRef(source) *= gain;
            if (source.source1 != null) source.source1.volume *= gain;
            if (source is SuperSource s)
            {
                SecondPreRef(s) *= gain;
                if (s.source2 != null) s.source2.volume *= gain;
            }

            GlassMoved++; _glassCutLogSum += Mathf.Log(cut); _glassLossSum += lossDb;
            GlassRow("1", "", at, straight, path, openings, blocked ? "1" : "0", cut, lossDb, clip);
        }

        private static void GlassSkip(string why, Vector3 at, float dist, string clip)
        {
            GlassLeft++;
            GlassSkips.TryGetValue(why, out int n); GlassSkips[why] = n + 1;
            GlassRow("0", why, at, dist, 0f, 0, "", 0f, 0f, clip);
        }

        private static void GlassRow(string moved, string why, Vector3 at, float dist, float path, int openings, string blocked, float cut, float lossDb, string clip)
        {
            if (!DiagLog.IsOpen) return;
            DiagLog.Row("glass.csv", string.Join(",", DiagLog.F(Time.time - DiagRunner.RaidStartTime, "0.00"), DiagLog.F(at.x, "0.0"), DiagLog.F(at.y, "0.0"), DiagLog.F(at.z, "0.0"), moved, why,
                DiagLog.F(dist, "0.0"), DiagLog.F(path, "0.0"), openings.ToString(), blocked, DiagLog.F(cut, "0"), DiagLog.F(lossDb, "0.0"), clip));
        }

        public static string GlassSummary()
        {
            var parts = new List<string>();
            foreach (var kv in GlassSkips) parts.Add($"{kv.Key} {kv.Value}");
            return $"GLASS BY ROUTE (6.7 {(Plugin.GlassRouteOn.Value ? "ON" : "off")}): moved and dulled {GlassMoved}" +
                   (GlassMoved > 0 ? $", typical cutoff {Mathf.Exp((float)(_glassCutLogSum / GlassMoved)):0} Hz, mean loss {_glassLossSum / GlassMoved:0.0} dB" : "") +
                   $"; left at the window: {(parts.Count > 0 ? string.Join(", ", parts) : "-")}; no occlusion pass (your own room, or not played) {GlassNoPass}  (glass.csv)\n";
        }

        private static bool FindRoute(SpatialAudioSystem sas, AudioSourceContainer c, Vector3 source, Vector3 listener, out Vector3 near, out float path, out int openings, out float closure, out string why)
        {
            near = Vector3.zero; path = 0f; openings = 0; closure = 0f; why = "";
            ISpatialAudioRoom room = c.CurrentAudioRoom;
            if (room == null || !room.IsValid) { why = "no room"; return false; }
            if (sas.IsSourceAndListenerInCommonOutdoor(room)) { why = "both outdoors"; return false; }
            if (sas.IsSourceInListenerRoom(c)) { why = "same room"; return false; }
            if (!sas.TryGetMatchingRoomPair(room, out RoomPair pair, out bool reversed) || pair == null || pair.Routes == null || pair.Routes.Length == 0) { why = "no route"; return false; }
            if (!DataProvider.TryGetData<SpatialAudioDataContainer>(out SpatialAudioDataContainer data) || data == null) { why = "no route data"; return false; }
            if (!BestRoute(data, pair, reversed, source, listener, out near, out path, out openings, out closure)) { why = "no usable route"; return false; }
            return true;
        }

        private static void TickBounces(float dt)
        {
            if (Bouncers.Count == 0) return;
            if (!Wanted || !MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated) { Bouncers.Clear(); return; } // the game puts the voices back on the grenades in Update
            SpatialAudioSystem sas = MonoBehaviourSingleton<SpatialAudioSystem>.Instance;
            Transform lt = sas.ListenerTransform;
            if (lt == null) return;
            float now = Time.time;
            double end = AudioSettings.dspTime + BounceHoldS;
            for (int i = Bouncers.Count - 1; i >= 0; i--)
            {
                Bouncer b = Bouncers[i];
                if (b.G == null || b.S == null) { Bouncers.RemoveAt(i); continue; }
                bool playing = b.S.source1 != null && b.S.source1.isPlaying;
                if (!playing && (b.Rb == null || b.Rb.velocity.sqrMagnitude < MovingSqr)) { b.Init = false; continue; }
                PlaceBounce(sas, lt.position, b, now, dt, end);
            }
        }

        private static void PlaceBounce(SpatialAudioSystem sas, Vector3 listener, Bouncer b, float now, float dt, double end)
        {
            Vector3 grenade = b.G.transform.position, toGrenade = grenade - listener;
            float straight = toGrenade.magnitude;
            if (straight < 0.01f) return;
            if (now >= b.NextScan) ScanBounce(sas, listener, b, toGrenade, straight, now);
            if (!b.HasRoute) { b.Init = false; return; }

            if (Plugin.BlastDullOn.Value) SetVoice(b.S, b.Cut, end);
            if (!Plugin.BlastOpeningOn.Value) { b.Init = false; return; }

            Vector3 toNear = b.Near - listener;
            float nearM = toNear.magnitude;
            Vector3 dirGrenade = toGrenade / straight, dirTarget = dirGrenade;
            float distTarget = straight, blend = Mathf.Clamp01(nearM / NearFadeM) * Mathf.Clamp01(Plugin.BlastOpeningStrength.Value);
            if (blend > 0f && nearM > 0.01f)
            {
                dirTarget = Vector3.Slerp(dirGrenade, toNear / nearM, blend);
                distTarget = Mathf.Lerp(straight, Mathf.Max(b.Path, straight), blend);
            }
            if (!b.Init) { b.CurDir = dirTarget; b.CurDist = distTarget; b.Init = true; }
            else
            {
                float k = 1f - Mathf.Exp(-dt / BounceSmoothS);
                b.CurDir = Vector3.Slerp(b.CurDir, dirTarget, k);
                b.CurDist = Mathf.Lerp(b.CurDist, distTarget, k);
            }
            b.S.Position = listener + b.CurDir.normalized * b.CurDist;
            if (!b.Counted) { b.Counted = true; BounceGrenades++; }
            BounceFrames++; _bounceMovedSum += Vector3.Angle(b.CurDir, dirGrenade);
        }

        private static void ScanBounce(SpatialAudioSystem sas, Vector3 listener, Bouncer b, Vector3 toGrenade, float straight, float now)
        {
            b.NextScan = now + BounceScanS;
            b.HasRoute = false;
            if (!GoContainersRef(sas).TryGetValue(b.G.gameObject.GetInstanceID(), out AudioSourceContainer c) || c == null) return;
            if (!FindRoute(sas, c, b.G.transform.position, listener, out Vector3 near, out float path, out int openings, out float _, out string _)) return;
            b.Near = near; b.Path = path; b.HasRoute = true;
            if (!Plugin.BlastDullOn.Value) return;
            int mask = sas.OcclusionSettings != null ? sas.OcclusionSettings.commonSettings.occlusionLayerMask.value : 1;
            bool blocked = CountedRay.Raycast(listener, toGrenade / straight, out RaycastHit _, straight, mask, QueryTriggerInteraction.Ignore);
            b.Cut = PortalPositioning.DullHzFor(toGrenade, near - listener, blocked, openings, Plugin.RouteDullStrength.Value);
        }

        // PortalIDs run from the pair's first room to its second. Reversed = the listener is in the first room.
        private static bool BestRoute(SpatialAudioDataContainer data, RoomPair pair, bool reversed, Vector3 blast, Vector3 listener, out Vector3 near, out float bestPath, out int openings, out float closure)
        {
            float bestCost = float.MaxValue;
            near = Vector3.zero; bestPath = 0f; openings = 0; closure = 0f;
            Vector3 start = reversed ? listener : blast, end = reversed ? blast : listener;
            foreach (RoomPair.Route r in pair.Routes)
            {
                short[] ids = r.PortalIDs;
                if (ids == null || ids.Length == 0) continue;
                Vector3 at = start, first = Vector3.zero, last = Vector3.zero;
                float path = 0f, shut = 0f;
                bool ok = true;
                for (int i = 0; i < ids.Length; i++)
                {
                    if (!data.TryGetPortalById(ids[i], out BaseSpatialAudioPortal p) || p == null) { ok = false; break; }
                    Vector3 centre = p.Center();
                    if (i == 0) first = centre;
                    last = centre;
                    path += Vector3.Distance(at, centre);
                    at = centre;
                    shut += Mathf.Clamp01(ClosedDoors.Scaled(p, p.PortalClosureLevel));
                }
                if (!ok) continue;
                path += Vector3.Distance(at, end);
                float cost = path + shut * ClosedDoorM;
                if (cost >= bestCost) continue;
                bestCost = cost; bestPath = path; openings = ids.Length; closure = shut;
                near = reversed ? first : last;
            }
            return bestCost < float.MaxValue;
        }

        private static AudioSource SecondSource(BetterSource source) =>
            source is SuperSourceDistant d ? Source2Ref(d) : source is SuperSource s ? s.source2 : null;

        private static void FilterVoice(BetterSource source, float cutoff)
        {
            AudioSource a = source.source1;
            if (a == null) return;
            float delay = source is SuperSourceDistant d ? d.Delay : 0f;
            SetVoice(source, cutoff, AudioSettings.dspTime + Mathf.Max(a.clip != null ? a.clip.length : 0f, 1f) + delay + ExtraHoldS);
        }

        private static void SetVoice(BetterSource source, float cutoff, double end)
        {
            SetFilter(source.source1, cutoff, end);
            SetFilter(SecondSource(source), cutoff, end);
        }

        private static void FilterOff(BetterSource source)
        {
            Off(source.source1);
            Off(SecondSource(source));
        }

        private static void Off(AudioSource a)
        {
            if (a != null && Filters.TryGetValue(a.gameObject, out Voice v) && v.Lp != null && v.Lp.enabled) v.Lp.enabled = false;
        }

        private static void SetFilter(AudioSource a, float cutoff, double end)
        {
            if (a == null) return;
            GameObject go = a.gameObject;
            if (!Filters.TryGetValue(go, out Voice v))
            {
                v = new Voice { Lp = go.AddComponent<AudioLowPassFilter>() }; // our own, separate from the game's occlusion filters
                if (v.Lp != null) v.Lp.lowpassResonanceQ = 1f;
                Filters[go] = v;
            }
            if (v.Lp == null) return;
            v.Lp.cutoffFrequency = cutoff;
            if (!v.Lp.enabled) v.Lp.enabled = true;
            v.End = end;
            _anyOn = true;
        }

        // Back to neutral when the sound has ended: these voices return to the game's pool.
        private void LateUpdate()
        {
            if (Bouncers.Count > 0)
            {
                Perf.Begin();
                try { TickBounces(Mathf.Max(Time.unscaledDeltaTime, 0f)); }
                catch (Exception e) { DiagRunner.ReportOnce("6.4 grenade bounce", e); }
                finally { Perf.End(Perf.Part.BlastFromOpening); }
            }
            if (!_anyOn) return;
            double now = AudioSettings.dspTime;
            bool any = false;
            foreach (var kv in Filters)
            {
                AudioLowPassFilter lp = kv.Value.Lp;
                if (lp == null || !lp.enabled) continue;
                if (now >= kv.Value.End) lp.enabled = false;
                else any = true;
            }
            _anyOn = any;
        }

        private static void Skip(string why, float dist)
        {
            if (BlastRecording.InLayer) return;
            Skips.TryGetValue(why, out int n); Skips[why] = n + 1;
            Row("0", why, dist, 0f, 0, 0f, 0f, "", 0f, 0f);
        }

        private static void Row(string moved, string why, float dist, float path, int openings, float turn, float movedDeg, string blocked, float cutoff, float closure)
        {
            if (!DiagLog.IsOpen || BlastRecording.InLayer) return;
            DiagLog.Row("blast.csv", string.Join(",", DiagLog.F(Time.time - DiagRunner.RaidStartTime, "0.00"), moved, why, DiagLog.F(dist, "0.0"), DiagLog.F(path, "0.0"), openings.ToString(),
                DiagLog.F(turn, "0"), DiagLog.F(movedDeg, "0"), blocked, DiagLog.F(cutoff, "0"), DiagLog.F(closure, "0.00")));
        }

        public static string Summary()
        {
            var parts = new List<string>();
            foreach (var kv in Skips) parts.Add($"{kv.Key} {kv.Value}");
            string state = Plugin.BlastOpeningOn.Value ? $"ON, strength {Plugin.BlastOpeningStrength.Value:0.00}" : "off";
            return $"BLAST FROM THE OPENING (6.4 {state}, 6.4b dulling {(Plugin.BlastDullOn.Value ? $"ON x{Plugin.BlastDullStrength.Value:0.00}" : "off")}, 6.2h thump level {Plugin.ThumpExplosionDb.Value:+0;-0;0} dB): moved {Moved}" +
                   (Moved > 0 ? $", on average {_movedSum / Moved:0} deg from the true direction" : "") + $"; dulled {Dulled}" + (Dulled > 0 ? $", typical cutoff {Mathf.Exp((float)(_cutLogSum / Dulled)):0} Hz" : "") +
                   $"; left at the true spot: {(parts.Count > 0 ? string.Join(", ", parts) : "-")}; grenade bounces moved for {BounceGrenades} grenades, {BounceFrames} frames" +
                   (BounceFrames > 0 ? $", on average {_bounceMovedSum / BounceFrames:0} deg from the true direction" : "") +
                   $"; room echoes (6.3e {(Plugin.RoomEchoOn.Value ? "ON" : "off")}) played {RoomPlayed}" + (RoomPlayed > 0 ? $", mean level {_roomLevelSum / RoomPlayed:0.0} dB" : "") + $", dropped by the headroom cap {RoomDropped}  (blast.csv, echoes.csv kind Room)\n";
        }
    }
}
