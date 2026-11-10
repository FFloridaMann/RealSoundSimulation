using System;
using System.Collections.Generic;
using System.Text;
using Audio.SpatialSystem;
using RealSoundSimulation.Diagnostics;
using EFT;
using EFT.Ballistics;
using HarmonyLib;
using UnityEngine;

namespace RealSoundSimulation.Acoustics
{
    // Fix 6 (roadmap step 4, 0.3.0): synthesised early reflections ("echoes") for your own gunshots.
    //
    // The game plays your own shot as a stereo recording (indoor or outdoor set) plus a hidden Meta reverb copy. Neither
    // knows where the walls actually are or which way you fired. This adds up to N short echoes per shot, each a delayed,
    // quieter, duller copy of the shot played from the surface it bounced off:
    //
    //   1. Find reflecting surfaces (when you fire):
    //      spec    mirror reflections. For each wall the enclosure probe hit, the point on that wall straight out from you
    //              (checked with one ray). Path = 2 x perpendicular distance. The classic slapback.
    //      diff    a probe hit whose wall has no mirror point in reach (corners, clutter): scattered, weaker, duller.
    //      aim     the 9-ray cone along your shot: surfaces you fire *at* scatter energy back, so firing into a room is
    //              loud and close, firing out of one is not.
    //      far     16 level rays out to 250 m (re-scanned every 5 m / 5 s): building faces, cliffs, hills. Distinct
    //              late echoes outdoors.
    //      Ground hits are ignored (the recordings already contain the ground bounce).
    //   2. Level of each echo, relative to the shot:
    //        spreading   -20 log10(path / 2 m)
    //        muzzle      the blast is louder forward: 0 dB toward where you aim, -"6c" dB straight behind
    //        surface     material loss (EFT bullet material tags: concrete ~0, wood -1.5..-3, soil -4, fabric -8 dB)
    //        scatter     diffuse/aim/far only: -3 dB and x cos(incidence) and the share of the ray set
    //        + setting "6a Echo level dB". Echoes within 4 ms and 30 deg are merged; the loudest N are played.
    //   3. Colour: a low-pass at the lowest of: air absorption for the path length (ISO 9613-1 fit: 40 m ~9 kHz,
    //      100 m ~5 kHz, 300 m ~2.2 kHz), the surface's cutoff, x0.75 for scattered, and x0.55 when it arrives from
    //      straight behind you (head shadow; the echoes use plain stereo panning, not HRTF).
    //   4. Timing: hooked on the gun's own sound queue (SuperBetterAudioQueue.Enqueue), so every sample the game
    //      schedules for your gun (single shot, full-auto firing loop, burst tail) gets the same echoes, scheduled at
    //      the sample's start + path / 343 m/s. A looping full-auto sample's echoes stop when the tail starts (+ delay).
    //   5. Output: own pool of 24 AudioSources on the game's "Guns/Gunshots" mixer group, so game volume and the
    //      in-game headset treat them like the shot. When all are busy the oldest is stopped (counted in the summary).
    internal static class OwnEchoes
    {
        // ---- state for the call in progress (FireBullet / StopFiringLoop on your own gun) ----
        [ThreadStatic] private static bool _own;
        [ThreadStatic] private static bool _tail;
        [ThreadStatic] private static AudioClip _echoClip;
        internal static AudioClip DryClip => _echoClip; // 0.7.4: the room tail uses the same dry recording
        private static Vector3 _aim = Vector3.forward;
        private static int _callId, _tapsForCall = -1;

        private static readonly AccessTools.FieldRef<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge> BridgeRef =
            AccessTools.FieldRefAccess<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge>("playersBridge");

        // ---- model constants ----
        public const float SpeedOfSound = 343f;
        private const float RefPath = 2f;          // path at which an echo is as loud as the shot (before losses)
        private const float MinPath = 3f;          // shorter (under ~9 ms) is left to Meta's early reflections
        private const float MinLevelDb = -48f;
        private const float MaxLevelDb = -3f;
        private const float EarlyEchoS = 0.025f; // 0.6.9 "6f": echoes arriving sooner than this fuse with the shot
        private const float ScatterDb = -3f;
        private const float MergeDelayS = 0.004f;
        private const float MergeCos = 0.866f;     // 30 deg
        private const float FarRange = 250f;
        private const int FarRays = 16;
        private const float FarElevationDeg = 2f;
        private const float FarMinPath = 60f;      // nearer than 30 m is covered by the probe
        private const int MaxValidations = 12;

        internal enum Kind : byte { Spec, Diff, Aim, Far }

        internal struct Tap
        {
            public Vector3 Pos, OutDir;
            public float Path, LevelDb, DirDb, Cutoff;
            public MaterialType Mat;
            public Kind Kind;
            public float Energy; // linear power, for merging
        }

        private static readonly Tap[] Cands = new Tap[96];
        private static int _candN;
        internal static readonly Tap[] Taps = new Tap[8];
        internal static int TapN;

        // far ring cache
        private static readonly Vector3[] FarPoint = new Vector3[FarRays];
        private static readonly Vector3[] FarNormal = new Vector3[FarRays];
        private static readonly Vector3[] FarDir = new Vector3[FarRays];
        private static readonly float[] FarDist = new float[FarRays];
        private static readonly Collider[] FarCol = new Collider[FarRays];
        private static Vector3 _farOrigin = new Vector3(1e9f, 0, 0);
        private static float _farTime = -100f;

        // planes already used this computation (dedupe the many probe hits on one wall)
        private static readonly Vector3[] PlaneN = new Vector3[32];
        private static readonly float[] PlaneH = new float[32];

        // ---- stats (summary.txt) ----
        public static int SamplesEchoed, TapsPlayed, Stolen, MaxBusy;
        public static float LastHardBoost;
        public static int GlassBarsTaps;
        private static int _glassMask = -1;
        public static readonly int[] TapsByKind = new int[4];
        public static readonly Dictionary<MaterialType, int> TapsByMaterial = new Dictionary<MaterialType, int>();
        private static float _levelSum, _pathSum;

        public static void Apply(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(WeaponSoundPlayer), nameof(WeaponSoundPlayer.FireBullet)),
                prefix: new HarmonyMethod(typeof(OwnEchoes), nameof(FirePrefix)),
                finalizer: new HarmonyMethod(typeof(OwnEchoes), nameof(ClearFinalizer)));
            h.Patch(AccessTools.Method(typeof(WeaponSoundPlayer), nameof(WeaponSoundPlayer.StopFiringLoop)),
                prefix: new HarmonyMethod(typeof(OwnEchoes), nameof(TailPrefix)),
                finalizer: new HarmonyMethod(typeof(OwnEchoes), nameof(ClearFinalizer)));
            h.Patch(AccessTools.Method(typeof(SoundBank), nameof(SoundBank.PickClipsByDistance)),
                postfix: new HarmonyMethod(typeof(OwnEchoes), nameof(PickPostfix)));
            h.Patch(AccessTools.Method(typeof(SuperBetterAudioQueue), nameof(SuperBetterAudioQueue.Enqueue)),
                postfix: new HarmonyMethod(typeof(OwnEchoes), nameof(EnqueuePostfix)));
        }

        public static void Reset()
        {
            SamplesEchoed = TapsPlayed = Stolen = MaxBusy = 0;
            Array.Clear(TapsByKind, 0, TapsByKind.Length);
            TapsByMaterial.Clear();
            Surfaces.Reset();
            Loudness.Reset();
            GlassBarsTaps = 0; _glassMask = -1;
            _levelSum = _pathSum = 0f;
            _farTime = -100f;
            Pending.Clear(); LoopEchoesSkipped = LoopEchoesLate = 0; EchoVoices.ResetStats();
            EchoVoices.StopAll();
        }

        private static bool IsOwn(WeaponSoundPlayer wsp)
        {
            BaseSoundPlayer.IObserverToPlayerBridge b = BridgeRef(wsp);
            return b != null && b.PointOfView == EPointOfView.FirstPerson;
        }

        private static void FirePrefix(WeaponSoundPlayer __instance, Vector3 shotDirection)
        {
            _own = false; _tail = false; _echoClip = null;
            try
            {
                if (!Plugin.EchoOn.Value || !IsOwn(__instance)) return;
                _own = true;
                _callId++;
                if (shotDirection.sqrMagnitude > 1e-6f) _aim = shotDirection.normalized;
            }
            catch (Exception e) { _own = false; DiagRunner.ReportOnce("6 own gun echoes (fire)", e); }
        }

        private static void TailPrefix(WeaponSoundPlayer __instance)
        {
            _own = false; _tail = true; _echoClip = null;
            try
            {
                if (!Plugin.EchoOn.Value || !IsOwn(__instance)) return;
                _own = true;
                _callId++;
            }
            catch (Exception e) { _own = false; DiagRunner.ReportOnce("6 own gun echoes (tail)", e); }
        }

        private static Exception ClearFinalizer(Exception __exception)
        {
            _own = false; _tail = false; _echoClip = null;
            return __exception;
        }

        // The echoes use the *outdoor* recording of the same distance tier: the driest version of the shot (the indoor set
        // already has a room baked in). Runs for every own pick; order against fix 3c's blend doesn't matter.
        private static void PickPostfix(SoundBank __instance, ref AudioClip clip1)
        {
            if (!_own || clip1 == null) return;
            try
            {
                _echoClip = clip1;
                if (!__instance.HasEnvironment || __instance.Environments == null || __instance.Environments.Length < 2) return;
                for (int env = 0; env < 2; env++)
                {
                    EnvironmentVariety ev = __instance.Environments[env];
                    if (ev == null) continue;
                    for (int k = 0; k < 3; k++)
                    {
                        DistanceVarity dv = ev[k];
                        if (dv?.Clips == null || Array.IndexOf(dv.Clips, clip1) < 0) continue;
                        Loudness.Measure(__instance, k);
                        AudioClip outdoor = null;
                        __instance.SelectRandomClip(__instance.Environments[0][k], ref outdoor);
                        if (outdoor != null) _echoClip = outdoor;
                        return;
                    }
                }
            }
            catch (Exception e) { DiagRunner.ReportOnce("6 own gun echoes (clip)", e); }
        }

        private static void EnqueuePostfix(SuperBetterAudioQueue __instance, AudioClip clip1, AudioClip clip2, double startTime, float clipTime, float volume, float pitch)
        {
            if (!_own) return;
            try
            {
                // 0.6.14: a loop held back (below) either turns out to be one round (next sample within LoopMinS: drop its
                // echoes - the tail's cover it) or has run long enough (schedule now; CloseLoops then ends them)
                ResolvePending(__instance, startTime);
                // Any echo of this queue's looping sample ends where this sample starts (the game cuts the loop there).
                EchoVoices.CloseLoops(__instance, startTime);

                AudioClip clip = _echoClip != null ? _echoClip : (clip2 != null ? clip2 : clip1);
                if (clip == null) return;
                if (!TryGetListener(out Vector3 o, out Vector3 fwd)) return;
                if (_tapsForCall != _callId)
                {
                    ComputeTaps(o, _aim);
                    _tapsForCall = _callId;
                }
                bool loop = clipTime <= 0f;
                string sample = _tail ? "tail" : (loop ? "loop" : "shot");
                SamplesEchoed++;
                if (loop && TapN > 0)
                {
                    // 0.6.14: hold a loop's echoes until it has run LoopMinS (raid 30: pool 48/48, 57 cuts - every single round
                    // of a full-auto gun is loop + tail, and the loop's echoes ran on under the tail's)
                    var pl = new PendingLoop { Owner = __instance, Clip = clip, Start = startTime, Volume = volume, Pitch = pitch, O = o, Fwd = fwd, N = TapN };
                    pl.T = new Tap[TapN]; Array.Copy(Taps, pl.T, TapN);
                    Pending.Add(pl);
                    return;
                }
                PlayTaps(__instance, clip, Taps, TapN, startTime, loop, clipTime, volume, pitch, o, fwd, sample, 0f);
                if (TapN == 0) LogNone(sample);
            }
            catch (Exception e) { DiagRunner.ReportOnce("6 own gun echoes (play)", e); }
        }

        private static void PlayTaps(object owner, AudioClip clip, Tap[] taps, int n, double startTime, bool loop, float clipTime,
                                     float volume, float pitch, Vector3 o, Vector3 fwd, string sample, float lateS)
        {
            float trim = Plugin.EchoLevelDb.Value;
            // 0.6.16: loudest first, so the headroom cap drops the quietest
            for (int i = 0; i < n; i++) Order[i] = i;
            for (int i = 1; i < n; i++)
            {
                int k = Order[i], j = i - 1;
                while (j >= 0 && taps[Order[j]].LevelDb < taps[k].LevelDb) { Order[j + 1] = Order[j]; j--; }
                Order[j + 1] = k;
            }
            for (int oi = 0; oi < n; oi++)
            {
                int i = Order[oi];
                ref Tap t = ref taps[i];
                float delay = t.Path / SpeedOfSound;
                float lvl = Mathf.Min(t.LevelDb + trim, MaxLevelDb + Mathf.Max(0f, trim));
                if (delay < EarlyEchoS) lvl += Mathf.Min(0f, Plugin.EarlyEchoTrimDb.Value); // 0.6.9 "6f"
                float cutoff = t.Cutoff * HeadShadow(o, fwd, t.Pos);
                bool stole = EchoVoices.Play(owner, clip, t.Pos, startTime + lateS + delay, loop ? 0.0 : clipTime, loop, volume * DbToLin(lvl), pitch, cutoff, delay,
                                             lateS * (pitch > 0f ? pitch : 1f), startTime, cls: EchoClass.Own);
                if (EchoVoices.LastDropped) continue; // at the headroom cap (counted there)
                TapsPlayed++;
                TapsByKind[(int)t.Kind]++;
                TapsByMaterial.TryGetValue(t.Mat, out int c); TapsByMaterial[t.Mat] = c + 1;
                _levelSum += lvl; _pathSum += t.Path;
                if (stole) Stolen++;
                LogTap(sample, i, ref t, delay, lvl, cutoff, o, fwd, stole);
            }
            if (EchoVoices.Busy > MaxBusy) MaxBusy = EchoVoices.Busy;
        }

        // ---- 0.6.14: loop echoes held back until the loop has run LoopMinS ----
        public const double LoopMinS = 0.2;
        private static readonly int[] Order = new int[16];
        private class PendingLoop { public object Owner; public AudioClip Clip; public double Start; public float Volume, Pitch; public Vector3 O, Fwd; public Tap[] T; public int N; }
        private static readonly List<PendingLoop> Pending = new List<PendingLoop>();
        public static int LoopEchoesSkipped, LoopEchoesLate;

        // The owner queue scheduled its next sample at nextStart.
        private static void ResolvePending(object owner, double nextStart)
        {
            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                PendingLoop pl = Pending[i];
                if (!ReferenceEquals(pl.Owner, owner)) continue;
                Pending.RemoveAt(i);
                if (nextStart - pl.Start < LoopMinS) { LoopEchoesSkipped += pl.N; continue; } // one round
                Schedule(pl);
            }
        }

        // Called every frame (EchoVoices.Update): schedule held loops that have now run LoopMinS (a little ahead).
        public static void TickPending(double now)
        {
            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                PendingLoop pl = Pending[i];
                if (pl.Owner == null || now + 0.05 < pl.Start + LoopMinS) continue;
                Pending.RemoveAt(i);
                if (Plugin.EchoOn.Value) Schedule(pl);
            }
        }

        private static void Schedule(PendingLoop pl)
        {
            // starts LoopMinS late, from the matching point in the loop recording, so it stays in step with the loop
            PlayTaps(pl.Owner, pl.Clip, pl.T, pl.N, pl.Start, true, 0f, pl.Volume, pl.Pitch, pl.O, pl.Fwd, "loop", (float)LoopMinS);
            LoopEchoesLate += pl.N;
        }

        private static bool TryGetListener(out Vector3 pos, out Vector3 fwd)
        {
            pos = Vector3.zero; fwd = Vector3.forward;
            if (!MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated) return false;
            Transform lt = MonoBehaviourSingleton<SpatialAudioSystem>.Instance.ListenerTransform;
            if (lt == null) return false;
            pos = lt.position; fwd = lt.forward;
            return true;
        }

        // ---------------- finding the reflections ----------------

        internal static void ComputeTaps(Vector3 o, Vector3 aim)
        {
            Diagnostics.Perf.Begin();
            try { ComputeTapsCore(o, aim); }
            finally { Diagnostics.Perf.End(Diagnostics.Perf.Part.EchoSearch); }
        }

        private static void ComputeTapsCore(Vector3 o, Vector3 aim)
        {
            _candN = 0; TapN = 0;
            int mask = EnclosureProbe.CurrentMask();
            if (!EnclosureProbe.Valid || Time.time - EnclosureProbe.LastSweepTime > 0.15f || (EnclosureProbe.LastOrigin - o).sqrMagnitude > 0.25f)
                EnclosureProbe.SweepNow();
            float dirMax = Plugin.EchoDirectivityDb.Value;

            // 1. mirror reflections from the probe's walls (+ scattered fallback)
            if (EnclosureProbe.Valid)
            {
                int planes = 0, validations = 0;
                for (int i = 0; i < EnclosureProbe.RayCount; i++)
                {
                    float d = EnclosureProbe.HitDist[i];
                    if (float.IsInfinity(d)) continue;
                    Vector3 n = EnclosureProbe.HitNormal[i];
                    if (EnclosureProbe.IsGround(n)) continue;
                    Vector3 p = EnclosureProbe.HitPoint[i];
                    float h = Vector3.Dot(o - p, n);
                    bool dup = false;
                    for (int k = 0; k < planes && !dup; k++)
                        dup = Vector3.Dot(PlaneN[k], n) > 0.9f && Mathf.Abs(PlaneH[k] - h) < 0.75f;
                    if (dup) continue;
                    if (planes < PlaneN.Length) { PlaneN[planes] = n; PlaneH[planes] = h; planes++; }

                    bool spec = false;
                    if (h * 2f >= MinPath && validations < MaxValidations)
                    {
                        validations++;
                        if (CountedRay.Raycast(o, -n, out RaycastHit vh, h + 1f, mask, QueryTriggerInteraction.Ignore)
                            && Mathf.Abs(vh.distance - h) < 0.6f && Vector3.Dot(vh.normal, n) > 0.9f)
                        {
                            spec = true;
                            AddCand(Kind.Spec, vh.point, -n, vh.distance * 2f, Material(vh.collider), 1f, 1f, aim, dirMax);
                        }
                    }
                    if (!spec && d * 2f >= MinPath)
                    {
                        Vector3 dir = EnclosureProbe.Dirs[i];
                        float cos = Mathf.Max(0.05f, Vector3.Dot(-dir, n));
                        AddCand(Kind.Diff, p, dir, d * 2f, Material(EnclosureProbe.HitCollider[i]), 0.25f * cos, ScatterDb, aim, dirMax);
                    }
                }
            }

            // 2. what you're firing at
            if (EnclosureProbe.MeasureAim(o, aim, mask, out _, out _))
            {
                for (int i = 0; i < EnclosureProbe.AimCone; i++)
                {
                    float d = EnclosureProbe.AimHitDist[i];
                    if (float.IsInfinity(d) || d * 2f < MinPath) continue;
                    Vector3 n = EnclosureProbe.AimHitNormal[i];
                    if (EnclosureProbe.IsGround(n)) continue;
                    Vector3 dir = EnclosureProbe.AimDirs[i];
                    float cos = Mathf.Max(0.05f, Vector3.Dot(-dir, n));
                    AddCand(Kind.Aim, EnclosureProbe.AimHitPoint[i], dir, d * 2f, Material(EnclosureProbe.AimHitCollider[i]), cos / EnclosureProbe.AimCone, ScatterDb, aim, dirMax);
                }
            }

            // 2b. (0.4.2) glass facades and window bars along the aim cone. They aren't on the sound-blocking layers, so the
            //     rays above pass through them. Intact glass reflects like glass; bars are a weak, dull reflector (grate).
            if (_glassMask == -1) _glassMask = LayerMask.GetMask("LowPolyCollider", "TransparentCollider") & ~mask;
            if (_glassMask != 0)
            {
                for (int i = 0; i < EnclosureProbe.AimCone; i++)
                {
                    float dOcc = EnclosureProbe.AimHitDist[i];
                    float maxD = float.IsInfinity(dOcc) ? EnclosureProbe.MaxRange : dOcc;
                    Vector3 dir = EnclosureProbe.AimDirs[i];
                    if (!CountedRay.Raycast(o, dir, out RaycastHit gh, maxD, _glassMask, QueryTriggerInteraction.Ignore) || gh.distance * 2f < MinPath) continue;
                    OutdoorExposure.Path gp = OutdoorExposure.Classify(gh.collider);
                    if (gp != OutdoorExposure.Path.Glass && gp != OutdoorExposure.Path.Bars) continue;
                    float cos = Mathf.Max(0.05f, Vector3.Dot(-dir, gh.normal));
                    AddCand(Kind.Aim, gh.point, dir, gh.distance * 2f, gp == OutdoorExposure.Path.Glass ? MaterialType.Glass : MaterialType.Grate,
                        cos / EnclosureProbe.AimCone, ScatterDb, aim, dirMax);
                    GlassBarsTaps++;
                }
            }

            // 3. far reflectors
            if (Plugin.EchoFar.Value)
            {
                ScanFar(o, mask);
                for (int i = 0; i < FarRays; i++)
                {
                    float d = FarDist[i];
                    if (float.IsInfinity(d) || d * 2f < FarMinPath) continue;
                    Vector3 n = FarNormal[i];
                    if (EnclosureProbe.IsGround(n)) continue;
                    float cos = Vector3.Dot(-FarDir[i], n);
                    if (cos < 0.3f) continue;
                    AddCand(Kind.Far, FarPoint[i], FarDir[i], d * 2f, Material(FarCol[i]), 0.5f * cos, ScatterDb, aim, dirMax);
                }
            }

            // 4. merge near-identical arrivals, keep the loudest N
            SortCandsByEnergy();
            // 0.3.3: hard closed rooms (stairwells, bare concrete) get up to 2 extra reflections.
            float hr = Fixes.GunEnvironmentBlend.HardRoomNow(); // 0.6.0: probe v2 with fix 9a on
            float hardRoom = float.IsNaN(hr) ? 0f : hr;
            // 0.4.1: up to 3 extra, and all echoes + "6e Hard room echo boost dB" x hardRoom (raid 11: stairwell shots
            // quieter than a stairwell should be; a hard closed room is loud because the sound builds up).
            int max = Mathf.Clamp(Plugin.EchoMaxTaps.Value + Mathf.RoundToInt(3f * hardRoom), 1, Taps.Length);
            for (int c = 0; c < _candN; c++)
            {
                ref Tap cand = ref Cands[c];
                bool merged = false;
                for (int t = 0; t < TapN && !merged; t++)
                {
                    if (Mathf.Abs(Taps[t].Path - cand.Path) / SpeedOfSound < MergeDelayS
                        && Vector3.Dot((Taps[t].Pos - o).normalized, (cand.Pos - o).normalized) > MergeCos)
                    {
                        Taps[t].Energy += cand.Energy;
                        Taps[t].LevelDb = 10f * Mathf.Log10(Taps[t].Energy);
                        merged = true;
                    }
                }
                if (merged || TapN >= max || cand.LevelDb < MinLevelDb) continue;
                Taps[TapN++] = cand;
            }
            float hardBoost = Plugin.EchoHardRoomDb.Value * hardRoom;
            LastHardBoost = hardBoost;
            for (int t = 0; t < TapN; t++) Taps[t].LevelDb = Mathf.Min(Taps[t].LevelDb + hardBoost, MaxLevelDb + hardBoost * 0.5f);
        }

        private static void AddCand(Kind kind, Vector3 pos, Vector3 outDir, float path, MaterialType mat, float share, float extraDb, Vector3 aim, float dirMax)
        {
            if (_candN >= Cands.Length) return;
            Materials.Echo(mat, out float matDb, out float matCut);
            float spread = -20f * Mathf.Log10(Mathf.Max(path, RefPath) / RefPath);
            float dirDb = -dirMax * (1f - Vector3.Dot(outDir.normalized, aim)) * 0.5f;
            float shareDb = 10f * Mathf.Log10(Mathf.Max(share, 1e-4f));
            float level = spread + dirDb + matDb + extraDb + shareDb;
            float cutoff = Mathf.Min(AirCutoff(path), matCut) * (kind == Kind.Spec ? 1f : 0.75f);
            Cands[_candN++] = new Tap
            {
                Pos = pos, OutDir = outDir, Path = path, LevelDb = level, DirDb = dirDb, Cutoff = cutoff,
                Mat = mat, Kind = kind, Energy = Mathf.Pow(10f, level / 10f),
            };
        }

        private static void SortCandsByEnergy()
        {
            for (int i = 1; i < _candN; i++)
            {
                Tap x = Cands[i];
                int j = i - 1;
                while (j >= 0 && Cands[j].Energy < x.Energy) { Cands[j + 1] = Cands[j]; j--; }
                Cands[j + 1] = x;
            }
        }

        private static void ScanFar(Vector3 o, int mask)
        {
            if ((o - _farOrigin).sqrMagnitude < 25f && Time.time - _farTime < 5f) return;
            _farOrigin = o; _farTime = Time.time;
            float el = FarElevationDeg * Mathf.Deg2Rad;
            for (int i = 0; i < FarRays; i++)
            {
                float az = i * (Mathf.PI * 2f / FarRays);
                Vector3 d = new Vector3(Mathf.Cos(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Sin(az) * Mathf.Cos(el));
                FarDir[i] = d;
                if (CountedRay.Raycast(o, d, out RaycastHit hit, FarRange, mask, QueryTriggerInteraction.Ignore))
                {
                    FarDist[i] = hit.distance; FarPoint[i] = hit.point; FarNormal[i] = hit.normal; FarCol[i] = hit.collider;
                }
                else { FarDist[i] = float.PositiveInfinity; FarCol[i] = null; }
            }
        }

        // ---------------- acoustics tables ----------------

        // Air absorption as a low-pass cutoff for a path length (ISO 9613-1 fit at 20 C / 70% RH, ~3 dB loss points).
        private static readonly float[] AirPathM = { 10f, 40f, 100f, 300f, 1000f };
        private static readonly float[] AirCutHz = { 20000f, 9000f, 5000f, 2200f, 600f };

        public static float AirCutoff(float path)
        {
            if (path <= AirPathM[0]) return AirCutHz[0];
            for (int i = 1; i < AirPathM.Length; i++)
            {
                if (path > AirPathM[i]) continue;
                float t = Mathf.InverseLerp(Mathf.Log(AirPathM[i - 1]), Mathf.Log(AirPathM[i]), Mathf.Log(path));
                return Mathf.Exp(Mathf.Lerp(Mathf.Log(AirCutHz[i - 1]), Mathf.Log(AirCutHz[i]), t));
            }
            return AirCutHz[AirCutHz.Length - 1];
        }

        private static MaterialType Material(Collider c) => Surfaces.Material(c);

        // No HRTF on the echoes, so front/back is cued the way the ear's shadow does it: from behind is duller.
        internal static float HeadShadow(Vector3 o, Vector3 fwd, Vector3 pos)
        {
            Vector3 a = pos - o;
            a.y = 0f; fwd.y = 0f;
            if (a.sqrMagnitude < 1e-4f || fwd.sqrMagnitude < 1e-4f) return 1f;
            float back = Mathf.Max(0f, -Vector3.Dot(a.normalized, fwd.normalized));
            return Mathf.Lerp(1f, 0.55f, back);
        }

        private static float DbToLin(float db) => Mathf.Pow(10f, db / 20f);

        // ---------------- logs ----------------

        private static void LogTap(string sample, int i, ref Tap t, float delay, float lvl, float cutoff, Vector3 o, Vector3 fwd, bool stole)
        {
            if (!DiagLog.IsOpen) return;
            Vector3 a = t.Pos - o;
            Vector3 ah = new Vector3(a.x, 0, a.z), fh = new Vector3(fwd.x, 0, fwd.z);
            float az = ah.sqrMagnitude > 1e-4f && fh.sqrMagnitude > 1e-4f ? Vector3.SignedAngle(fh, ah, Vector3.up) : 0f;
            float el = a.sqrMagnitude > 1e-4f ? Mathf.Asin(Mathf.Clamp(a.normalized.y, -1f, 1f)) * Mathf.Rad2Deg : 0f;
            DiagLog.Row("echoes.csv", string.Join(",",
                DiagLog.F(Time.time - DiagRunner.RaidStartTime, "0.00"), sample, TapN.ToString(), i.ToString(), t.Kind.ToString(),
                t.Mat.ToString(), DiagLog.F(t.Path, "0.0"), DiagLog.F(delay * 1000f, "0"), DiagLog.F(lvl, "0.0"),
                DiagLog.F(t.DirDb, "0.0"), DiagLog.F(cutoff, "0"), DiagLog.F(az, "0"), DiagLog.F(el, "0"), stole ? "1" : "0"));
        }

        private static void LogNone(string sample)
        {
            if (!DiagLog.IsOpen) return;
            DiagLog.Row("echoes.csv", string.Join(",", DiagLog.F(Time.time - DiagRunner.RaidStartTime, "0.00"), sample, "0", "", "none", "", "", "", "", "", "", "", "", ""));
        }

        public static string Summary()
        {
            var sb = new StringBuilder();
            sb.AppendLine("OWN GUN ECHOES (fix 6)");
            if (SamplesEchoed == 0) { sb.AppendLine("  none played (fix 6 off, or no own shots)"); return sb.ToString(); }
            sb.AppendLine($"  samples echoed {SamplesEchoed} (shots, firing loops, tails)   echoes played {TapsPlayed} = {(float)TapsPlayed / SamplesEchoed:0.0} per sample");
            sb.AppendLine($"  by kind: mirror {TapsByKind[0]}  scattered {TapsByKind[1]}  aim {TapsByKind[2]}  far {TapsByKind[3]}");
            if (TapsPlayed > 0) sb.AppendLine($"  mean level {_levelSum / TapsPlayed:0.0} dB   mean path {_pathSum / TapsPlayed:0.0} m");
            sb.AppendLine($"  voices: max busy {MaxBusy} of {EchoVoices.PoolSize}, stolen {Stolen}; loop echoes held back for one-round loops (skipped) {LoopEchoesSkipped}, started 0.2 s late on longer loops {LoopEchoesLate}; dropped by the headroom cap {EchoVoices.CapDropped} (cap = real voices - game sources playing - 16, 16..{EchoVoices.PoolSize}; at raid end {EchoVoices.Allowed()})");
            var mats = new List<string>();
            foreach (var kv in TapsByMaterial) mats.Add($"{kv.Key}:{kv.Value}");
            sb.AppendLine("  surfaces: " + string.Join("  ", mats) + "   (None = no bullet material tag found)");
            sb.AppendLine($"  windows registered as glass: {Surfaces.WindowsFound}   glass-facade/bar candidates (0.4.2): {GlassBarsTaps}");
            sb.Append(Loudness.Summary());
            return sb.ToString();
        }
    }

    // 0.6.61 (6.3): who owns a voice. Only bot echoes can ever lose one.
    internal enum EchoClass : byte { Other, Own, Bot }

    // The echo voice pool: plain AudioSources (2D panning by position, no distance roll-off; level and filter are set per
    // echo) routed to the game's gunshot mixer group.
    internal class EchoVoices : MonoBehaviour
    {
        // 0.6.9: 48 (was 24). Raid 28: every own sample was a full-auto loop plus a tail, 6 echoes each; the pool was full
        // and 274 voices were stolen (256 by tail echoes), each cut off mid-sound - heard as a doubled or stuttering shot.
        // 0.6.14: 96 (raid 30: 48/48 busy, 57 cuts with no bots). Unity has ~196 real voices; the game peaked at 40 playing
        // sources with 16 bot shooters (raid 25). Not higher than 96.
        public const int PoolSize = 96;
        // 0.6.61: voices that are only ever used while a replaced echo fades out, so the new echo can start at once; never counted against the cap
        private const int HandoverVoices = 8;
        private const float ReleaseS = 0.04f, ReplaceMarginDb = 1f, FadeOverClipDb = 30f;
        private const double LoopSafetyS = 15.0, FadeEarlyS = 0.04;
        private const float MinFadeS = 0.05f;

        private class Voice
        {
            public GameObject Go;
            public AudioSource Src;
            public AudioLowPassFilter Lp;
            public double Start, BusyUntil, LoopStart, FadeStart, FadeEnd;
            public float FadeBase;
            public bool OpenLoop, Fading;
            public object Owner;
            public float Delay;
            public EchoClass Cls;
            public int Tier;
            public float StartDb, Dur;
            public bool Releasing;
        }

        private static readonly Voice[] Pool = new Voice[PoolSize + HandoverVoices];
        private static bool _built;
        private static AnimationCurve _flat;

        public static int Busy
        {
            get
            {
                if (!_built) return 0;
                double now = AudioSettings.dspTime; int n = 0;
                foreach (Voice v in Pool) if (v.BusyUntil > now && !v.Releasing) n++;
                return n;
            }
        }

        private static void Build()
        {
            if (_built) return;
            _flat = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 1f));
            for (int i = 0; i < Pool.Length; i++)
            {
                var go = new GameObject("RealSoundSimulation echo " + i);
                DontDestroyOnLoad(go);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 1f;
                src.spatialize = false;
                src.dopplerLevel = 0f;
                src.spread = 0f;
                src.reverbZoneMix = 0f;
                src.bypassReverbZones = true;
                src.priority = 220; // 0.6.15 (was 40): when Unity runs short of real voices, game sounds keep theirs and ours go virtual first
                src.rolloffMode = AudioRolloffMode.Custom;
                src.maxDistance = 100000f;
                src.SetCustomCurve(AudioSourceCurveType.CustomRolloff, _flat);
                var lp = go.AddComponent<AudioLowPassFilter>();
                lp.lowpassResonanceQ = 1f;
                lp.cutoffFrequency = 22000f;
                Pool[i] = new Voice { Go = go, Src = src, Lp = lp };
            }
            _built = true;
        }

        // Returns true if a busy voice had to be stopped to make room.
        public static bool LastDropped, LastReplaced; public static int CapDropped;
        public static int ReplacedForOwn, ReplacedForOther, ReplacedForBot, OwnDropped, OwnDroppedWithBots, MaxBot;
        public static void ResetStats() { CapDropped = ReplacedForOwn = ReplacedForOther = ReplacedForBot = OwnDropped = OwnDroppedWithBots = MaxBot = 0; }
        public static UnityEngine.Audio.AudioMixerGroup OutputOverride; // 0.6.58: set by Fixes.ConcussionProof.FeltOn while a blast's felt layers start
        public static float VolumeTrim = 1f;        private static int _realVoices = -1;
        private const int ReserveVoices = 32; // 0.6.37 (voice budget for step 6, was 16); the mod's own opening sounds are counted too
        public static int Allowed()
        {
            if (_realVoices < 0) _realVoices = AudioSettings.GetConfiguration().numRealVoices;
            // 0.7.6: the room tail's voices (at most 6) are counted too
            return Mathf.Clamp(_realVoices - Diagnostics.DiagRunner.LastPlayingInContainers - Fixes.OpeningSounds.Voices - Fixes.RoomTail.Voices - ReserveVoices, 16, PoolSize);
        }

        public static bool Play(object owner, AudioClip clip, Vector3 pos, double start, double duration, bool loop, float volume, float pitch, float cutoff, float delay,
                                float clipOffset = 0f, double loopStart = 0.0, float minCutoff = 400f, bool ignoreCap = false, float fadeS = 0f, EchoClass cls = EchoClass.Other, int tier = 0)
        {
            Build();
            double now = AudioSettings.dspTime;
            // 0.6.16: headroom cap. Our busy voices stay under what Unity has left after the game's own playing sources
            // (counted every 0.5 s) and a margin of 16, so the game's sounds are never virtualised for our echoes. At the
            // cap the new echo is dropped (callers offer a shot's echoes loudest first). Quiet raids never reach it.
            LastDropped = false; LastReplaced = false;
            float startDb = volume > 1e-5f ? 20f * Mathf.Log10(volume) : -100f;
            Count(now, out int busy, out int own, out int bot);
            if (!ignoreCap && !HasRoom(cls, busy, own, bot)) // 0.6.37: the through-wall layer (at most 4) goes first
            {
                Voice victim = Plugin.EchoReplaceOn.Value ? Victim(now, cls, tier, startDb) : null;
                if (victim == null)
                {
                    LastDropped = true; CapDropped++;
                    if (cls == EchoClass.Own) { OwnDropped++; if (bot > 0) OwnDroppedWithBots++; }
                    return false;
                }
                Release(victim, now);
                LastReplaced = true; bot--;
                if (cls == EchoClass.Own) ReplacedForOwn++; else if (cls == EchoClass.Bot) ReplacedForBot++; else ReplacedForOther++;
            }
            if (cls == EchoClass.Bot && bot + 1 > MaxBot) MaxBot = bot + 1;
            Voice pick = null; bool stole = false;
            foreach (Voice v in Pool) if (v.BusyUntil <= now) { pick = v; break; }
            if (pick == null)
            {
                // 0.6.9: take the voice that ends soonest (least left to cut), not the one that started first - that was
                // often a loop echo still sounding, or the previous burst's tail in full swing.
                foreach (Voice v in Pool) if (v.Cls == EchoClass.Bot && (pick == null || v.BusyUntil < pick.BusyUntil)) pick = v; // 0.6.61: a bot echo goes first
                if (pick == null) foreach (Voice v in Pool) if (pick == null || v.BusyUntil < pick.BusyUntil) pick = v;
                pick.Src.Stop();
                stole = true;
            }
            var ba = MonoBehaviourSingleton<BetterAudio>.Instantiated ? MonoBehaviourSingleton<BetterAudio>.Instance : null;
            if (ba != null) pick.Src.outputAudioMixerGroup = OutputOverride != null ? OutputOverride : ba.GunshotMixerGroup; // 0.6.58: "6.9j"
            pick.Go.transform.position = pos;
            pick.Src.clip = clip;
            pick.Src.loop = loop;
            pick.Src.volume = Mathf.Clamp01(volume * (OutputOverride != null ? VolumeTrim : 1f));
            pick.Src.pitch = pitch > 0f ? pitch : 1f;
            pick.Lp.cutoffFrequency = Mathf.Clamp(cutoff, minCutoff, 22000f);
            pick.Src.time = clipOffset > 0f && clip != null && clip.length > 0f ? Mathf.Repeat(clipOffset, clip.length) : 0f; // 0.6.14: late loop echoes
            pick.Src.PlayScheduled(start);
            pick.Start = start; pick.Owner = owner; pick.Delay = delay; pick.OpenLoop = loop; pick.LoopStart = loopStart > 0.0 ? loopStart : start - delay;
            pick.Cls = cls; pick.Tier = tier; pick.StartDb = startDb; pick.Dur = loop ? 0f : (float)duration; pick.Releasing = false;
            pick.Fading = !loop && fadeS > 0f;
            if (pick.Fading)
            {
                // 0.6.44: done a little before the scheduled end, so the last volume step of a frame-rate fade is already silent
                pick.FadeEnd = start + duration - FadeEarlyS;
                pick.FadeStart = pick.FadeEnd - Mathf.Max(fadeS - (float)FadeEarlyS, MinFadeS);
                pick.FadeBase = pick.Src.volume;
            }
            if (loop)
            {
                pick.BusyUntil = start + LoopSafetyS;
                pick.Src.SetScheduledEndTime(start + LoopSafetyS);
            }
            else
            {
                double end = start + duration;
                pick.Src.SetScheduledEndTime(end);
                pick.BusyUntil = end + 0.02;
            }
            return stole;
        }

        private static void Count(double now, out int busy, out int own, out int bot)
        {
            busy = own = bot = 0;
            foreach (Voice v in Pool)
            {
                if (v.BusyUntil <= now || v.Releasing) continue;
                busy++;
                if (v.Cls == EchoClass.Own) own++; else if (v.Cls == EchoClass.Bot) bot++;
            }
        }

        public static void Snapshot(out int busy, out int own, out int bot)
        {
            if (!_built) { busy = own = bot = 0; return; }
            Count(AudioSettings.dspTime, out busy, out own, out bot);
        }

        // 0.6.61 (6.3): how many voices bot echoes may hold right now. Other echoes and the voices held free for your gun come off the top.
        private static int BotLimit(int busy, int own, int bot)
        {
            int allowed = Allowed(), held = Mathf.Max(0, Mathf.Min(Plugin.OwnEchoShare.Value, allowed / 2) - own);
            return Mathf.Min(Plugin.BotEchoCeiling.Value, allowed - held - (busy - bot));
        }

        private static bool HasRoom(EchoClass cls, int busy, int own, int bot) => cls == EchoClass.Bot ? bot < BotLimit(busy, own, bot) : busy < Allowed();

        // Bot voices still free to take right now.
        public static int BotHeadroom()
        {
            Build();
            Count(AudioSettings.dspTime, out int busy, out int own, out int bot);
            return BotLimit(busy, own, bot) - bot;
        }

        // How loud an echo is at this moment: a recording's ring dies away, so it counts for less the further through its length it is.
        private static float LevelNow(Voice v, double now)
        {
            if (v.Dur <= 0f || now <= v.Start) return v.StartDb;
            return v.StartDb - FadeOverClipDb * Mathf.Clamp01((float)((now - v.Start) / v.Dur));
        }

        // The bot echo to give up: a later echo of its shooter's set first (a shooter's first echo matters more than another's third), then the quietest now.
        // Your gun and every other sound can take any of them; a bot echo only one that ranks below it. Nothing but bot echoes is ever chosen.
        private static Voice Victim(double now, EchoClass cls, int tier, float startDb)
        {
            Voice worst = null; float worstDb = 0f;
            foreach (Voice v in Pool)
            {
                if (v.Cls != EchoClass.Bot || v.Releasing || v.BusyUntil <= now) continue;
                float db = LevelNow(v, now);
                if (worst == null || v.Tier > worst.Tier || (v.Tier == worst.Tier && db < worstDb)) { worst = v; worstDb = db; }
            }
            if (worst == null || cls != EchoClass.Bot) return worst;
            return worst.Tier > tier || (worst.Tier == tier && worstDb < startDb - ReplaceMarginDb) ? worst : null;
        }

        // Not started yet: nothing to hear, free it. Playing: a short fade, on a voice no longer counted, so the new echo starts on another.
        private static void Release(Voice v, double now)
        {
            v.OpenLoop = false; v.Owner = null;
            if (v.Start > now) { v.Src.Stop(); v.BusyUntil = 0; v.Fading = false; return; }
            v.Releasing = true; v.Fading = true;
            v.FadeBase = v.Src.volume; v.FadeStart = now; v.FadeEnd = now + ReleaseS;
            v.BusyUntil = v.FadeEnd + 0.02;
            v.Src.SetScheduledEndTime(v.FadeEnd + 0.01);
        }

        // The owner queue scheduled a new sample at `nextStart`: its looping echoes end there, each shifted by its delay.
        public static void CloseLoops(object owner, double nextStart)
        {
            if (!_built) return;
            foreach (Voice v in Pool)
            {
                if (!v.OpenLoop || !ReferenceEquals(v.Owner, owner)) continue;
                if (nextStart - v.LoopStart < OwnEchoes.LoopMinS)
                {
                    // 0.6.14: one round - free it at once (normally held back and never scheduled; this is the safety net)
                    v.Src.Stop(); v.BusyUntil = 0; v.OpenLoop = false; v.Owner = null;
                    continue;
                }
                double end = nextStart + v.Delay;
                v.Src.SetScheduledEndTime(end);
                v.BusyUntil = end + 0.02;
                v.OpenLoop = false;
            }
        }

        public static void StopAll()
        {
            if (!_built) return;
            foreach (Voice v in Pool)
            {
                if (v.Src != null) v.Src.Stop();
                v.BusyUntil = 0; v.OpenLoop = false; v.Owner = null; v.Fading = false; v.Releasing = false;
            }
        }

        private void Update()
        {
            if (!_built) return;
            if (!Plugin.EchoOn.Value) { if (Busy > 0) StopAll(); return; }
            double now = AudioSettings.dspTime;
            OwnEchoes.TickPending(now);
            foreach (Voice v in Pool)
            {
                if (v.OpenLoop && now > v.Start + LoopSafetyS - 0.1) { v.OpenLoop = false; v.Owner = null; }
                if (!v.Fading) continue;
                if (now >= v.BusyUntil) { v.Fading = false; v.Releasing = false; continue; }
                if (now <= v.FadeStart) continue;
                float left = Mathf.Clamp01((float)((v.FadeEnd - now) / (v.FadeEnd - v.FadeStart)));
                v.Src.volume = v.FadeBase * left * left;
            }
        }
    }
}
