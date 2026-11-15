using System.Collections.Generic;
using Audio.AmbientSubsystem;
using Audio.SpatialSystem;
using RealSoundSimulation.Diagnostics;
using HarmonyLib;
using UnityEngine;

namespace RealSoundSimulation.Fixes
{
    // 0.9.3, setting "8h Room tones blend at room borders" (Labs raid_20261114_112725: at the basement ladder the game
    // swapped amb_bunker and amb_laboratory_general five times in 12 s, and in a gap between rooms it stopped the hum).
    // The game plays one room's tone at a time and swaps it the moment its room check changes (RoomToneController), and
    // stops it when you are in no room at all. Here the tone follows where you stand, not when the check flips:
    //  - a neighbouring room with a different tone (through one of your room's open portals, or the room you were last in)
    //    is heard at the border with your room at the same level as yours (equal power, -3 dB each), fading out by Width
    //    (1.5 m) into your room. The distance is measured to the neighbour's own room boxes, so it is the same whichever
    //    side the game thinks you are on, and the same whether you cross fast or slow.
    //  - in no room (a gap between rooms) or a room without a tone, the last room's tone keeps playing.
    //  - outside (an outdoor room) the tones fade out, as the game does.
    // While on, the game's own swap (ChangeRoomTone) is skipped; switching it off hands the tone back to the game.
    // The levels go through RoomTone (the sources' occlusion volume factor), together with the draught level (8g).
    internal static class RoomToneBlend
    {
        public const string Header = "t,room,clip,neighbour,neighbourClip,neighbourM,mix,gainRoom,gainNeighbour,playing";
        private const float Width = 1.5f, Smooth = 0.08f, LogEvery = 0.25f;

        private static RoomToneController _ctl;
        private static bool _bypass;
        public static bool Active;
        private static SpatialAudioRoom _a, _here, _last, _loggedA, _loggedB; // _a: whose player plays the hum; _here: the room you are in
        private static readonly List<SpatialAudioRoom> Rooms = new List<SpatialAudioRoom>();
        private static readonly List<float> Gains = new List<float>(), Targets = new List<float>();
        private static float _nextLog, _maxMix;
        private static int _changes, _blendRows;

        public static int Count => Rooms.Count;
        public static SpatialAudioRoom RoomAt(int i) => Rooms[i];
        public static float GainAt(int i) => Gains[i];

        public static void Apply(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(RoomToneController), nameof(RoomToneController.ChangeRoomTone)),
                prefix: new HarmonyMethod(typeof(RoomToneBlend), nameof(ChangePrefix)));
        }

        // The controller is made at map load; keep it so the game can be handed its tone back.
        private static bool ChangePrefix(RoomToneController __instance)
        {
            _ctl = __instance;
            return !Active || _bypass || !Raid.IsOn;
        }

        public static void Reset()
        {
            Active = false;
            _a = _here = _last = _loggedA = _loggedB = null;
            Rooms.Clear(); Gains.Clear(); Targets.Clear();
            _nextLog = 0f; _maxMix = 0f; _changes = 0; _blendRows = 0;
        }

        // Called by AmbienceMix every frame while fix 8 is on, before RoomTone.Tick.
        public static void Tick(float dt)
        {
            if (!Plugin.RoomToneBlendOn.Value) { if (Active) Off(); return; }
            if (!MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated) return;
            SpatialAudioSystem sas = MonoBehaviourSingleton<SpatialAudioSystem>.Instance;
            Transform lt = sas.ListenerTransform;
            if (lt == null) return;
            if (!Active) TakeOver();
            Vector3 lp = lt.position;

            for (int i = 0; i < Targets.Count; i++) Targets[i] = 0f;
            var cur = sas.ListenerCurrentRoom as SpatialAudioRoom;
            bool valid = cur != null && cur.IsValid;
            SpatialAudioRoom b = null; float bd = float.NaN, mix = 0f;
            if (valid && ((ISpatialAudioRoom)cur).IsOutdoor)
            {
                if (_a != null) _last = _a; // outside: the tones fade out, as the game does
                _a = _here = null;
            }
            else
            {
                if (valid && HasTone(cur))
                {
                    _here = cur;
                    // a different hum: it takes over. The same hum (most of a building shares one): the one already playing
                    // carries on, as the game does, instead of restarting the same loop from another room's player
                    // (Icebreaker raid_20261114_133032: ten fade-out/fade-in restarts of amb_icebreaker_living_roomtone).
                    if (_a == null || cur.AmbientData.RoomTone != _a.AmbientData.RoomTone)
                    {
                        if (_a != null) { _last = _a; _changes++; }
                        _a = cur;
                        RoomTone.Note(cur);
                    }
                }
                if (_a != null) // also in a gap or a room without a tone: the last tone stays
                {
                    // neighbours of the room you are in (not of the room whose player happens to be playing the hum)
                    SpatialAudioRoom here = _here ?? _a;
                    float best = Width;
                    if (here.allPortals != null)
                        foreach (ISpatialPortal ip in here.allPortals)
                            if (ip is BaseSpatialAudioPortal bp && bp.IsOpen()) Consider(bp.FrontRoom == here ? bp.BackRoom : bp.FrontRoom, lp, ref b, ref best);
                    Consider(_last, lp, ref b, ref best);
                    if (b != null) { bd = best; mix = 0.5f * (1f - Smooth01(best / Width)); }
                    SetTarget(_a, Mathf.Cos(mix * Mathf.PI * 0.5f));
                    if (b != null) SetTarget(b, Mathf.Sin(mix * Mathf.PI * 0.5f));
                }
            }

            float k = 1f - Mathf.Exp(-dt / Smooth); // only smooths the steps; the level itself follows the position
            for (int i = Rooms.Count - 1; i >= 0; i--)
            {
                SpatialAudioRoom r = Rooms[i];
                if (r == null) { RemoveAt(i); continue; }
                Gains[i] += (Targets[i] - Gains[i]) * k;
                RoomAmbientSoundPlayer p = RoomTone.Player(r);
                bool playing = p != null && p.IsPlaying;
                if (Targets[i] > 0.001f) { if (!playing) r.PlayRoomToneSound(); }
                else if (Gains[i] < 0.01f) { if (playing) r.StopRoomToneSound(); RemoveAt(i); }
            }
            if (mix > _maxMix) _maxMix = mix;
            Log(b, bd, mix);
        }

        private static void Consider(SpatialAudioRoom r, Vector3 lp, ref SpatialAudioRoom b, ref float best)
        {
            if (r == null || r == _a || !r.IsValid || ((ISpatialAudioRoom)r).IsOutdoor || !HasTone(r)) return;
            if (r.AmbientData.RoomTone == _a.AmbientData.RoomTone) return; // same hum on both sides: nothing to blend
            float d = Distance(r, lp);
            if (d < best) { best = d; b = r; }
        }

        // How far you are from the room's own boxes (0 inside them).
        private static float Distance(SpatialAudioRoom r, Vector3 p)
        {
            BoxCollider[] boxes = r.Colliders;
            if (boxes == null || boxes.Length == 0) return Mathf.Sqrt(r.GetRoomBounds().SqrDistance(p));
            float best = float.MaxValue;
            foreach (BoxCollider bc in boxes)
            {
                if (bc == null) continue;
                Transform t = bc.transform;
                Vector3 local = t.InverseTransformPoint(p) - bc.center, h = bc.size * 0.5f;
                var c = new Vector3(Mathf.Clamp(local.x, -h.x, h.x), Mathf.Clamp(local.y, -h.y, h.y), Mathf.Clamp(local.z, -h.z, h.z));
                float d = Vector3.Distance(p, t.TransformPoint(c + bc.center));
                if (d < best) best = d;
            }
            return best;
        }

        private static bool HasTone(SpatialAudioRoom r) => r.AmbientData != null && r.AmbientData.RoomTone != null;

        private static void SetTarget(SpatialAudioRoom r, float g)
        {
            int i = Rooms.IndexOf(r);
            if (i < 0) { Rooms.Add(r); Gains.Add(0f); Targets.Add(g); }
            else Targets[i] = g;
        }

        private static void RemoveAt(int i) { Rooms.RemoveAt(i); Gains.RemoveAt(i); Targets.RemoveAt(i); }

        // Take the tone the game is playing now and carry on from it.
        private static void TakeOver()
        {
            Active = true;
            Rooms.Clear(); Gains.Clear(); Targets.Clear();
            _a = _here = null;
            if (_ctl != null && _ctl._prevEnteredRoom is SpatialAudioRoom pr && RoomTone.Player(pr) is RoomAmbientSoundPlayer p && p.IsPlaying)
            {
                Rooms.Add(pr); Gains.Add(1f); Targets.Add(1f);
                _a = pr;
                RoomTone.Note(pr);
            }
        }

        // Switched off (8h or fix 8) in a raid: stop the tones this played and let the game play the current room's again.
        public static void Off()
        {
            if (!Active) return;
            Active = false;
            foreach (SpatialAudioRoom r in Rooms) if (r != null && RoomTone.Player(r) is RoomAmbientSoundPlayer p && p.IsPlaying) r.StopRoomToneSound();
            Rooms.Clear(); Gains.Clear(); Targets.Clear();
            _a = _here = _last = null;
            if (_ctl == null || !Raid.IsOn || !MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated) return;
            _ctl.Clear();
            _bypass = true;
            try { _ctl.ChangeRoomTone(MonoBehaviourSingleton<SpatialAudioSystem>.Instance.ListenerCurrentRoom); }
            finally { _bypass = false; }
        }

        private static void Log(SpatialAudioRoom b, float bd, float mix)
        {
            if (!DiagLog.IsOpen) return;
            bool changed = _a != _loggedA || b != _loggedB;
            if (!changed && (b == null || Time.time < _nextLog)) return;
            _nextLog = Time.time + LogEvery; _loggedA = _a; _loggedB = b;
            _blendRows++;
            int ia = _a != null ? Rooms.IndexOf(_a) : -1, ib = b != null ? Rooms.IndexOf(b) : -1;
            DiagLog.Row("roomblend.csv", string.Join(",", DiagLog.F(Time.time - DiagRunner.RaidStartTime, "0.00"),
                DiagLog.Csv(_a != null ? _a.name : ""), DiagLog.Csv(Clip(_a)), DiagLog.Csv(b != null ? b.name : ""), DiagLog.Csv(Clip(b)),
                DiagLog.F(bd, "0.00"), DiagLog.F(mix), ia >= 0 ? DiagLog.F(Gains[ia]) : "", ib >= 0 ? DiagLog.F(Gains[ib]) : "", Rooms.Count.ToString()));
        }

        private static string Clip(SpatialAudioRoom r) => r != null && r.AmbientData != null && r.AmbientData.RoomTone != null ? r.AmbientData.RoomTone.name : "";

        private static float Smooth01(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        public static string Summary() =>
            $"  room tone blend (8h {(Plugin.RoomToneBlendOn.Value ? "ON" : "off")}): room changes {_changes}, strongest blend {_maxMix:0.00} (0.5 = at a border), rows {_blendRows} (roomblend.csv)\n";
    }
}
