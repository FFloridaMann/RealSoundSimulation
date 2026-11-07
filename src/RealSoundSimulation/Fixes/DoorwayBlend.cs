using System;
using Audio.AmbientSubsystem;
using Audio.SpatialSystem;
using Audio.SpatialSystem.Data;
using Comfort.Common;
using EFT;
using EFT.DataProviding;
using EFT.EnvironmentEffect;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Audio;

namespace RealSoundSimulation.Fixes
{
    // Fix 3: smooth doorways (solutions doc 2.11, revised after raids 1-4).
    // The game switches the whole mix between its Outdoor and Indoor snapshots in 0.1 s (in) / 0.5 s (out), using
    // trigger volumes, and the outdoor ambience separately: in raid 2 it sat at 0 in the doorway and jumped to full on
    // stepping out. Here one value, "outdoorness" (0 deep inside, 0.5 in the doorway, 1 outside), is worked out every
    // frame and drives both:
    //   mix:      Outdoor snapshot weight = outdoorness, Indoor = 1 - outdoorness
    //   ambience: outdoor ambience rises from the room's own level to full as you reach the doorway; room tone fades out
    //             over the first metre or so outside.
    // While on, the game's own snapshot switch and ambience fades are skipped. Turning it off hands control back.
    //
    // How outdoorness is measured (0.2.1). Take the nearest open doorway to outside (inside: only doorways of the room you
    // are in). Measure your distance from the doorway's PLANE along its outward direction (s: negative inside, positive
    // outside) and your sideways distance from its centre (lat). In front of the doorway the value comes from s, so it
    // passes smoothly through 0.5 wherever the game's room boundary happens to be. Further than LatFull..LatZero to the
    // side, it fades to the baked room's answer (0 inside, 1 outside), so walking along an outside wall past a door stays
    // outdoors. 0.2.0 used the distance to the doorway's centre, which jumped (0.24 -> 1.0 in raid 4) because the game's
    // room boundary sits about a metre from the doorway centre.
    internal class DoorwayBlend : MonoBehaviour
    {
        public static bool Active { get; private set; }
        public static float Outdoorness = float.NaN;       // smoothed, applied value (logs)
        public static float TargetOutdoorness = float.NaN; // unsmoothed, always computed in raid (also used by fixes 3c and 4)
        public static int PortalsConsidered;

        private const float SmoothTime = 0.12f;
        private const float LatFull = 0.75f;  // sideways distance from the doorway centre with full doorway influence (m)
        private const float LatZero = 1.75f;  // sideways distance where the doorway no longer counts (m)
        private const byte ClosedState = (byte)BaseSpatialAudioPortal.PortalState.Closed;
        private const float StickyMargin = 1.0f; // another doorway must be this much closer to take over (m)

        private static int _lastPortalId;
        // Exposed for ambience.csv: which doorway was used, distance through its plane (s) and sideways (lat).
        public static int DebugPortalId;
        public static float DebugS = float.NaN, DebugLat = float.NaN;

        // Outward (towards outdoors) direction per doorway, per outdoor area; built once from the game's room data.
        private struct PortalGeo { public bool Valid; public Vector3 Out; }
        private static readonly System.Collections.Generic.Dictionary<int, PortalGeo[]> GeoByOutRoom = new System.Collections.Generic.Dictionary<int, PortalGeo[]>();
        private static readonly AccessTools.FieldRef<AmbientSoundBlender, AudioMixer> BlenderMixerRef =
            AccessTools.FieldRefAccess<AmbientSoundBlender, AudioMixer>("_masterMixer");

        private readonly AudioMixerSnapshot[] _snaps = new AudioMixerSnapshot[2];
        private readonly float[] _weights = new float[2];
        private float _o = -1f, _lastSnapO = -1f, _lastAmb = -1f, _lastTone = -1f;
        private float _roomLevel = 0f, _roomLevelTarget = 0f;
        private float _expValue = float.NaN; // fix 8
        private float _lastV2Db = 999f, _lastV2Cut = 22000f; // fix 9
        // 0.6.9: the indoor-layer level last written (1 = the game's), and whether that mixer fade also reaches the rain
        // player (decided from the blenders' own indoor parameter names, read once per raid)
        public static float IndoorApplied = 1f;
        public static bool IndoorParamsIncludeRain;
        public static string IndoorParamNames = "";
        private static bool _paramsRead;
        private EnvironmentSoundBlendSystem _blend;
        private float _nextBlendSearch;

        public static void Apply(Harmony h)
        {
            var skip = new HarmonyMethod(typeof(DoorwayBlend), nameof(SkipWhileActive));
            h.Patch(AccessTools.Method(typeof(BetterAudio), nameof(BetterAudio.TransitToEnvironment), new[] { typeof(EnvironmentType) }), prefix: skip);
            h.Patch(AccessTools.Method(typeof(BetterAudio), nameof(BetterAudio.TransitToEnvironment), new[] { typeof(EnvironmentType), typeof(float) }), prefix: skip);
            h.Patch(AccessTools.Method(typeof(AmbientSoundBlender), nameof(AmbientSoundBlender.BlendToIndoor)), prefix: skip);
            h.Patch(AccessTools.Method(typeof(AmbientSoundBlender), nameof(AmbientSoundBlender.BlendToOutdoor)), prefix: skip);
            h.Patch(AccessTools.Method(typeof(AmbientSoundBlender), nameof(AmbientSoundBlender.BlendToTargetValue)), prefix: skip);
            h.Patch(AccessTools.Method(typeof(AmbientSoundBlender), nameof(AmbientSoundBlender.CalculateAndApplyMixerParameters)), prefix: skip);
        }

        private static bool SkipWhileActive() => !Active;

        private void LateUpdate()
        {
            bool inRaid = SpatialAudioSystem.Initialized && MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated
                          && MonoBehaviourSingleton<BetterAudio>.Instantiated && GamePlayerOwner.MyPlayer != null;
            if (!inRaid)
            {
                if (Active) Deactivate(false);
                TargetOutdoorness = Outdoorness = float.NaN;
                _blend = null; _o = -1f;
                GeoByOutRoom.Clear(); // next raid may be another map
                _lastPortalId = 0;
                return;
            }

            try
            {
                SpatialAudioSystem sas = MonoBehaviourSingleton<SpatialAudioSystem>.Instance;
                Transform lt = sas.ListenerTransform;
                if (lt == null) return;
                TargetOutdoorness = ComputeTarget(sas, lt.position, out ISpatialAudioRoom room, out bool indoor);

                if (!Plugin.DoorwayOn.Value)
                {
                    if (Active) Deactivate(true);
                    Outdoorness = float.NaN;
                    return;
                }

                float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
                _o = _o < 0f || !Active ? TargetOutdoorness : _o + (TargetOutdoorness - _o) * (1f - Mathf.Exp(-dt / SmoothTime));
                Outdoorness = _o;
                if (!Active) { Active = true; _lastSnapO = _lastAmb = _lastTone = -1f; }

                ApplySnapshots(MonoBehaviourSingleton<BetterAudio>.Instance, _o);

                if (indoor && room?.RoomAmbientData != null) _roomLevelTarget = Mathf.Clamp01(room.RoomAmbientData.OutdoorAmbientVolume);
                // 0.5.8: fix 3h (raising this level by the glass around you) removed; fix 8 replaced it in 0.4.2.
                _roomLevel = _roomLevel + (_roomLevelTarget - _roomLevel) * (1f - Mathf.Exp(-dt / 0.3f));
                _expValue = AmbienceMix.OutdoorValue(dt, _roomLevelTarget);
                ApplyAmbience(_o, _roomLevel);
            }
            catch (Exception e)
            {
                Diagnostics.DiagRunner.ReportOnce("fix 3 doorway blend", e);
            }
        }

        // 0 deep inside .. 0.5 at the doorway .. 1 outside (blend widths from config). See the class comment.
        private static float ComputeTarget(SpatialAudioSystem sas, Vector3 lp, out ISpatialAudioRoom room, out bool indoor)
        {
            room = sas.ListenerCurrentRoom;
            indoor = room != null && room.IsValid && !room.IsOutdoor;
            float roomAnswer = indoor ? 0f : 1f;
            PortalsConsidered = 0;
            int outId = sas.ListenerCurrentOutdoorRoomID;
            if (!DataProvider.TryGetData<OutdoorPortalsDataContainer>(out OutdoorPortalsDataContainer data)
                || !data.TryGetOutdoorPortalData(outId, out PortalData[] portals) || portals == null)
                return roomAnswer;

            short[] ids = null;
            if (indoor) data.TryGetIndoorRoomsIdsByOutRoomID(outId, out ids);
            int bestIdx = -1, stickyIdx = -1; float best = float.MaxValue, stickyD = float.MaxValue;
            for (int i = 0; i < portals.Length; i++)
            {
                PortalData p = portals[i];
                if (p.id == 0 || p.state == ClosedState) continue;
                float d = (p.position - lp).sqrMagnitude;
                // Sticky (0.2.2): the doorway used last frame stays a candidate on both sides of the crossing, even though
                // inside only the current room's doorways normally count. Raid 5 jumped 0.15 -> 0.69 when the inside and
                // outside searches picked different doorways.
                if (p.id == _lastPortalId) { stickyIdx = i; stickyD = d; }
                // Inside, only doorways that open from the room you are standing in.
                if (indoor && (ids == null || p.startIndex < 0 || p.startIndex >= ids.Length || ids[p.startIndex] != room.ID)) continue;
                PortalsConsidered++;
                if (d < best) { best = d; bestIdx = i; }
            }
            // Keep last frame's doorway unless another is clearly (StickyMargin m) closer.
            if (stickyIdx >= 0 && (bestIdx < 0 || Mathf.Sqrt(stickyD) <= Mathf.Sqrt(best) + StickyMargin)
                && Mathf.Sqrt(stickyD) < LatZero + Mathf.Max(Plugin.DoorwayInside.Value, Plugin.DoorwayOutside.Value))
            {
                bestIdx = stickyIdx; best = stickyD;
            }
            if (bestIdx < 0) { _lastPortalId = 0; DebugPortalId = 0; DebugS = DebugLat = float.NaN; return roomAnswer; }
            _lastPortalId = portals[bestIdx].id;
            DebugPortalId = _lastPortalId;

            float wIn = Mathf.Max(0.1f, Plugin.DoorwayInside.Value), wOut = Mathf.Max(0.1f, Plugin.DoorwayOutside.Value);
            PortalGeo[] geo = GetGeo(data, outId, portals);
            if (geo == null || !geo[bestIdx].Valid)
            {
                // No usable orientation for this doorway: fall back to distance from its centre (0.2.0 behaviour).
                float dc = Mathf.Sqrt(best);
                DebugS = float.NaN; DebugLat = dc;
                return indoor ? 0.5f * (1f - Mathf.Clamp01(dc / wIn)) : 0.5f + 0.5f * Mathf.Clamp01(dc / wOut);
            }

            Vector3 v = lp - portals[bestIdx].position;
            float s = Vector3.Dot(v, geo[bestIdx].Out);                  // + outside the doorway plane, - inside
            float lat = Mathf.Sqrt(Mathf.Max(0f, v.sqrMagnitude - s * s)); // sideways from the doorway centre
            DebugS = s; DebugLat = lat;
            float planeAnswer = s >= 0f ? 0.5f + 0.5f * Mathf.Clamp01(s / wOut) : 0.5f - 0.5f * Mathf.Clamp01(-s / wIn);
            float doorwayWeight = 1f - Smooth01((lat - LatFull) / (LatZero - LatFull));
            return Mathf.Lerp(roomAnswer, planeAnswer, doorwayWeight);
        }

        // For each doorway: which way is outside. The doorway's plane normal (its transform.up) points either way, so it is
        // oriented away from the centre of the indoor room it belongs to.
        private static PortalGeo[] GetGeo(OutdoorPortalsDataContainer data, int outId, PortalData[] portals)
        {
            if (GeoByOutRoom.TryGetValue(outId, out PortalGeo[] g) && g.Length == portals.Length) return g;
            g = new PortalGeo[portals.Length];
            if (data.TryGetPortalsByRelativeOutdoorRoomID(outId, out System.Collections.Generic.List<BaseSpatialAudioPortal> list)
                && list != null && list.Count == portals.Length)
            {
                for (int i = 0; i < portals.Length; i++)
                {
                    BaseSpatialAudioPortal bp = list[i];
                    if (bp == null || portals[i].id == 0) continue;
                    SpatialAudioRoom inRoom = bp.FrontRoom != null && Audio.Extensions.AudioExtensions.IsIndoor(bp.FrontRoom.Type) ? bp.FrontRoom : bp.BackRoom;
                    if (inRoom == null || !Audio.Extensions.AudioExtensions.IsIndoor(inRoom.Type)) continue;
                    Vector3 n = portals[i].normal;
                    if (n.sqrMagnitude < 1e-6f) continue;
                    n.Normalize();
                    float side = Vector3.Dot(((ISpatialAudioRoom)inRoom).Bounds.center - portals[i].position, n);
                    if (Mathf.Abs(side) < 0.05f) continue; // room centre lies in the doorway plane: orientation unknown
                    g[i] = new PortalGeo { Valid = true, Out = side > 0f ? -n : n };
                }
            }
            GeoByOutRoom[outId] = g;
            return g;
        }

        private void ApplySnapshots(BetterAudio ba, float o)
        {
            if (Mathf.Abs(o - _lastSnapO) < 0.005f) return;
            if (ba.Master == null || ba.Snapshots == null || ba.Snapshots.Length < 2) return;
            _snaps[0] = ba.Snapshots[0]; // Outdoor
            _snaps[1] = ba.Snapshots[1]; // Indoor
            _weights[0] = o;
            _weights[1] = 1f - o;
            ba.Master.TransitionToSnapshots(_snaps, _weights, 0.05f);
            _lastSnapO = o;
        }

        private void ApplyAmbience(float o, float roomLevel)
        {
            if (_blend == null)
            {
                if (Time.unscaledTime < _nextBlendSearch) return;
                _nextBlendSearch = Time.unscaledTime + 2f;
                _blend = FindObjectOfType<EnvironmentSoundBlendSystem>();
                if (_blend == null) return;
            }
            AmbientSoundBlender[] blenders = _blend._ambientSoundBlenders;
            if (blenders == null) return;
            // The game initialises its ambience blenders about 30 s into the raid; before that they have no mixer and
            // setting them throws (raid 4). Wait until every blender is ready.
            for (int i = 0; i < blenders.Length; i++)
                if (blenders[i] != null && BlenderMixerRef(blenders[i]) == null) return;
            if (!_paramsRead) ReadIndoorParams(blenders);

            float amb = Mathf.Lerp(roomLevel, 1f, Smooth01(2f * o));   // full by the time you reach the doorway
            float tone = Smooth01(2f * (1f - o));                       // room tone gone once you are outside the blend width
            // fix 8 (0.4.2): the outdoor level comes from the exposure measure instead of the doorway fade (no sideways swings
            // at wide entrances, raid 12), and the indoor layers fade near openings.
            if (!float.IsNaN(_expValue))
            {
                amb = _expValue;
                // 0.5.7: the indoor layers (roof rain, room tone, indoor wind) follow how enclosed you are, not the game's
                // room map, so covered spots it calls outdoor - a garage entrance under a concrete ceiling (raid 20) - get
                // them too, fading out as you reach open air. 8d sets how far they fade where the outside is open around you.
                tone = AmbienceMix.IndoorLayers();
            }
            // 0.6.0, fix 9: probe v2 sets the outdoor volume (dB) and muffling (Hz) directly, and the indoor layers.
            if (AmbienceMix.V2Active && !float.IsNaN(AmbienceMix.V2Level))
            {
                float lvF = AmbienceMix.V2Level, cut = AmbienceMix.V2Cutoff, t2 = AmbienceMix.V2Indoor;
                AmbienceMix.ApplyFloor(ref lvF, ref cut); // 0.6.9 "9l"
                float volDb = Mathf.Clamp(10f * Mathf.Log10(Mathf.Max(lvF, 1e-8f)), -80f, 0f);
                bool setV = Mathf.Abs(volDb - _lastV2Db) > 0.05f || Mathf.Abs(Mathf.Log(cut / Mathf.Max(_lastV2Cut, 1f))) > 0.01f;
                bool setT = Mathf.Abs(t2 - _lastTone) > 0.003f;
                for (int i = 0; i < blenders.Length; i++)
                {
                    AmbientSoundBlender b = blenders[i];
                    if (b == null) continue;
                    if (setV) AmbienceMix.ApplyOutdoorDirect(b, BlenderMixerRef(b), volDb, cut);
                    if (setT) b.SetIndoorMixerParams(t2, 0f, true);
                }
                if (setV) { _lastV2Db = volDb; _lastV2Cut = cut; }
                if (setT) { _lastTone = t2; IndoorApplied = t2; }
                _lastAmb = -1f; // so the v1 path re-applies if v2 is switched off
                return;
            }
            _lastV2Db = 999f;
            bool setAmb = Mathf.Abs(amb - _lastAmb) > 0.003f, setTone = Mathf.Abs(tone - _lastTone) > 0.003f;
            if (!setAmb && !setTone) return;
            for (int i = 0; i < blenders.Length; i++)
            {
                AmbientSoundBlender b = blenders[i];
                if (b == null) continue;
                if (setAmb) b.SetOutdoorMixerParams(amb, 0f, true);
                if (setTone) b.SetIndoorMixerParams(tone, 0f, true);
            }
            if (setAmb) _lastAmb = amb;
            if (setTone) { _lastTone = tone; IndoorApplied = tone; }
        }

        // 0.6.9: which mixer volumes the game's indoor-layer fade (SetIndoorMixerParams) moves. If one is the rain's, roof
        // rain is compensated for it (AmbienceMix.RoofRain), so openings around you don't turn the roof down.
        private static void ReadIndoorParams(AmbientSoundBlender[] blenders)
        {
            _paramsRead = true;
            var names = new System.Collections.Generic.SortedSet<string>();
            foreach (AmbientSoundBlender b in blenders)
            {
                if (b == null || b._indoorMixerParams == null) continue;
                if (!b._indoorMixerParams.TryGetParameters(Audio.AmbientSubsystem.Data.EMixerParameterType.Volume, out Audio.Data.AudioMixerParamsContainer c) || c == null) continue;
                for (int i = 0; i < c.Count; i++) names.Add(c[i]);
            }
            IndoorParamNames = string.Join(" ", names);
            IndoorParamsIncludeRain = false;
            foreach (string n in names)
            {
                string l = n.ToLowerInvariant();
                if (l.Contains("precip") || l.Contains("rain")) IndoorParamsIncludeRain = true;
            }
            Plugin.Log.LogInfo($"Indoor-layer mixer volumes: {IndoorParamNames} (rain among them: {IndoorParamsIncludeRain})");
            ReportNames = IndoorParamNames; ReportRain = IndoorParamsIncludeRain;
        }

        // 0.6.14: what the summary reports is kept until the next raid starts (ClearReport); the end-of-raid reset ran
        // before the summary was written, so it said "not read"
        public static string ReportNames = ""; public static bool ReportRain;
        public static void ClearReport() { ReportNames = ""; ReportRain = false; }
        public static void ResetIndoorParams() { _paramsRead = false; IndoorApplied = 1f; IndoorParamsIncludeRain = false; IndoorParamNames = ""; }

        private static float Smooth01(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        // Hand control back to the game: snap the snapshot to whatever the game currently thinks, and let the ambience
        // system re-apply its own state for the current room.
        private void Deactivate(bool stillInRaid)
        {
            Active = false;
            _o = -1f;
            if (!stillInRaid) return;
            try
            {
                if (EnvironmentManager.Instance != null && MonoBehaviourSingleton<BetterAudio>.Instantiated)
                    MonoBehaviourSingleton<BetterAudio>.Instance.TransitToEnvironment(EnvironmentManager.Instance.Environment);
                if (_blend != null) _blend.method_0();
            }
            catch (Exception e)
            {
                Diagnostics.DiagRunner.ReportOnce("fix 3 doorway blend (turning off)", e);
            }
        }
    }
}
