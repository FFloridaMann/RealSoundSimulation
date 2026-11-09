using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Audio.SpatialSystem;
using HarmonyLib;
using UnityEngine;

namespace RealSoundSimulation.Acoustics
{
    // 10b (0.6.35): sound routes between rooms from EFT 1.0, in place of the game's own route file for the maps that have one. Sound in another
    // room reaches you along these routes (fix 2 and 6.1 use them). The files and the idea come from Sound Engine Backport by red_the_fox, MIT
    // licence (THIRD_PARTY_NOTICES.md). Each file has a list of where its rooms and portals should be: if the map doesn't match, the game's own
    // routes are used and the log says why. Files live in plugins/FFloridaMann-RealSoundSimulation/seb_routes/AudioBakeData (not in git). Applies at the next raid.
    internal static class ExtraRoutes
    {
        private sealed class LoadState
        {
            public string Map, GamePath, FilePath;
            public SceneCheck Check;
            public bool Using;
            public readonly Stopwatch Watch = new Stopwatch();
        }

        private sealed class SceneCheck
        {
            public int Version;
            public readonly List<KeyValuePair<short, Vector3>> Rooms = new List<KeyValuePair<short, Vector3>>();
            public readonly List<KeyValuePair<short, Vector3>> Portals = new List<KeyValuePair<short, Vector3>>();
        }

        private const float ToleranceSqr = 0.0025f; // 5 cm
        private static readonly ConditionalWeakTable<SpatialAudioDataLoader, LoadState> States = new ConditionalWeakTable<SpatialAudioDataLoader, LoadState>();
        private static string Dir => Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? "", "seb_routes", "AudioBakeData");
        private static string _last = "no map loaded yet";

        public static string State => Plugin.ExtraRoutesOn.Value ? "ON (" + _last + ")" : "off";

        public static void Apply(Harmony h)
        {
            h.Patch(AccessTools.Constructor(typeof(SpatialAudioDataLoader), new[] { typeof(string), typeof(MonoBehaviour) }), postfix: new HarmonyMethod(typeof(ExtraRoutes), nameof(CtorPostfix)));
            h.Patch(AccessTools.Method(typeof(SpatialAudioDataLoader), "LoadBakeDataAsync"), prefix: new HarmonyMethod(typeof(ExtraRoutes), nameof(LoadPrefix)));
            h.Patch(AccessTools.Method(typeof(SpatialAudioDataLoader), "DataLoadedHandler"), postfix: new HarmonyMethod(typeof(ExtraRoutes), nameof(LoadedPostfix)));
        }

        private static void Say(string s) { _last = s; Plugin.Log.LogInfo("10b routes: " + s); }

        private static void CtorPostfix(SpatialAudioDataLoader __instance, string dataPath)
        {
            try
            {
                string file = Path.GetFileName((dataPath ?? "").Replace('\\', '/'));
                var st = new LoadState { Map = Path.GetFileNameWithoutExtension(file), GamePath = __instance._fullPath };
                st.Watch.Start();
                States.Remove(__instance);
                States.Add(__instance, st);
                if (!Plugin.ExtraRoutesOn.Value) return;
                string path = Path.Combine(Dir, file), scene = path + ".scene";
                if (!File.Exists(path)) { Say(st.Map + ": the game's routes (no file for this map)"); return; }
                if (!File.Exists(scene)) { Say(st.Map + ": the game's routes (no scene check file)"); return; }
                st.Check = ReadCheck(scene);
                st.FilePath = path;
                st.Using = true;
            }
            catch (Exception e) { Plugin.Log.LogError("10b routes: could not prepare, the game's are used: " + e); }
        }

        private static void LoadPrefix(SpatialAudioDataLoader __instance, ref string path, SpatialAudioDataContainer container)
        {
            if (!States.TryGetValue(__instance, out LoadState st)) return;
            try { path = Verify(st, container, path); }
            catch (Exception e)
            {
                st.Using = false;
                path = st.GamePath;
                Plugin.Log.LogError($"10b routes: {st.Map}: scene check failed, the game's routes are used: {e}");
            }
            __instance._fullPath = path;
        }

        private static string Verify(LoadState st, SpatialAudioDataContainer c, string path)
        {
            if (!st.Using) return path;
            var why = new StringBuilder();
            int missing = 0, moved = 0;
            Compare("room", st.Check.Rooms, c.RoomsByID, ref missing, ref moved, why);
            Compare("portal", st.Check.Portals, c.PortalsByID, ref missing, ref moved, why);
            if (missing + moved > 0)
            {
                st.Using = false;
                Say($"{st.Map}: the game's routes (map differs: {missing} missing, {moved} moved of {st.Check.Rooms.Count} rooms and {st.Check.Portals.Count} portals; {why})");
                return st.GamePath;
            }
            return st.FilePath;
        }

        private static void Compare<T>(string kind, List<KeyValuePair<short, Vector3>> expected, Dictionary<short, T> scene, ref int missing, ref int moved, StringBuilder why) where T : Component
        {
            foreach (KeyValuePair<short, Vector3> e in expected)
            {
                if (!scene.TryGetValue(e.Key, out T v) || v == null)
                {
                    if (missing + moved < 5) why.Append($"{kind} {e.Key} missing; ");
                    missing++;
                }
                else if ((v.transform.position - e.Value).sqrMagnitude > ToleranceSqr)
                {
                    if (missing + moved < 5) why.Append($"{kind} {e.Key} moved; ");
                    moved++;
                }
            }
        }

        private static void LoadedPostfix(SpatialAudioDataLoader __instance, SpatialAudioDataContainer dataContainer)
        {
            try
            {
                if (dataContainer == null || dataContainer.IsDisposed || !States.TryGetValue(__instance, out LoadState st)) return;
                st.Watch.Stop();
                int routes = 0, badSteps = 0;
                foreach (RoomPair p in dataContainer.RoomPairsByID.Values)
                    foreach (RoomPair.Route r in p.Routes)
                    {
                        routes++;
                        foreach (short id in r.PortalIDs) if (id == 0) badSteps++;
                    }
                string whose = st.Using ? "EFT 1.0 routes" : "the game's routes";
                Say($"{st.Map}: {whose} in {st.Watch.Elapsed.TotalSeconds:0.0} s, {dataContainer.RoomPairsByID.Count} room pairs, {routes} routes, {badSteps} bad steps");
            }
            catch (Exception e) { DiagRunnerReport(e); }
        }

        private static void DiagRunnerReport(Exception e) => Plugin.Log.LogWarning("10b routes: summary failed: " + e.Message);

        private static SceneCheck ReadCheck(string path)
        {
            var c = new SceneCheck();
            foreach (string raw in File.ReadAllLines(path))
            {
                string t = raw.Trim();
                if (t.Length == 0 || t[0] == '#') continue;
                string[] a = t.Split(' ');
                if (a[0] == "version") c.Version = int.Parse(a[1], CultureInfo.InvariantCulture);
                else if (a[0] == "room" || a[0] == "portal")
                {
                    var e = new KeyValuePair<short, Vector3>(short.Parse(a[1], CultureInfo.InvariantCulture), new Vector3(F(a[2]), F(a[3]), F(a[4])));
                    (a[0] == "room" ? c.Rooms : c.Portals).Add(e);
                }
            }
            if (c.Version != 1 || c.Rooms.Count == 0) throw new InvalidDataException($"{path}: unknown version {c.Version} or no rooms");
            return c;
        }

        private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
    }
}
