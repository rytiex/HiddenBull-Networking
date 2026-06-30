using HiddenBull.Networking.Server;
using HiddenBull.Networking.Data;

using UnityEngine.SceneManagement;
using UnityEngine;
using Mirror;

using System.Collections.Generic;
using System.Threading.Tasks;

namespace HiddenBull.Networking
{
    public sealed partial class NetworkSessionManager
    {
        private const float SceneLoadTimeoutSeconds = 120f;
        private const float PingBroadcastInterval = .5f;

        private readonly Dictionary<int, Coroutine> _sceneTimeouts = new();
        private string _currentSceneId;
        private bool _serverSceneLoaded;

        private readonly Dictionary<int, ClientData> _clients = new();
        private readonly HashSet<int> _announced = new();
        private float _pingTimer;

        public override void OnStartServer()
        {
            base.OnStartServer();
            ServerBanModeration.OnKickRequested += HandleKickRequested;
            ServerRoles.OnRoleAssigned += HandleRoleChanged;
            ServerRoles.OnRoleRemoved += HandleRoleRemoved;

            NetworkServer.RegisterHandler<SceneReadyMessage>(OnSceneReady);
            NetworkServer.RegisterHandler<ChatSendMessage>(ServerChat.HandleClientSend);

            NetworkState.Server.RaiseStarted_Internal();
            NetworkState.Roles.SetCatalog_Internal(new List<string>(ServerRoles.AllRoles.Keys).ToArray());
            Debug.Log($"[{nameof(NetworkSessionManager)}] Server started.");

            // Load the active scene (default fallback) on the server before clients arrive.
            ServerLoadAndBroadcast();
        }
        public override void OnStopServer()
        {
            base.OnStopServer();
            ServerBanModeration.OnKickRequested -= HandleKickRequested;
            ServerRoles.OnRoleAssigned -= HandleRoleChanged;
            ServerRoles.OnRoleRemoved -= HandleRoleRemoved;

            NetworkServer.UnregisterHandler<SceneReadyMessage>();
            NetworkServer.UnregisterHandler<ChatSendMessage>();

            foreach (var co in _sceneTimeouts.Values) if (co != null) StopCoroutine(co);
            _sceneTimeouts.Clear();
            _announced.Clear();
            _currentSceneId = null;
            _serverSceneLoaded = false;
            _pingTimer = 0f;

            _clients.Clear();
            ServerChat.Clear();
            NetworkState.Clear();
            NetworkState.Server.RaiseStopped_Internal();
            Debug.Log($"[{nameof(NetworkSessionManager)}] Server stopped.");
        }

        /// <summary>
        /// The framework never spawns players; the game does, via NetworkServer.AddPlayerForConnection
        /// when NetworkState.Server.OnClientConnected fires. No-op blocks Mirror's client-initiated
        /// AddPlayer path so a stray/forged AddPlayerMessage can't auto-spawn anything.
        /// </summary>
        public override void OnServerAddPlayer(NetworkConnectionToClient conn) { }
        public override void OnServerConnect(NetworkConnectionToClient conn)
        {
            base.OnServerConnect(conn);

            if (conn.authenticationData is not ClientData data)
            {
                Debug.LogError($"[{nameof(NetworkSessionManager)}] Connection {conn.connectionId} has no ClientData. Disconnecting.");
                conn.Disconnect();
                return;
            }

            _clients[conn.connectionId] = data;

            if (conn != NetworkServer.localConnection)
            {
                conn.Send(new RoleCatalogMessage { RoleNames = new List<string>(ServerRoles.AllRoles.Keys).ToArray() });
                conn.Send(new TickConfigMessage { TickRate = NetworkState.Tick.TickRate });
            }

            // If the server scene is ready, tell this connection to load it now. If the server is still
            // loading its scene, BroadcastSceneToAll reaches this connection once the load completes.
            if (_serverSceneLoaded) SendSceneLoad(conn);

            Debug.Log($"[{nameof(NetworkSessionManager)}] {data.PlayerName} ({data.SteamId}) connected; awaiting scene ready.");
        }
        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            ClearSceneTimeout(conn);

            if (_clients.TryGetValue(conn.connectionId, out var data))
            {
                _clients.Remove(conn.connectionId);
                ServerChat.RemoveAll(data.SteamId);

                if (_announced.Remove(conn.connectionId))
                {
                    RebuildRoster();
                    NetworkState.Server.RaiseClientDisconnected_Internal(data);
                    Debug.Log($"[{nameof(NetworkSessionManager)}] {data.PlayerName} ({data.SteamId}) left.");
                }
            }

            base.OnServerDisconnect(conn);
        }

        /// <summary>
        /// Sets the active scene (default fallback) and, on an active server, loads it and re-syncs
        /// clients. The server never spawns players - the game does that on OnClientConnected.
        /// </summary>
        public bool SetServerScene(string sceneId)
        {
            var scene = Scenes.SceneUtils.Resolve(sceneId);
            string resolvedId = scene != null ? scene.Id : string.Empty;

            // Already on this scene and it's loaded -> nothing to do (no reload / re-broadcast).
            if (_serverSceneLoaded && resolvedId == _currentSceneId) return false;

            _currentSceneId = resolvedId;
            if (NetworkServer.active) ServerLoadAndBroadcast();
            return true;
        }

        private async void ServerLoadAndBroadcast()
        {
            var scene = Scenes.SceneUtils.Resolve(_currentSceneId);
            _currentSceneId = scene != null ? scene.Id : string.Empty;   // normalize to the resolved id (default if it fell back)

            // No scene flagged anywhere -> run sceneless; clients just ready.
            if (scene == null || string.IsNullOrEmpty(scene.SceneName))
            {
                _serverSceneLoaded = true;
                BroadcastSceneToAll();
                return;
            }

            _serverSceneLoaded = false;
            NetworkServer.SetAllClientsNotReady();

            try
            {
                if (NetworkSceneGate.LoadSceneAsync != null)
                    await NetworkSceneGate.LoadSceneAsync(scene.SceneName);
                else
                {
                    var op = SceneManager.LoadSceneAsync(scene.SceneName);
                    if (op != null)
                    {
                        var tcs = new TaskCompletionSource<bool>();
                        op.completed += _ => tcs.SetResult(true);
                        await tcs.Task;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[{nameof(NetworkSessionManager)}] Server scene load '{scene.SceneName}' failed: {ex.Message}.");
            }

            if (!NetworkServer.active) return;   // server stopped mid-load

            NetworkServer.SpawnObjects();
            _serverSceneLoaded = true;
            BroadcastSceneToAll();
            NetworkState.Scene.RaiseLoaded_Internal(_currentSceneId);
            Debug.Log($"[{nameof(NetworkSessionManager)}] Server scene '{scene.SceneName}' loaded.");
        }
        private void BroadcastSceneToAll()
        {
            foreach (var conn in NetworkServer.connections.Values)
                SendSceneLoad(conn);
        }
        private void SendSceneLoad(NetworkConnectionToClient conn)
        {
            conn.Send(new SceneLoadMessage { SceneId = _currentSceneId ?? string.Empty });

            // Host (local) has the scene in-process and never times out; remote clients do.
            if (conn == NetworkServer.localConnection) return;
            ClearSceneTimeout(conn);
            _sceneTimeouts[conn.connectionId] = StartCoroutine(SceneLoadTimeout(conn));
        }

        /// <summary>Client finished loading the active scene and is ready. Announce it (= "connected").</summary>
        private void OnSceneReady(NetworkConnectionToClient conn, SceneReadyMessage msg)
        {
            if (!_clients.TryGetValue(conn.connectionId, out var data)) return;

            ClearSceneTimeout(conn);
            if (!conn.isReady) NetworkServer.SetClientReady(conn);

            if (_announced.Contains(conn.connectionId)) return;   // already announced (e.g. a scene change)
            AnnounceReady(conn, data);
        }
        private void AnnounceReady(NetworkConnectionToClient conn, ClientData data)
        {
            if (!_announced.Add(conn.connectionId)) return;
            RebuildRoster();
            NetworkState.Server.RaiseClientConnected_Internal(data);
            ServerChat.Join(data.SteamId, ServerChat.GlobalChannel, leaveOthers: false);
            ServerChat.SendCatalogTo(conn);
            Debug.Log($"[{nameof(NetworkSessionManager)}] {data.PlayerName} ({data.SteamId}) ready.");
        }

        private void ClearSceneTimeout(NetworkConnectionToClient conn)
        {
            if (conn == null) return;
            if (_sceneTimeouts.TryGetValue(conn.connectionId, out var co))
            {
                if (co != null) StopCoroutine(co);
                _sceneTimeouts.Remove(conn.connectionId);
            }
        }
        private System.Collections.IEnumerator SceneLoadTimeout(NetworkConnectionToClient conn)
        {
            yield return new WaitForSeconds(SceneLoadTimeoutSeconds);
            _sceneTimeouts.Remove(conn.connectionId);

            if (conn != null && !_announced.Contains(conn.connectionId) &&
                NetworkServer.connections.ContainsKey(conn.connectionId))
            {
                Debug.LogWarning($"[{nameof(NetworkSessionManager)}] Connection {conn.connectionId} did not finish the scene within {SceneLoadTimeoutSeconds}s. Dropping.");
                conn.Disconnect();
            }
        }

        /// <summary>Sends a localized notification to a single connection.</summary>
        public void SendNotification(NetworkConnectionToClient conn, ServerMessageType type, PicoShot.Localization.TextNode content)
        {
            if (conn == null) return;
            if (!_announced.Contains(conn.connectionId)) return;
            conn.Send(new ServerNotificationMessage
            {
                Type = type,
                Content = content.IsEmpty ? PicoShot.Localization.TextNode.Empty : content
            });
        }

        /// <summary>Sends a localized notification to all connected clients.</summary>
        public void BroadcastNotification(ServerMessageType type, PicoShot.Localization.TextNode content)
        {
            foreach (var conn in NetworkServer.connections.Values)
                SendNotification(conn, type, content);
        }

        /// <summary>
        /// True if the given SteamID is currently a connected client.
        /// Used by DuplicateConnectionValidator and the IP-mode identity transform.
        /// </summary>
        private bool IsSteamIdInUse(ulong steamId)
        {
            foreach (var data in _clients.Values)
                if (data.SteamId == steamId)
                    return true;
            return false;
        }

        private void HandleRoleChanged(ulong steamId, string roleName) => RebuildRoster();
        private void HandleRoleRemoved(ulong steamId) => RebuildRoster();

        /// <summary>
        /// Builds the SteamID-keyed roster from ANNOUNCED (ready) connections only, applies it to the
        /// facade locally (dedicated consumers + host share the static), then sends it to remote clients.
        /// </summary>
        private void RebuildRoster()
        {
            var infos = new List<PlayerInfo>(_announced.Count);
            foreach (var kvp in _clients)
            {
                if (!_announced.Contains(kvp.Key)) continue;
                var data = kvp.Value;
                string role = ServerRoles.GetRoleName(data.SteamId);
                infos.Add(new PlayerInfo { SteamId = data.SteamId, Name = data.PlayerName, RoleName = role });
            }

            var arr = infos.ToArray();
            NetworkState.Players.ServerApply_Internal(arr);

            foreach (var conn in NetworkServer.connections.Values)
            {
                if (conn == NetworkServer.localConnection) continue;
                if (!_announced.Contains(conn.connectionId)) continue;
                conn.Send(new PlayerRosterMessage { Players = arr });
            }
        }

        /// <summary>
        /// Server-authoritative ping. Mirror already measures each connection's RTT
        /// (NetworkConnectionToClient.rtt); we sample it on a fixed interval and push a
        /// compact snapshot to everyone. Single source: the host reads the same numbers,
        /// so it simply shows ~0 for itself and there is no second code path.
        /// </summary>
        private void PumpPings()
        {
            if (!NetworkServer.active) return;

            _pingTimer += Time.unscaledDeltaTime;
            if (_pingTimer < PingBroadcastInterval) return;
            _pingTimer = 0f;

            var entries = new List<PingEntry>(_announced.Count);
            foreach (var conn in NetworkServer.connections.Values)
            {
                if (!_announced.Contains(conn.connectionId)) continue;
                if (conn.authenticationData is not ClientData data) continue;

                int ms = Mathf.RoundToInt((float)(conn.rtt * 1000.0));
                entries.Add(new PingEntry
                {
                    SteamId = data.SteamId,
                    PingMs = (ushort)Mathf.Clamp(ms, 0, ushort.MaxValue)
                });
            }

            var arr = entries.ToArray();
            NetworkState.Players.ApplyPings_Internal(arr);

            foreach (var conn in NetworkServer.connections.Values)
            {
                if (conn == NetworkServer.localConnection) continue;
                if (!_announced.Contains(conn.connectionId)) continue;
                conn.Send(new PingSnapshotMessage { Pings = arr });
            }
        }

        private void HandleKickRequested(ulong steamId, PicoShot.Localization.TextNode reason)
        {
            foreach (var kvp in _clients)
            {
                if (kvp.Value.SteamId != steamId) continue;

                if (NetworkServer.connections.TryGetValue(kvp.Key, out var conn))
                {
                    SendNotification(conn, ServerMessageType.Disconnect, reason);
                    StartCoroutine(DelayedKickDisconnect(conn));
                    Debug.Log($"[{nameof(NetworkSessionManager)}] Kicked {kvp.Value.PlayerName}. Reason: {reason}");
                }
                return;
            }

            Debug.LogWarning($"[{nameof(NetworkSessionManager)}] Kick requested for SteamID {steamId} but player not found.");
        }
        private System.Collections.IEnumerator DelayedKickDisconnect(NetworkConnectionToClient conn)
        {
            yield return new WaitForSeconds(.1f);
            conn?.Disconnect();
        }
    }
}