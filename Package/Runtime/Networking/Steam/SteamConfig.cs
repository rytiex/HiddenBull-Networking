using UnityEngine;

namespace HiddenBull.Networking.Steam
{
    /// <summary>
    /// Steam app identity, edited in the Inspector and assigned on SteamLifecycle. Lives as an
    /// asset so it ships with the game/package without a scene dependency.
    /// </summary>
    [CreateAssetMenu(fileName = "SteamConfig", menuName = "HiddenBull/Steam Config")]
    public sealed class SteamConfig : ScriptableObject
    {
        [SerializeField] private uint appId = 480;
        [SerializeField] private string gameDescription = "Name Of Game";
        [SerializeField] private string modDir = "nameofgame";

        public uint AppId => appId;
        public string GameDescription => gameDescription;
        public string ModDir => modDir;
    }
}