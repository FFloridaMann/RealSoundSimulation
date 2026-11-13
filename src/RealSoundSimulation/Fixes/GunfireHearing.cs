using System;
using System.Collections.Generic;
using System.Linq;
using RealSoundSimulation.Acoustics;
using RealSoundSimulation.Diagnostics;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace RealSoundSimulation.Fixes
{
    // 0.7.13 "5r": your ears ring from gunfire when you wear no headset. User, 10 Nov 2026: "can we add in the ear ringing without headset
    // logic from my older mod? same logic basis". Ported from the author's earlier audio mod ("Gunfire hearing"), new code:
    //   noise    every shot near you adds to it: yours, and anyone else's within "5r nearby" m, less the further away. How much depends on
    //            the calibre, the muzzle device (anything with a suppressor on it adds nothing), the barrel and the ammo. Subsonic shots
    //            add nothing. It drains once the shooting has stopped for half a second.
    //   room     instead of the game's indoor flag, the room the mod measures around you: a small closed room counts up to 60% more, a big
    //            hall 20%, a doorway half that. Another shooter's shot through a wall counts by how much the game's occlusion lets through.
    //   ring     without a headset, a ring fades in as the noise builds and out as it drains, using the game's own ringing sound. If a
    //            blast's ring is already playing, firing holds that ring where it is instead of starting a second one.
    //   dull     everything but the guns gets quieter while the noise is high (never while a concussion cut or another fade is working).
    //   gear     a headset takes off half, helmets that cover the ears a little; a helmet with a built-in headset counts as a headset.
    // Only your own ears, so it's the same with Fika.
    //
    // 0.7.14: tuned after the first raid (raid_20261110_212706): 49 pistol shots were left out as subsonic and six Tavor shots in 7 s
    // never built up. User: "present but not so punishing its just annoying: a few shots should start some sort of ring and a lot a lot
    // of ringing", and by ammo: supersonic unsuppressed rings quickest, subsonic unsuppressed a little slower, supersonic suppressed
    // slower than that, subsonic suppressed not at all. So: those four tiers (the suppressor no longer also counts through its own
    // loudness), the noise waits 2 s after a shot before draining and then drains 3 a second, and the ring starts at 10 (faint, so a
    // few shots are heard) and is full at 70. A final pass may simulate the noise level with the probes.
    //
    // 13 Nov 2026 "mixer owner": two changes after a raid where, with no headset, voices and guns went "in and out" until extraction.
    //   hold     firing while a blast's ring played pushed the game's ring end later on every shot, with no limit, so in a fight the
    //            game's ring, and its deafening, never ended. Now the hold is at most 10 s past the ring's own end. When the mod runs
    //            the game's ring (GameRing, 6.9u) only the ring's sound is held and the deafening runs its own course.
    //   dull     the dulling is asked for through MixerOwner as so many dB off Main, added to whatever else asks. It no longer keeps
    //            its own copy of Main's volume, and no longer has to stand back while a ring or a cut is working.
    internal class GunfireHearing : MonoBehaviour
    {
        private const float MaxNoise = 150f, NoiseFade = 3f, DropPerNoise = 0.15f, RingStart = 10f, RingFull = 70f, RingFloor = 0.15f;
        private const float SubsonicOpen = 0.75f, SupersonicSuppressed = 0.45f; // 0.7.14: supersonic open = 1, subsonic suppressed = 0
        private const float DropRise = 1f, DropFall = 2f, RingFadeTime = 1.5f, RingSilent = 0.001f, GearCheckInterval = 0.5f;
        private const float FiringWindow = 2f, VolumeFinished = 0.01f, SpeedOfSound = 335f;
        private const float MinAmmoFactor = 0.2f, MaxAmmoFactor = 2f, MinModFactor = 0.1f, LoudnessScale = 80f, UnknownCaliber = 2f;
        private const float NearbyMax = 5f, NearbyBoost = 1.25f, LightHelmet = 0.9f, HeavyHelmet = 0.8f, HeadsetLetsThrough = 0.5f;
        private const float BigRoomExtra = 0.2f, SmallRoomExtra = 0.6f, SmallRoomM = 2f, BigRoomM = 8f, MinThroughWall = 0.1f;
        private const float RingHoldSeconds = 2.5f, MaxRingProgress = 0.95f, MaxHoldPastEndS = 10f, SameEndS = 0.01f;

        private static readonly Dictionary<string, float> Calibers = new Dictionary<string, float>
        {
            ["9x18PM"] = 2.2f, ["57x28"] = 3f, ["46x30"] = 2.3f, ["9x21"] = 2.35f, ["762x25TT"] = 2.55f, ["1143x23ACP"] = 2.3f,
            ["9x19PARA"] = 2.4f, ["9x33R"] = 4f, ["127x33"] = 5f, ["762x35"] = 2f, ["9x39"] = 1.9f, ["762x39"] = 2.6f,
            ["545x39"] = 2.63f, ["556x45NATO"] = 2.65f, ["366TKM"] = 2.68f, ["762x51"] = 2.8f, ["762x54R"] = 2.82f, ["68x51"] = 3f,
            ["127x55"] = 3.8f, ["86x70"] = 5f, ["127x108"] = 7f, ["127x99"] = 6.5f, ["23x75"] = 3.35f, ["12g"] = 3f, ["20g"] = 2.9f,
            ["30x29"] = 2.4f, ["40x46"] = 2.5f, ["40x53"] = 2.5f
        };

        private static readonly AccessTools.FieldRef<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge> BridgeRef =
            AccessTools.FieldRefAccess<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge>("playersBridge");
        private static readonly AccessTools.FieldRef<WeaponSoundPlayer, SuperBetterAudioQueue> QueueRef =
            AccessTools.FieldRefAccess<WeaponSoundPlayer, SuperBetterAudioQueue>("_queue");
        private static readonly AccessTools.FieldRef<BetterAudio, float> RingEnd = AccessTools.FieldRefAccess<BetterAudio, float>("float_2");
        private static readonly AccessTools.FieldRef<BetterAudio, float> RingLength = AccessTools.FieldRefAccess<BetterAudio, float>("float_1");
        private static readonly AccessTools.FieldRef<Player, AudioClip> RingClip = AccessTools.FieldRefAccess<Player, AudioClip>("_tinnitus");

        private static AudioSource _ring;
        private static float _ringLevel, _noise, _volumeDrop, _firingUntil = -1f, _nextGearCheck;
        private static bool _wearsHeadset, _capCounted;
        private static float _ownEnd = float.NaN, _heldEnd = float.NaN; // the game's ring: where it would have ended by itself, and where the hold last put its end
        private static Player _gearOf;

        // Raid figures for the summary.
        public static int OwnShots, OtherShots, Subsonic, RingStarts, Holds, HoldsCapped, SuperOpen, SubOpen, SuperSup;
        public static float MaxNoiseSeen, MaxDrop, RingSeconds;

        public static void Apply(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(WeaponSoundPlayer), nameof(WeaponSoundPlayer.FireBullet)),
                postfix: new HarmonyMethod(typeof(GunfireHearing), nameof(FirePostfix)));
        }

        public static void Reset()
        {
            _noise = _volumeDrop = _ringLevel = 0f; _firingUntil = -1f; _nextGearCheck = 0f; _gearOf = null; _ownEnd = _heldEnd = float.NaN; _capCounted = false;
            if (_ring != null) _ring.Stop();
            OwnShots = OtherShots = Subsonic = RingStarts = Holds = HoldsCapped = SuperOpen = SubOpen = SuperSup = 0;
            MaxNoiseSeen = MaxDrop = RingSeconds = 0f;
        }

        // ---------------- gear ----------------

        // A headset in the ear slot, or one built into the helmet (the game's own ringing check misses those).
        internal static bool WearsHeadset(Player player)
        {
            if (player == null) return false;
            try
            {
                if (player.Equipment.GetSlot(EquipmentSlot.Earpiece).ContainedItem is Headphones) return true;
                if (player.Equipment.GetSlot(EquipmentSlot.Headwear).ContainedItem is CompoundItem helmet)
                    return helmet.GetAllItemsFromCollection().OfType<Headphones>().Any();
            }
            catch { }
            return false;
        }

        private static float HelmetFactor(Player player)
        {
            try
            {
                if (!(player.Equipment.GetSlot(EquipmentSlot.Headwear).ContainedItem is CompoundItem helmet)) return 1f;
                IEnumerable<EDeafStrength> strengths = helmet.GetItemComponentsInChildren<CompositeArmorComponent>().Select(a => a.Deaf);
                if (helmet.Template is ArmoredEquipmentTemplate template) strengths = strengths.Append(template.DeafStrength);
                switch (strengths.OrderByDescending(s => (int)s).FirstOrDefault())
                {
                    case EDeafStrength.Low: return LightHelmet;
                    case EDeafStrength.High: return HeavyHelmet;
                    default: return 1f;
                }
            }
            catch { return 1f; }
        }

        private static float Protection(Player player) => (WearsHeadset(player) ? HeadsetLetsThrough : 1f) * HelmetFactor(player);

        // ---------------- shots ----------------

        private static void FirePostfix(WeaponSoundPlayer __instance, Ammo ammo, Vector3 shotPosition)
        {
            if (!Plugin.EarRingOn.Value) return;
            try
            {
                BaseSoundPlayer.IObserverToPlayerBridge b = BridgeRef(__instance);
                Player main = Singleton<GameWorld>.Instantiated && !(Singleton<GameWorld>.Instance is HideoutGameWorld) ? Singleton<GameWorld>.Instance.MainPlayer : null;
                if (b == null || main == null || ammo == null) return;
                IWeapon weapon = b.Weapon;
                if (weapon == null || weapon.IsUnderbarrelWeapon) return;

                bool own = b.PointOfView == EPointOfView.FirstPerson || ReferenceEquals(b.iPlayer, main);
                float distance = own ? 0f : Vector3.Distance(shotPosition, main.Position);
                if (distance > Plugin.EarRingNearbyM.Value) return;
                bool subsonic = weapon.Item is Weapon fired && IsSubsonic(fired, ammo);
                bool suppressed = __instance.IsSilenced;
                float tier;
                if (suppressed && subsonic) { Subsonic++; return; } // 0.7.14: subsonic and suppressed: no ringing
                if (suppressed) { tier = SupersonicSuppressed; SuperSup++; }
                else if (subsonic) { tier = SubsonicOpen; SubOpen++; }
                else { tier = 1f; SuperOpen++; }

                float loudness = ShotLoudness(weapon, ammo) * tier * Plugin.EarRingStrength.Value * RoomFactor();
                if (!own)
                {
                    SuperBetterAudioQueue q = QueueRef(__instance);
                    float through = q != null && q.AudioSources != null && q.AudioSources[0] != null ? q.AudioSources[0].OcclusionVolumeFactor : 1f;
                    loudness = Mathf.Min(loudness * (1f - distance / 100f) * NearbyBoost, NearbyMax) * Mathf.Max(through, MinThroughWall);
                    OtherShots++;
                }
                else OwnShots++;

                _noise = Mathf.Min(_noise + loudness * Protection(main), MaxNoise);
                if (_noise > MaxNoiseSeen) MaxNoiseSeen = _noise;
                _firingUntil = Time.time + FiringWindow;
            }
            catch (Exception e) { DiagRunner.ReportOnce("5r gunfire hearing (shot)", e); }
        }

        // The room the mod measures around you: a small closed room rings you more than a big hall, a doorway half.
        private static float RoomFactor()
        {
            float room = EnclosureProbe.Room, meanHit = EnclosureProbe.MeanHit;
            if (float.IsNaN(room)) return 1f;
            float small = float.IsNaN(meanHit) ? 0.5f : Mathf.InverseLerp(BigRoomM, SmallRoomM, meanHit);
            return 1f + Mathf.Clamp01(room) * Mathf.Lerp(BigRoomExtra, SmallRoomExtra, small);
        }

        // The speed this barrel gives this round, so a short barrel can make a round subsonic. Grenade launchers are slow but loud.
        private static bool IsSubsonic(Weapon gun, Ammo ammo)
        {
            string caliber = gun.AmmoCaliber ?? "";
            if (caliber.StartsWith("30x") || caliber.StartsWith("40x")) return false;
            return ammo.InitialSpeed * gun.SpeedFactor <= SpeedOfSound;
        }

        // Calibre, barrel, ammo and everything on the muzzle. A device with a suppressor on it doesn't add its own noise.
        private static float ShotLoudness(IWeapon weapon, Ammo ammo)
        {
            float ammoFactor = Mathf.Clamp((1f - (weapon.SpeedFactor - 1f) * 2f) * Mathf.Clamp(ammo.ammoRec / 100f + 1f, 0.8f, 2f), MinAmmoFactor, MaxAmmoFactor);
            float modLoudness = 0f;
            string caliberName = "";
            if (weapon.Item is Weapon gun)
            {
                caliberName = gun.AmmoCaliber ?? "";
                foreach (Mod mod in gun.Mods)
                {
                    if (mod is Silencer || mod.Slots.Any(slot => slot.ContainedItem is Silencer)) continue; // 0.7.14: the suppressor counts by tier
                    modLoudness += mod.Template.Loudness;
                }
            }
            if (!Calibers.TryGetValue(caliberName, out float caliber)) caliber = UnknownCaliber;
            return Mathf.Max(1f + modLoudness / LoudnessScale, MinModFactor) * caliber * ammoFactor;
        }

        // ---------------- every frame ----------------

        private void Update()
        {
            try { Tick(); }
            catch (Exception e) { DiagRunner.ReportOnce("5r gunfire hearing (update)", e); }
        }

        private static void Tick()
        {
            Player player = Singleton<GameWorld>.Instantiated && !(Singleton<GameWorld>.Instance is HideoutGameWorld) ? Singleton<GameWorld>.Instance.MainPlayer : null;
            bool alive = player != null && player.HealthController != null && player.HealthController.IsAlive;
            if (!alive || !Plugin.EarRingOn.Value || !Singleton<BetterAudio>.Instantiated)
            {
                if (_noise > 0f || _volumeDrop > 0f || _ringLevel > 0f) { _noise = _volumeDrop = _ringLevel = 0f; Write(0f); }
                if (_ring != null && _ring.isPlaying) _ring.Stop();
                return;
            }

            // 11 Nov 2026 (user): "reduce the ear ringing when shooting duration and give the user and I a slider". Was NoiseFade (3) a
            // second, up to ~47 s from the loudest; now the loudest ring drains over "Ear ringing - longest ring s".
            if (Time.time >= _firingUntil) _noise = Mathf.Max(0f, _noise - (MaxNoise - RingStart) / Mathf.Max(Plugin.EarRingLongestS.Value, 1f) * Time.deltaTime);
            float drop = Mathf.Min(_noise * DropPerNoise, Plugin.EarRingDullDb.Value);
            _volumeDrop = Approach(_volumeDrop, drop, drop > _volumeDrop ? DropRise : DropFall);
            if (_volumeDrop < VolumeFinished && drop <= 0f) _volumeDrop = 0f;
            if (_volumeDrop > MaxDrop) MaxDrop = _volumeDrop;

            UpdateRing(player);
            Write(_volumeDrop);
        }

        // Only without a headset, and not over the game's own ring from a blast or a head hit.
        private static void UpdateRing(Player player)
        {
            if (Time.time >= _nextGearCheck || !ReferenceEquals(player, _gearOf))
            {
                _nextGearCheck = Time.time + GearCheckInterval;
                _wearsHeadset = WearsHeadset(player);
                _gearOf = player;
            }

            // 0.7.14: faint as soon as it starts, so a few shots are heard, then up to full
            float target = _wearsHeadset || _noise <= RingStart ? 0f : Mathf.Lerp(RingFloor, 1f, Mathf.InverseLerp(RingStart, RingFull, _noise)) * Plugin.EarRingVolume.Value;
            // A blast's ring and gunfire's ring are one ring: firing while a blast rings keeps that ring where it is.
            if (GameRinging)
            {
                if (target > 0f && Time.time < _firingUntil) HoldGameRing();
                target = 0f;
            }
            _ringLevel = Approach(_ringLevel, target, RingFadeTime);

            AudioSource src = RingSource();
            if (_ringLevel <= RingSilent)
            {
                if (src.isPlaying) src.Stop();
                return;
            }
            RingSeconds += Time.deltaTime;
            src.volume = _ringLevel;
            if (!src.isPlaying && RingClip(player) != null)
            {
                BetterAudio ba = Singleton<BetterAudio>.Instance;
                src.outputAudioMixerGroup = ba.MasterMixerGroup; // where the game's own ring plays; follows the overall volume
                src.clip = RingClip(player);
                src.Play();
                RingStarts++;
            }
        }

        private static AudioSource RingSource()
        {
            if (_ring != null) return _ring;
            var go = new GameObject("RealSoundSimulation ear ring");
            DontDestroyOnLoad(go);
            _ring = go.AddComponent<AudioSource>();
            _ring.playOnAwake = false;
            _ring.spatialBlend = 0f;
            _ring.loop = true;
            _ring.priority = 0;
            return _ring;
        }

        private static bool GameRinging
        {
            get
            {
                BetterAudio ba = Singleton<BetterAudio>.Instance;
                return ba != null && RingEnd(ba) > Time.time;
            }
        }

        // The ring recording your character carries, for the mod's run of the game's ring when the game passes none.
        internal static AudioClip ClipOf(Player player) => player != null ? RingClip(player) : null;

        // Firing while a blast's ring plays keeps that ring going, but never for more than 10 s past its own end.
        private static void HoldGameRing()
        {
            BetterAudio ba = Singleton<BetterAudio>.Instance;
            if (GameRing.Hold(ba)) return; // the mod runs this ring: only its sound is held, and GameRing keeps the count

            // The game's own coroutine runs it (6.9u off, or it failed). Its ring follows a curve from start to end: moving the end
            // later while keeping the same point on the curve holds it at its current strength, deafening included, and it fades
            // normally once the shooting stops. An end this hold didn't set is a new ring, or a new blast: its own end counts from there.
            float end = RingEnd(ba);
            if (float.IsNaN(_heldEnd) || Mathf.Abs(end - _heldEnd) > SameEndS) { _ownEnd = end; _heldEnd = float.NaN; _capCounted = false; }
            float left = end - Time.time;
            if (left >= RingHoldSeconds) return;
            float newEnd = Time.time + RingHoldSeconds;
            if (newEnd > _ownEnd + MaxHoldPastEndS)
            {
                if (!_capCounted) { _capCounted = true; HoldsCapped++; }
                return; // held long enough: it fades out now, however much you fire
            }
            float length = Mathf.Max(RingLength(ba), 0.01f);
            float progress = Mathf.Clamp(1f - left / length, 0f, MaxRingProgress);
            RingEnd(ba) = newEnd;
            RingLength(ba) = RingHoldSeconds / (1f - progress);
            _heldEnd = newEnd;
            Holds++;
        }

        private static float Approach(float value, float target, float seconds) => Mathf.Lerp(value, target, 1f - Mathf.Exp(-Time.deltaTime / seconds));

        // The main mixer leaves out the guns. The mixer owner adds this to the game's value and to the ring, the concussion cut and a
        // blast's dip, and puts the game's value back when it ends.
        private static void Write(float dropDb)
        {
            if (dropDb > 0f) MixerOwner.Offset(MixKey.Main, MixBy.Gunfire, -dropDb);
            else MixerOwner.Clear(MixKey.Main, MixBy.Gunfire);
        }

        public static string Summary()
        {
            if (!Plugin.EarRingOn.Value) return "GUNFIRE HEARING (5r off)\n";
            return $"GUNFIRE HEARING (5r ON, ring {Plugin.EarRingVolume.Value:0.00}, strength x{Plugin.EarRingStrength.Value:0.00}, longest ring {Plugin.EarRingLongestS.Value:0} s, dulls up to {Plugin.EarRingDullDb.Value:0} dB, nearby {Plugin.EarRingNearbyM.Value:0} m): " +
                   $"shots counted yours {OwnShots}, others {OtherShots} (supersonic {SuperOpen}, subsonic {SubOpen}, supersonic suppressed {SuperSup}; subsonic suppressed left out {Subsonic}); most noise {MaxNoiseSeen:0} (ring from {RingStart:0}, full at {RingFull:0}); " +
                   $"ring started {RingStarts} times, {RingSeconds:0} s in all; a blast's ring held {Holds + GameRing.HeldFrames} frames (capped {HoldsCapped + GameRing.Capped} times at {MaxHoldPastEndS:0} s); most dulled {MaxDrop:0.0} dB; headset on at the end {_wearsHeadset}\n";
        }
    }
}
