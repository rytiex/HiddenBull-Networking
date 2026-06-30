using HiddenBull.Networking.Data;

using UnityEngine;
using Mirror;

using System.Net;
using System;

namespace HiddenBull.Networking.Transport
{
    internal sealed class HiddenBullTransport : Mirror.Transport
    {
#if UNITY_SERVER
        private static bool IsDedicated => SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;
#else
        private static bool IsDedicated => false;
#endif

        private TransportServer _server;
        private TransportClient _client;

        private ServerTransportMode _serverMode = ServerTransportMode.Steam;
        private ushort _serverPort = 27015;
        private bool _shutdownCalled;

        #region Init
        /// <summary>
        /// Must be called before Mirror's StartServer/StartHost. Sets the mode
        /// that ServerStart() will use when Mirror invokes it.
        /// </summary>
        public void SetServerMode(ServerTransportMode mode, ushort port = 27015)
        {
            _serverMode = mode;
            _serverPort = port;
            string portInfo =
                (mode == ServerTransportMode.IP || mode == ServerTransportMode.Both)
                ? $" (port {port})" : string.Empty;
            Debug.Log($"[{nameof(HiddenBullTransport)}] Server mode set: {mode}{portInfo}");
        }
        public override bool Available() => Steam.SteamInformation.Initialized;
        public override int GetMaxPacketSize(int channelId) =>
            channelId == Channels.Unreliable ? 1200 : 1048576;
        #endregion

        #region Client
        public override bool ClientConnected() => _client?.Connected ?? false;
        public override void ClientConnect(string address)
        {
            if (!Steam.SteamInformation.Initialized)
            {
                Debug.LogError($"[{nameof(HiddenBullTransport)}] Steam client not initialized.");
                OnClientDisconnected?.Invoke();
                return;
            }

            if (ulong.TryParse(address, out ulong steamId) &&
                steamId == Steam.SteamInformation.LocalSteamId)
            {
                Debug.LogError($"[{nameof(HiddenBullTransport)}] Cannot connect to your own SteamID. Use Host mode to play as both server and client.");
                OnClientDisconnected?.Invoke();
                return;
            }

            _client = new TransportClient();

            _client.OnConnected += () => OnClientConnected?.Invoke();
            _client.OnDisconnected += () => OnClientDisconnected?.Invoke();
            _client.OnDataReceived += (data, ch) => OnClientDataReceived?.Invoke(data, ch);

            if (ulong.TryParse(address, out steamId) && steamId > 76561197960265728)
                _client.ConnectSteam(steamId);
            else
                ParseAndConnectIP(address);
        }
        public override void ClientConnect(Uri uri) =>
            ClientConnect(uri.Scheme == "steam" ? uri.Host : $"{uri.Host}:{uri.Port}");

        public override void ClientSend(ArraySegment<byte> segment, int channelId)
        {
            byte[] data = new byte[segment.Count];
            Array.Copy(segment.Array, segment.Offset, data, 0, segment.Count);
            _client?.Send(data, channelId);
        }
        public override void ClientDisconnect()
        {
            try
            {
                _client?.Disconnect();
            }
            finally
            {
                _client = null;
            }
        }

        private void ParseAndConnectIP(string address)
        {
            string ip = "127.0.0.1";
            ushort port = 27015;

            if (address.Contains(":"))
            {
                string[] parts = address.Split(':');
                ip = parts[0];
                if (ushort.TryParse(parts[1], out ushort p)) port = p;
            }
            else if (IPAddress.TryParse(address, out _))
            {
                ip = address;
            }

            _client.ConnectIP(ip, port);
        }
        #endregion

        #region Server
        public override bool ServerActive() => _server?.Active ?? false;
        public override string ServerGetClientAddress(int connectionId) => _server?.GetClientAddress(connectionId) ?? string.Empty;

        public override void ServerStart()
        {
            var server = new TransportServer(NetworkManager.singleton.maxConnections);

            server.OnConnected += (id, address) => OnServerConnectedWithAddress?.Invoke(id, address);
            server.OnDisconnected += id => OnServerDisconnected?.Invoke(id);
            server.OnDataReceived += (id, data, ch) => OnServerDataReceived?.Invoke(id, data, ch);

            bool started = _serverMode switch
            {
                ServerTransportMode.IP => server.StartIP(_serverPort),
                ServerTransportMode.Steam => server.StartSteam(),
                ServerTransportMode.Both => server.StartBoth(_serverPort),
                _ => false
            };

            if (!started)
            {
                Debug.LogError($"[{nameof(HiddenBullTransport)}] Failed to start server in {_serverMode} mode.");
                return;
            }

            _server = server;
            Debug.Log($"[{nameof(HiddenBullTransport)}] Server started in {_serverMode} mode.");
        }
        public override void ServerStop()
        {
            try
            {
                _server?.Shutdown();
            }
            finally
            {
                _server = null;
            }
        }

        public override Uri ServerUri()
        {
            if (_serverMode == ServerTransportMode.IP)
            {
                return new UriBuilder
                {
                    Scheme = "ip",
                    Host = GetLocalIPv4(),
                    Port = _serverPort
                }.Uri;
            }

            return new UriBuilder
            {
                Scheme = "steam",
                Host = Steam.SteamInformation.LocalSteamId.ToString()
            }.Uri;
        }
        private static string GetLocalIPv4()
        {
            try
            {
                foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                {
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        return ip.ToString();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[{nameof(HiddenBullTransport)}] Failed to resolve local IPv4. Reason: {e}");
            }
            return "127.0.0.1";
        }

        public override void ServerSend(int connectionId, ArraySegment<byte> segment, int channelId)
        {
            byte[] data = new byte[segment.Count];
            Array.Copy(segment.Array, segment.Offset, data, 0, segment.Count);
            _server?.Send(connectionId, data, channelId);
        }
        public override void ServerDisconnect(int connectionId) => _server?.Disconnect(connectionId);
        #endregion

        #region Lifecycle
        public override void ClientEarlyUpdate() { if (enabled) _client?.ReceiveData(); }
        public override void ServerEarlyUpdate() { if (enabled) _server?.ReceiveData(); }
        public override void ClientLateUpdate() { if (enabled) _client?.FlushData(); }
        public override void ServerLateUpdate() { if (enabled) _server?.FlushData(); }

        public override void Shutdown()
        {
            if (_shutdownCalled) return;
            _shutdownCalled = true;

            _client?.Disconnect();
            _server?.Shutdown();
            _client = null;
            _server = null;

            Debug.Log($"[{nameof(HiddenBullTransport)}] Shutdown.");
        }
        private void OnDestroy() => Shutdown();
        #endregion
    }
}