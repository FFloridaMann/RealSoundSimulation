using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;

namespace RealSoundSimulation.Diagnostics
{
    // One folder per raid with CSV files. Writes are buffered and flushed every couple of seconds and at raid end.
    internal static class DiagLog
    {
        private static readonly Dictionary<string, StreamWriter> Writers = new Dictionary<string, StreamWriter>();
        private static readonly Dictionary<string, string> Headers = new Dictionary<string, string>
        {
            ["containers.csv"] = "t,id,dist,maxDist,priority,room,roomOutdoor,sameRoomAsListener,prop,obst,occl,gunOccludedMixer,ranT,ranD,ranR,T,D,R,sources,occluder",
            ["shots.csv"] = "t,shooterId,dist,rolloff,silenced,mixer,occl,prop,obst,shooterRoom,listenerRoom,los,beyond250,clock",
            ["timing.csv"] = "t,frames,spatialUpdateAvgMs,spatialUpdateMaxMs,spatialLateAvgMs,spatialLateMaxMs,betterAudioAvgMs,betterAudioMaxMs,activeProcessors,containers,aliveBots,audioBlocks,audioBlockMs,audioGapAvgMs,audioGapMaxMs,audioLate15,audioLate2x,audioStalls,batchWaitAvgMs,batchWaitMaxMs,batchWaitedFrames",
            ["sources.csv"] = "t,betterSources,playing,realVoices,virtualVoices",
            ["probe.csv"] = "t,x,y,z,roomIndoor,doorwayTarget,enclosure,horizontal,overhead,meanHitM,wallClosure,roofClosure,room,sizeFactor,thinShare,zenithClosure",
            ["ownshots.csv"] = "t,kind,source,gameEnvironment,doorwayTarget,enclosure,horizontal,overhead,aimEnclosure,aimNearestM,indoorWeight,applied,reverbBoostDb,wallClosure,roofClosure,room,sizeFactor,thinShare,aimClosure,zenithClosure,muzzleRoom,muzzleRoof,muzzleSize,muzzleFromHeadM,roomUsed,hardShare,hardRoom,glassShare,probe,v2Room,v2MuzzleRoom,v2Size,v2HardRoom,v2Rt60,weightUsed,muzzleRoomSphere,v2MuzzleRoomSphere,tailPlayed,tailLevelDb,tailDelayMs,tailAz,tailThrough,gun",
            ["echoes.csv"] = "t,sample,taps,tap,kind,material,pathM,delayMs,levelDb,directivityDb,cutoffHz,azimuthDeg,elevationDeg,stolen",
            ["marks.csv"] = "t,mark,x,y,z,roomIndoor,doorwayTarget,enclosure,wallClosure,roofClosure,zenithClosure,room,sizeFactor,thinShare,meanHitM,lastOwnShotT,lastIndoorWeight,surfaces,exposureOpen,exposureGlass,exposureOther,openRays,glassRays,blockedRays,nearestOpenM,nearestGlassM,roomOutdoorFloor,outdoorValue,ceilingM,ceilingMat,floorsAbove,roofMat,v2Level,v2CutoffHz,v2Focus,v2SkyOpen,v2Coverage,v2Room,v2Rt60",
            ["exposure.csv"] = Acoustics.OutdoorExposure.Header,
            ["gunsounds.csv"] = Fixes.GunSoundSource.Header,
            ["probe2.csv"] = Acoustics.AcousticProbe.Header,
            ["paths.csv"] = Acoustics.AcousticProbe.PathsHeader,
            ["rain.csv"] = "t,environment,intensity,clip,targetVolume,gameWouldSilence,fixOn",
            ["rain2.csv"] = Fixes.AmbienceMix.RainHeader,
            ["distant.csv"] = Fixes.DistantThroughOpenings.Header,
            ["roomtone.csv"] = Fixes.RoomTone.Header,
            ["spawns.csv"] = "t,clock,aliveBots,delta",
            ["botshots.csv"] = BotShots.Header,
            ["blastmix.csv"] = Fixes.ConcussionProof.MixHeader, // 0.7.22
            ["headsetmix.csv"] = Fixes.ConcussionProof.HeadsetHeader, // 0.7.22
            ["mixer.csv"] = Fixes.MixerOwner.Header, // 13 Nov 2026
            ["botechoes.csv"] = Fixes.BotEchoes.Header,
            ["voices.csv"] = Fixes.BotEchoes.VoicesHeader,
            ["routes.csv"] = RouteLog.Header,
            ["thump.csv"] = Fixes.ThroughWall.Header,
            ["blast.csv"] = Fixes.BlastFromOpening.Header,
            ["blastrec.csv"] = Fixes.BlastRecording.Header,
            ["shock.csv"] = Fixes.ShockWave.Header,
            ["fragments.csv"] = Fixes.BlastFromOpening.FragmentHeader,
            ["passby.csv"] = Fixes.FragmentPassBy.Header,
            ["blastlevel.csv"] = Fixes.BlastRecording.LevelHeader,
            ["concussion.csv"] = Fixes.ConcussionProof.Header,
            ["concdose.csv"] = Fixes.ConcussionProof.DoseHeader,
            ["glass.csv"] = Fixes.BlastFromOpening.GlassHeader,
            ["slowframes.csv"] = "t,clock,frameMs,batchWaitMs,muzzleWaitMs,rays,aliveBots", // 0.6.20: rows describe the previous frame
            ["rays.csv"] = "t,mark,ray,azimuthDeg,elevationDeg,wallM,wallCollider,outsideAtM,path,betweenCollider,lastSampleRoom,lastSampleM,weight,roofOverOutside,yourCeiling",
            ["transitions.csv"] = "t,system,now,x,y,z,fadeS,room",
            ["ambience.csv"] = "t,x,y,z,snapshotIndoor,roomIndoor,outdoorAmbience,doorwayTarget,doorwayApplied,doorwayPortals,portalId,throughPlaneM,sidewaysM,exposureApplied,indoorLayers,bedPan,bedCutoffHz,roofRainFactor,openingShare,openingVoices,opening1Az,opening1Gain",
        };
        private static float _lastFlush;

        // 0.7.0: in the SPT folder itself, next to EscapeFromTarkov.exe, so players can find it to send a bug report.
        public const string FolderName = "RealSoundSimulation Logs";
        public static string RootDir => Path.Combine(BepInEx.Paths.GameRootPath, FolderName);
        public static string RaidDir { get; private set; }
        // 0.6.15: "Write logs" is live - switched off in raid, no more rows are built or written (callers check IsOpen);
        // the summary is still written (HasRaidDir)
        public static bool IsOpen => RaidDir != null && Plugin.LogsOn.Value;
        public static bool HasRaidDir => RaidDir != null;

        public static void Open()
        {
            Close();
            RaidDir = Path.Combine(RootDir, "raid_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + (Fixes.Fika.Present ? "_p" + System.Diagnostics.Process.GetCurrentProcess().Id : "")); // 0.7.11: two Fika copies on one PC start raids in the same second
            Directory.CreateDirectory(RaidDir);
        }

        public static void Row(string file, string line)
        {
            if (RaidDir == null || !Plugin.LogsOn.Value) return;
            if (!Writers.TryGetValue(file, out StreamWriter w))
            {
                w = new StreamWriter(Path.Combine(RaidDir, file), false, new UTF8Encoding(false), 1 << 16);
                if (Headers.TryGetValue(file, out string h)) w.WriteLine(h);
                Writers[file] = w;
            }
            w.WriteLine(line);
        }

        // 0.6.19: allocation-free rows for the files written many times a second (containers.csv: up to ~300 rows/s in a big
        // fight). Row(file, string) builds ~25 strings per row; Mono's StringBuilder.Append(int/float) also goes through
        // ToString, so digits are appended by hand. Usage: var sb = Begin(); AppendF(sb, x, 2); sb.Append(','); ... EndRow(file).
        private static readonly StringBuilder RowSb = new StringBuilder(512);
        private static char[] _rowBuf = new char[1024];
        private static readonly char[] Digits = new char[24];
        private static readonly long[] Pow10 = { 1, 10, 100, 1000, 10000, 100000, 1000000 };

        public static StringBuilder Begin() { RowSb.Clear(); return RowSb; }

        public static void AppendI(StringBuilder sb, long v)
        {
            if (v < 0) { sb.Append('-'); v = -v; }
            if (v == 0) { sb.Append('0'); return; }
            int n = 0;
            while (v > 0 && n < Digits.Length) { Digits[n++] = (char)('0' + (int)(v % 10)); v /= 10; }
            while (n > 0) sb.Append(Digits[--n]);
        }

        // Fixed decimals (0..6), invariant, nothing for NaN/infinity (same as F()).
        public static void AppendF(StringBuilder sb, float v, int decimals)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return;
            if (decimals < 0) decimals = 0; else if (decimals > 6) decimals = 6;
            double d = v;
            if (d < 0) { sb.Append('-'); d = -d; }
            long scale = Pow10[decimals];
            long scaled = (long)Math.Round(d * scale);
            AppendI(sb, scaled / scale);
            if (decimals == 0) return;
            sb.Append('.');
            long frac = scaled % scale;
            for (long p = scale / 10; p > 0; p /= 10) sb.Append((char)('0' + (int)(frac / p % 10)));
        }

        public static void AppendB(StringBuilder sb, bool v) => sb.Append(v ? '1' : '0');

        // Same quoting as Csv(), without the copy when there is no comma.
        public static void AppendS(StringBuilder sb, string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            if (s.IndexOf(',') < 0) { sb.Append(s); return; }
            sb.Append('"');
            for (int i = 0; i < s.Length; i++) sb.Append(s[i] == '"' ? '\'' : s[i]);
            sb.Append('"');
        }

        public static void EndRow(string file)
        {
            if (RaidDir == null || !Plugin.LogsOn.Value) return;
            if (!Writers.TryGetValue(file, out StreamWriter w))
            {
                w = new StreamWriter(Path.Combine(RaidDir, file), false, new UTF8Encoding(false), 1 << 16);
                if (Headers.TryGetValue(file, out string h)) w.WriteLine(h);
                Writers[file] = w;
            }
            int len = RowSb.Length;
            if (_rowBuf.Length < len) _rowBuf = new char[Math.Max(len, _rowBuf.Length * 2)];
            RowSb.CopyTo(0, _rowBuf, 0, len);
            w.Write(_rowBuf, 0, len);
            w.WriteLine();
        }

        public static void Text(string file, string text)
        {
            if (RaidDir == null) return; // settings.txt and summary.txt: written even with "Write logs" switched off mid-raid
            File.WriteAllText(Path.Combine(RaidDir, file), text);
        }

        public static void MaybeFlush(float now)
        {
            if (now - _lastFlush < 2f) return;
            _lastFlush = now;
            foreach (var w in Writers.Values) w.Flush();
        }

        public static void Close()
        {
            foreach (var w in Writers.Values)
            {
                try { w.Flush(); w.Dispose(); } catch { }
            }
            Writers.Clear();
            RaidDir = null;
        }

        // 0.7.2: every loaded BepInEx plugin (name, version, id) and the server mod folders, by name only (no full paths,
        // so nothing of the player's own folders ends up in a report).
        private static string ModList()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Game {UnityEngine.Application.version}  Real Sound Simulation {Plugin.Version}  {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine();
            sb.AppendLine("BepInEx plugins (name | version | id | folder):");
            try
            {
                var rows = new List<string>();
                foreach (var kv in BepInEx.Bootstrap.Chainloader.PluginInfos)
                {
                    var m = kv.Value.Metadata;
                    string dir = "";
                    try { dir = Path.GetFileName(Path.GetDirectoryName(kv.Value.Location)); } catch { }
                    rows.Add($"  {m.Name} | {m.Version} | {m.GUID} | {dir}");
                }
                rows.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string r in rows) sb.AppendLine(r);
            }
            catch (Exception e) { sb.AppendLine("  (could not list: " + e.Message + ")"); }
            sb.AppendLine();
            sb.AppendLine("Server mods (folder names):");
            foreach (string sub in new[] { @"SPT_Runtime\user\mods", @"SPT\user\mods", @"user\mods" })
            {
                string dir = Path.Combine(BepInEx.Paths.GameRootPath, sub);
                if (!Directory.Exists(dir)) continue;
                try
                {
                    var names = new List<string>();
                    foreach (string d in Directory.GetDirectories(dir)) names.Add(Path.GetFileName(d));
                    names.Sort(StringComparer.OrdinalIgnoreCase);
                    foreach (string n in names) sb.AppendLine("  " + n);
                }
                catch (Exception e) { sb.AppendLine("  (could not list: " + e.Message + ")"); }
                break;
            }
            return sb.ToString();
        }

        // 0.7.0: one zip per raid for bug reports: the raid folder plus the startup self-check, the shared-patch list and any
        // errors. Built on a worker thread after the files are closed, so raid end doesn't hitch.
        public static void ZipForBugReport(string raidDir)
        {
            if (raidDir == null || !Directory.Exists(raidDir)) return;
            string root = RootDir;
            string zip = Path.Combine(root, "Bug report " + Path.GetFileName(raidDir).Replace("raid_", "") + ".zip");
            string mods = ModList(); // read here, on the main thread
            string bepinexLog = Path.Combine(BepInEx.Paths.BepInExRootPath, "LogOutput.log");
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    if (File.Exists(zip)) File.Delete(zip);
                    using (var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
                    {
                        string folder = Path.GetFileName(raidDir);
                        foreach (string f in Directory.GetFiles(raidDir))
                            System.IO.Compression.ZipFileExtensions.CreateEntryFromFile(archive, f, folder + "/" + Path.GetFileName(f));
                        foreach (string name in new[] { "selfcheck.txt", "patches.txt", "errors.txt" })
                        {
                            string f = Path.Combine(root, name);
                            if (File.Exists(f)) System.IO.Compression.ZipFileExtensions.CreateEntryFromFile(archive, f, name);
                        }
                        // 0.7.2: the player's own setup, which differs from the developer's
                        using (var w = new StreamWriter(archive.CreateEntry("mods.txt").Open(), new UTF8Encoding(false)))
                            w.Write(mods);
                        if (File.Exists(bepinexLog))
                        {
                            // BepInEx keeps this file open while the game runs, so read it with sharing allowed
                            using (var src = new FileStream(bepinexLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                            using (var dst = archive.CreateEntry("LogOutput.log").Open())
                                src.CopyTo(dst);
                        }
                    }
                }
                catch (Exception e) { Error("bug report zip failed: " + e.Message); }
            });
        }

        // Plugin errors also go to RealSoundSimulation Logs/errors.txt, because BepInEx's LogOutput.log is not always readable.
        public static void Error(string text)
        {
            try
            {
                Directory.CreateDirectory(RootDir);
                File.AppendAllText(Path.Combine(RootDir, "errors.txt"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {text}{Environment.NewLine}");
            }
            catch { }
        }

        // Invariant-culture number for CSV.
        public static string F(float v, string fmt = "0.###") => float.IsNaN(v) ? "" : v.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);

        public static string Csv(string s) => s == null ? "" : (s.IndexOf(',') >= 0 ? "\"" + s.Replace("\"", "'") + "\"" : s);
    }
}
