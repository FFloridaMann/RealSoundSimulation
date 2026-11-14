using Audio.AmbientSubsystem;
using Comfort.Common;
using UnityEngine;

namespace RealSoundSimulation.Fixes
{
    // The game's indoor/outdoor ambience blender (EnvironmentSoundBlendSystem), shared by fix 3 and the ambience log.
    // The game's AmbientAudioSystem only sets up blenders placed under it, so that object is searched, not the whole scene.
    // Before 0.9.2 both searched the whole scene until they found one. The Icebreaker custom map has none, and a whole-scene
    // search there took ~50 ms: every 0.1 s with bug report logs on, every 2 s without. Now the whole scene is searched at most
    // once per raid, as a fallback, and a map with no blender leaves the doorway ambience fade to the map's own sound.
    internal static class BlendSystem
    {
        private static EnvironmentSoundBlendSystem _found;
        private static float _nextTry;
        private static bool _sceneSearched;

        public static void Reset() { _found = null; _nextTry = 0f; _sceneSearched = false; }

        public static EnvironmentSoundBlendSystem Get()
        {
            if (_found != null) return _found;
            if (Time.unscaledTime < _nextTry) return null;
            _nextTry = Time.unscaledTime + 2f;
            if (!MonoBehaviourSingleton<AmbientAudioSystem>.Instantiated) return null;
            AmbientAudioSystem ambient = MonoBehaviourSingleton<AmbientAudioSystem>.Instance;
            if (ambient == null) return null;
            _found = ambient.GetComponentInChildren<EnvironmentSoundBlendSystem>();
            if (_found != null)
            {
                Plugin.Log.LogInfo("fix 3: ambience blender found under the game's ambient audio system");
                return _found;
            }
            if (_sceneSearched) return null;
            _sceneSearched = true;
            _found = Object.FindObjectOfType<EnvironmentSoundBlendSystem>();
            Plugin.Log.LogInfo(_found != null
                ? "fix 3: ambience blender found by a scene search (not under the ambient audio system)"
                : "fix 3: this map has no indoor/outdoor ambience blender; the doorway ambience fade is left to the map");
            return _found;
        }
    }
}
