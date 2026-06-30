using UnityEngine;

namespace HiddenBull
{
    /// <summary>
    /// Boots the persistent manager object from Resources before any scene loads, and keeps it alive
    /// across scene loads. Scene-independent: works whether you enter Play from any scene or a build.
    /// </summary>
    internal sealed class GameLoadManager : MonoBehaviour
    {
        private const string BootPrefab = "BOOT_GameInitializer";
        private static GameLoadManager _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (_instance != null) return;   // already booted (e.g. domain reload disabled in editor)

            var prefab = Resources.Load<GameLoadManager>(BootPrefab);
            if (prefab == null)
            {
                Debug.LogError($"[{nameof(GameLoadManager)}] '{BootPrefab}' not found in Resources.");
                return;
            }

            _instance = Instantiate(prefab);
            _instance.name = prefab.name;
            DontDestroyOnLoad(_instance.gameObject);
        }
        private void Awake()
        {
            // If the prefab also got dropped into a scene, keep only the bootstrapped instance.
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
        }
    }
}