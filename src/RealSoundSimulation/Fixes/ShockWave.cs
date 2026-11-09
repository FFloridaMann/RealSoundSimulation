using System;
using System.Collections.Generic;
using RealSoundSimulation.Acoustics;
using RealSoundSimulation.Diagnostics;
using UnityEngine;
using UnityEngine.Audio;

namespace RealSoundSimulation.Fixes
{
    // 6.9 (0.6.53): the part of an explosion you feel. User, 9 Nov 2026: "they just lack the sense of a shock wave that the player can feel".
    // raid_20261109_114154: the thump is a recording with its highs taken off, so it has no sharp front, and it sits at full volume for anything within 20 m in the open.
    //   pulse    a generated hit, in the middle of the head with a slight lean to the blast: a 2 ms rise and short push and pull (Friedlander shape, stretched so headphones can play it),
    //            then a 42 to 80 Hz boom dying over about 0.2 s. Level follows the Kinney-Graham peak pressure of a 100 g TNT charge: 0 dB at 5 m, -7 at 10 m, -13 at 20 m, with 6.2d's wall loss.
    //            Through a wall it keeps only what's under 200 Hz. Fades out over the last 40% of "6.9b". Not for flashbangs or smoke.
    //   thump    above full volume 6.2 adds a second voice exactly in step with the first, up to +6 dB, so a close blast can be louder than one 20 m away.
    //   duck     the rest of the world dips for a moment when the blast arrives, up to "6.9c" dB, on the Main and ambience mixer volumes; explosions and guns are on another bus.
    //            Bring Back Concussion cuts sound on the same controls inside its own radius. Any control someone else moves is let go at once and never put back by us,
    //            so the two can't fight and nothing can be left quiet.
    // The pulse's shape, the 200 Hz and the duck's depth and timing are by ear, not measured.
    internal class ShockWave : MonoBehaviour
    {
        public const string Header = "t,played,reason,distM,walls,lossDb,pressureKPa,levelDb,volume,cutoffHz,delayMs,thumpExtra,duckDb,clip";

        private const float FullAtM = 5f, ChargeRoot = 0.464f, AirKPa = 101.3f, MinVolume = 0.002f, ThroughWallHz = 200f, OpenHz = 22000f, LeanToBlast = 0.25f;
        private const float RiseS = 0.002f, PushS = 0.014f, LengthS = 0.45f, BodyShare = 0.8f, BodyDecayS = 0.09f, BodyLowHz = 42f, BodyHighHz = 80f, SweepS = 0.03f;
        private const float AttackS = 0.015f, HoldS = 0.08f, TotalS = 0.38f, DuckRangeDb = 24f, MovedByOther = 0.05f, TooQuietDb = -60f;

        private struct Slot { public string Key; public float Base, Last; public bool On; }
        private static readonly Slot[] Slots = new Slot[3];
        private static readonly Dictionary<string, int> Skips = new Dictionary<string, int>();
        private static AudioClip _clip;
        private static bool _armed, _active;
        private static double _armAt, _t0;
        private static float _armDepth, _depth, _fromEnv, _lastExtra;

        public static int Played, ThumpDoubled, Ducks, LetGo;
        private static double _volSum;

        public static void Reset()
        {
            if (_active) { try { Restore(); } catch (Exception e) { DiagRunner.ReportOnce("6.9 duck (reset)", e); } }
            _armed = _active = false;
            _lastExtra = 0f;
            Skips.Clear();
            Played = ThumpDoubled = Ducks = LetGo = 0;
            _volSum = 0;
        }

        // Peak overpressure, Kinney-Graham.
        private static float PressureKPa(float m)
        {
            float z = Mathf.Max(m, 0.5f) / ChargeRoot, a = z / 4.5f, b = z / 0.048f, c = z / 0.32f, d = z / 1.35f;
            return AirKPa * 808f * (1f + a * a) / (Mathf.Sqrt(1f + b * b) * Mathf.Sqrt(1f + c * c) * Mathf.Sqrt(1f + d * d));
        }

        private static AudioClip Clip()
        {
            if (_clip != null) return _clip;
            const int rate = 48000;
            int n = (int)(rate * LengthS);
            var data = new float[n];
            float phase = 0f, peak = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float front = Mathf.Clamp01(t / RiseS) * (1f - t / PushS) * Mathf.Exp(-1.2f * t / PushS);
                float hz = BodyLowHz + (BodyHighHz - BodyLowHz) * Mathf.Exp(-t / SweepS);
                phase += 2f * Mathf.PI * hz / rate;
                float body = BodyShare * Mathf.Sin(phase) * Mathf.Exp(-t / BodyDecayS);
                data[i] = (front + body) * Mathf.Clamp01((LengthS - t) / 0.03f);
                peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            }
            if (peak > 0f) for (int i = 0; i < n; i++) data[i] /= peak;
            _clip = AudioClip.Create("rss_shock_pulse", n, 1, rate, false);
            _clip.SetData(data, 0);
            _clip.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return _clip;
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
            float extra = _lastExtra;
            _lastExtra = 0f;
            if (!Plugin.ShockOn.Value) return;
            string name = sound != null ? sound.name : "";
            float reach = Plugin.ShockReachM.Value, kPa = PressureKPa(dist);
            if (name.IndexOf("flash", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("smoke", StringComparison.OrdinalIgnoreCase) >= 0) { Skip("not a blast", dist, walls, lossDb, kPa, name); return; }
            if (dist >= reach) { Skip("out of reach", dist, walls, lossDb, kPa, name); return; }
            if (wallFade <= 0f) { Skip("stopped by walls", dist, walls, lossDb, kPa, name); return; }

            float pressureDb = 20f * Mathf.Log10(kPa / PressureKPa(FullAtM)) - lossDb * Plugin.ThumpWallLoss.Value;
            float level = pressureDb + Plugin.ShockLevelDb.Value;
            float fade = wallFade * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(reach * 0.6f, reach, dist)));
            float vol = Mathf.Clamp01(Mathf.Pow(10f, level / 20f)) * fade;
            if (vol < MinVolume) { Skip("too quiet", dist, walls, lossDb, kPa, name); return; }

            Vector3 toBlast = pos - listener.position;
            Vector3 lean = Vector3.Lerp(listener.forward, toBlast.sqrMagnitude > 0.01f ? toBlast.normalized : listener.forward, LeanToBlast);
            Vector3 at = listener.position + (lean.sqrMagnitude > 0.001f ? lean.normalized : listener.forward);
            AudioClip clip = Clip();
            float cutoff = walls == 0 ? OpenHz : ThroughWallHz;
            double start = AudioSettings.dspTime + delay;
            EchoVoices.Play(clip, clip, at, start, clip.length, false, vol, 1f, cutoff, delay, minCutoff: 20f, ignoreCap: true);
            Played++; _volSum += vol;

            float duck = Plugin.ShockDuckDb.Value * Mathf.Clamp01(1f + pressureDb / DuckRangeDb) * fade;
            if (duck >= 0.1f) { _armed = true; _armAt = start; _armDepth = duck; Ducks++; }
            if (DiagLog.IsOpen) Row("1", "", dist, walls, lossDb, kPa, level, vol, cutoff, delay, extra, duck, name);
        }

        private void Update()
        {
            if (!_armed && !_active) return;
            try { Tick(); }
            catch (Exception e) { DiagRunner.ReportOnce("6.9 duck", e); _armed = _active = false; }
        }

        private static float Envelope(float t)
        {
            if (t < AttackS) return Mathf.Lerp(_fromEnv, 1f, t / AttackS);
            if (t < HoldS) return 1f;
            return 0.5f + 0.5f * Mathf.Cos(Mathf.PI * Mathf.InverseLerp(HoldS, TotalS, t));
        }

        private static void Tick()
        {
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
                if (!StillOurs(m, i)) continue;
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
            Slots[i].On = got && Slots[i].Base > TooQuietDb;
        }

        // Someone else has moved this control since we last set it: it's theirs now.
        private static bool StillOurs(AudioMixer m, int i)
        {
            if (m.GetFloat(Slots[i].Key, out float now) && Mathf.Abs(now - Slots[i].Last) <= MovedByOther) return true;
            Slots[i].On = false;
            LetGo++;
            return false;
        }

        private static void Restore()
        {
            if (!MonoBehaviourSingleton<BetterAudio>.Instantiated) return;
            AudioMixer m = MonoBehaviourSingleton<BetterAudio>.Instance.Master;
            if (m == null) return;
            for (int i = 0; i < Slots.Length; i++)
            {
                if (!Slots[i].On) continue;
                if (StillOurs(m, i)) m.SetFloat(Slots[i].Key, Slots[i].Base);
                Slots[i].On = false;
            }
        }

        private static void Skip(string why, float dist, int walls, float lossDb, float kPa, string name)
        {
            Skips.TryGetValue(why, out int n); Skips[why] = n + 1;
            if (DiagLog.IsOpen) Row("0", why, dist, walls, lossDb, kPa, 0f, 0f, 0f, 0f, 0f, 0f, name);
        }

        private static void Row(string played, string why, float dist, int walls, float lossDb, float kPa, float level, float vol, float cutoff, float delay, float extra, float duck, string name)
        {
            DiagLog.Row("shock.csv", string.Join(",", DiagLog.F(Time.time - DiagRunner.RaidStartTime, "0.00"), played, why, DiagLog.F(dist, "0.0"), walls.ToString(), DiagLog.F(lossDb, "0.0"), DiagLog.F(kPa, "0.0"),
                DiagLog.F(level, "0.0"), DiagLog.F(vol, "0.000"), DiagLog.F(cutoff, "0"), DiagLog.F(delay * 1000f, "0"), DiagLog.F(extra, "0.000"), DiagLog.F(duck, "0.0"), name.Replace(',', ';')));
        }

        public static string Summary()
        {
            var parts = new List<string>();
            foreach (KeyValuePair<string, int> kv in Skips) parts.Add($"{kv.Key} {kv.Value}");
            string state = Plugin.ShockOn.Value ? $"ON, level {Plugin.ShockLevelDb.Value:+0;-0;0} dB, reach {Plugin.ShockReachM.Value:0} m, duck {Plugin.ShockDuckDb.Value:0} dB" : "off";
            return $"SHOCK WAVE (6.9 {state}): pulses {Played}" + (Played > 0 ? $", mean volume {_volSum / Played:0.00}" : "") + $"; thump second voice {ThumpDoubled}; ducks {Ducks}, mixer controls let go to something else {LetGo}; not played: {(parts.Count > 0 ? string.Join(", ", parts) : "-")}  (shock.csv)\n";
        }
    }
}
