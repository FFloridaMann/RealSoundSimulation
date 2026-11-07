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
            ["shots.csv"] = "t,shooterId,dist,rolloff,silenced,mixer,occl,prop,obst,shooterRoom,listenerRoom,los,beyond250",
            ["timing.csv"] = "t,frames,spatialUpdateAvgMs,spatialUpdateMaxMs,spatialLateAvgMs,spatialLateMaxMs,betterAudioAvgMs,betterAudioMaxMs,activeProcessors,containers",
            ["sources.csv"] = "t,betterSources,playing,realVoices,virtualVoices",
            ["probe.csv"] = "t,x,y,z,roomIndoor,doorwayTarget,enclosure,horizontal,overhead,meanHitM,wallClosure,roofClosure,room,sizeFactor,thinShare,zenithClosure",
            ["ownshots.csv"] = "t,kind,source,gameEnvironment,doorwayTarget,enclosure,horizontal,overhead,aimEnclosure,aimNearestM,indoorWeight,applied,reverbBoostDb,wallClosure,roofClosure,room,sizeFactor,thinShare,aimClosure,zenithClosure,muzzleRoom,muzzleRoof,muzzleSize,muzzleFromHeadM,roomUsed,hardShare,hardRoom,glassShare",
            ["echoes.csv"] = "t,sample,taps,tap,kind,material,pathM,delayMs,levelDb,directivityDb,cutoffHz,azimuthDeg,elevationDeg,stolen",
            ["marks.csv"] = "t,mark,x,y,z,roomIndoor,doorwayTarget,enclosure,wallClosure,roofClosure,zenithClosure,room,sizeFactor,thinShare,meanHitM,lastOwnShotT,lastIndoorWeight,surfaces,exposureOpen,exposureGlass,exposureOther,openRays,glassRays,blockedRays,nearestOpenM,nearestGlassM,roomOutdoorFloor,outdoorValue,ceilingM,ceilingMat,floorsAbove,roofMat",
            ["exposure.csv"] = Acoustics.OutdoorExposure.Header,
            ["rain.csv"] = "t,environment,intensity,clip,targetVolume,gameWouldSilence,fixOn",
            ["rays.csv"] = "t,mark,ray,azimuthDeg,elevationDeg,wallM,wallCollider,outsideAtM,path,betweenCollider,lastSampleRoom,lastSampleM,weight,roofOverOutside,yourCeiling",
            ["transitions.csv"] = "t,system,now,x,y,z,fadeS,room",
            ["ambience.csv"] = "t,x,y,z,snapshotIndoor,roomIndoor,outdoorAmbience,doorwayTarget,doorwayApplied,doorwayPortals,portalId,throughPlaneM,sidewaysM,exposureApplied,indoorLayers,bedPan,bedCutoffHz,roofRainFactor,openingShare,openingVoices,opening1Az,opening1Gain",
        };
        private static float _lastFlush;

        public static string RootDir => Path.Combine(BepInEx.Paths.BepInExRootPath, "RealSoundSimulationLogs");
        public static string RaidDir { get; private set; }
        public static bool IsOpen => RaidDir != null;

        public static void Open()
        {
            Close();
            RaidDir = Path.Combine(RootDir, "raid_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(RaidDir);
        }

        public static void Row(string file, string line)
        {
            if (RaidDir == null) return;
            if (!Writers.TryGetValue(file, out StreamWriter w))
            {
                w = new StreamWriter(Path.Combine(RaidDir, file), false, new UTF8Encoding(false), 1 << 16);
                if (Headers.TryGetValue(file, out string h)) w.WriteLine(h);
                Writers[file] = w;
            }
            w.WriteLine(line);
        }

        public static void Text(string file, string text)
        {
            if (RaidDir == null) return;
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

        // Plugin errors also go to RealSoundSimulationLogs/errors.txt, because BepInEx's LogOutput.log is not always readable.
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
