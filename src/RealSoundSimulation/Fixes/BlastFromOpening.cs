using System;
using System.Collections.Generic;
using System.Linq;
using Audio.SpatialSystem;
using RealSoundSimulation.Acoustics;
using RealSoundSimulation.Diagnostics;
using EFT;
using EFT.DataProviding;
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
    //   tone     the same rule as 6.1b ("6.4b", strength and floor from 6.1c and 6.1d), on our own filter, back to neutral when the sound ends
    // Same room, both outdoors or no baked route: left at the true spot. No raid numbers behind the 15 m or the levels yet: blast.csv records each blast.
    internal class BlastFromOpening : MonoBehaviour
    {
        private const float ClosedDoorM = 15f, NearFadeM = 1f, ExtraHoldS = 0.25f;

        private static readonly AccessTools.FieldRef<SpatialAudioSystem, Dictionary<int, AudioSourceContainer>> ContainersRef =
            AccessTools.FieldRefAccess<SpatialAudioSystem, Dictionary<int, AudioSourceContainer>>("_allRegisteredSoundContainers");
        private static readonly AccessTools.FieldRef<SuperSourceDistant, AudioSource> Source2Ref =
            AccessTools.FieldRefAccess<SuperSourceDistant, AudioSource>("_source2");

        private class Voice { public AudioLowPassFilter Lp; public double End; }

        private static readonly Dictionary<GameObject, Voice> Filters = new Dictionary<GameObject, Voice>(); // by object: a filter acts on every source on it
        private static readonly Dictionary<string, int> Skips = new Dictionary<string, int>();
        private static bool _pending, _anyOn;
        private static Vector3 _blast;

        public static int Moved, Dulled;
        private static double _movedSum, _cutLogSum;

        public const string Header = "t,moved,reason,distM,pathM,openings,turnDeg,movedDeg,lineBlocked,cutoffHz,closure";

        public static void Apply(Harmony h)
        {
            var pre = new HarmonyMethod(typeof(BlastFromOpening), nameof(PlayPrefix));
            var fin = new HarmonyMethod(typeof(BlastFromOpening), nameof(PlayFinalizer));
            h.Patch(typeof(BetterAudio).GetMethods().First(m => m.Name == "PlayAtPoint" && m.GetParameters().Length > 2 && m.GetParameters()[1].ParameterType == typeof(SoundBank) && m.GetParameters()[2].ParameterType == typeof(int)), prefix: pre, finalizer: fin);
            h.Patch(AccessTools.Method(typeof(BetterAudio), nameof(BetterAudio.PlayAtPointDistant)), prefix: pre, finalizer: fin);
            h.Patch(AccessTools.Method(typeof(SpatialAudioSystem), nameof(SpatialAudioSystem.RunOneShotPropagationOcclusionTest)), postfix: new HarmonyMethod(typeof(BlastFromOpening), nameof(OcclusionPostfix)));
            h.Patch(AccessTools.Method(typeof(AudioSourceContainer), nameof(AudioSourceContainer.Clear)), postfix: new HarmonyMethod(typeof(BlastFromOpening), nameof(ClearPostfix)));
        }

        public static void Reset()
        {
            foreach (var kv in Filters) if (kv.Value.Lp != null && kv.Value.Lp.enabled) kv.Value.Lp.enabled = false;
            Filters.Clear(); Skips.Clear();
            _pending = _anyOn = false;
            Moved = Dulled = 0; _movedSum = _cutLogSum = 0;
        }

        private static void PlayPrefix(Vector3 position, SoundBank bank)
        {
            _pending = bank != null && bank.SourceType == BetterAudio.AudioSourceGroupType.Grenades;
            _blast = position;
        }

        private static Exception PlayFinalizer(Exception __exception)
        {
            _pending = false;
            return __exception;
        }

        private static void ClearPostfix(AudioSourceContainer __instance) => __instance._positionOffset = Vector3.zero;

        private static void OcclusionPostfix(SpatialAudioSystem __instance, BetterSource source, int __result)
        {
            if (!_pending) return;
            _pending = false;
            if (!Plugin.BlastOpeningOn.Value && !Plugin.BlastDullOn.Value) return;
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
            ISpatialAudioRoom room = c.CurrentAudioRoom;
            if (room == null || !room.IsValid) { Skip("no room", straight); return; }
            if (sas.IsSourceAndListenerInCommonOutdoor(room)) { Skip("both outdoors", straight); return; }
            if (sas.IsSourceInListenerRoom(c)) { Skip("same room", straight); return; }
            if (!sas.TryGetMatchingRoomPair(room, out RoomPair pair, out bool reversed) || pair == null || pair.Routes == null || pair.Routes.Length == 0) { Skip("no route", straight); return; }
            if (!DataProvider.TryGetData<SpatialAudioDataContainer>(out SpatialAudioDataContainer data) || data == null) { Skip("no route data", straight); return; }
            if (!BestRoute(data, pair, reversed, blast, listener, out Vector3 near, out float path, out int openings, out float closure)) { Skip("no usable route", straight); return; }

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
                    Moved++; _movedSum += movedDeg;
                }
            }

            if (Plugin.BlastDullOn.Value && straight > 0.01f)
            {
                int mask = sas.OcclusionSettings != null ? sas.OcclusionSettings.commonSettings.occlusionLayerMask.value : 1;
                blocked = CountedRay.Raycast(listener, toBlast / straight, out RaycastHit _, straight, mask, QueryTriggerInteraction.Ignore);
                cutoff = PortalPositioning.DullHzFor(toBlast, toNear, blocked, openings);
                FilterVoice(source, cutoff);
                Dulled++; _cutLogSum += Mathf.Log(cutoff);
            }

            Row(moved ? "1" : "0", "", straight, path, openings, Vector3.Angle(toBlast, toNear), movedDeg, blocked ? "1" : "0", cutoff, closure);
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
                    shut += Mathf.Clamp01(p.PortalClosureLevel);
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

        private static void FilterVoice(BetterSource source, float cutoff)
        {
            AudioSource a = source.source1, b = null;
            float delay = 0f;
            if (source is SuperSourceDistant d) { b = Source2Ref(d); delay = d.Delay; }
            else if (source is SuperSource s) b = s.source2;
            if (a == null) return;
            double end = AudioSettings.dspTime + Mathf.Max(a.clip != null ? a.clip.length : 0f, 1f) + delay + ExtraHoldS;
            SetFilter(a, cutoff, end);
            SetFilter(b, cutoff, end);
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
            Skips.TryGetValue(why, out int n); Skips[why] = n + 1;
            Row("0", why, dist, 0f, 0, 0f, 0f, "", 0f, 0f);
        }

        private static void Row(string moved, string why, float dist, float path, int openings, float turn, float movedDeg, string blocked, float cutoff, float closure)
        {
            if (!DiagLog.IsOpen) return;
            DiagLog.Row("blast.csv", string.Join(",", DiagLog.F(Time.time - DiagRunner.RaidStartTime, "0.00"), moved, why, DiagLog.F(dist, "0.0"), DiagLog.F(path, "0.0"), openings.ToString(),
                DiagLog.F(turn, "0"), DiagLog.F(movedDeg, "0"), blocked, DiagLog.F(cutoff, "0"), DiagLog.F(closure, "0.00")));
        }

        public static string Summary()
        {
            var parts = new List<string>();
            foreach (var kv in Skips) parts.Add($"{kv.Key} {kv.Value}");
            string state = Plugin.BlastOpeningOn.Value ? $"ON, strength {Plugin.BlastOpeningStrength.Value:0.00}" : "off";
            return $"BLAST FROM THE OPENING (6.4 {state}, 6.4b dulling {(Plugin.BlastDullOn.Value ? "ON" : "off")}, 6.2h thump level {Plugin.ThumpExplosionDb.Value:+0;-0;0} dB): moved {Moved}" +
                   (Moved > 0 ? $", on average {_movedSum / Moved:0} deg from the true direction" : "") + $"; dulled {Dulled}" + (Dulled > 0 ? $", typical cutoff {Mathf.Exp((float)(_cutLogSum / Dulled)):0} Hz" : "") +
                   $"; left at the true spot: {(parts.Count > 0 ? string.Join(", ", parts) : "-")}  (blast.csv)\n";
        }
    }
}
