using System;
using System.Collections.Generic;
using Audio.SpatialSystem;
using RealSoundSimulation.Acoustics;
using RealSoundSimulation.Diagnostics;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.DataProviding;
using EFT.Interactive;
using HarmonyLib;
using UnityEngine;

namespace RealSoundSimulation.Fixes
{
    // 6.5 (0.6.47): a closed door muffles by what it's made of. User, 9 Nov 2026, Capital: "doors I dont think closed should muffle so much especially not wood doors".
    // The game's route cost adds a shut door's closure level straight on and caps the total at 1 (PropagationJob), and a closure of 1 is common (raid_20261109_102037),
    // so one shut door can use up the whole score, plywood or steel. Real doors are thin and have gaps round them.
    //   value    the closure the game uses is held down to the door material's value x "6.5a". Never raised; an open door stays open.
    //   when     every value the game hands its route data goes through here, so sounds follow it from the next update. Applied again after the raid starts,
    //            once the doors exist to be read, and when the settings change.
    // Touches only the route data: a portal's own closure level, which the game animates, is left alone. Every sound through a door is affected, not only grenades.
    // Values are hand-set, nothing measured.
    //
    // 0.6.52: the doors are found by reading them from the scene once the raid has them. GameWorld.FindDoor crashes in SPT (its World component is null), so from 0.6.47 to 0.6.51 no real door
    // was ever held down, and a blast whose route ran through a shut door lost 6.4 as well. raid_20261109_114154, a grenade behind a wood door shut during the fuse: route cost 0.03 to 0.77.
    internal class ClosedDoors : MonoBehaviour
    {
        private const float UnknownValue = 0.7f, RetryS = 3f;
        private const int MaxTries = 5, MaxEmptyReads = 20;

        private static readonly Dictionary<short, float> Values = new Dictionary<short, float>();
        private static readonly Dictionary<MaterialType, float> Scores = new Dictionary<MaterialType, float>();
        private static readonly Dictionary<string, int> Found = new Dictionary<string, int>();
        private static readonly Dictionary<string, WorldInteractiveObject> Doors = new Dictionary<string, WorldInteractiveObject>();
        private static int _tries, _emptyReads;
        private static float _next;

        public static void Apply(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(SpatialAudioDataContainer), nameof(SpatialAudioDataContainer.UpdatePortalDataInternal)), prefix: new HarmonyMethod(typeof(ClosedDoors), nameof(UpdatePrefix)));
            h.Patch(AccessTools.Method(typeof(SpatialAudioDataContainer), nameof(SpatialAudioDataContainer.CreatePortalData)), postfix: new HarmonyMethod(typeof(ClosedDoors), nameof(CreatePostfix)));
            Plugin.DoorMufflingOn.SettingChanged += (s, e) => ApplyAll();
            Plugin.DoorMufflingStrength.SettingChanged += (s, e) => ApplyAll();
        }

        public static void Reset()
        {
            Values.Clear(); Found.Clear(); Doors.Clear();
            _tries = _emptyReads = 0; _next = 0f;
        }

        // The doors only exist once the world has loaded, and the game fills its route data before that.
        private void Update()
        {
            if (_tries >= MaxTries || !SpatialAudioSystem.Initialized || !Singleton<GameWorld>.Instantiated || Time.time < _next) return;
            _next = Time.time + RetryS;
            try { if (ReadDoors() == 0 && ++_emptyReads < MaxEmptyReads) return; } // none in the scene yet: this doesn't use up a try
            catch (Exception e) { DiagRunner.ReportOnce("6.5 closed doors (read doors)", e); }
            _tries++;
            ApplyAll();
        }

        private static int ReadDoors()
        {
            Doors.Clear();
            foreach (WorldInteractiveObject d in UnityEngine.Object.FindObjectsOfType<WorldInteractiveObject>())
                if (d != null && !string.IsNullOrEmpty(d.Id)) Doors[d.Id] = d;
            return Doors.Count;
        }

        private static void ApplyAll()
        {
            try
            {
                if (!SpatialAudioSystem.Initialized || !DataProvider.TryGetData<SpatialAudioDataContainer>(out SpatialAudioDataContainer data) || data == null) return;
                foreach (KeyValuePair<short, BaseSpatialAudioPortal> kv in data.PortalsByID)
                {
                    BaseSpatialAudioPortal p = kv.Value;
                    if (p != null && p.portalType == BaseSpatialAudioPortal.PortalType.Opening)
                        data.UpdatePortalData(kv.Key, p.state, p.PortalClosureLevel, p.Depth, p.traversalMaxCost); // queued: the game swaps it in at its next update
                }
            }
            catch (Exception e) { DiagRunner.ReportOnce("6.5 closed doors (apply)", e); }
        }

        private static void UpdatePrefix(SpatialAudioDataContainer __instance, short portalID, ref float closureLevel)
        {
            try
            {
                if (__instance.PortalsByID.TryGetValue(portalID, out BaseSpatialAudioPortal p)) closureLevel = Scaled(p, closureLevel);
            }
            catch (Exception e) { DiagRunner.ReportOnce("6.5 closed doors (update)", e); }
        }

        private static void CreatePostfix(BaseSpatialAudioPortal portal, ref RoomPair.AudioPortalData __result)
        {
            try { __result.closureLevel = Scaled(portal, __result.closureLevel); }
            catch (Exception e) { DiagRunner.ReportOnce("6.5 closed doors (create)", e); }
        }

        // What the routes should count for this door's closure. 6.4 picks its route with this too.
        internal static float Scaled(BaseSpatialAudioPortal p, float raw)
        {
            if (!Plugin.DoorMufflingOn.Value || p == null || raw <= 0f || p.portalType != BaseSpatialAudioPortal.PortalType.Opening) return raw;
            try { return Mathf.Min(raw, DoorValue(p) * Plugin.DoorMufflingStrength.Value); }
            catch (Exception e) { DiagRunner.ReportOnce("6.5 closed doors (door value)", e); return raw; } // a bad door keeps the game's value and can't take 6.4 down with it
        }

        private static float DoorValue(BaseSpatialAudioPortal p)
        {
            if (Values.TryGetValue(p.ID, out float v)) return v;
            if (!(p is SpatialAudioPortal s) || string.IsNullOrEmpty(s.DoorID)) return Remember(p.ID, UnknownValue, "no door");
            if (!Doors.TryGetValue(s.DoorID, out WorldInteractiveObject door) || door == null)
                return _tries >= MaxTries ? Remember(p.ID, UnknownValue, "door not found") : UnknownValue; // not read yet: read it next time
            if (!p.IsPositionIdentical(door.transform.position)) return Remember(p.ID, UnknownValue, "door elsewhere");
            MaterialType m = DoorMaterial(door);
            return Remember(p.ID, ValueOf(m), m == MaterialType.None ? "untagged" : m.ToString());
        }

        private static float Remember(short id, float value, string what)
        {
            Values[id] = value;
            Found.TryGetValue(what, out int n); Found[what] = n + 1;
            return value;
        }

        // The door's biggest material by collider size, so a metal handle doesn't decide a wooden door.
        private static MaterialType DoorMaterial(WorldInteractiveObject door)
        {
            Scores.Clear();
            foreach (Collider c in door.GetComponentsInChildren<Collider>(true))
            {
                MaterialType m = Surfaces.Material(c);
                if (m == MaterialType.None) continue;
                Vector3 b = c.bounds.size;
                float size = 0.001f + b.x * b.y + b.y * b.z + b.x * b.z; // a disabled collider has empty bounds: it still counts a little
                Scores.TryGetValue(m, out float t);
                Scores[m] = t + size;
            }
            MaterialType best = MaterialType.None;
            float top = 0f;
            foreach (KeyValuePair<MaterialType, float> kv in Scores)
                if (kv.Value > top) { top = kv.Value; best = kv.Key; }
            return best;
        }

        private static float ValueOf(MaterialType m)
        {
            switch (m)
            {
                case MaterialType.Cardboard: case MaterialType.Fabric: return 0.2f;
                case MaterialType.WoodThin: return 0.3f;
                case MaterialType.Plastic: case MaterialType.Glass: return 0.4f;
                case MaterialType.WoodThick: return 0.5f;
                case MaterialType.MetalThin: case MaterialType.GarbageMetal: return 0.6f;
                case MaterialType.MetalThick: case MaterialType.MetalNoDecal: return 0.8f;
                default: return UnknownValue;
            }
        }

        public static string Summary()
        {
            var parts = new List<string>();
            foreach (KeyValuePair<string, int> kv in Found) parts.Add($"{kv.Key} {kv.Value}");
            return $"CLOSED DOORS (6.5 {(Plugin.DoorMufflingOn.Value ? $"ON, strength {Plugin.DoorMufflingStrength.Value:0.00}" : "off")}): {Doors.Count} doors in the scene; shut doors read: {(parts.Count > 0 ? string.Join(", ", parts) : "none")}\n";
        }
    }
}
