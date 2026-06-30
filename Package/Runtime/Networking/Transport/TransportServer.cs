using Steamworks.Data;
using Steamworks;
using UnityEngine;

using System.Runtime.InteropServices;
using System.Collections.Generic;
using System;

namespace HiddenBull.Networking.Transport
{
    internal sealed class TransportServer
    {
        public bool Active { get; private set; }

#if UNITY_SERVER
        private static bool IsDedicated => SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;
#else
        private static bool IsDedicated => false;
#endif

        public event Action<int, string> OnConnected;
        public event Action<int> OnDisconnected;
        public event Action<int, ArraySegment<byte>, int> OnDataReceived;

        private readonly int _maxConnections;

        // Two independent sockets - either may be null depending on start mode
        private SocketManager _relaySocket;
        private SocketManager _ipSocket;

        private readonly Dictionary<Connection, int> _connToId = new();
        private readonly Dictionary<int, Connection> _idToConn = new();
        private readonly Dictionary<int, ulong> _idToSteamId = new();
        private int _nextId = 1;

        private const int MaxMessages = 256;

        internal TransportServer(int maxConnections)
        {
            _maxConnections = maxConnections;
        }

        #region Lifecycle
        internal bool StartSteam()
        {
            if (Active) return false;

            try
            {
                SteamNetworkingUtils.InitRelayNetworkAccess();
                _relaySocket = CreateRelaySocket();

                Active = true;
                Debug.Log($"[{nameof(TransportServer)}] Started. Mode: Steam Relay. Dedicated: {IsDedicated}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{nameof(TransportServer)}] Failed to start: {ex.Message}");
                return false;
            }
        }
        internal bool StartIP(ushort port)
        {
            if (Active) return false;

            try
            {
                _ipSocket = CreateIPSocket(port);

                Active = true;
                Debug.Log($"[{nameof(TransportServer)}] Started. Mode: IP:{port}.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{nameof(TransportServer)}] Failed to start: {ex.Message}");
                return false;
            }
        }
        internal bool StartBoth(ushort port)
        {
            if (Active) return false;

            try
            {
                SteamNetworkingUtils.InitRelayNetworkAccess();
                _relaySocket = CreateRelaySocket();
                _ipSocket = CreateIPSocket(port);

                Active = true;
                Debug.Log($"[{nameof(TransportServer)}] Started. Mode: Steam Relay + IP:{port}. Dedicated: {IsDedicated}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{nameof(TransportServer)}] Failed to start dual: {ex.Message}");
                _relaySocket?.Close();
                _ipSocket?.Close();
                _relaySocket = null;
                _ipSocket = null;
                return false;
            }
        }
        private SocketManager CreateRelaySocket()
        {
            var s = SteamNetworkingSockets.CreateRelaySocket<ServerSocketManager>();
            s.Server = this;
            return s;
        }
        private SocketManager CreateIPSocket(ushort port)
        {
            var address = NetAddress.AnyIp(port);
            var s = SteamNetworkingSockets.CreateNormalSocket<ServerSocketManager>(address);
            s.Server = this;
            return s;
        }
        internal void Shutdown()
        {
            if (!Active) return;
            Active = false;  // set first - exception olsa bile re-entry early return yapar

            foreach (var conn in _idToConn.Values)
            {
                try
                {
                    if (Steam.SteamInformation.Initialized)
                        conn.Close(false, 0, "Server shutdown");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[{nameof(TransportServer)}] Connection close suppressed (Steam shutdown race): {ex.Message}");
                }
            }

            _connToId.Clear();
            _idToConn.Clear();
            _idToSteamId.Clear();

            try
            {
                if (Steam.SteamInformation.Initialized)
                {
                    _relaySocket?.Close();
                    _ipSocket?.Close();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[{nameof(TransportServer)}] Socket close suppressed (Steam shutdown race): {ex.Message}");
            }

            _relaySocket = null;
            _ipSocket = null;

            Debug.Log($"[{nameof(TransportServer)}] Shutdown.");
        }        
        #endregion

        #region Socket Callbacks
        internal void HandleConnecting(Connection conn)
        {
            if (_connToId.Count >= _maxConnections)
            {
                conn.Close(false, 0, "Server is full.");
                Debug.LogWarning($"[{nameof(TransportServer)}] Rejected: server full.");
                return;
            }
            conn.Accept();
        }
        internal void HandleConnected(Connection conn, ConnectionInfo info)
        {
            int id = _nextId++;
            _connToId[conn] = id;
            _idToConn[id] = conn;

            // Identity varies by socket type:
            // - Relay socket: Identity carries SteamID
            // - Normal IP socket: Identity may carry IP (SteamID = 0)
            ulong steamId = info.Identity.SteamId.Value;
            string address = steamId != 0 ? steamId.ToString() : info.Address.ToString();
            _idToSteamId[id] = steamId;

            OnConnected?.Invoke(id, address);
            Debug.Log($"[{nameof(TransportServer)}] Connection {id} established. Address: {address}");
        }
        internal void HandleDisconnected(Connection conn)
        {
            if (!_connToId.TryGetValue(conn, out int id)) return;

            _connToId.Remove(conn);
            _idToConn.Remove(id);
            _idToSteamId.Remove(id);

            OnDisconnected?.Invoke(id);
            Debug.Log($"[{nameof(TransportServer)}] Connection {id} disconnected.");
        }
        internal void HandleMessage(Connection conn, IntPtr data, int size)
        {
            if (!_connToId.TryGetValue(conn, out int id)) return;

            byte[] buffer = new byte[size];
            Marshal.Copy(data, buffer, 0, size);

            if (buffer.Length == 0) return;

            int channel = buffer[^1];
            byte[] payload = new byte[buffer.Length - 1];
            Array.Copy(buffer, payload, payload.Length);

            OnDataReceived?.Invoke(id, new ArraySegment<byte>(payload), channel);
        }
        #endregion

        #region Data
        internal void ReceiveData()
        {
            _relaySocket?.Receive(MaxMessages);
            _ipSocket?.Receive(MaxMessages);
        }
        internal void FlushData()
        {
            foreach (var conn in _idToConn.Values)
                conn.Flush();
        }
        internal void Send(int id, byte[] data, int channelId)
        {
            if (!_idToConn.TryGetValue(id, out Connection conn)) return;

            byte[] message = new byte[data.Length + 1];
            Array.Copy(data, message, data.Length);
            message[data.Length] = (byte)channelId;

            SendType sendType = channelId == Mirror.Channels.Reliable ? SendType.Reliable : SendType.Unreliable;
            conn.SendMessage(message, 0, message.Length, sendType);
        }
        #endregion

        #region Utility
        internal void Disconnect(int id, string reason = "Disconnected by server")
        {
            if (!_idToConn.TryGetValue(id, out Connection conn)) return;
            conn.Close(false, 0, reason);
        }
        internal string GetClientAddress(int id) =>
            _idToSteamId.TryGetValue(id, out ulong steamId) ? steamId.ToString() : string.Empty;
        #endregion

        #region SocketManager
        private sealed class ServerSocketManager : SocketManager
        {
            internal TransportServer Server;

            public override void OnConnecting(Connection conn, ConnectionInfo info) =>
                Server.HandleConnecting(conn);

            public override void OnConnected(Connection conn, ConnectionInfo info)
            {
                base.OnConnected(conn, info);   // SetConnectionPollGroup happens here
                Server.HandleConnected(conn, info);
            }

            public override void OnDisconnected(Connection conn, ConnectionInfo info)
            {
                Server.HandleDisconnected(conn);
                base.OnDisconnected(conn, info);
            }

            public override void OnMessage(Connection conn, NetIdentity identity, IntPtr data, int size, long messageNum, long recvTime, int channel) =>
                Server.HandleMessage(conn, data, size);
        }
        #endregion
    }
}