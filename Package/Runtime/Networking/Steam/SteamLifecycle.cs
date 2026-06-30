using HiddenBull.Networking.Data;
using UnityEngine;

namespace HiddenBull.Networking.Steam
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Network/Steam Lifecycle")]
    public sealed class SteamLifecycle : MonoBehaviour
    {
        public static System.Func<System.Threading.Tasks.Task> PreServerStart;

        private void Awake()
        {
            var found = Resources.LoadAll<SteamConfig>("");
            if (found.Length == 0 || found[0] == null)
            {
                Debug.LogError($"[{nameof(SteamLifecycle)}] No SteamConfig found in Resources — networking disabled.");
                return;
            }
            SteamInformation.Configure(found[0]);

            if (SteamInformation.IsDedicated)
                InitDedicated();
            else
                InitClient();
        }
        private void OnDestroy()
        {
            if (SteamInformation.IsDedicated)
            {
                SteamServerBootstrap.OnReady -= HandleServerReady;
                SteamServerBootstrap.OnFailed -= HandleServerFailed;
                SteamServerBootstrap.OnDisconnected -= HandleServerDisconnected;
            }
            else
            {
                SteamClientBootstrap.OnJoinServerRequested -= HandleJoinServerRequested;
                NetworkChatGate.SteamFilter = null;
                SteamServerBrowser.Disable();
            }
        }

        private void InitClient()
        {
            SteamClientBootstrap.OnJoinServerRequested += HandleJoinServerRequested;
            SteamClientBootstrap.Init();
            SteamServerBrowser.Enable();

            // Steam per-user text filter for incoming chat (respects each player's Steam profanity setting).
            if (SteamInformation.Initialized)
            {
                Steamworks.SteamUtils.InitFilterText();
                NetworkChatGate.SteamFilter = (sender, text) =>
                    Steamworks.SteamUtils.FilterText(Steamworks.TextFilteringContext.Chat, sender, text);
            }
        }
        private void InitDedicated()
        {
            var config = ServerConfig.Load();
            SteamServerBootstrap.OnReady += HandleServerReady;
            SteamServerBootstrap.OnFailed += HandleServerFailed;
            SteamServerBootstrap.OnDisconnected += HandleServerDisconnected;
            SteamServerBootstrap.Init(config);
        }

        private void HandleJoinServerRequested(string address, string password)
        {
            if (NetworkSessionManager.singleton == null)
            {
                Debug.LogError($"[{nameof(SteamLifecycle)}] NetworkSessionManager not found, cannot start client.");
                return;
            }

            NetworkSessionManager.singleton.StartClient(new ClientConnectSettings
            {
                Address = address,
                Password = password
            });
        }

        private async void HandleServerReady()
        {
            if (NetworkSessionManager.singleton == null)
            { Debug.LogError($"[{nameof(SteamLifecycle)}] NetworkSessionManager not found, cannot start server."); return; }

            if (PreServerStart != null) await PreServerStart();

            var config = ServerConfig.Load();
            if (string.IsNullOrEmpty(config.Scene))
            {
                var fallback = Scenes.SceneUtils.Resolve(config.Scene); // = default
                if (fallback != null)
                {
                    config.Scene = fallback.Id;
                    config.Save();
                    Debug.Log($"[{nameof(SteamLifecycle)}] No scene in config; defaulted to '{config.Scene}' and saved.");
                }
            }

            NetworkSessionManager.singleton.SetServerScene(config.Scene);
            NetworkSessionManager.singleton.StartServer(ServerStartSettings.FromConfig(config));
        }
        private void HandleServerFailed(string reason)
        {
            Debug.LogError($"[{nameof(SteamLifecycle)}] Server failed: {reason}. Quitting.");
            Application.Quit(1);
        }
        private void HandleServerDisconnected()
        {
            if (NetworkSessionManager.singleton != null && Mirror.NetworkServer.active)
                NetworkSessionManager.singleton.StopServer();
        }

        private void OnApplicationQuit()
        {
            if (SteamInformation.IsDedicated)
                SteamServerBootstrap.Shutdown();
            else
                SteamClientBootstrap.Shutdown();
        }
    }
}