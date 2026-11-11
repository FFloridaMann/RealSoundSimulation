using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RealSoundSimulation.Fixes
{
    // 0.7.11: Fika (SPT co-op). The read-only review of Fika 2.4.6 found the mod works alongside it as it is: Fika patches none of
    // the methods this mod patches, other players' shots reach WeaponSoundPlayer.FireBullet as third-person shots (so they're
    // "other guns"), and each grenade explodes once on each machine. The one gap: a Fika headless host (the machine that runs the
    // bots, with no one listening) would run all of this mod. So this mod switches itself off there.
    // Fika is looked up by name while the game runs, so this mod doesn't need it and copies none of its code (Fika is CC BY-NC-SA).
    internal static class Fika
    {
        private const string BackendUtils = "Fika.Core.Main.Utils.FikaBackendUtils";
        private static bool _looked;
        private static Type _utils;

        private static Type Utils
        {
            get
            {
                if (!_looked)
                {
                    _looked = true;
                    try { _utils = AccessTools.TypeByName(BackendUtils); }
                    catch { _utils = null; }
                }
                return _utils;
            }
        }

        // Fika is installed.
        public static bool Present => Utils != null;

        // No graphics: a headless game, Fika's or any other. Checked at start-up.
        public static bool NoGraphics(out string why)
        {
            why = Application.isBatchMode ? "batch mode" : SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null ? "no graphics device" : null;
            return why != null;
        }

        // Fika's own headless flag, set by its headless plugin. Checked at each raid start, since that plugin may set it after this one loads.
        public static bool IsHeadless()
        {
            try
            {
                PropertyInfo p = Utils != null ? AccessTools.Property(Utils, "IsHeadless") : null;
                return p != null && p.GetValue(null) is bool b && b;
            }
            catch { return false; }
        }
    }
}
