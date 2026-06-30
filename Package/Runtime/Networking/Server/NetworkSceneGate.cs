using System;
using System.Threading.Tasks;

namespace HiddenBull.Networking
{
    /// <summary>
    /// Seam for HOW a scene is loaded (loading screen, bundle, etc.). Used on both server (its own
    /// scene) and client (the server's scene). Null = framework falls back to SceneManager by name.
    /// The network layer only deals with scene NAMES; it never loads scenes itself directly.
    /// </summary>
    public static class NetworkSceneGate
    {
        /// <summary>Load a scene by its name and complete when it is fully loaded. Null = use SceneManager.</summary>
        public static Func<string, Task> LoadSceneAsync;
    }
}