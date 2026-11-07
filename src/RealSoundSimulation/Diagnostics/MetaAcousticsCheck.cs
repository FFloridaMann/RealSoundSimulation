using System;
using System.Linq;
using UnityEngine;

namespace RealSoundSimulation.Diagnostics
{
    // Are Meta XR's room acoustics actually running in this raid? Counts the Meta acoustic map and geometry components in
    // the scene (found by name, so the plugin doesn't depend on Meta's assembly). The own-gun reverb boost (fix 5) and any
    // reliance on Meta's reverb only make sense if a map is present and enabled.
    internal static class MetaAcousticsCheck
    {
        public static string Describe()
        {
            try
            {
                return "META XR ACOUSTICS\n" +
                       "  " + Count("MetaXRAcousticMap") + "\n" +
                       "  " + Count("MetaXRAcousticGeometry") + "\n" +
                       "  " + Count("MetaXRAcousticControlZone");
            }
            catch (Exception e)
            {
                return "META XR ACOUSTICS\n  check failed: " + e.Message;
            }
        }

        private static string Count(string typeName)
        {
            Type t = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => { try { return a.GetType(typeName, false); } catch { return null; } })
                .FirstOrDefault(x => x != null);
            if (t == null) return $"{typeName}: type not loaded";
            UnityEngine.Object[] all = UnityEngine.Object.FindObjectsOfType(t);
            int enabled = all.OfType<Behaviour>().Count(b => b.isActiveAndEnabled);
            return $"{typeName}: {all.Length} in scene, {enabled} active";
        }
    }
}
