using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using RealSoundSimulation.Diagnostics;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace RealSoundSimulation.Fixes
{
    internal enum GunSoundSet { SPT, SEB }

    // Setting 0: gun sounds from SPT or from Sound Engine Backport (SEB, EFT 1.0 recordings).
    // SEB's banks have the same names as SPT's (akm_main, akm_tail...) but hold 6 to 48 single-shot takes where SPT
    // holds one loop. tools/build_seb_sounds.py copies them with new internal names into seb_sounds/ so they can load
    // next to SPT's. Here, on each trigger pull (never during a burst), a gun's six banks are swapped to SEB's by
    // name, or back. Guns that can fire full-auto stay on SPT unless 0a is on: the game loops the Body clip for them,
    // and a single-shot take looped is not a burst. SEB bundles load in the background the first time they're needed;
    // until then the gun keeps SPT's. Logged to gunsounds.csv.
    internal static class GunSoundSource
    {
        public const string Header = "t,player,firstPerson,gun,auto,set,reason,banksSwapped";

        private static readonly AccessTools.FieldRef<WeaponSoundPlayer, bool> IsFiringRef =
            AccessTools.FieldRefAccess<WeaponSoundPlayer, bool>("_isFiring");
        private static readonly AccessTools.FieldRef<WeaponSoundPlayer, float> LoopLengthRef =
            AccessTools.FieldRefAccess<WeaponSoundPlayer, float>("_firingLoopLength");
        private static readonly AccessTools.FieldRef<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge> BridgeRef =
            AccessTools.FieldRefAccess<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge>("playersBridge");

        private sealed class GunState
        {
            public SoundBank[] Spt;   // Body, BodySilenced, Tail, TailSilenced, Doublet, DoubletSilenced as the game set them
            public bool OnSeb;
            public string LastReason;
        }

        private static readonly ConditionalWeakTable<WeaponSoundPlayer, GunState> Guns = new ConditionalWeakTable<WeaponSoundPlayer, GunState>();

        // seb_sounds/index.txt
        private static string _dir;
        private static readonly Dictionary<string, string> BankFile = new Dictionary<string, string>();
        private static readonly Dictionary<string, string[]> Deps = new Dictionary<string, string[]>();

        private enum Load { None, Loading, Loaded, Failed }
        private static readonly Dictionary<string, Load> BundleState = new Dictionary<string, Load>();
        private static readonly Dictionary<string, AssetBundle> Bundles = new Dictionary<string, AssetBundle>();
        private static readonly Dictionary<string, Load> AssetState = new Dictionary<string, Load>();   // per bank bundle
        private static readonly Dictionary<string, SoundBank> SebBanks = new Dictionary<string, SoundBank>();

        public static string IndexProblem { get; private set; }
        public static int SwapsToSeb, SwapsToSpt, BundlesLoaded, LoadFailures;

        public static void Apply(Harmony h)
        {
            ReadIndex();
            h.Patch(AccessTools.Method(typeof(WeaponSoundPlayer), nameof(WeaponSoundPlayer.FireBullet)),
                prefix: new HarmonyMethod(typeof(GunSoundSource), nameof(FirePrefix)) { priority = Priority.First });
        }

        public static void Reset()
        {
            SwapsToSeb = SwapsToSpt = 0;
        }

        private static void ReadIndex()
        {
            _dir = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? "", "seb_sounds");
            string index = Path.Combine(_dir, "index.txt");
            if (!File.Exists(index))
            {
                IndexProblem = $"{index} not found: run tools/build_seb_sounds.py. Gun sounds stay SPT.";
                Plugin.Log.LogWarning(IndexProblem);
                return;
            }
            foreach (string raw in File.ReadAllLines(index))
            {
                if (raw.Length == 0 || raw[0] == '#') continue;
                string[] p = raw.Split('\t');
                if (p.Length < 3) continue;
                if (p[0] == "bundle") Deps[p[1]] = p[2] == "-" ? new string[0] : p[2].Split(',');
                else if (p[0] == "bank") BankFile[p[1]] = p[2];
            }
            Plugin.Log.LogInfo($"SEB gun sounds: {BankFile.Count} banks in {Deps.Count} bundles indexed");
        }

        // Runs first on every shot. Never skips the original.
        private static void FirePrefix(WeaponSoundPlayer __instance)
        {
            try
            {
                if (IsFiringRef(__instance)) return; // mid-burst: never change banks
                if (!Guns.TryGetValue(__instance, out GunState g))
                {
                    if (Plugin.GunSounds.Value == GunSoundSet.SPT) return; // never touched, nothing to restore
                    g = new GunState { Spt = Get(__instance) };
                    Guns.Add(__instance, g);
                }

                string reason;
                bool wantSeb = Plugin.GunSounds.Value == GunSoundSet.SEB;
                if (!wantSeb) reason = "setting";
                else if (__instance.IsAutoWeapon && !Plugin.SebOnFullAuto.Value) { wantSeb = false; reason = "full-auto gun (0a off)"; }
                else reason = SebReady(g.Spt);
                if (wantSeb && reason != null) wantSeb = false;

                if (wantSeb && !g.OnSeb)
                {
                    int n = Set(__instance, g.Spt, true);
                    g.OnSeb = true; SwapsToSeb++;
                    Note(__instance, g, "SEB", "", n);
                }
                else if (!wantSeb && g.OnSeb)
                {
                    Set(__instance, g.Spt, false);
                    g.OnSeb = false; SwapsToSpt++;
                    Note(__instance, g, "SPT", reason, 0);
                }
                else if (!wantSeb && reason != g.LastReason && reason != "setting")
                {
                    Note(__instance, g, "SPT", reason, 0); // e.g. "SEB loading", once per reason
                }
            }
            catch (Exception e)
            {
                DiagLog.Error($"gun sound source: {e}");
            }
        }

        private static SoundBank[] Get(WeaponSoundPlayer w) =>
            new[] { w.Body, w.BodySilenced, w.Tail, w.TailSilenced, w.Doublet, w.DoubletSilenced };

        // null = every bank this gun has is loaded from SEB (or SEB has no version of a secondary one); else why not.
        private static string SebReady(SoundBank[] spt)
        {
            if (IndexProblem != null) return "seb_sounds not built";
            if (spt[0] == null || !BankFile.ContainsKey(spt[0].name)) return "not in SEB";
            string waiting = null;
            foreach (SoundBank b in spt)
            {
                if (b == null || !BankFile.TryGetValue(b.name, out string file)) continue;
                if (SebBanks.ContainsKey(b.name)) continue;
                AssetState.TryGetValue(file, out Load s);
                if (s == Load.Failed) return "SEB load failed";
                Request(file);
                waiting = "SEB loading";
            }
            return waiting;
        }

        private static int Set(WeaponSoundPlayer w, SoundBank[] spt, bool seb)
        {
            var v = new SoundBank[6];
            int swapped = 0;
            for (int i = 0; i < 6; i++)
            {
                v[i] = spt[i];
                if (seb && spt[i] != null && SebBanks.TryGetValue(spt[i].name, out SoundBank s) && s != null) { v[i] = s; swapped++; }
            }
            w.Body = v[0]; w.BodySilenced = v[1]; w.Tail = v[2]; w.TailSilenced = v[3]; w.Doublet = v[4]; w.DoubletSilenced = v[5];
            // as the game does when the silencer changes (WeaponSoundPlayer.IsSilenced setter)
            SoundBank body = w.IsSilenced ? (w.BodySilenced ?? w.Body) : w.Body;
            if (body != null) LoopLengthRef(w) = body.ClipLength;
            return swapped;
        }

        private static void Note(WeaponSoundPlayer w, GunState g, string set, string reason, int swapped)
        {
            g.LastReason = reason;
            if (!DiagLog.IsOpen) return;
            bool fp = false;
            string player = "";
            try
            {
                BaseSoundPlayer.IObserverToPlayerBridge b = BridgeRef(w);
                if (b != null) { fp = b.PointOfView == EPointOfView.FirstPerson; player = b.iPlayer?.Id.ToString() ?? ""; }
            }
            catch { }
            DiagLog.Row("gunsounds.csv", string.Join(",", DiagLog.F(Time.time - DiagRunner.RaidStartTime, "0.00"), player, fp ? "1" : "0",
                DiagLog.Csv(g.Spt[0] != null ? g.Spt[0].name : w.name), w.IsAutoWeapon ? "1" : "0", set, DiagLog.Csv(reason), swapped.ToString()));
        }

        // ---------------- background loading ----------------

        private static void Request(string file)
        {
            if (AssetState.TryGetValue(file, out Load s) && s != Load.None) return;
            AssetState[file] = Load.Loading;
            if (Deps.TryGetValue(file, out string[] deps))
                foreach (string d in deps) LoadBundle(d);
            LoadBundle(file);
            TryLoadAssets(file);
        }

        private static void LoadBundle(string file)
        {
            if (BundleState.TryGetValue(file, out Load s) && s != Load.None) return;
            BundleState[file] = Load.Loading;
            AssetBundleCreateRequest req = AssetBundle.LoadFromFileAsync(Path.Combine(_dir, file));
            req.completed += _ =>
            {
                if (req.assetBundle == null)
                {
                    BundleState[file] = Load.Failed; LoadFailures++;
                    string msg = $"SEB gun sounds: could not load {file} (guns that need it stay SPT)";
                    Plugin.Log.LogError(msg); DiagLog.Error(msg);
                }
                else { Bundles[file] = req.assetBundle; BundleState[file] = Load.Loaded; BundlesLoaded++; }
                foreach (string bankFile in new List<string>(AssetState.Keys))
                    if (AssetState[bankFile] == Load.Loading) TryLoadAssets(bankFile);
            };
        }

        // Banks are loaded once their bundle and every bundle it references are loaded (Unity resolves the clips then).
        private static void TryLoadAssets(string file)
        {
            var need = new List<string>();
            if (Deps.TryGetValue(file, out string[] deps)) need.AddRange(deps);
            need.Add(file);
            foreach (string f in need)
            {
                BundleState.TryGetValue(f, out Load s);
                if (s == Load.Failed) { AssetState[file] = Load.Failed; return; }
                if (s != Load.Loaded) return;
            }
            AssetState[file] = Load.Loaded; // assets requested; banks register when done
            AssetBundleRequest r = Bundles[file].LoadAllAssetsAsync<SoundBank>();
            r.completed += _ =>
            {
                int n = 0;
                foreach (UnityEngine.Object o in r.allAssets)
                    if (o is SoundBank sb && Usable(sb)) { SebBanks[sb.name] = sb; n++; }
                Plugin.Log.LogInfo($"SEB gun sounds: {file} ready, {n} banks");
                if (n == 0) AssetState[file] = Load.Failed;
            };
        }

        // A bank with a clip that didn't resolve would play silence; skip it (the gun keeps SPT's version of that bank).
        private static bool Usable(SoundBank b)
        {
            if (b.Environments == null || b.Environments.Length == 0) return Skip(b, "no environments");
            foreach (EnvironmentVariety env in b.Environments)
            {
                if (env?.Clips == null) continue;
                foreach (DistanceVarity list in env.Clips)
                    if (list?.Clips != null)
                        foreach (AudioClip c in list.Clips)
                            if (c == null) return Skip(b, "a clip did not resolve");
            }
            return true;
        }

        private static bool Skip(SoundBank b, string why)
        {
            string msg = $"SEB gun sounds: bank {b.name} skipped ({why})";
            Plugin.Log.LogWarning(msg); DiagLog.Error(msg);
            return false;
        }

        public static string Summary() =>
            $"GUN SOUNDS (setting 0): {Plugin.GunSounds.Value}{(Plugin.SebOnFullAuto.Value ? " incl. full-auto guns" : "")} | " +
            $"SEB banks loaded {SebBanks.Count}/{BankFile.Count}, bundles {BundlesLoaded}, load failures {LoadFailures} | " +
            $"guns switched to SEB {SwapsToSeb}, back to SPT {SwapsToSpt}{(IndexProblem != null ? " | " + IndexProblem : "")}\n";
    }
}
