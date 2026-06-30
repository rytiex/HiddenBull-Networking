using Steamworks.Data;
using Steamworks;
using UnityEngine;

using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace HiddenBull.Networking.Transport
{
    internal sealed class TransportClient
    {
        public bool Connected { get; private set; }

        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<ArraySegment<byte>, int> OnDataReceived;

        private ConnectionManager _manager;
        private Connection HostConnection => _manager.Connection;

        private CancellationTokenSource _cancelToken;
        private TaskCompletionSource<bool> _connectedTcs;
        private readonly int _timeout;

        private bool _disconnecting;

        // Cold-start retry support
        private Func<ClientConnectionManager> _connectFactory;
        private int _retryAttempts;
        private const int MaxRetries = 2;
        private const float RetryDelaySeconds = 0.5f;

        private const int MaxMessages = 256;

        internal TransportClient(int timeout = 25)
        {
            _timeout = timeout;
        }

        #region Connect
        internal void ConnectSteam(ulong targetSteamId)
        {
            Debug.Log($"[{nameof(TransportClient)}] Connecting to SteamID {targetSteamId}.");

            StartConnect(() =>
            {
                SteamNetworkingUtils.InitRelayNetworkAccess();
                var manager = SteamNetworkingSockets.ConnectRelay<ClientConnectionManager>(targetSteamId);
                manager.Client = this;
                return manager;
            });
        }
        internal void ConnectIP(string ip, ushort port)
        {
            Debug.Log($"[{nameof(TransportClient)}] Connecting to {ip}:{port}.");

            StartConnect(() =>
            {
                var address = NetAddress.From(ip, port);
                var manager = SteamNetworkingSockets.ConnectNormal<ClientConnectionManager>(address);
                manager.Client = this;
                return manager;
            });
        }
        private async void StartConnect(Func<ClientConnectionManager> connectFactory)
        {
            Cleanup();

            _disconnecting = false;
            _cancelToken = new CancellationTokenSource();
            _connectedTcs = new TaskCompletionSource<bool>();
            _connectFactory = connectFactory;

            try
            {
                _manager = connectFactory();

                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(_timeout), _cancelToken.Token);

                if (await Task.WhenAny(_connectedTcs.Task, timeoutTask) != _connectedTcs.Task)
                {
                    Debug.LogError($"[{nameof(TransportClient)}] Connection timed out.");
                    Disconnect("Connection timeout.");
                }
            }
            catch (OperationCanceledException)
            {
                Debug.Log($"[{nameof(TransportClient)}] Connection cancelled.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{nameof(TransportClient)}] Connection error: {ex.Message}");
                FireDisconnected();
            }
        }
        private async Task RetryConnectAsync()
        {
            await Task.Delay(TimeSpan.FromSeconds(RetryDelaySeconds));
            if (_disconnecting || _connectFactory == null) return;

            _manager = null;   // discard failed manager
            StartConnect(_connectFactory);
        }
        #endregion

        #region Callbacks
        internal void HandleConnected()
        {
            Connected = true;
            _connectedTcs?.TrySetResult(true);
            _retryAttempts = 0;
            OnConnected?.Invoke();
            Debug.Log($"[{nameof(TransportClient)}] Connected.");
        }
        internal void HandleDisconnected()
        {
            if (_disconnecting) return;

            bool wasConnected = Connected;
            Connected = false;
            _connectedTcs?.TrySetResult(false);

            // Auto-retry if disconnected before reaching Connected state.
            // Steam Sockets sometimes fails its first cold handshake; a brief retry
            // typically succeeds because the subsystem has warmed up by then.
            if (!wasConnected && _retryAttempts < MaxRetries && _connectFactory != null)
            {
                _retryAttempts++;
                Debug.LogWarning($"[{nameof(TransportClient)}] Connection failed before establishing. Retrying ({_retryAttempts}/{MaxRetries})...");
                _ = RetryConnectAsync();
                return;
            }

            // Prevent Mirror's disconnect chain from re-entering Disconnect()
            // and double-logging.
            _disconnecting = true;
            FireDisconnected();
            Debug.Log($"[{nameof(TransportClient)}] Disconnected by remote.");
        }
        internal void HandleMessage(IntPtr data, int size)
        {
            // Steam may dispatch a message before the Connected callback fires.
            // Drop it - Mirror would log an error trying to handle data without
            // an initialized NetworkClient.connection.
            if (!Connected) return;

            byte[] buffer = new byte[size];
            Marshal.Copy(data, buffer, 0, size);

            if (buffer.Length == 0) return;

            int channel = buffer[^1];
            byte[] payload = new byte[buffer.Length - 1];
            Array.Copy(buffer, payload, payload.Length);

            OnDataReceived?.Invoke(new ArraySegment<byte>(payload), channel);
        }
        #endregion

        #region Data
        internal void ReceiveData() => _manager?.Receive(MaxMessages);
        internal void FlushData()
        {
            if (_manager != null && Connected)
                HostConnection.Flush();
        }
        internal void Send(byte[] data, int channelId)
        {
            if (_manager == null || !Connected) return;

            byte[] message = new byte[data.Length + 1];
            Array.Copy(data, message, data.Length);
            message[data.Length] = (byte)channelId;

            SendType sendType = channelId == Mirror.Channels.Reliable ? SendType.Reliable : SendType.Unreliable;
            HostConnection.SendMessage(message, 0, message.Length, sendType);
        }
        #endregion

        #region Disconnect
        internal void Disconnect(string reason = "Client disconnected.")
        {
            if (_disconnecting) return;
            _disconnecting = true;

            _connectFactory = null;
            _retryAttempts = 0;

            _cancelToken?.Cancel();
            _connectedTcs?.TrySetResult(false);

            if (_manager != null)
            {
                if (Connected)
                {
                    try
                    {
                        // Guard: Steam may already be shut down (e.g. Play Mode exit),
                        // in which case Connection.Close NREs on SteamNetworkingSockets.Internal.
                        if (Steam.SteamInformation.Initialized)
                            HostConnection.Close(false, 0, reason);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[{nameof(TransportClient)}] Close suppressed (Steam shutdown race): {ex.Message}");
                    }
                }
                _manager = null;
            }

            Connected = false;
            FireDisconnected();
            Cleanup();
            Debug.Log($"[{nameof(TransportClient)}] Disconnected: {reason}");
        }
        private void FireDisconnected() => OnDisconnected?.Invoke();
        private void Cleanup()
        {
            _cancelToken?.Cancel();
            _cancelToken?.Dispose();
            _cancelToken = null;
        }
        #endregion

        #region ConnectionManager
        private sealed class ClientConnectionManager : ConnectionManager
        {
            internal TransportClient Client;

            public override void OnConnected(ConnectionInfo info) => Client.HandleConnected();
            public override void OnDisconnected(ConnectionInfo info) => Client.HandleDisconnected();
            public override void OnMessage(IntPtr data, int size, long messageNum, long recvTime, int channel) =>
                Client.HandleMessage(data, size);
        }
        #endregion
    }
}