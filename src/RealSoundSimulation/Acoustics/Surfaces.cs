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
    //
    // Closure: how much a surface counts towards "being in a room" (0 = see-through, 1 = solid wall).
    //   chain fence, grate, shattered glass              0.1
    //   glass, thin wood (planks, slats), cardboard, fabric, soft   setting "3f Thin and glass surfaces count" (0.5)
    //   everything else (concrete, metal, thick wood, untagged)     1
    internal static class Surfaces
    {
        private static readonly Dictionary<int, MaterialType> Cache = new Dictionary<int, MaterialType>();
        private static bool _windowsRegistered;
        public static int WindowsFound;

        public static void Reset()
        {
            Cache.Clear();
            _windowsRegistered = false;
            WindowsFound = 0;
        }

        public static MaterialType Material(Collider c)
        {
            if (c == null) return MaterialType.None;
            if (!_windowsRegistered) RegisterWindows();
            int id = c.GetInstanceID();
            if (Cache.TryGetValue(id, out MaterialType m)) return m;
            BallisticCollider bc = c.GetComponent<BallisticCollider>();
            if (bc == null) bc = c.GetComponentInParent<BallisticCollider>();
            m = bc != null ? bc.TypeOfMaterial : MaterialType.None;
            if (m == MaterialType.None && c.GetComponentInParent<WindowBreaker>() != null) m = MaterialType.Glass;
            Cache[id] = m;
            return m;
        }

        private static void RegisterWindows()
        {
            _windowsRegistered = true;
            try
            {
                foreach (WindowBreaker w in Object.FindObjectsOfType<WindowBreaker>())
                {
                    if (w == null) continue;
                    WindowsFound++;
                    if (w.ObstructiveCollider != null) Cache[w.ObstructiveCollider.GetInstanceID()] = MaterialType.Glass;
                    foreach (Collider c in w.GetComponentsInChildren<Collider>(true))
                        if (c != null) Cache[c.GetInstanceID()] = MaterialType.Glass;
                }
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
