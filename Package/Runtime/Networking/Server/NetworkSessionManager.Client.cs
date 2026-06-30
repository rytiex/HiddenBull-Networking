using HiddenBull.Networking.Data;
using HiddenBull.Scenes;

using UnityEngine.SceneManagement;
using UnityEngine;

using System.Threading.Tasks;

namespace HiddenBull.Networking
{
    public sealed partial class NetworkSessionManager
    {
        public override void OnStartClient()
        {
            base.OnStartClient();
            NetworkState.Client.SetConnecting_Internal(true);

            Mirror.NetworkClient.RegisterHandler<ServerNotificationMessage>(OnServerNotification);
            Mirror.NetworkClient.RegisterHandler<PlayerRosterMessage>(msg => NetworkState.Players.ClientApply_Internal(msg.Players));
            Mirror.NetworkClient.RegisterHandler<PingSnapshotMessage>(msg => NetworkState.Players.ApplyPings_Internal(msg.Pings));
            Mirror.NetworkClient.RegisterHandler<RoleCatalogMessage>(msg => NetworkState.Roles.SetCatalog_Internal(msg.RoleNames));
            Mirror.NetworkClient.RegisterHandler<TickConfigMessage>(msg => NetworkState.Tick.SetTickRate_Internal(msg.TickRate));
            Mirror.NetworkClient.RegisterHandler<SceneLoadMessage>(OnSceneLoad, false);
            Mirror.NetworkClient.RegisterHandler<ChatMessage>(msg => NetworkState.Communication.Text.RaiseReceived_Internal(msg), false);
            Mirror.NetworkClient.RegisterHandler<ChannelMembershipMessage>(msg => NetworkState.Communication.SetChannels_Internal(msg.Channels), false);
            Mirror.NetworkClient.RegisterHandler<ChannelCatalogMessage>(msg => NetworkState.Communication.SetCatalog_Internal(msg.Channels), false);

            Debug.Log($"[{nameof(NetworkSessionManager)}] Client started.");
        }
        public override void OnStopClient()
        {
            base.OnStopClient();
            Mirror.NetworkClient.UnregisterHandler<ServerNotificationMessage>();
            Mirror.NetworkClient.UnregisterHandler<PlayerRosterMessage>();
            Mirror.NetworkClient.UnregisterHandler<PingSnapshotMessage>();
            Mirror.NetworkClient.UnregisterHandler<RoleCatalogMessage>();
            Mirror.NetworkClient.UnregisterHandler<TickConfigMessage>();
            Mirror.NetworkClient.UnregisterHandler<SceneLoadMessage>();
            Mirror.NetworkClient.UnregisterHandler<ChatMessage>();
            Mirror.NetworkClient.UnregisterHandler<ChannelMembershipMessage>();
            Mirror.NetworkClient.UnregisterHandler<ChannelCatalogMessage>();
        }

        private void OnServerNotification(ServerNotificationMessage msg)
        {
            switch (msg.Type)
            {
                case ServerMessageType.Info: NetworkState.Client.RaiseServerInfo_Internal(msg.Content); break;
                case ServerMessageType.Warning: NetworkState.Client.RaiseServerWarning_Internal(msg.Content); break;
                case ServerMessageType.Disconnect:
                    NetworkState.Client.RaiseDisconnectReason_Internal(msg.Content);
                    Debug.Log($"[{nameof(NetworkSessionManager)}] Disconnect reason: {msg.Content}");
                    break;
            }
        }

        public override void OnClientConnect()
        {
            // NOTE: intentionally NOT calling base.OnClientConnect(). Readiness is deferred until the
            // server's scene is loaded (see OnSceneLoad) - for the host too, after the server-side load.
            NetworkState.Players.SetLocal_Internal(new PlayerInfo
            {
                SteamId = Steam.SteamInformation.LocalSteamId,
                Name = Steam.SteamInformation.LocalName,
                RoleName = string.Empty
            });
            NetworkState.Client.RaiseConnected_Internal();
            Debug.Log($"[{nameof(NetworkSessionManager)}] Connected to server.");
        }
        public override void OnClientDisconnect()
        {
            base.OnClientDisconnect();
            NetworkState.Client.RaiseDisconnected_Internal();
            NetworkState.Clear();
            LoadMenuScene();
            Debug.Log($"[{nameof(NetworkSessionManager)}] Disconnected from server.");
        }

        private async void OnSceneLoad(SceneLoadMessage msg)
        {
            if (Mirror.NetworkServer.active || string.IsNullOrEmpty(msg.SceneId))
            {
                ReadyAndReport();
                return;
            }

            if (!SceneUtils.TryGet(msg.SceneId, out var scene) || string.IsNullOrEmpty(scene.SceneName))
            {
                Debug.LogError($"[{nameof(NetworkSessionManager)}] Server scene '{msg.SceneId}' is unknown to this client. Disconnecting.");
                StopClient();
                return;
            }

            try { await LoadSceneLocal(scene.SceneName); }
            catch (System.Exception ex)
            {
                Debug.LogError($"[{nameof(NetworkSessionManager)}] Scene load '{scene.SceneName}' failed: {ex.Message}. Disconnecting.");
                StopClient();
                return;
            }

            if (!Mirror.NetworkClient.active) return;
            NetworkState.Scene.RaiseLoaded_Internal(scene.Id);
            ReadyAndReport();
            Debug.Log($"[{nameof(NetworkSessionManager)}] Scene '{scene.SceneName}' loaded; reported ready.");
        }
        private async void LoadMenuScene()
        {
            var menu = SceneUtils.Menu;
            if (menu == null || string.IsNullOrEmpty(menu.SceneName)) return;
            if (SceneManager.GetActiveScene().name == menu.SceneName) return;
            await LoadSceneLocal(menu.SceneName);
        }
        private async Task LoadSceneLocal(string sceneName)
        {
            if (NetworkSceneGate.LoadSceneAsync != null) { await NetworkSceneGate.LoadSceneAsync(sceneName); return; }
            var op = SceneManager.LoadSceneAsync(sceneName);
            if (op != null)
            {
                var tcs = new TaskCompletionSource<bool>();
                op.completed += _ => tcs.SetResult(true);
                await tcs.Task;
            }
        }
        private void ReadyAndReport()
        {
            if (!Mirror.NetworkClient.ready) Mirror.NetworkClient.Ready();
            Mirror.NetworkClient.Send(new SceneReadyMessage());
        }

        /// <summary>
        /// Connects to a server. Steam auth ticket preparation and AuthRequestMessage
        /// send happen automatically through the authenticator.
        /// </summary>
        public void StartClient(ClientConnectSettings settings)
        {
            if (ulong.TryParse(settings.Address, out var sid) && sid != 0 && sid == Steam.SteamInformation.LocalSteamId)
            {
                Debug.LogWarning($"[{nameof(NetworkSessionManager)}] Cannot connect to your own server. Use Host mode.");
                return;
            }

            _authenticator.SetSessionPassword(settings.Password);
            networkAddress = settings.Address;
            base.StartClient();
            Debug.Log($"[{nameof(NetworkSessionManager)}] Connecting to {settings.Address}.");
        }
    }
}