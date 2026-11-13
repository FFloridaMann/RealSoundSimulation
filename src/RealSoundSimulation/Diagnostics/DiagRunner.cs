using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Audio.SpatialSystem;
using Comfort.Common;
using Audio.SpatialSystem.SpatialAudioCalculator.CombinedOcclusionCalculator;
using EFT;
using HarmonyLib;
using UnityEngine;

using RealSoundSimulation.Fixes;

namespace RealSoundSimulation.Diagnostics
{
    // Watches the game's spatial audio system while in raid: logs every sound container's occlusion state, counts voices,
    // dumps each audio group's rolloff curve the first time it's seen, times the audio systems, and writes a summary.
    internal sealed class DiagRunner : MonoBehaviour
    {
        private static readonly AccessTools.FieldRef<SpatialAudioSystem, Dictionary<int, AudioSourceContainer>> ContainersRef =
            AccessTools.FieldRefAccess<SpatialAudioSystem, Dictionary<int, AudioSourceContainer>>("_allRegisteredSoundContainers");
        private static readonly AccessTools.FieldRef<SpatialAudioSystem, Dictionary<int, SoundOcclusionProcessor>> ProcessorsRef =
            AccessTools.FieldRefAccess<SpatialAudioSystem, Dictionary<int, SoundOcclusionProcessor>>("_allRegisteredProcessorsByContainerID");
        private static readonly AccessTools.FieldRef<SpatialAudioSystem, List<SoundOcclusionProcessor>> ActiveRef =
            AccessTools.FieldRefAccess<SpatialAudioSystem, List<SoundOcclusionProcessor>>("_activeProcessors");
        private static readonly AccessTools.FieldRef<BetterSource, AudioGroupPreset> PresetRef =
            AccessTools.FieldRefAccess<BetterSource, AudioGroupPreset>("Preset");

        private static readonly HashSet<string> Reported = new HashSet<string>();

        private float _raidStart, _nextContainerLog, _nextScan, _nextTiming, _nextOverlay;
        private int _lastBots = -1, _maxBots, _spawned; // 0.6.18
        internal static float LastSpawnChange = float.NegativeInfinity; // 0.6.19: Time.time the alive-bot count last changed
        private int _backoffFrames; // 0.6.19: frames the diagnostics skipped after a spawn
        internal static int LastAliveBots = -1; // 0.6.20: for slowframes.csv
        private AudioThreadProbe _audioProbe; private float _nextAudioProbeTry; // 0.6.20

        // Everyone alive except you; -1 if the game world isn't there.
        private static int AliveBots()
        {
            if (!Singleton<GameWorld>.Instantiated) return -1;
            GameWorld gw = Singleton<GameWorld>.Instance;
            if (gw?.AllAlivePlayersList == null) return -1;
            int n = gw.AllAlivePlayersList.Count;
            Player me = GamePlayerOwner.MyPlayer;
            if (me != null && gw.AllAlivePlayersList.Contains(me)) n--;
            return n;
        }
        private int _framesInWindow, _maxActive, _maxContainers, _maxPlaying, _maxBetterSources, _maxContainerPlaying;
        public static int LastPlayingInContainers; // 0.6.16: game sources playing in sound containers at the last sample (0.5 s)
        private long _activeSum, _activeSamples;
        private int _containerRows, _rowsTierLow, _rowsTierMed, _rowsTierHigh, _rowsPropagated, _rowsGunOccluded;
        private readonly HashSet<string> _dumpedSourceKeys = new HashSet<string>();
        private readonly Dictionary<string, SortedDictionary<int, int>> _maxDistByGroup = new Dictionary<string, SortedDictionary<int, int>>();
        private readonly StringBuilder _sb = new StringBuilder(4096);
        private string _overlayText = "";
        private GUIStyle _style;
        private readonly List<Row> _rows = new List<Row>(256);

        private string _fixStateAtStart = "";
        internal static float RaidStartTime => Raid.StartTime;
        private Action _onStart, _onEnd;

        private void Awake()
        {
            _onStart = () => Plugin.Safe("raid start (logs)", RaidStart);
            _onEnd = () => Plugin.Safe("raid end (logs)", RaidEnd);
        }

        // Raid decides when a raid starts and ends; this only opens and closes the logs.
        private void OnEnable() { Raid.Started += _onStart; Raid.Ended += _onEnd; }
        private void OnDisable() { Raid.Started -= _onStart; Raid.Ended -= _onEnd; }

        private struct Row
        {
            public int Id, Sources; public float Dist, MaxDist, Prop, Obst, Occl, T, D, R; public string Prio, Room, Occluder;
            public bool Outdoor, SameRoom, RanT, RanD, RanR, HasCalc;
        }

        internal static void ReportOnce(string what, Exception e)
        {
            if (!Reported.Add(what)) return;
            Plugin.Log.LogWarning($"Diagnostics: {what} failed once (further errors hidden): {e}");
            DiagLog.Error($"{what} failed once (further errors hidden): {e}");
        }

        private void Update()
        {
            if (Plugin.OverlayKey.Value.IsDown()) Plugin.OverlayOn.Value = !Plugin.OverlayOn.Value;

            if (!Raid.IsOn) return;
            if (Plugin.MarkKey.Value.IsDown()) Plugin.Safe("mark", Mark);

            float now = Time.time;
            _framesInWindow++;
            bool backoff = now - LastSpawnChange < Plugin.SpawnBackoffS.Value; // 0.6.19: a spawn wave is making frames long; add nothing
            if (backoff) _backoffFrames++;
            Perf.Begin();
            try { Tick(now, backoff); }
            catch (Exception e) { ReportOnce("tick", e); }
            finally { Perf.End(Perf.Part.DiagTick); }
            try { AudioCounters.Sample(); } // 0.6.20: one read per counter, also during the back-off (spawn frames are the ones to see)
            catch (Exception e) { ReportOnce("audio counters", e); }
            if (backoff) return;
            Perf.Begin();
            try { EnvProbe.Tick(now); }
            catch (Exception e) { ReportOnce("env probe (tick)", e); }
            finally { Perf.End(Perf.Part.EnvLog); }
            Perf.Begin();
            try { DiagLog.MaybeFlush(Time.realtimeSinceStartup); }
            finally { Perf.End(Perf.Part.LogFlush); }
        }

        // Mark key (0.3.1): "this spot sounds wrong". One row in marks.csv with the position, what the probe measures now,
        // the last own shot and the surfaces the probe rays hit (material:count), plus a line in the BepInEx log.
        private int _marks;
        private void Mark()
        {
            _marks++;
            Vector3 p = Acoustics.EnclosureProbe.LastOrigin;
            var counts = new Dictionary<string, int>();
            for (int i = 0; i < Acoustics.EnclosureProbe.RayCount; i++)
            {
                if (float.IsInfinity(Acoustics.EnclosureProbe.HitDist[i]) || Acoustics.EnclosureProbe.IsGround(Acoustics.EnclosureProbe.HitNormal[i])) continue;
                string m = Acoustics.EnclosureProbe.HitMat[i].ToString();
                counts.TryGetValue(m, out int c); counts[m] = c + 1;
            }
            string surfaces = string.Join(" ", counts.Select(kv => $"{kv.Key}:{kv.Value}"));
            DiagLog.Row("marks.csv", string.Join(",",
                DiagLog.F(Time.time - _raidStart, "0.00"), _marks.ToString(), DiagLog.F(p.x, "0.00"), DiagLog.F(p.y, "0.00"), DiagLog.F(p.z, "0.00"),
                DoorwayBlend.TargetOutdoorness < 0.5f ? "1" : "0", DiagLog.F(DoorwayBlend.TargetOutdoorness),
                DiagLog.F(Acoustics.EnclosureProbe.Enclosure), DiagLog.F(Acoustics.EnclosureProbe.HorizClosure), DiagLog.F(Acoustics.EnclosureProbe.OverClosure),
                DiagLog.F(Acoustics.EnclosureProbe.ZenithClosure), DiagLog.F(Acoustics.EnclosureProbe.Room), DiagLog.F(Acoustics.EnclosureProbe.SizeFactor), DiagLog.F(Acoustics.EnclosureProbe.ThinShare),
                DiagLog.F(Acoustics.EnclosureProbe.MeanHit, "0.0"),
                float.IsNaN(GunEnvironmentBlend.LastShotTime) ? "" : DiagLog.F(GunEnvironmentBlend.LastShotTime - _raidStart, "0.00"),
                DiagLog.F(GunEnvironmentBlend.LastIndoorWeight), surfaces,
                DiagLog.F(Acoustics.OutdoorExposure.ExposureOpen), DiagLog.F(Acoustics.OutdoorExposure.ExposureGlass), DiagLog.F(Acoustics.OutdoorExposure.ExposureOther),
                Acoustics.OutdoorExposure.OpenN.ToString(), Acoustics.OutdoorExposure.GlassN.ToString(), Acoustics.OutdoorExposure.BlockedN.ToString(),
                DiagLog.F(Acoustics.OutdoorExposure.NearestOpenM, "0.0"), DiagLog.F(Acoustics.OutdoorExposure.NearestGlassM, "0.0"),
                DiagLog.F(Acoustics.OutdoorExposure.RoomFloor), DiagLog.F(Acoustics.OutdoorExposure.Value),
                DiagLog.F(Acoustics.OutdoorExposure.CeilingM, "0.0"), Acoustics.OutdoorExposure.CeilingMat.ToString(),
                Acoustics.OutdoorExposure.FloorsAbove.ToString(), Acoustics.OutdoorExposure.RoofMat.ToString(),
                DiagLog.F(Acoustics.AcousticProbe.Level), DiagLog.F(Acoustics.AcousticProbe.CutoffHz, "0"), DiagLog.F(Acoustics.AcousticProbe.Focus),
                DiagLog.F(Acoustics.AcousticProbe.SkyOpen), DiagLog.F(Acoustics.AcousticProbe.Coverage), DiagLog.F(Acoustics.AcousticProbe.Room),
                DiagLog.F(Acoustics.AcousticProbe.Rt60, "0.00")));
            Acoustics.OutdoorExposure.DumpRays(_marks, Time.time - _raidStart);
            Acoustics.AcousticProbe.DumpPaths(_marks, Time.time - _raidStart);
            Plugin.Log.LogInfo($"Mark {_marks} at {p} (room {Acoustics.EnclosureProbe.Room:0.00}, surfaces {surfaces})");
        }

        // ---------------- raid lifecycle ----------------

        private void RaidStart()
        {
            _raidStart = Raid.StartTime;
            _nextContainerLog = _nextScan = _nextTiming = _nextOverlay = 0f;
            _framesInWindow = _maxActive = _maxContainers = _maxPlaying = _maxBetterSources = _maxContainerPlaying = 0; LastPlayingInContainers = 0;
            _lastBots = -1; _maxBots = 0; _spawned = 0; LastSpawnChange = float.NegativeInfinity; _backoffFrames = 0;
            _activeSum = _activeSamples = 0;
            _containerRows = _rowsTierLow = _rowsTierMed = _rowsTierHigh = _rowsPropagated = _rowsGunOccluded = 0;
            _dumpedSourceKeys.Clear();
            _maxDistByGroup.Clear();
            ShotProbe.Reset();
            EnvProbe.Reset(_raidStart);
            _fixStateAtStart = Plugin.FixState();
            Perf.Reset(Time.realtimeSinceStartup);
            TimingPatches.SpatialUpdate.ResetAll(); TimingPatches.SpatialLate.ResetAll(); TimingPatches.BetterAudioUpdate.ResetAll();
            _nextAudioProbeTry = 0f; Plugin.Safe("audio thread probe", AttachAudioProbe); // 0.6.20
            BatchWait.Reset(); AudioCounters.Stop(); // 0.6.20
            if (!Plugin.LogsOn.Value) return;

            DiagLog.Open();
            if (Plugin.AudioThreadTimingOn.Value) Plugin.Safe("audio counters", AudioCounters.RaidStart); // 0.6.20: after Open (writes into the raid folder)
            Plugin.Safe("shared patches", () => // 0.6.20: once every plugin has loaded (other mods patch after our Awake)
            {
                string report = SharedPatches.Describe();
                DiagLog.Text("patches.txt", report);
                System.IO.File.WriteAllText(System.IO.Path.Combine(DiagLog.RootDir, "patches.txt"), report);
            });
            SpatialAudioSystem sas = MonoBehaviourSingleton<SpatialAudioSystem>.Instance;
            var cfg = AudioSettings.GetConfiguration();
            var sb = new StringBuilder();
            sb.AppendLine($"Raid start {DateTime.Now:yyyy-MM-dd HH:mm:ss}  Unity {Application.unityVersion}  game {Application.version}");
            sb.AppendLine($"CPU logical cores: {SystemInfo.processorCount}  -> BSG audio quality tier: {AudioUtils.GetQualityByLogicCores()}");
            sb.AppendLine($"Audio: {cfg.sampleRate} Hz, dspBuffer {cfg.dspBufferSize}, speakers {cfg.speakerMode}, realVoices {cfg.numRealVoices}, virtualVoices {cfg.numVirtualVoices}");
            sb.AppendLine("Fixes at raid start: " + Plugin.FixState());
            sb.AppendLine($"Spatializer plugin: '{AudioSettings.GetSpatializerPluginName()}'");
            if (sas.LocationInfo != null)
            {
                var li = sas.LocationInfo;
                sb.AppendLine($"Location bake: {li.relativeBakeDataPath}  rooms {li.roomsCount}  portals {li.portalsCount}  routes {li.routesCount}  roomPairs {li.roomPairsCount}  maxRoutes {li.maxRoutesCount}");
            }
            sb.AppendLine();
            sb.AppendLine("Effective occlusion settings (after the server's values were applied):");
            sb.AppendLine(sas.OcclusionSettings != null ? JsonUtility.ToJson(sas.OcclusionSettings, true) : "(none)");
            DiagLog.Text("settings.txt", sb.ToString());
            Plugin.Log.LogInfo("Diagnostics: logging raid to " + DiagLog.RaidDir);
        }

        private void RaidEnd()
        {
            string raidDir = DiagLog.RaidDir;
            if (DiagLog.HasRaidDir) DiagLog.Text("summary.txt", BuildSummary());
            DiagLog.Close();
            if (raidDir != null && Plugin.LogsOn.Value) DiagLog.ZipForBugReport(raidDir); // 0.7.0
            Plugin.Safe("audio counters", AudioCounters.Stop); // 0.6.20
            _overlayText = "";
            if (_audioProbe != null) { Destroy(_audioProbe); _audioProbe = null; } // 0.6.20
        }

        // 0.6.19: the audio-thread probe lives on the AudioListener's object (OnAudioFilterRead only runs there). The
        // listener can change mid-raid (camera swaps), so it is re-attached if it goes missing, at most every 5 s.
        private void AttachAudioProbe()
        {
            if (!Plugin.AudioThreadTimingOn.Value) return;
            if (_audioProbe != null) return;
            AudioListener l = FindObjectOfType<AudioListener>();
            if (l == null) return;
            _audioProbe = l.gameObject.AddComponent<AudioThreadProbe>();
            _audioProbe.Init(AudioSettings.GetConfiguration().sampleRate);
        }

        // ---------------- per-frame work ----------------

        private void Tick(float now, bool backoff)
        {
            SpatialAudioSystem sas = MonoBehaviourSingleton<SpatialAudioSystem>.Instance;
            List<SoundOcclusionProcessor> active = ActiveRef(sas);
            int activeCount = active?.Count ?? 0;
            _activeSum += activeCount; _activeSamples++;
            if (activeCount > _maxActive) _maxActive = activeCount;

            bool wantRows = !backoff && (now >= _nextContainerLog || (Plugin.OverlayOn.Value && now >= _nextOverlay)); // 0.6.19: not during a spawn wave
            if (wantRows)
            {
                CollectRows(sas);
                if (now >= _nextContainerLog)
                {
                    // 0.6.15: every 1 s when there are more than 50 containers (raid 31: 167 containers x 2/s was the largest row writer)
                    _nextContainerLog = now + (_rows.Count > 50 ? Mathf.Max(1f, Plugin.ContainerLogInterval.Value) : Plugin.ContainerLogInterval.Value);
                    if (DiagLog.IsOpen) WriteRows(now - _raidStart);
                }
                if (Plugin.OverlayOn.Value && now >= _nextOverlay)
                {
                    _nextOverlay = now + 0.25f;
                    BuildOverlay(sas, activeCount);
                }
            }

            // 0.6.15: 0 = off (the default). Raid 31: FrameDiag found this FindObjectsOfType scan behind 83 frames of ~56 ms -
            // the plugin's only hitch. The playing-sources count at every container sample (0.6.14) replaces it.
            if (!backoff && Plugin.SourceScanInterval.Value > 0f && now >= _nextScan)
            {
                _nextScan = now + Plugin.SourceScanInterval.Value;
                Perf.Begin();
                try { ScanSources(now - _raidStart); }
                finally { Perf.End(Perf.Part.SourceScan); }
            }

            // 0.6.18: alive bots (everyone alive but you), checked every frame; spawns.csv gets a row whenever it changes.
            // Raid 33: the two biggest frames (869 and 940 ms) had no gunfire nearby but grew the heap 75-100 MB - this lines
            // FrameDiag's slow frames (clock time) up with spawn waves.
            int bots = AliveBots();
            LastAliveBots = bots; // 0.6.20
            if (bots >= 0 && bots != _lastBots)
            {
                if (_lastBots >= 0 && DiagLog.IsOpen)
                    DiagLog.Row("spawns.csv", string.Join(",", DiagLog.F(now - _raidStart, "0.00"), DateTime.Now.ToString("HH:mm:ss.fff"),
                        bots.ToString(), (bots - _lastBots).ToString("+0;-0;0")));
                if (_lastBots >= 0 && bots > _lastBots) _spawned += bots - _lastBots;
                if (_lastBots >= 0) LastSpawnChange = now; // 0.6.19 (not the first reading)
                _lastBots = bots;
                if (bots > _maxBots) _maxBots = bots;
            }

            if (!backoff && now >= _nextTiming)
            {
                _nextTiming = now + 5f;
                if (DiagLog.IsOpen)
                {
                    var u = TimingPatches.SpatialUpdate; var l = TimingPatches.SpatialLate; var b = TimingPatches.BetterAudioUpdate;
                    AudioThreadProbe.Window aw = _audioProbe != null ? _audioProbe.Snapshot() : default(AudioThreadProbe.Window); // 0.6.20
                    DiagLog.Row("timing.csv", string.Join(",", DiagLog.F(now - _raidStart, "0.0"), _framesInWindow.ToString(),
                        DiagLog.F(u.WindowAvgMs, "0.000"), DiagLog.F(u.WindowMaxMs, "0.000"),
                        DiagLog.F(l.WindowAvgMs, "0.000"), DiagLog.F(l.WindowMaxMs, "0.000"),
                        DiagLog.F(b.WindowAvgMs, "0.000"), DiagLog.F(b.WindowMaxMs, "0.000"),
                        activeCount.ToString(), _rows.Count.ToString(), bots >= 0 ? bots.ToString() : "",
                        _audioProbe != null ? aw.Calls.ToString() : "", _audioProbe != null ? DiagLog.F((float)aw.BlockMs, "0.00") : "",
                        _audioProbe != null ? DiagLog.F((float)aw.GapAvgMs, "0.00") : "", _audioProbe != null ? DiagLog.F((float)aw.GapMaxMs, "0.0") : "",
                        _audioProbe != null ? aw.Late15.ToString() : "", _audioProbe != null ? aw.Late2.ToString() : "", _audioProbe != null ? aw.Stalls.ToString() : "",
                        DiagLog.F((float)BatchWait.WinAvg, "0.000"), DiagLog.F((float)BatchWait.WinMax, "0.00"), BatchWait.WinWaited.ToString())); // 0.6.20 (6.0b)
                }
                else if (_audioProbe != null) _audioProbe.Snapshot(); // keep the window at 5 s for the overlay
                if (_audioProbe == null && Plugin.AudioThreadTimingOn.Value && now >= _nextAudioProbeTry) { _nextAudioProbeTry = now + 5f; Plugin.Safe("audio thread probe", AttachAudioProbe); }
                TimingPatches.SpatialUpdate.ResetWindow(); TimingPatches.SpatialLate.ResetWindow(); TimingPatches.BetterAudioUpdate.ResetWindow();
                AudioCounters.WriteWindow(now - _raidStart); BatchWait.ResetWindow(); // 0.6.20
                _framesInWindow = 0;
            }
        }

        private void CollectRows(SpatialAudioSystem sas)
        {
            _rows.Clear();
            Dictionary<int, AudioSourceContainer> containers = ContainersRef(sas);
            Dictionary<int, SoundOcclusionProcessor> processors = ProcessorsRef(sas);
            Transform listener = sas.ListenerTransform;
            if (containers == null || listener == null) return;
            Vector3 lp = listener.position;
            ISpatialAudioRoom lroom = sas.ListenerCurrentRoom;

            int playingInContainers = 0; // 0.6.14: playing sources in the sound containers, at every container sample
            foreach (KeyValuePair<int, AudioSourceContainer> kv in containers)
            {
                AudioSourceContainer c = kv.Value;
                if (c == null || !c.IsValid) continue;
                if (c._sources != null) foreach (BetterSource b in c._sources) if (b != null && b.source1 != null && b.source1.isPlaying) playingInContainers++;
                var r = new Row
                {
                    Id = c.ID, Sources = c.Count, Dist = Vector3.Distance(lp, c.CurrentPosition), MaxDist = c.MaxDistance,
                    Prop = c.PropagationEffect, Obst = c.ObstructionEffect, Occl = c.OcclusionEffect, Prio = "-",
                    T = float.NaN, D = float.NaN, R = float.NaN,
                };
                ISpatialAudioRoom room = c.CurrentAudioRoom;
                r.Room = ShotProbe.RoomName(room);
                try
                {
                    r.Outdoor = room != null && room.IsValid && room.IsOutdoor;
                    r.SameRoom = room != null && lroom != null && room.IsValid && lroom.IsValid && sas.IsSourceInListenerRoom(c);
                }
                catch { }

                if (processors != null && processors.TryGetValue(c.ID, out SoundOcclusionProcessor p) && p != null)
                {
                    r.Prio = p.Priority.ToString();
                    r.Occluder = p._occluder != null ? p._occluder.GetType().Name : "";
                    if (p._occluder is CombinedSpatialAudioOccluder co && co._obstructionCalculator is SpatialAudioOcclusionCombinedCalculator oc)
                    {
                        r.HasCalc = true;
                        r.RanT = oc._ranTransmission; r.RanD = oc._ranDiffraction; r.RanR = oc._ranReflection;
                        AggregateOcclusionResult a = oc.LastAggregateResult;
                        r.T = a.transmissionScore; r.D = a.diffractionScore; r.R = a.reflectionScore;
                    }
                }
                _rows.Add(r);
            }
            if (_rows.Count > _maxContainers) _maxContainers = _rows.Count;
            if (playingInContainers > _maxContainerPlaying) _maxContainerPlaying = playingInContainers;
            LastPlayingInContainers = playingInContainers; // 0.6.16: the echo headroom cap reads it
        }

        private void WriteRows(float t)
        {
            string ts = DiagLog.F(t, "0.00");
            foreach (Row r in _rows)
            {
                _containerRows++;
                if (r.Prio == "Low") _rowsTierLow++; else if (r.Prio == "Medium") _rowsTierMed++; else if (r.Prio == "High") _rowsTierHigh++;
                if (r.Prop > 0f) _rowsPropagated++;
                if (r.Occl > 0.55f) _rowsGunOccluded++;
                // 0.6.19: built without allocating (up to ~300 rows/s in a big fight; same columns, fixed decimals)
                var sb = DiagLog.Begin();
                sb.Append(ts); sb.Append(','); DiagLog.AppendI(sb, r.Id); sb.Append(','); DiagLog.AppendF(sb, r.Dist, 1); sb.Append(',');
                DiagLog.AppendF(sb, r.MaxDist, 0); sb.Append(','); sb.Append(r.Prio); sb.Append(','); DiagLog.AppendS(sb, r.Room); sb.Append(',');
                DiagLog.AppendB(sb, r.Outdoor); sb.Append(','); DiagLog.AppendB(sb, r.SameRoom); sb.Append(',');
                DiagLog.AppendF(sb, r.Prop, 3); sb.Append(','); DiagLog.AppendF(sb, r.Obst, 3); sb.Append(','); DiagLog.AppendF(sb, r.Occl, 3); sb.Append(',');
                DiagLog.AppendB(sb, r.Occl > 0.55f); sb.Append(',');
                if (r.HasCalc) DiagLog.AppendB(sb, r.RanT); sb.Append(','); if (r.HasCalc) DiagLog.AppendB(sb, r.RanD); sb.Append(','); if (r.HasCalc) DiagLog.AppendB(sb, r.RanR); sb.Append(',');
                DiagLog.AppendF(sb, r.T, 3); sb.Append(','); DiagLog.AppendF(sb, r.D, 3); sb.Append(','); DiagLog.AppendF(sb, r.R, 3); sb.Append(',');
                DiagLog.AppendI(sb, r.Sources); sb.Append(','); DiagLog.AppendS(sb, r.Occluder);
                DiagLog.EndRow("containers.csv");
            }
        }

        private void ScanSources(float t)
        {
            BetterSource[] all = FindObjectsOfType<BetterSource>();
            int playing = 0;
            foreach (BetterSource s in all)
            {
                if (s == null || s.source1 == null) continue;
                AudioSource a = s.source1;
                if (!a.isPlaying) continue;
                playing++;

                string group;
                try { AudioGroupPreset p = PresetRef(s); group = p != null ? p.name : "?"; }
                catch { group = "?"; }
                string mixer = a.outputAudioMixerGroup != null ? a.outputAudioMixerGroup.name : "none";

                if (!_maxDistByGroup.TryGetValue(group, out var hist)) _maxDistByGroup[group] = hist = new SortedDictionary<int, int>();
                int bucket = Mathf.RoundToInt(a.maxDistance / 25f) * 25;
                hist.TryGetValue(bucket, out int n); hist[bucket] = n + 1;

                string key = group + "|" + mixer + "|" + a.rolloffMode;
                if (DiagLog.IsOpen && _dumpedSourceKeys.Add(key)) DiagLog.Row("rolloff_dump.txt", DescribeSource(group, mixer, s, a));
            }
            if (playing > _maxPlaying) _maxPlaying = playing;
            if (all.Length > _maxBetterSources) _maxBetterSources = all.Length;
            if (DiagLog.IsOpen)
            {
                var cfg = AudioSettings.GetConfiguration();
                DiagLog.Row("sources.csv", string.Join(",", DiagLog.F(t, "0.0"), all.Length.ToString(), playing.ToString(), cfg.numRealVoices.ToString(), cfg.numVirtualVoices.ToString()));
            }
        }

        private static string DescribeSource(string group, string mixer, BetterSource s, AudioSource a)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"== group '{group}'  mixer '{mixer}'  type {s.GetType().Name}  object '{s.name}'");
            sb.AppendLine($"   rolloff {a.rolloffMode}  min {a.minDistance:0.##}  max {a.maxDistance:0.#}  spatialBlend {a.spatialBlend:0.##}  spatialize {a.spatialize}  spread {a.spread:0}  doppler {a.dopplerLevel:0.##}  priority {a.priority}");
            AppendCurve(sb, "custom rolloff", a, AudioSourceCurveType.CustomRolloff);
            AppendCurve(sb, "spatial blend", a, AudioSourceCurveType.SpatialBlend);
            AppendCurve(sb, "spread", a, AudioSourceCurveType.Spread);
            AudioLowPassFilter lpf = a.GetComponent<AudioLowPassFilter>();
            if (lpf != null)
            {
                sb.Append($"   AudioLowPassFilter enabled {lpf.enabled} cutoff {lpf.cutoffFrequency:0} Hz");
                if (lpf.customCutoffCurve != null && lpf.customCutoffCurve.length > 0) sb.Append("  distance curve " + Keys(lpf.customCutoffCurve));
                sb.AppendLine();
            }
            AudioHighPassFilter hpf = a.GetComponent<AudioHighPassFilter>();
            if (hpf != null) sb.AppendLine($"   AudioHighPassFilter enabled {hpf.enabled} cutoff {hpf.cutoffFrequency:0} Hz");
            var comps = a.GetComponents<Component>().Select(x => x == null ? "null" : x.GetType().Name);
            sb.AppendLine("   components: " + string.Join(", ", comps));
            return sb.ToString();
        }

        private static void AppendCurve(StringBuilder sb, string label, AudioSource a, AudioSourceCurveType type)
        {
            try
            {
                AnimationCurve c = a.GetCustomCurve(type);
                if (c != null && c.length > 0) sb.AppendLine($"   {label} (x = distance / maxDistance): {Keys(c)}");
            }
            catch { }
        }

        private static string Keys(AnimationCurve c) => string.Join(" ", c.keys.Select(k => $"{k.time:0.###}:{k.value:0.###}"));

        // ---------------- overlay ----------------

        private void BuildOverlay(SpatialAudioSystem sas, int activeCount)
        {
            _sb.Clear();
            var u = TimingPatches.SpatialUpdate; var l = TimingPatches.SpatialLate; var b = TimingPatches.BetterAudioUpdate;
            _sb.Append("<b>Real Sound Simulation diagnostics</b>  (F9)\n");
            _sb.Append($"tier {AudioUtils.GetQualityByLogicCores()} ({SystemInfo.processorCount} cores)  listener room {ShotProbe.RoomName(sas.ListenerCurrentRoom)}\n");
            _sb.Append($"active processors {activeCount} (1 updated per frame)  containers {_rows.Count}  playing sources ~{_maxPlaying} max\n");
            _sb.Append($"ms/frame  spatial upd {u.WindowAvgMs:0.000}/{u.WindowMaxMs:0.00}  late {l.WindowAvgMs:0.000}/{l.WindowMaxMs:0.00}  BetterAudio {b.WindowAvgMs:0.000}/{b.WindowMaxMs:0.00}\n");
            _sb.Append($"shots {ShotProbe.Shots}  LOS+occluded mixer {ShotProbe.ShotsWithLosOccluded}/{ShotProbe.ShotsWithLos}  >250m LOS occluded {ShotProbe.ShotsBeyond250WithLosOccluded}/{ShotProbe.ShotsBeyond250WithLos}  mixer flips {ShotProbe.MixerFlips}\n");
            _sb.Append(Perf.OverlayLine(Time.realtimeSinceStartup));
            if (_audioProbe != null) _sb.Append(_audioProbe.OverlayLine()); // 0.6.20
            _sb.Append($"probe batch wait avg {BatchWait.WinAvg:0.000} max {BatchWait.WinMax:0.00} ms, frames waited {BatchWait.WinWaited} (window)\n"); // 0.6.20
            _sb.Append(Acoustics.OutdoorExposure.OverlayLine());
            _sb.Append(Acoustics.AcousticProbe.OverlayLine());
            _sb.Append("  dist   prio   prop  obst  occl  stages  T    D    R    room\n");
            foreach (Row r in _rows.OrderBy(x => x.Dist).Take(Plugin.OverlayRows.Value))
            {
                string stages = r.HasCalc ? $"{(r.RanT ? "T" : "-")}{(r.RanD ? "D" : "-")}{(r.RanR ? "R" : "-")}" : "   ";
                string mode = r.Prop > 0f ? "<color=#7fd0ff>P</color>" : (r.Obst > 0f ? "<color=#ffb070>O</color>" : " ");
                _sb.Append($"{mode}{r.Dist,6:0.0} {r.Prio,-6} {r.Prop,5:0.00} {r.Obst,5:0.00} {(r.Occl > 0.55f ? "<color=#ff6060>" : "")}{r.Occl,5:0.00}{(r.Occl > 0.55f ? "</color>" : "")}  {stages}    {Num(r.T)} {Num(r.D)} {Num(r.R)} {(r.SameRoom ? "=" : "")}{r.Room}\n");
            }
            _overlayText = _sb.ToString();
        }

        private static string Num(float v) => float.IsNaN(v) ? "  - " : v.ToString("0.00");

        private void OnGUI()
        {
            if (!Raid.IsOn || !Plugin.OverlayOn.Value || string.IsNullOrEmpty(_overlayText)) return;
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 12, wordWrap = false };
                _style.normal.textColor = Color.white;
                Font mono = Font.CreateDynamicFontFromOSFont("Consolas", 12);
                if (mono != null) _style.font = mono;
            }
            var content = new GUIContent(_overlayText);
            Vector2 size = _style.CalcSize(content);
            GUI.Box(new Rect(8, 8, size.x + 16, size.y + 12), GUIContent.none);
            GUI.Label(new Rect(16, 14, size.x, size.y), content, _style);
        }

        // ---------------- summary ----------------

        private string BuildSummary()
        {
            var sb = new StringBuilder();
            float dur = Time.time - _raidStart;
            sb.AppendLine($"Raid summary  {DateTime.Now:yyyy-MM-dd HH:mm:ss}  duration {dur / 60f:0.0} min");
            sb.AppendLine($"CPU logical cores {SystemInfo.processorCount} -> audio tier {AudioUtils.GetQualityByLogicCores()}");
            sb.AppendLine("Fixes at start: " + _fixStateAtStart);
            sb.AppendLine("Fixes at end:   " + Plugin.FixState());
            if (Raid.ResetsFailed > 0) sb.AppendLine($"  !! {Raid.ResetsFailed} feature resets failed at raid start (errors.txt); the others still ran");
            sb.AppendLine($"  fix 1 held back {GunMixerFix.HeldBack} contrary readings | fix 2 changed {ObstructionScoreFix.Changed} scores by >0.05 | fix 3c blended {GunEnvironmentBlend.Blended} own shots/tails | fix 4 applied {OffMapFix.Applied} times");
            sb.AppendLine();
            sb.AppendLine("PERFORMANCE (main thread, per call)");
            foreach (var m in new[] { TimingPatches.SpatialUpdate, TimingPatches.SpatialLate, TimingPatches.BetterAudioUpdate })
                sb.AppendLine($"  {m.Name,-28} avg {m.TotalAvgMs:0.000} ms  max {m.TotalMaxMs:0.00} ms  frames over 1 ms: {m.TotalOver1Ms} of {m.TotalCalls}");
            sb.AppendLine($"  active occlusion processors: avg {(_activeSamples == 0 ? 0 : (double)_activeSum / _activeSamples):0.0}  max {_maxActive}   (BSG updates ONE per frame -> #8/#16)");
            sb.AppendLine($"  sound containers seen at once: max {_maxContainers}");
            var cfg = AudioSettings.GetConfiguration();
            sb.AppendLine(Plugin.SourceScanInterval.Value > 0f
                ? $"  playing sources (all, FindObjectsOfType every {Plugin.SourceScanInterval.Value:0} s): max {_maxPlaying}  vs realVoices {cfg.numRealVoices}  (BetterSource objects max {_maxBetterSources})"
                : $"  playing sources (all): source scan off (Source scan interval s = 0); realVoices {cfg.numRealVoices}");
            sb.AppendLine($"  playing sources in sound containers (counted at every container sample, every {Plugin.ContainerLogInterval.Value:0.0} s): max {_maxContainerPlaying}; our echo voices max busy {Acoustics.OwnEchoes.MaxBusy} of {Acoustics.EchoVoices.PoolSize}");
            sb.AppendLine($"  others alive (bots, and other players in co-op): max {_maxBots}, {_spawned} spawned during the raid (each change in spawns.csv, with clock time to line up with FrameDiag)");
            sb.AppendLine($"  diagnostics backed off for {_backoffFrames} frames after spawns (Diagnostics back off after a spawn s = {Plugin.SpawnBackoffS.Value:0.0})"); // 0.6.19
            sb.AppendLine();
            sb.Append(_audioProbe != null ? _audioProbe.Summary() : "AUDIO THREAD: probe off or not attached (Time the audio thread)\n"); // 0.6.20
            sb.Append(SharedPatches.Summary());
            sb.Append(AudioCounters.Summary()); // 0.6.20 (6.0a)
            sb.Append(BatchWait.Summary()); // 0.6.20 (6.0b)
            sb.Append(BotShots.Summary()); // 0.6.21
            sb.Append(RouteLog.Summary()); // 0.6.22 (6.1)
            sb.Append(PortalPositioning.Summary()); // 0.6.24
            sb.AppendLine();
            sb.AppendLine("OCCLUSION (container samples)");
            sb.AppendLine($"  samples {_containerRows}: priority Low {_rowsTierLow} / Medium {_rowsTierMed} / High {_rowsTierHigh}");
            sb.AppendLine($"  propagated (different room) {_rowsPropagated}   occlusion > 0.55 (gun 'Occluded' mixer threshold) {_rowsGunOccluded}");
            sb.AppendLine();
            sb.AppendLine("GUNSHOTS (remote shots, max 4/s per weapon)");
            sb.AppendLine($"  logged {ShotProbe.Shots}   with straight line of sight {ShotProbe.ShotsWithLos}, of which on Occluded mixer {ShotProbe.ShotsWithLosOccluded}");
            sb.AppendLine($"  beyond 250 m with line of sight {ShotProbe.ShotsBeyond250WithLos}, of which on Occluded mixer {ShotProbe.ShotsBeyond250WithLosOccluded}   <- #4 '250 m cliff' if this is most of them");
            sb.AppendLine($"  gun mixer group changes during fire: {ShotProbe.MixerFlips}   <- #7 flip-flop");
            sb.AppendLine("  gunshot bank rolloff (audible range m, 50 m buckets): " + string.Join("  ", ShotProbe.RolloffHistogram.Select(kv => $"{kv.Key}:{kv.Value}")));
            sb.AppendLine();
            sb.Append(EnvProbe.Summary());
            sb.Append(Acoustics.OwnEchoes.Summary());
            sb.Append(Perf.Summary(Time.realtimeSinceStartup));
            sb.Append(Acoustics.OutdoorExposure.Summary());
            sb.Append(Acoustics.AcousticProbe.Summary());
            sb.Append(AmbienceMix.Summary());
            sb.Append(DistantThroughOpenings.Summary());
            sb.Append(GunSoundSource.Summary());
            sb.Append(OutdoorTail.Summary());
            sb.Append(ThroughWall.Summary());
            sb.Append(ExplosionEchoes.Summary());
            sb.Append(BotEchoes.Summary()); // 0.6.61
            sb.Append(ShotVariation.Summary()); // 0.7.3
            sb.Append(RoomTail.Summary()); // 0.7.4
            sb.Append(BlastFromOpening.Summary()); // 0.6.41
            sb.Append(BlastRecording.Summary()); // 0.6.49
            sb.Append(ShockWave.Summary()); // 0.6.53
            sb.Append(ConcussionProof.Summary()); // 0.6.55
            sb.Append(GunfireHearing.Summary()); // 0.7.13
            sb.Append(MixerOwner.Summary()); // 13 Nov 2026
            sb.Append(Adrenaline.Summary()); // 0.7.22
            sb.Append(BlastFromOpening.FragmentSummary()); // 0.6.55
            sb.Append(FragmentPassBy.Summary()); // 0.6.56
            sb.Append(Acoustics.CornerSearch.Summary().TrimEnd('\n') + $"; bot shots: answers with a corner {PortalPositioning.GunCornerFound}, without {PortalPositioning.GunCornerNone}\n"); // 0.6.51
            sb.Append(BlastFromOpening.GlassSummary()); // 0.6.49
            sb.Append(ClosedDoors.Summary()); // 0.6.47
            sb.AppendLine($"RAIN (fix 7): clip picks {RainDropoutFix.Calls}, of which the game would have silenced the rain {RainDropoutFix.WouldDrop}, prevented {RainDropoutFix.Prevented}; crossfades held until the previous one finished {RainDropoutFix.Deferred}; same-clip crossfades done as a volume ramp {RainDropoutFix.VolumeOnly}");
            sb.AppendLine(MetaAcousticsCheck.Describe());
            sb.AppendLine();
            sb.AppendLine("AudioSource maxDistance by group (25 m buckets, sampled while playing)");
            foreach (var g in _maxDistByGroup.OrderBy(x => x.Key))
                sb.AppendLine($"  {g.Key,-22} " + string.Join("  ", g.Value.Select(kv => $"{kv.Key}:{kv.Value}")));
            sb.AppendLine();
            sb.AppendLine("Files: containers.csv (every container every interval), shots.csv, transitions.csv, ambience.csv, probe.csv, ownshots.csv, echoes.csv, exposure.csv, rain.csv, marks.csv, rays.csv, timing.csv, sources.csv, rolloff_dump.txt, settings.txt, patches.txt, audio-counters.txt, audiocounters.csv, slowframes.csv, botshots.csv, routes.csv, errors.txt (if any)");
            return sb.ToString();
        }
    }
}
