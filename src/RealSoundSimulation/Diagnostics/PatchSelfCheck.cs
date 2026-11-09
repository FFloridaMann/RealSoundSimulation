using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;

namespace RealSoundSimulation.Diagnostics
{
    // Resolves every game member the planned fixes depend on and reports which exist in this client build.
    // Run at startup; after any SPT/EFT update, read BepInEx/RealSoundSimulationLogs/selfcheck.txt first.
    internal static class PatchSelfCheck
    {
        private struct Target
        {
            public string Fix, Type, Member, Kind; // Kind: M method, F field, P property
            public Target(string fix, string type, string member, string kind) { Fix = fix; Type = type; Member = member; Kind = kind; }
        }

        private static readonly Target[] Targets =
        {
            // setting 0 gun sounds (SPT or SEB)
            new Target("6.6", "BetterAudio", "PlayAtPointDistant", "M"),
            new Target("6.6", "EFT.SoundBank", "BlendOptions", "F"),
            new Target("6.6", "EFT.SoundBank", "HasEnvironment", "F"),
            new Target("6.6", "EFT.DistanceBlendOptions", "Values", "F"),
            new Target("6.7", "EFT.Interactive.WindowBreaker", "PlayHitSound", "M"),
            new Target("6.7", "EFT.Interactive.WindowBreaker", "PlayPieceSound", "M"),
            new Target("6.7c", "Systems.Effects.EffectsCommutator", "PlayHitEffect", "M"),
            new Target("6.7f", "EFT.Ballistics.Shot", "PositionHistory", "F"),
            new Target("6.7", "BetterSource", "PreOcclusionVolume", "F"),
            new Target("6.7", "SuperSource", "_secondPreEffectsVolume", "F"),
            new Target("6.8", "SuperSourceDistant", "_secondPreEffectsVolume", "F"),
            new Target("6.4d", "SuperSourceDistant", "Delay", "F"),
            new Target("6.2", "BetterAudio", "PlayAtPoint", "M"),
            new Target("6.2", "BetterAudio", "PlayAtPointDistant", "M"),
            new Target("6.3", "BetterAudio", "PlayAtPoint", "M"),
            new Target("6.3", "BetterAudio", "PlayAtPointDistant", "M"),
            new Target("6.4", "Audio.SpatialSystem.SpatialAudioSystem", "RunOneShotPropagationOcclusionTest", "M"),
            new Target("6.4", "Audio.SpatialSystem.SpatialAudioSystem", "_allRegisteredSoundContainers", "F"),
            new Target("6.4", "Audio.SpatialSystem.AudioSourceContainer", "SetPositionOffset", "M"),
            new Target("6.4", "Audio.SpatialSystem.AudioSourceContainer", "Clear", "M"),
            new Target("6.4", "Audio.SpatialSystem.AudioSourceContainer", "_positionOffset", "F"),
            new Target("6.4", "Audio.SpatialSystem.BaseSpatialAudioPortal", "Center", "M"),
            new Target("6.4", "SuperSourceDistant", "_source2", "F"),
            new Target("6.4", "Audio.SpatialSystem.SpatialAudioSystem", "_registeredGameObjectContainers", "F"),
            new Target("6.4", "EFT.Grenade", "InitAudioSource", "M"),
            new Target("6.4", "EFT.Grenade", "ReleaseAudioSource", "M"),
            new Target("6.4", "EFT.Grenade", "_collisionSource", "F"),
            new Target("6.5", "Audio.SpatialSystem.SpatialAudioDataContainer", "UpdatePortalDataInternal", "M"),
            new Target("6.5", "Audio.SpatialSystem.SpatialAudioDataContainer", "CreatePortalData", "M"),
            new Target("6.5", "Audio.SpatialSystem.SpatialAudioDataContainer", "UpdatePortalData", "M"),
            new Target("6.5", "Audio.SpatialSystem.SpatialAudioPortal", "DoorID", "F"),
            new Target("10a", "BetterAudio", "LoadGroupPreset", "M"),
            new Target("10a", "BetterSource", "CheckBinauralAllowed", "M"),
            new Target("10a", "BetterSource", "_forceStereo", "F"),
            new Target("10a", "BetterSource", "Preset", "F"),
            new Target("10b", "Audio.SpatialSystem.SpatialAudioDataLoader", "LoadBakeDataAsync", "M"),
            new Target("10b", "Audio.SpatialSystem.SpatialAudioDataLoader", "DataLoadedHandler", "M"),
            new Target("10b", "Audio.SpatialSystem.SpatialAudioDataLoader", "_fullPath", "F"),
            new Target("0", "WeaponSoundPlayer", "FireBullet", "M"),
            new Target("0", "WeaponSoundPlayer", "Body", "F"),
            new Target("0", "WeaponSoundPlayer", "BodySilenced", "F"),
            new Target("0", "WeaponSoundPlayer", "Tail", "F"),
            new Target("0", "WeaponSoundPlayer", "TailSilenced", "F"),
            new Target("0", "WeaponSoundPlayer", "Doublet", "F"),
            new Target("0", "WeaponSoundPlayer", "DoubletSilenced", "F"),
            new Target("0", "WeaponSoundPlayer", "_isFiring", "F"),
            new Target("0", "WeaponSoundPlayer", "_firingLoopLength", "F"),
            new Target("0", "WeaponSoundPlayer", "Non_auto", "F"),
            new Target("0", "EFT.SoundBank", "Environments", "F"),
            // 2.1 portal positioning
            new Target("2.1", "Audio.SpatialSystem.SoundPropagationJobScheduler", "Schedule", "M"),
            new Target("2.1", "Audio.SpatialSystem.SoundPropagationJobScheduler", "Complete", "M"),
            new Target("2.1", "Audio.SpatialSystem.SoundPropagationJobScheduler", "_lowestCost", "F"),
            new Target("2.1", "Audio.SpatialSystem.SoundPropagationJobScheduler", "_lowestDistance", "F"),
            new Target("2.1", "Audio.SpatialSystem.SoundPropagationJobScheduler", "_routeNodesData", "F"),
            new Target("2.1", "Audio.SpatialSystem.SoundPropagationJobScheduler", "_routesSettingsData", "F"),
            new Target("2.1", "Audio.SpatialSystem.ParallelSoundPropagationCalculator", "_propagationJobScheduler", "F"),
            new Target("2.1", "Audio.SpatialSystem.CombinedSpatialAudioOccluder", "_propagationCalculator", "F"),
            new Target("2.1", "Audio.SpatialSystem.AudioRouteData", "NodeStartIndex", "F"),
            // 2.2 path combining
            new Target("2.2", "Audio.SpatialSystem.SpatialAudioOcclusionCombinedCalculator", "GetScore", "M"),
            new Target("2.2", "Audio.SpatialSystem.SpatialAudioOcclusionCombinedCalculator", "OnLateUpdate", "M"),
            new Target("2.2", "Audio.SpatialSystem.SpatialAudioOcclusionCombinedCalculator", "_ranDiffraction", "F"),
            new Target("2.2", "Audio.SpatialSystem.CombinedSpatialAudioOccluder", "_obstructionCalculator", "F"),
            // 2.3 mixer hysteresis
            new Target("2.3", "WeaponSoundPlayer", "UpdateMixerGroup", "M"),
            new Target("2.3", "WeaponSoundPlayer", "_currentMixerGroup", "F"),
            new Target("2.3", "EFT.Weapons.AudioWeaponUtils", "GetGunshotOcclusionMixerGroup", "M"),
            // 2.4 250 m cliff, 3.1 materials
            new Target("2.4", "Audio.SpatialSystem.SpatialAudioCalculator.TransmissionCalculator", "StartCalculation", "M"),
            new Target("3.1", "Audio.SpatialSystem.SpatialAudioCalculator.TransmissionCalculator", "CompleteCalculation", "M"),
            new Target("3.1", "Audio.SpatialSystem.SpatialAudioCalculator.TransmissionCalculator", "_l2sHits", "F"),
            new Target("3.1", "Audio.SpatialSystem.SpatialAudioCalculator.TransmissionCalculator", "_s2lHits", "F"),
            new Target("3.1", "EFT.Ballistics.BallisticCollider", "TypeOfMaterial", "P"),
            // 2.5 air absorption / 2.12 headset
            new Target("2.5", "Audio.SpatialSystem.SpatialAudioOccluder", "ApplyOcclusionToSource", "M"),
            new Target("2.5", "BetterSource", "SetLowPassFilterParameters", "M"),
            new Target("2.12", "Audio.SpatialSystem.MetaSpatialAudioSource", "HrtfIntensity", "P"),
            new Target("2.12", "EFT.Player", "ProtagonistHearing", "P"),
            // 2.6 / 2.7 gun report
            new Target("2.6", "WeaponSoundPlayer", "FireBullet", "M"),
            new Target("2.6", "EFT.SoundBank", "PickClipsByDistance", "M"),
            new Target("2.7", "SuperBetterAudioQueue", "Enqueue", "M"),
            // 2.8 update rate
            new Target("2.8", "Audio.SpatialSystem.SpatialAudioSystem", "method_25", "M"),
            new Target("2.8", "Audio.SpatialSystem.SpatialAudioSystem", "_activeProcessors", "F"),
            new Target("2.8", "Audio.SpatialSystem.SpatialAudioSystem", "_scheduledProcessor", "F"),
            new Target("2.8", "Audio.SpatialSystem.CombinedSpatialAudioOccluder", "OnUpdateOcclusionEffects", "M"),
            // 2.9 / 2.10 crack and flyby
            new Target("2.9", "SonicBulletSoundPlayer", "FireBullet", "M"),
            new Target("2.10", "FlyingBulletSoundPlayer", "FrameCoroutine", "M"),
            new Target("2.10", "FlyingBulletSoundPlayer", "PlaySound", "M"),
            // 2.11 snapshot
            new Target("2.11", "BetterAudio", "TransitToEnvironment", "M"),
            new Target("2.11", "Audio.AmbientSubsystem.EnvironmentSoundBlendSystem", "OnPlayerRoomChanged", "M"),
            new Target("2.11", "Audio.AmbientSubsystem.EnvironmentSoundBlendSystem", "_ambientSoundBlenders", "F"),
            new Target("2.11", "Audio.AmbientSubsystem.AmbientSoundBlender", "_currentOutdoorFilterValue", "F"),
            new Target("2.11", "Audio.AmbientSubsystem.AmbientSoundBlender", "BlendToOutdoor", "M"),
            new Target("2.11", "EFT.EnvironmentEffect.EnvironmentManager", "SetTriggerForPlayer", "M"),
            new Target("fix3", "Audio.AmbientSubsystem.AmbientSoundBlender", "BlendToIndoor", "M"),
            new Target("fix3", "Audio.AmbientSubsystem.AmbientSoundBlender", "BlendToTargetValue", "M"),
            new Target("fix3", "Audio.AmbientSubsystem.AmbientSoundBlender", "CalculateAndApplyMixerParameters", "M"),
            new Target("fix3", "Audio.AmbientSubsystem.AmbientSoundBlender", "SetOutdoorMixerParams", "M"),
            new Target("fix3", "Audio.AmbientSubsystem.AmbientSoundBlender", "SetIndoorMixerParams", "M"),
            new Target("fix3", "Audio.AmbientSubsystem.EnvironmentSoundBlendSystem", "method_0", "M"),
            new Target("fix3", "Audio.SpatialSystem.OutdoorPortalsDataContainer", "TryGetOutdoorPortalData", "M"),
            new Target("fix3", "Audio.SpatialSystem.OutdoorPortalsDataContainer", "TryGetIndoorRoomsIdsByOutRoomID", "M"),
            new Target("fix3", "Audio.SpatialSystem.SpatialAudioSystem", "ListenerCurrentOutdoorRoomID", "P"),
            new Target("fix3", "BetterAudio", "Snapshots", "F"),
            new Target("fix3", "BetterAudio", "Master", "F"),
            new Target("fix3", "Audio.SpatialSystem.OutdoorPortalsDataContainer", "TryGetPortalsByRelativeOutdoorRoomID", "M"),
            new Target("fix3", "Audio.SpatialSystem.BaseSpatialAudioPortal", "FrontRoom", "P"),
            new Target("fix3", "Audio.SpatialSystem.BaseSpatialAudioPortal", "BackRoom", "P"),
            new Target("fix3", "Audio.AmbientSubsystem.AmbientSoundBlender", "_masterMixer", "F"),
            new Target("fix3c", "WeaponSoundPlayer", "StopFiringLoop", "M"),
            new Target("fix3c", "EFT.SoundBank", "PickClipsByDistance", "M"),
            new Target("fix3c", "EFT.SoundBank", "SelectRandomClip", "M"),
            new Target("fix3c", "EFT.SoundBank", "Environments", "F"),
            new Target("5", "Audio.ReverbSubsystem.ReverbSuperSource", "_spatializerA", "F"),
            new Target("5", "Audio.ReverbSubsystem.ReverbSuperSource", "_spatializerB", "F"),
            new Target("5", "Audio.SpatialSystem.BaseSpatialAudioSource", "ReverbSendDB", "P"),
            new Target("5", "Audio.SpatialSystem.BaseSpatialAudioSource", "EarlyReflectionsSendDB", "P"),
            new Target("5", "Audio.SpatialSystem.BaseSpatialAudioSource", "UpdateParameters", "M"),
            new Target("fix4", "Audio.SpatialSystem.ParallelSoundPropagationCalculator", "OnUpdate", "M"),
            new Target("9k", "BetterSource", "UpdateSourceVolume", "M"),
            new Target("9k", "BetterSource", "OcclusionVolumeFactor", "P"),
            new Target("9k", "BetterSource", "Position", "P"),
            new Target("9k", "Audio.SpatialSystem.AudioSourceContainer", "_sources", "F"),
            // 0.6.9
            new Target("8g", "Audio.AmbientSubsystem.RoomToneController", "ChangeRoomTone", "M"),
            new Target("8g", "Audio.AmbientSubsystem.RoomToneController", "_prevEnteredRoom", "F"),
            new Target("8g", "SpatialAudioRoom", "_roomAmbientSoundPlayer", "F"),
            new Target("8g", "Audio.AmbientSubsystem.RoomAmbientSoundPlayer", "_source", "F"),
            new Target("8g", "Audio.AmbientSubsystem.RoomAmbientData", "RoomTone", "F"),
            new Target("7", "Audio.AmbientSubsystem.PrecipitationAmbientBlender", "UpdateGlobalsVolumeMult", "M"),
            new Target("7", "Audio.AmbientSubsystem.PrecipitationAmbientBlender", "_globalsVolumeMult", "F"),
            new Target("8", "Audio.AmbientSubsystem.AmbientSoundBlender", "_indoorMixerParams", "F"),
            // 0.6.11
            new Target("3m", "EFT.SoundBank", "PickSingleClip", "M"),
            new Target("3m", "WeaponSoundPlayer", "IsSilenced", "P"),
            new Target("9k", "BetterAudio", "PlayAtPoint", "M"),
            new Target("9k", "Audio.SpatialSystem.SpatialAudioSystem", "_audioRoomStorage", "F"),
            new Target("6", "SuperBetterAudioQueue", "Enqueue", "M"),
            new Target("1d", "WeaponSoundPlayer", "RegisterSourceForOcclusionProcess", "M"),
            new Target("1d", "Audio.SpatialSystem.SpatialAudioSystem", "_registeredPlayerProcessors", "F"),
            new Target("1d", "Audio.SpatialSystem.SoundOcclusionProcessor", "_occluder", "F"),
            new Target("1d", "Audio.SpatialSystem.CombinedSpatialAudioOccluder", "_smoothedPropagationEffect", "F"),
            new Target("1d", "Audio.SpatialSystem.CombinedSpatialAudioOccluder", "_smoothedObstructionEffect", "F"),
            new Target("log", "WeaponSoundPlayer", "_isFiring", "F"),
            new Target("6.3h", "EFT.SoundBank", "SelectRandomClip", "M"),
            new Target("6.3h", "EFT.SoundBank", "PickClipsByDistance", "M"),
            new Target("1e", "SuperBetterAudioQueue", "Update", "M"),
            new Target("6.1", "BetterAudioQueue", "Pose", "M"),
            new Target("1e", "SuperBetterAudioQueue", "_samples", "F"),
            new Target("1e", "SuperBetterAudioQueue", "_nextUpdate", "F"),
            new Target("1e", "SuperBetterAudioQueue", "_lastSource", "F"),
            new Target("1e", "BetterAudio", "BorrowWeaponAudioQueue", "M"),
            new Target("1e", "WeaponSoundPlayer", "Release", "M"),
            new Target("1e", "WeaponSoundPlayer", "_releaseTime", "F"),
            new Target("1e", "WeaponSoundPlayer", "_startFire", "F"),
            new Target("1e", "WeaponSoundPlayer", "_delay", "F"),
            new Target("6.1", "Audio.SpatialSystem.ParallelSoundPropagationCalculator", "_propagationJobScheduler", "F"),
            new Target("6.1", "Audio.SpatialSystem.ParallelSoundPropagationCalculator", "_prevRoomPairID", "F"),
            new Target("6.1", "Audio.SpatialSystem.SoundPropagationJobScheduler", "_routeNodesData", "F"),
            new Target("6.1", "Audio.SpatialSystem.SoundPropagationJobScheduler", "_routesSettingsData", "F"),
            new Target("6.1", "Audio.SpatialSystem.SoundPropagationJobScheduler", "_lowestCost", "F"),
            new Target("6.1", "Audio.SpatialSystem.SoundPropagationJobScheduler", "_lowestDistance", "F"),
            new Target("6.1", "Audio.SpatialSystem.SpatialAudioSystem", "TryGetMatchingRoomPair", "M"),
            new Target("6", "WeaponSoundPlayer", "StopFiringLoop", "M"),
            new Target("6", "BetterAudio", "GunshotMixerGroup", "F"),
            new Target("6", "EFT.Ballistics.BallisticCollider", "TypeOfMaterial", "P"),
            new Target("3c/6", "EFT.Interactive.WindowBreaker", "ObstructiveCollider", "F"),
            new Target("7", "Audio.AmbientSubsystem.PrecipitationAmbientBlender", "SetClip", "M"),
            new Target("7", "Audio.AmbientSubsystem.PrecipitationAmbientBlender", "_crossfader", "F"),
            new Target("7", "Audio.AmbientSubsystem.PrecipitationAmbientBlender", "_currentAmbientData", "F"),
            new Target("7", "Audio.AmbientSubsystem.PrecipitationAmbientBlender", "_currentSeason", "F"),
            new Target("7", "Audio.AmbientSubsystem.PrecipitationAmbientBlender", "_ambientSoundData", "F"),
            new Target("7", "Audio.AmbientSubsystem.PrecipitationAmbientBlender", "_lastClipHash", "F"),
            new Target("8", "Audio.AmbientSubsystem.PrecipitationAmbientBlender", "CurrentTargetVolume", "P"),
            new Target("8", "Audio.AmbientSubsystem.PrecipitationAmbientBlender", "StartCrossfader", "M"),
            new Target("8", "Audio.AmbientSubsystem.PrecipitationAmbientBlender", "_precipitationSource", "F"),
            new Target("8", "Audio.AmbientSubsystem.PrecipitationAmbientBlender", "_environment", "F"),
            new Target("8", "Audio.AmbientSubsystem.WindAmbientBlender", "_windSource", "F"),
            new Target("8", "Audio.AmbientSubsystem.WindAmbientBlender", "_windMixSource", "F"),
            new Target("8", "Audio.AmbientSubsystem.DayTimeAmbientBlender", "_outdoorAmbientDaySource", "F"),
            new Target("8", "Audio.AmbientSubsystem.DayTimeAmbientBlender", "_outdoorAmbientNightSource", "F"),
            new Target("4b", "Audio.SpatialSystem.SpatialAudioSystem", "_audioRoomStorage", "F"),
            new Target("4b", "Audio.SpatialSystem.AudioRoomStorage", "FindActualCurrentRoom", "M"),
            new Target("4b", "Audio.AmbientSubsystem.RoomAmbientData", "OutdoorAmbientVolume", "F"),
            new Target("fix4", "Audio.SpatialSystem.ParallelSoundPropagationCalculator", "_propagationScore", "F"),
            new Target("fix4", "Audio.SpatialSystem.ParallelSoundPropagationCalculator", "_earlyOutThisFrame", "F"),
            // 3.2 bot portals
            new Target("3.2", "Audio.SpatialSystem.AudioRoomStorage", "FindActualCurrentRoom", "M"),
            new Target("3.2", "BotHearingSensor", "OnSoundPlayedInner", "M"),
            // diagnostics itself
            new Target("diag", "Audio.SpatialSystem.SpatialAudioSystem", "_allRegisteredSoundContainers", "F"),
            new Target("diag", "Audio.SpatialSystem.SpatialAudioSystem", "_allRegisteredProcessorsByContainerID", "F"),
            new Target("diag", "BaseSoundPlayer", "playersBridge", "F"),
            new Target("diag", "WeaponSoundPlayer", "_queue", "F"),
            new Target("diag", "BetterSource", "Preset", "F"),
        };

        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Real Sound Simulation self-check  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("Fix   Status   Member");
            int missing = 0;
            foreach (Target t in Targets)
            {
                bool ok = Resolve(t);
                if (!ok) missing++;
                sb.AppendLine($"{t.Fix,-5} {(ok ? "OK     " : "MISSING")}  {t.Type}.{t.Member}");
            }
            sb.AppendLine(missing == 0 ? "All targets present." : $"{missing} target(s) missing: the fixes listed against them need re-mapping for this build.");

            Directory.CreateDirectory(DiagLog.RootDir);
            File.WriteAllText(Path.Combine(DiagLog.RootDir, "selfcheck.txt"), sb.ToString());
            if (missing == 0) Plugin.Log.LogInfo($"Self-check: all {Targets.Length} targets present.");
            else Plugin.Log.LogWarning($"Self-check: {missing} of {Targets.Length} targets missing. See {Path.Combine(DiagLog.RootDir, "selfcheck.txt")}");
        }

        // 0.6.20: every method target (all overloads), for SharedPatches: which other mods patch them.
        internal static List<(string Fix, MethodBase Method)> MethodTargets()
        {
            var list = new List<(string, MethodBase)>();
            var seen = new HashSet<MethodBase>();
            foreach (Target t in Targets)
            {
                if (t.Kind != "M") continue;
                try
                {
                    Type type = AccessTools.TypeByName(t.Type);
                    if (type == null) continue;
                    foreach (MethodInfo m in type.GetMethods(AccessTools.all))
                        if (m.Name == t.Member && seen.Add(m)) list.Add((t.Fix, m));
                }
                catch { }
            }
            return list;
        }

        private static bool Resolve(Target t)
        {
            try
            {
                Type type = AccessTools.TypeByName(t.Type);
                if (type == null) return false;
                switch (t.Kind)
                {
                    case "M": return AccessTools.Method(type, t.Member) != null;
                    case "F": return AccessTools.Field(type, t.Member) != null;
                    case "P": return AccessTools.Property(type, t.Member) != null;
                }
            }
            catch (AmbiguousMatchException)
            {
                return true; // overloads exist; that's fine
            }
            catch
            {
            }
            return false;
        }
    }
}
