using System.Collections.Generic;
using UnityEngine;

namespace HiddenBull.Scenes
{
    public enum SceneRole { None, Default, Menu }

    /// <summary>A loadable scene definition. Id is the stable key sent over the network.</summary>
    [CreateAssetMenu(fileName = "SceneData", menuName = "HiddenBull/Scene Data")]
    public sealed class SceneData : ScriptableObject
    {
        [SerializeField] private SceneRole role;
        [SerializeField] private string displayName;
        [SerializeField] private SceneField scene;

        /// <summary>A scene plays one role: Default = connect fallback; Menu = disconnect target.</summary>
        public SceneRole Role => role;
        public bool IsDefault => role == SceneRole.Default;
        public bool IsMenu => role == SceneRole.Menu;

        /// <summary>Network key = the scene's name from the SceneField (no separate id to keep in sync).</summary>
        public string Id => scene?.SceneName;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? Id : displayName;
        public string SceneName => scene?.SceneName;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (role == SceneRole.None) return;
            UnityEditor.EditorApplication.delayCall -= EnforceSingleRole;
            UnityEditor.EditorApplication.delayCall += EnforceSingleRole;
        }
        private void EnforceSingleRole()
        {
            if (this == null || role == SceneRole.None) return;   // asset may be gone by the deferred call
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets($"t:{nameof(SceneData)}"))
            {
                var other = UnityEditor.AssetDatabase.LoadAssetAtPath<SceneData>(
                    UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                if (other == null || other == this || other.role != role) continue;
                other.role = SceneRole.None;   // only one scene per role
                UnityEditor.EditorUtility.SetDirty(other);
            }
            UnityEditor.AssetDatabase.SaveAssets();
        }
#endif
    }

    /// <summary>Registry of known scenes, keyed by SceneData.Id. The network layer sends an Id;
    /// the client resolves it here to find which scene to load.</summary>
    internal static class SceneUtils
    {
        private static readonly Dictionary<string, SceneData> _scenes = new(System.StringComparer.OrdinalIgnoreCase);
        private static SceneData _default;
        private static SceneData _menu;
        private static bool _loaded;

        public static IReadOnlyCollection<SceneData> All { get { EnsureLoaded(); return _scenes.Values; } }
        public static SceneData Default { get { EnsureLoaded(); return _default; } }
        public static SceneData Menu { get { EnsureLoaded(); return _menu; } }

        public static bool TryGet(string id, out SceneData data)
        {
            EnsureLoaded();
            return _scenes.TryGetValue(id, out data);
        }
        public static SceneData Resolve(string id)
        {
            EnsureLoaded();
            if (!string.IsNullOrEmpty(id) && _scenes.TryGetValue(id, out var data)) return data;
            return _default;
        }

        private static void EnsureLoaded() { if (!_loaded) Reload(); }
        public static void Reload()
        {
            _scenes.Clear();
            _default = null;
            _menu = null;
            foreach (var data in Resources.LoadAll<SceneData>(""))
            {
                if (data == null || string.IsNullOrEmpty(data.Id)) continue;
                if (!_scenes.TryAdd(data.Id, data))
                { Debug.LogWarning($"[SceneUtils] Duplicate scene id '{data.Id}' ({data.name}); ignoring."); continue; }

                if (data.IsDefault)
                {
                    if (_default == null) _default = data;
                    else Debug.LogWarning($"[SceneUtils] Multiple default scenes; keeping '{_default.Id}', ignoring '{data.Id}'.");
                }
                else if (data.IsMenu)
                {
                    if (_menu == null) _menu = data;
                    else Debug.LogWarning($"[SceneUtils] Multiple menu scenes; keeping '{_menu.Id}', ignoring '{data.Id}'.");
                }
            }
            _loaded = true;
            Debug.Log($"[SceneUtils] Loaded {_scenes.Count} scene(s). Default: {(_default != null ? _default.Id : "<none>")}, Menu: {(_menu != null ? _menu.Id : "<none>")}");
        }
    }
}