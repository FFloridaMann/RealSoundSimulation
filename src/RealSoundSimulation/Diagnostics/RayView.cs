using UnityEngine;
using UnityEngine.Rendering;

namespace RealSoundSimulation.Diagnostics
{
    // Probe ray view (0.6.0): draws the latest path of each of the 128 probe v2 directions in the world, so you can see
    // what the probe sees. Toggle with "Probe ray view key" (F11) in raid. Colours:
    //   green   reached open sky directly
    //   cyan    reached open sky after bouncing
    //   yellow  reached the sky through glass
    //   red     trapped (absorbed, or still bouncing after 3 reflections)
    //   magenta a long interior (ran 60 m without hitting anything, but under a roof)
    // Drawn on top of everything, 1.5 cm wide. Nothing is drawn while it's off.
    internal static class RayView
    {
        private static GameObject _root;
        private static LineRenderer[] _lines;
        private static bool _on;
        private static Material _mat;

        public static void Toggle()
        {
            _on = !_on;
            if (!_on) Hide();
            Plugin.Log.LogInfo($"Probe ray view {(_on ? "on" : "off")}");
        }

        public static void Hide()
        {
            if (_root != null) _root.SetActive(false);
        }

        public static void Tick()
        {
            if (!_on || !Acoustics.AcousticProbe.Valid) { Hide(); return; }
            if (_root == null && !Build()) { _on = false; return; }
            if (!_root.activeSelf) _root.SetActive(true);
            for (int i = 0; i < Acoustics.AcousticProbe.N; i++)
            {
                LineRenderer lr = _lines[i];
                int n = Acoustics.AcousticProbe.PtN[i];
                ref Acoustics.AcousticProbe.Result r = ref Acoustics.AcousticProbe.Latest[i];
                if (n < 2 || r.Fate == Acoustics.AcousticProbe.Fate.None) { lr.positionCount = 0; continue; }
                lr.positionCount = n;
                for (int k = 0; k < n; k++) lr.SetPosition(k, Acoustics.AcousticProbe.Pts[i * Acoustics.AcousticProbe.MaxPts + k]);
                Color c;
                switch (r.Fate)
                {
                    case Acoustics.AcousticProbe.Fate.Sky: c = r.ViaGlass ? new Color(1f, 0.9f, 0.2f) : new Color(0.2f, 1f, 0.3f); break;
                    case Acoustics.AcousticProbe.Fate.SkyAfterBounces: c = r.ViaGlass ? new Color(1f, 0.9f, 0.2f) : new Color(0.2f, 0.9f, 1f); break;
                    case Acoustics.AcousticProbe.Fate.LongInterior: c = new Color(1f, 0.3f, 1f); break;
                    default: c = new Color(1f, 0.25f, 0.2f, 0.6f); break;
                }
                lr.startColor = c; lr.endColor = c;
            }
        }

        private static bool Build()
        {
            Shader sh = Shader.Find("Hidden/Internal-Colored") ?? Shader.Find("Sprites/Default");
            if (sh == null) { Plugin.Log.LogWarning("Probe ray view: no line shader found"); return false; }
            _mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            if (_mat.HasProperty("_ZTest")) _mat.SetInt("_ZTest", (int)CompareFunction.Always);
            if (_mat.HasProperty("_Cull")) _mat.SetInt("_Cull", (int)CullMode.Off);
            _root = new GameObject("CA2 probe ray view");
            Object.DontDestroyOnLoad(_root);
            _lines = new LineRenderer[Acoustics.AcousticProbe.N];
            for (int i = 0; i < _lines.Length; i++)
            {
                var go = new GameObject("ray" + i);
                go.transform.SetParent(_root.transform, false);
                LineRenderer lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = _mat;
                lr.useWorldSpace = true;
                lr.widthMultiplier = 0.015f;
                lr.shadowCastingMode = ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.positionCount = 0;
                _lines[i] = lr;
            }
            return true;
        }
    }
}
