using System.Collections.Generic;
using EFT.Ballistics;
using EFT.Interactive;
using UnityEngine;

namespace RealSoundSimulation.Acoustics
{
    // What a ray hit, acoustically. Shared by the enclosure probe and the echoes (0.3.1).
    //
    // Material: the collider's EFT bullet material tag (BallisticCollider.TypeOfMaterial, on the collider or a parent).
    // Windows are special: a window blocks rays with a separate "obstructive" collider (WindowBreaker.ObstructiveCollider)
    // that carries no tag, so before 0.3.1 windows read as whatever the building around them was (raid 7: 8 glass echoes
    // in a whole raid, in a glass office). At the first lookup each raid, every WindowBreaker's colliders are registered
    // as Glass. A broken window's obstructive collider is switched off by the game, so rays then pass through it.
    // 0.9.3: but its other colliders (frame and sash, e.g. Window_wood_02-point_C02_Close_COLLIDER) stay on, and they
    // were Glass for the whole raid, so a broken window still sounded closed (Streets raid_20261114_120507, marks 7-10;
    // Streets has many windows broken from the start). A window's colliders are now checked on every lookup: Glass while
    // it is whole, GlassShattered (see-through for every feature) once WindowBreaker.IsDamaged, except the shards left
    // stuck in the frame, which stay Glass.
    //
    // Closure: how much a surface counts towards "being in a room" (0 = see-through, 1 = solid wall).
    //   chain fence, grate, shattered glass              0.1
    //   glass, thin wood (planks, slats), cardboard, fabric, soft   setting "3f Thin and glass surfaces count" (0.5)
    //   everything else (concrete, metal, thick wood, untagged)     1
    internal static class Surfaces
    {
        private static readonly Dictionary<int, MaterialType> Cache = new Dictionary<int, MaterialType>();
        private static readonly Dictionary<int, WindowBreaker> WindowOf = new Dictionary<int, WindowBreaker>(); // 0.9.3
        private static readonly List<WindowBreaker> Windows = new List<WindowBreaker>();
        private static bool _windowsRegistered;
        public static int WindowsFound;
        public static int WindowsBrokenAtStart = -1;

        public static void Reset()
        {
            Cache.Clear();
            WindowOf.Clear(); Windows.Clear();
            _windowsRegistered = false;
            WindowsFound = 0; WindowsBrokenAtStart = -1; WindowFillers = 0;
        }

        public static MaterialType Material(Collider c)
        {
            if (c == null) return MaterialType.None;
            if (!_windowsRegistered) StartupTiming.Run("first use", "windows", RegisterWindows);
            int id = c.GetInstanceID();
            if (WindowOf.TryGetValue(id, out WindowBreaker w)) return WindowMaterial(c, w);
            if (Cache.TryGetValue(id, out MaterialType m)) return m;
            BallisticCollider bc = c.GetComponent<BallisticCollider>();
            if (bc == null) bc = c.GetComponentInParent<BallisticCollider>();
            m = bc != null ? bc.TypeOfMaterial : MaterialType.None;
            if (m == MaterialType.None)
            {
                // a collider made after the scan, e.g. a shard left stuck when a window breaks
                WindowBreaker wb = c.GetComponentInParent<WindowBreaker>();
                if (wb != null) { WindowOf[id] = wb; return WindowMaterial(c, wb); }
                if (IsWindowFiller(c)) { m = MaterialType.GlassShattered; WindowFillers++; }
            }
            Cache[id] = m;
            return m;
        }

        public static int WindowFillers;

        // 0.9.3: a window built into a building (no WindowBreaker) has colliders that bullets hit (..._BALLISTIC_<material>: the
        // frame, and the glass if it has any) and an untagged one filling the whole opening so you can't walk through it
        // (..._COLLIDER). That untagged one read as a solid pane or as glass, so broken decorative windows sounded closed
        // (Streets raid_20261114_122648, Window_wood_02-point_C02_Close_COLLIDER, marks 1-3). When the window has colliders
        // that bullets hit, the untagged one passes sound like shattered glass, and real glass is still caught by its own
        // glass collider. A window with no bullet colliders keeps the old rules (glass drawn there counts as glass, 0.5.2).
        private static bool IsWindowFiller(Collider c)
        {
            Transform root = null, t = c.transform;
            for (int i = 0; i < 3 && t != null; i++, t = t.parent)
                if (t.name.IndexOf("window", System.StringComparison.OrdinalIgnoreCase) >= 0) root = t;
            return root != null && root.GetComponentInChildren<BallisticCollider>(true) != null;
        }

        // Whether this collider belongs to a window: its material can change during the raid, so callers must not keep it.
        public static bool IsWindow(Collider c) => c != null && WindowOf.ContainsKey(c.GetInstanceID());

        private static MaterialType WindowMaterial(Collider c, WindowBreaker w)
        {
            if (w == null || !w.IsDamaged) return MaterialType.Glass;
            return c.name.IndexOf("stuckpiece", System.StringComparison.OrdinalIgnoreCase) >= 0 ? MaterialType.Glass : MaterialType.GlassShattered;
        }

        public static int WindowsBroken()
        {
            int n = 0;
            foreach (WindowBreaker w in Windows) if (w != null && w.IsDamaged) n++;
            return n;
        }

        // 0.9.3: Raid.Prepare registers them on the loading screen, so the first probe ray after you spawn doesn't pay for it.
        internal static void RegisterWindowsNow() { if (!_windowsRegistered) RegisterWindows(); }

        private static void RegisterWindows()
        {
            _windowsRegistered = true;
            try
            {
                foreach (WindowBreaker w in Object.FindObjectsOfType<WindowBreaker>())
                {
                    if (w == null) continue;
                    WindowsFound++;
                    Windows.Add(w);
                    if (w.ObstructiveCollider != null) WindowOf[w.ObstructiveCollider.GetInstanceID()] = w;
                    foreach (Collider c in w.GetComponentsInChildren<Collider>(true))
                        if (c != null) WindowOf[c.GetInstanceID()] = w;
                }
                WindowsBrokenAtStart = WindowsBroken();
            }
            catch (System.Exception e) { Diagnostics.DiagRunner.ReportOnce("surfaces: window scan", e); }
        }

        public static bool IsThin(MaterialType m)
        {
            switch (m)
            {
                case MaterialType.Glass: case MaterialType.WoodThin: case MaterialType.Cardboard:
                case MaterialType.Fabric: case MaterialType.GenericSoft: case MaterialType.GarbagePaper:
                case MaterialType.Chainfence: case MaterialType.Grate: case MaterialType.GlassShattered:
                    return true;
                default:
                    return false;
            }
        }

        // Hard, bare surfaces that make a room ring (0.3.3): a concrete stairwell keeps its long indoor sound.
        public static bool IsHard(MaterialType m)
        {
            switch (m)
            {
                case MaterialType.Concrete: case MaterialType.Stone: case MaterialType.Tile: case MaterialType.Asphalt:
                case MaterialType.MetalThick: case MaterialType.MetalNoDecal: case MaterialType.GenericHard:
                    return true;
                default:
                    return false;
            }
        }

        public static float Closure(MaterialType m)
        {
            switch (m)
            {
                case MaterialType.Chainfence: case MaterialType.Grate: case MaterialType.GlassShattered:
                    return 0.1f;
                case MaterialType.Glass: case MaterialType.WoodThin: case MaterialType.Cardboard:
                case MaterialType.Fabric: case MaterialType.GenericSoft: case MaterialType.GarbagePaper:
                    return Mathf.Clamp01(Plugin.ThinSurfaceCount.Value);
                default:
                    return 1f;
            }
        }
    }
}
