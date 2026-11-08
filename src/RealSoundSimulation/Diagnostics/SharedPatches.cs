using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Audio.SpatialSystem;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Audio;

namespace RealSoundSimulation.Diagnostics
{
    // 0.6.20: which other mods patch the game members this plugin patches or plans to patch, and what the mixer looks like
    // once every plugin has loaded. Written once per raid start (patches.txt in the raid folder, and the latest copy next
    // to selfcheck.txt), because other plugins may install their patches after our Awake ran. Changes no sound.
    //
    // Why: two mods on the same method run in Harmony priority order; a prefix that returns false skips the game's
    // method but not the other mod's prefix, and the last postfix wins. Sound Engine Backport's client, for one, replaces
    // WeaponSoundPlayer.FireBullet and rewrites BetterAudio.Master/Snapshots to its own mixer. Before a raid with another
    // audio mod installed, read this file: a SHARED line names the methods where run order decides the result, and the
    // mixer line says whether fix 3's Outdoor/Indoor snapshot order still holds.
    internal static class SharedPatches
    {
        public const string SebClientGuid = "red.soundenginebackport.client";
        public const string SebGuid = "red.soundenginebackport";

        public static bool Ran { get; private set; }
        public static bool SebPresent { get; private set; }
        public static int SharedCount { get; private set; }
        public static int ForeignCount { get; private set; }
        public static string SharedNames { get; private set; } = "";

        private struct Line { public string Kind; public string Name; public string Fix; public List<string> Detail; }

        public static string Describe()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Real Sound Simulation shared patches  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            SharedCount = ForeignCount = 0; SharedNames = ""; Ran = true;

            // 1. every plugin BepInEx loaded
            try
            {
                var infos = BepInEx.Bootstrap.Chainloader.PluginInfos;
                SebPresent = infos.ContainsKey(SebClientGuid) || infos.ContainsKey(SebGuid);
                sb.AppendLine($"Plugins loaded ({infos.Count}):");
                foreach (var kv in infos.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                {
                    var md = kv.Value?.Metadata;
                    sb.AppendLine($"  {kv.Key,-44} {md?.Name,-36} {md?.Version}");
                }
                sb.AppendLine($"Sound Engine Backport client: {(SebPresent ? "PRESENT" : "absent")}");
            }
            catch (Exception e) { sb.AppendLine("Plugins: could not list: " + e.Message); }

            // 2. the mixer as it is now (another mod may have replaced it)
            sb.AppendLine(MixerLine());
            sb.AppendLine();

            // 3. methods: everything this plugin patched, plus the self-check's method targets (planned fixes too)
            var methods = new Dictionary<MethodBase, string>();
            try
            {
                foreach (MethodBase m in Harmony.GetAllPatchedMethods())
                {
                    Patches p = Harmony.GetPatchInfo(m);
                    if (p != null && p.Owners.Contains(Plugin.Guid)) methods[m] = "";
                }
            }
            catch (Exception e) { sb.AppendLine("Patched methods: could not list: " + e.Message); }
            foreach (var t in PatchSelfCheck.MethodTargets())
                if (!methods.ContainsKey(t.Method) || methods[t.Method] == "") methods[t.Method] = t.Fix;

            var lines = new List<Line>();
            foreach (var kv in methods)
            {
                Patches p;
                try { p = Harmony.GetPatchInfo(kv.Key); } catch { continue; }
                if (p == null) continue;
                bool ours = p.Owners.Contains(Plugin.Guid);
                bool foreign = p.Owners.Any(o => o != Plugin.Guid);
                if (!foreign) continue;
                var l = new Line { Kind = ours ? "SHARED " : "FOREIGN", Name = Name(kv.Key), Fix = kv.Value, Detail = new List<string>() };
                Add(l.Detail, "prefix", p.Prefixes); Add(l.Detail, "postfix", p.Postfixes);
                Add(l.Detail, "transpiler", p.Transpilers); Add(l.Detail, "finalizer", p.Finalizers);
                lines.Add(l);
            }
            lines = lines.OrderBy(l => l.Kind).ThenBy(l => l.Name).ToList();
            SharedCount = lines.Count(l => l.Kind.StartsWith("SHARED"));
            ForeignCount = lines.Count - SharedCount;
            SharedNames = string.Join(", ", lines.Where(l => l.Kind.StartsWith("SHARED")).Select(l => l.Name));

            sb.AppendLine("Methods another mod patches, among those this plugin patches (SHARED) or only plans to (FOREIGN):");
            sb.AppendLine("  (prefixes run highest priority first; a prefix returning false skips the game's method, not other prefixes; the last postfix wins)");
            if (lines.Count == 0) sb.AppendLine("  none: no other mod patches any of these methods");
            foreach (Line l in lines)
            {
                sb.AppendLine($"  {l.Kind}  {l.Name}{(l.Fix != "" ? $"   (self-check fix {l.Fix})" : "")}");
                foreach (string d in l.Detail) sb.AppendLine("      " + d);
            }
            return sb.ToString();
        }

        private static void Add(List<string> into, string kind, IEnumerable<Patch> patches)
        {
            if (patches == null) return;
            foreach (Patch p in patches.OrderByDescending(x => x.priority))
            {
                string who = p.owner == Plugin.Guid ? "ours   " : "foreign";
                string method;
                try { method = p.PatchMethod != null ? $"{p.PatchMethod.DeclaringType?.FullName}.{p.PatchMethod.Name}" : "?"; }
                catch { method = "?"; }
                into.Add($"{who}  {kind,-10} priority {p.priority,4}  {p.owner,-40} {method}");
            }
        }

        private static string Name(MethodBase m)
        {
            string ps = string.Join(", ", m.GetParameters().Select(x => x.ParameterType.Name));
            return $"{m.DeclaringType?.FullName}.{m.Name}({ps})";
        }

        public static string MixerLine()
        {
            try
            {
                if (!MonoBehaviourSingleton<BetterAudio>.Instantiated) return "Mixer: BetterAudio not up yet";
                BetterAudio ba = MonoBehaviourSingleton<BetterAudio>.Instance;
                AudioMixer master = ba.Master;
                var snaps = ba.Snapshots;
                string s = snaps == null ? "null" : string.Join(" ", snaps.Select((x, i) => $"[{i}]={(x != null ? x.name : "null")}"));
                return $"Mixer: Master '{(master != null ? master.name : "null")}'  Snapshots {s}  (fix 3 expects [0]=Outdoor [1]=Indoor)  " +
                       $"GunshotMixerGroup '{ba.GunshotMixerGroup?.name}'  GunshotOccludedMixerGroup '{ba.GunshotOccludedMixerGroup?.name}'  " +
                       $"spatializer '{AudioSettings.GetSpatializerPluginName()}'";
            }
            catch (Exception e) { return "Mixer: could not read: " + e.Message; }
        }

        public static string Summary() => !Ran ? "SHARED PATCHES: not checked (Write logs was off at raid start)\n" :
            $"SHARED PATCHES (patches.txt): SEB client {(SebPresent ? "PRESENT" : "absent")}; methods patched by another mod: {SharedCount} shared with ours" +
            $"{(SharedCount > 0 ? " (" + SharedNames + ")" : "")}, {ForeignCount} on planned targets only\n  {MixerLine()}\n";
    }
}
