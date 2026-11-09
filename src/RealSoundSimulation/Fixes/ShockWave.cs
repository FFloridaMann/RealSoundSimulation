using System;
using System.Collections.Generic;
using RealSoundSimulation.Acoustics;
using RealSoundSimulation.Diagnostics;
using UnityEngine;
using UnityEngine.Audio;

namespace RealSoundSimulation.Fixes
{
    // 6.9 (0.6.53, indoors 0.6.54): the part of an explosion you feel. User, 9 Nov 2026: "they just lack the sense of a shock wave that the player can feel", then "at mid distances and indoors around corners and rooms".
    // raid_20261109_121839: every blast in shock.csv had a wall on the straight line, so a hit that took the straight line averaged volume 0.06, even in the same room at 6 m. A blast wave goes through the openings.
    //   way      the stronger of: through the openings (or around a corner, 6.8) or the straight line through the walls. Same room or a clear line counts as open. Peak pressure is Kinney-Graham for 100 g TNT over the
    //            length of the way, 0 dB at 5 m. Each 90 degrees of turning costs "6.9d" and each opening after the first half that (published corner shielding: 43 to 60%, about 5 to 8 dB); a shut door costs its closure x 20 dB.
    //   hit      a generated pressure hit, a 2 ms rise, a short push and pull (Friedlander shape, stretched so headphones can play it), then a 42 to 80 Hz boom. It comes from the way it arrives. The front softens with each bend
    //            (3 kHz, halving per bend); only a real path through a wall is cut to 200 Hz. Arrives by the length of the way, with the bang.
    //   enclosed confined blasts are stronger (reflections pile up): up to "6.9e" dB by how shut in the blast point is (6.6's rays), then "6.9f" fading repeat hits spaced by the size of that room, because indoors a shock
    //            arrives as a train of shrinking pulses.
    //   push     a long low swell after the hit (the gas pressure phase, which lasts until the room vents): strongest and longest in a small or shut room. "6.9g", -24 = off.
    //   thump    above full volume 6.2 adds a second voice in step, up to +6 dB.
    //   dip      the rest of the world dips for a moment, up to "6.9c", on the Main and ambience mixer volumes; explosions and guns are on another bus. It sits on top of what anything else sets, every frame,
    //            so it can't fight Bring Back Concussion's cut or the ambience blend, and it only restores a control nobody else has moved.
    // Levels, the boost, the repeats and the swell are by ear; the papers behind them are mostly small-scale.
    [DefaultExecutionOrder(32000)]
    internal class ShockWave : MonoBehaviour
    {
        public const string Header = "t,played,reason,way,distM,pathM,bends,closure,walls,lossDb,enclosed,roomM,pressureKPa,levelDb,volume,cutoffHz,delayMs,thumpExtra,duckDb,repeats,clip";

        private const float FullAtM = 5f, ChargeRoot = 0.464f, AirKPa = 101.3f, MinVolume = 0.002f, ThroughWallHz = 200f, OpenHz = 22000f, RouteHz = 3000f, LeanToBlast = 0.25f, HeadStartM = 1.5f, DoorDb = 20f, SameM = 0.2f, HeldMaxS = 0.2f;
        private const float RiseS = 0.002f, PushS = 0.014f, LengthS = 0.45f, BodyShare = 0.8f, BodyDecayS = 0.09f, BodyLowHz = 42f, BodyHighHz = 80f, SweepS = 0.03f;
        private const float AttackS = 0.015f, HoldS = 0.08f, TotalS = 0.38f, DuckRangeDb = 24f, MovedByOther = 0.05f, TooQuietDb = -60f;
        private const float EnclosedFrom = 0.2f, RepeatMinS = 0.006f, RepeatMaxS = 0.05f, RepeatDull = 0.7f, PushOffDb = -24f;
        private static readonly float[] RepeatAt = { 1f, 1.7f, 2.5f, 3.4f }, RepeatDb = { -5f, -9f, -13f, -17f };

        private enum WayKind { None, Clear, Route, Waiting }
        private struct Way { public WayKind Kind; public Vector3 At, Near; public float Path, Turn, Closure; public int Openings; public string Name; }
        private struct Held { public Transform Listener; public Vector3 Pos; public float Dist, LossDb, WallFade, Delay, Extra; public int Walls; public double Played; public string Name; public bool Proof; }
        private struct Slot { public string Key; public float Base, Last; public bool On, Moved; }

        private static readonly Slot[] Slots = new Slot[3];
        private static readonly Dictionary<string, int> Skips = new Dictionary<string, int>(), Ways = new Dictionary<string, int>();
        private static AudioClip _hit, _slap, _push;
        private static Way _way;
        private static Held _held;
        private static bool _hasHeld, _armed, _active;
        private static double _armAt, _t0;
        private static float _armDepth, _depth, _fromEnv, _lastExtra;

        public static int Played, ThumpDoubled, Ducks, Followed, Repeats, Pushes;
        private static double _volSum;

        public static void Reset()
        {
            if (_active) { try { Restore(); } catch (Exception e) { DiagRunner.ReportOnce("6.9 dip (reset)", e); } }
            _armed = _active = _hasHeld = false;
            _way = default;
            _lastExtra = 0f;
            Skips.Clear(); Ways.Clear();
            Played = ThumpDoubled = Ducks = Followed = Repeats = Pushes = 0;
            _volSum = 0;
        }

        // ---- what 6.4 and 6.8 found for this blast; they run during the game's play call, before Explosion()
        public static void NoteClear(Vector3 at, string name) => _way = new Way { Kind = WayKind.Clear, At = at, Name = name };
        public static void NoteRoute(Vector3 at, float path, int openings, float closure, Vector3 near, float turn) => _way = new Way { Kind = WayKind.Route, At = at, Path = path, Openings = openings, Closure = closure, Near = near, Turn = turn, Name = "route" };
        public static void NoteWaiting(Vector3 at) => _way = new Way { Kind = WayKind.Waiting, At = at };

        public static void NoteCorner(Vector3 at, bool found, float path, int corners, float turn, Vector3 near)
        {
            Way w = found ? new Way { Kind = WayKind.Route, At = at, Path = path, Openings = corners, Near = near, Turn = turn, Name = "corner" } : default;
            if (_hasHeld && (_held.Pos - at).sqrMagnitude < SameM * SameM) { _hasHeld = false; Fire(_held, w); return; }
            _way = w;
        }

        private static float PressureKPa(float m)
        {
            float z = Mathf.Max(m, 0.5f) / ChargeRoot, a = z / 4.5f, b = z / 0.048f, c = z / 0.32f, d = z / 1.35f;
            return AirKPa * 808f * (1f + a * a) / (Mathf.Sqrt(1f + b * b) * Mathf.Sqrt(1f + c * c) * Mathf.Sqrt(1f + d * d));
        }

        private static float PressureDb(float m) => 20f * Mathf.Log10(PressureKPa(m) / PressureKPa(FullAtM));

        internal static AudioClip MakeClip(string name, float lengthS, Func<float, float> shape)
        {
            const int rate = 48000;
            int n = (int)(rate * lengthS);
            var data = new float[n];
            float peak = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                data[i] = shape(t) * Mathf.Clamp01((lengthS - t) / 0.02f);
                peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            }
            if (peak > 0f) for (int i = 0; i < n; i++) data[i] /= peak;
            AudioClip c = AudioClip.Create(name, n, 1, rate, false);
            c.SetData(data, 0);
            c.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return c;
        }

        private static float Front(float t, float riseS, float pushS) => Mathf.Clamp01(t / riseS) * (1f - t / pushS) * Mathf.Exp(-1.2f * t / pushS);

        private static void BuildClips()
        {
            if (_hit != null && _slap != null && _push != null) return;
            float phase = 0f;
            _hit = MakeClip("rss_shock_hit", LengthS, t =>
            {
                phase += 2f * Mathf.PI * (BodyLowHz + (BodyHighHz - BodyLowHz) * Mathf.Exp(-t / SweepS)) / 48000f;
                return Front(t, RiseS, PushS) + BodyShare * Mathf.Sin(phase) * Mathf.Exp(-t / BodyDecayS);
            });
            _slap = MakeClip("rss_shock_slap", 0.06f, t => Front(t, 0.0015f, 0.009f));
            float phase2 = 0f;
            _push = MakeClip("rss_shock_push", 1.2f, t =>
            {
                phase2 += 2f * Mathf.PI * (36f + 19f * Mathf.Exp(-t / 0.15f)) / 48000f;
                return Mathf.Clamp01(t / 0.03f) * Mathf.Sin(phase2) * Mathf.Exp(-t / 0.35f);
            });
        }

        // How loud 6.2's second voice is, for a thump this many dB over full volume. 0 = none.
        public static float ThumpExtra(float db, float fade)
        {
            if (!Plugin.ShockOn.Value || db <= 0f) return 0f;
            float extra = (Mathf.Min(Mathf.Pow(10f, db / 20f), 2f) - 1f) * fade;
            if (extra < MinVolume) return 0f;
            ThumpDoubled++;
            _lastExtra = extra;
            return extra;
        }

        public static void Explosion(Transform listener, Vector3 pos, float dist, int walls, float lossDb, float wallFade, float delay, AudioClip sound)
        {
            Way w = _way;
            _way = default;
            float extra = _lastExtra;
            _lastExtra = 0f;
            if (!Plugin.ShockOn.Value) return;
            if (w.Kind != WayKind.None && (w.At - pos).sqrMagnitude >= SameM * SameM) w = default;
            string name = sound != null ? sound.name.Replace(',', ';') : "";
            if (name.IndexOf("flash", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("smoke", StringComparison.OrdinalIgnoreCase) >= 0) { Skip("not a blast", dist, walls, lossDb, name); return; }
            if (dist >= Plugin.ShockReachM.Value) { Skip("out of reach", dist, walls, lossDb, name); return; }
            if (_hasHeld) { _hasHeld = false; Fire(_held, default); }
            var h = new Held { Listener = listener, Pos = pos, Dist = dist, LossDb = lossDb, WallFade = wallFade, Delay = delay, Extra = extra, Walls = walls, Played = AudioSettings.dspTime, Name = name, Proof = ConcussionProof.Active };
            if (w.Kind == WayKind.Waiting) { _held = h; _hasHeld = true; return; } // the corner search answers in a frame or two; the sound is still on its way
            Fire(h, w);
        }

        // 0.6.55: inside the concussion radius everything here goes past that mod's cut, and the dip is left to it.
        private static void Fire(Held h, Way w)
        {
            bool set = h.Proof && !ConcussionProof.Active;
            if (set) ConcussionProof.Set();
            try { FireCore(h, w); }
            finally { if (set) ConcussionProof.Clear(); }
        }

        public static void EndDipNow()
        {
            _armed = false;
            if (!_active) return;
            Restore();
            _active = false;
        }

        private static void FireCore(Held h, Way w)
        {
            if (h.Listener == null) return;
            float reach = Plugin.ShockReachM.Value;
            float through = h.WallFade > 0f ? PressureDb(h.Dist) - h.LossDb * Plugin.ThumpWallLoss.Value : float.NegativeInfinity;
            float via = float.NegativeInfinity, path = h.Dist, bends = 0f, cutoff = ThroughWallHz;
            string way = "wall";
            bool open = h.Walls == 0 || w.Kind == WayKind.Clear;
            Vector3 toward = h.Pos;
            if (open) { via = PressureDb(h.Dist); way = h.Walls == 0 ? "open" : w.Name; cutoff = OpenHz; }
            else if (w.Kind == WayKind.Route)
            {
                path = Mathf.Max(w.Path, h.Dist);
                bends = Mathf.Min(w.Turn, 135f) / 90f + 0.5f * Mathf.Max(w.Openings - 1, 0);
                via = PressureDb(path) - bends * Plugin.ShockCornerDb.Value - w.Closure * DoorDb;
                way = w.Name;
                cutoff = Mathf.Max(RouteHz * Mathf.Pow(2f, -(bends + w.Closure * 2f)), ThroughWallHz);
                if (w.Near != Vector3.zero) toward = w.Near;
            }
            bool usesWay = via >= through;
            if (!usesWay) { path = h.Dist; bends = 0f; cutoff = ThroughWallHz; way = "wall"; toward = h.Pos; }
            float best = usesWay ? via : through;
            if (float.IsNegativeInfinity(best)) { Skip("stopped by walls", h.Dist, h.Walls, h.LossDb, h.Name); return; }

            float openShare = BlastRecording.MeasureAt(h.Pos, out float roomM);
            float enclosed = usesWay ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(BlastRecording.AllIndoorBelow, Plugin.BlastOutdoorShare.Value, openShare)) : 0f;
            float boosted = best + Plugin.ShockEnclosedDb.Value * enclosed;
            float level = boosted + Plugin.ShockLevelDb.Value;
            float fade = (usesWay ? 1f : h.WallFade) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(reach * 0.6f, reach, path)));
            float vol = Mathf.Clamp01(Mathf.Pow(10f, level / 20f)) * fade;
            if (vol < MinVolume) { Skip("too quiet", h.Dist, h.Walls, h.LossDb, h.Name); return; }

            BuildClips();
            Vector3 l = h.Listener.position, to = toward - l, fwd = h.Listener.forward;
            Vector3 lean = Vector3.Lerp(fwd, to.sqrMagnitude > 0.01f ? to.normalized : fwd, LeanToBlast);
            lean = lean.sqrMagnitude > 0.001f ? lean.normalized : fwd;
            float delay = usesWay && w.Kind == WayKind.Route ? Mathf.Max(path - HeadStartM, 0f) / OwnEchoes.SpeedOfSound : h.Delay;
            double now = AudioSettings.dspTime, start = Math.Max(h.Played + delay, now);
            EchoVoices.Play(_hit, _hit, l + lean, start, _hit.length, false, vol, 1f, cutoff, delay, minCutoff: 20f, ignoreCap: true);
            Played++; _volSum += vol;
            Ways.TryGetValue(way, out int ways); Ways[way] = ways + 1;

            int repeats = 0;
            if (enclosed >= EnclosedFrom)
            {
                double gap = Mathf.Clamp(2f * roomM / OwnEchoes.SpeedOfSound, RepeatMinS, RepeatMaxS);
                int count = Mathf.Min(Plugin.ShockRepeats.Value, RepeatAt.Length);
                for (int i = 0; i < count; i++)
                {
                    float v = Mathf.Clamp01(Mathf.Pow(10f, (level + RepeatDb[i]) / 20f)) * fade * enclosed;
                    if (v < MinVolume) continue;
                    Vector3 side = Quaternion.AngleAxis((i % 2 == 0 ? 1f : -1f) * (20f + 15f * i), Vector3.up) * lean;
                    float t = (float)(gap * RepeatAt[i]);
                    EchoVoices.Play(_slap, _slap, l + side, start + t, _slap.length, false, v, 1f, Mathf.Max(cutoff * Mathf.Pow(RepeatDull, i + 1), ThroughWallHz), delay + t, minCutoff: 20f);
                    if (!EchoVoices.LastDropped) { repeats++; Repeats++; }
                }

                float trim = Plugin.ShockPushDb.Value;
                if (trim > PushOffDb)
                {
                    float small = Mathf.InverseLerp(14f, 3f, roomM), shut = usesWay && w.Kind == WayKind.Route ? w.Closure : 0f;
                    float v = Mathf.Clamp01(Mathf.Pow(10f, (level - 9f + 6f * small + trim) / 20f)) * fade * enclosed;
                    float len = Mathf.Clamp(Mathf.Lerp(0.25f, 0.7f, small) + 0.15f * shut, 0.2f, 1.1f);
                    if (v >= MinVolume)
                    {
                        EchoVoices.Play(_push, _push, l + lean, start + 0.02, len, false, v, 1f, OpenHz, delay, minCutoff: 20f, ignoreCap: true, fadeS: len * 0.5f);
                        Pushes++;
                    }
                }
            }

            float duck = Plugin.ShockDuckDb.Value * Mathf.Clamp01(1f + boosted / DuckRangeDb) * fade;
            if (h.Proof) duck = 0f;
            if (duck >= 0.1f) { _armed = true; _armAt = start; _armDepth = duck; Ducks++; }
            if (DiagLog.IsOpen) Row("1", "", way, h.Dist, path, bends, usesWay && w.Kind == WayKind.Route ? w.Closure : 0f, h.Walls, h.LossDb, enclosed, roomM, level, vol, cutoff, delay, h.Extra, duck, repeats, h.Name);
        }

        private void LateUpdate()
        {
            if (!_armed && !_active && !_hasHeld) return;
            try
            {
                if (_hasHeld && AudioSettings.dspTime > _held.Played + HeldMaxS) { _hasHeld = false; Fire(_held, default); }
                Tick();
            }
            catch (Exception e) { DiagRunner.ReportOnce("6.9 shock wave", e); _armed = _active = false; }
        }

        private static float Envelope(float t)
        {
            if (t < AttackS) return Mathf.Lerp(_fromEnv, 1f, t / AttackS);
            if (t < HoldS) return 1f;
            return 0.5f + 0.5f * Mathf.Cos(Mathf.PI * Mathf.InverseLerp(HoldS, TotalS, t));
        }

        private static void Tick()
        {
            if (!_armed && !_active) return;
            if (!MonoBehaviourSingleton<BetterAudio>.Instantiated) { _armed = _active = false; return; }
            BetterAudio ba = MonoBehaviourSingleton<BetterAudio>.Instance;
            AudioMixer m = ba.Master;
            if (m == null || ba.AudioMixerData == null) { _armed = _active = false; return; }
            double now = AudioSettings.dspTime;
            if (_armed && now >= _armAt)
            {
                _armed = false;
                float current = _active ? Envelope((float)(now - _t0)) * _depth : 0f;
                if (!_active)
                {
                    Take(m, 0, ba.AudioMixerData.MainMixerVolume);
                    Take(m, 1, ba.AudioMixerData.AmbientInMixerVolume);
                    Take(m, 2, ba.AudioMixerData.AmbientOutMixerVolume);
                    _active = true;
                }
                _depth = Mathf.Max(_armDepth, current);
                _fromEnv = _depth > 0f ? current / _depth : 0f;
                _t0 = now;
            }
            if (!_active) return;
            float t = (float)(now - _t0);
            if (t >= TotalS) { Restore(); _active = false; return; }
            float down = Envelope(t) * _depth;
            for (int i = 0; i < Slots.Length; i++)
            {
                if (!Slots[i].On) continue;
                if (!m.GetFloat(Slots[i].Key, out float cur)) { Slots[i].On = false; continue; }
                if (Mathf.Abs(cur - Slots[i].Last) > MovedByOther) // someone else set it: the dip goes on top of their value
                {
                    Slots[i].Base = cur;
                    if (!Slots[i].Moved) { Slots[i].Moved = true; Followed++; }
                }
                if (Slots[i].Base <= TooQuietDb) { Slots[i].Last = cur; continue; }
                float v = Slots[i].Base - down;
                m.SetFloat(Slots[i].Key, v);
                Slots[i].Last = v;
            }
        }

        private static void Take(AudioMixer m, int i, string key)
        {
            bool got = !string.IsNullOrEmpty(key) && m.GetFloat(key, out Slots[i].Base);
            Slots[i].Key = key;
            Slots[i].Last = Slots[i].Base;
            Slots[i].Moved = false;
            Slots[i].On = got;
        }

        // Only a control still at our value goes back; anything else belongs to whoever moved it.
        private static void Restore()
        {
            if (!MonoBehaviourSingleton<BetterAudio>.Instantiated) return;
            AudioMixer m = MonoBehaviourSingleton<BetterAudio>.Instance.Master;
            if (m == null) return;
            for (int i = 0; i < Slots.Length; i++)
            {
                if (!Slots[i].On) continue;
                if (m.GetFloat(Slots[i].Key, out float cur) && Mathf.Abs(cur - Slots[i].Last) <= MovedByOther) m.SetFloat(Slots[i].Key, Slots[i].Base);
                Slots[i].On = false;
            }
        }

        private static void Skip(string why, float dist, int walls, float lossDb, string name)
        {
            Skips.TryGetValue(why, out int n); Skips[why] = n + 1;
            if (DiagLog.IsOpen) Row("0", why, "", dist, dist, 0f, 0f, walls, lossDb, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0, name);
        }

        private static void Row(string played, string why, string way, float dist, float path, float bends, float closure, int walls, float lossDb, float enclosed, float roomM, float level, float vol, float cutoff, float delay, float extra, float duck, int repeats, string name)
        {
            DiagLog.Row("shock.csv", string.Join(",", DiagLog.F(Time.time - DiagRunner.RaidStartTime, "0.00"), played, why, way, DiagLog.F(dist, "0.0"), DiagLog.F(path, "0.0"), DiagLog.F(bends, "0.00"), DiagLog.F(closure, "0.00"), walls.ToString(), DiagLog.F(lossDb, "0.0"),
                DiagLog.F(enclosed, "0.00"), DiagLog.F(roomM, "0.0"), DiagLog.F(PressureKPa(path), "0.0"), DiagLog.F(level, "0.0"), DiagLog.F(vol, "0.000"), DiagLog.F(cutoff, "0"), DiagLog.F(delay * 1000f, "0"), DiagLog.F(extra, "0.000"), DiagLog.F(duck, "0.0"), repeats.ToString(), name));
        }

        private static string Counts(Dictionary<string, int> d)
        {
            var parts = new List<string>();
            foreach (KeyValuePair<string, int> kv in d) parts.Add($"{kv.Key} {kv.Value}");
            return parts.Count > 0 ? string.Join(", ", parts) : "-";
        }

        public static string Summary()
        {
            string state = Plugin.ShockOn.Value ? $"ON, level {Plugin.ShockLevelDb.Value:+0;-0;0} dB, reach {Plugin.ShockReachM.Value:0} m, corner {Plugin.ShockCornerDb.Value:0} dB, enclosed +{Plugin.ShockEnclosedDb.Value:0} dB, {Plugin.ShockRepeats.Value} repeats, push {Plugin.ShockPushDb.Value:+0;-0;0} dB, dip {Plugin.ShockDuckDb.Value:0} dB" : "off";
            return $"SHOCK WAVE (6.9 {state}): hits {Played}" + (Played > 0 ? $", mean volume {_volSum / Played:0.00}" : "") + $" (by way: {Counts(Ways)}); repeat hits {Repeats}; slow pushes {Pushes}; thump second voice {ThumpDoubled}; dips {Ducks}, controls also moved by something else {Followed}; not played: {Counts(Skips)}  (shock.csv)\n";
        }
    }
}
