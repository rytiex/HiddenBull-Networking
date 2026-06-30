using HiddenBull.Networking.Data;
using UnityEngine;
using Steamworks;

using System.Reflection;
using System;

namespace HiddenBull.Networking.Steam
{
    public static class SteamInformation
    {
        private static SteamConfig _config;
        internal static void Configure(SteamConfig config)
        {
            _config = config;
            if (config == null)
                Debug.LogWarning($"[{nameof(SteamInformation)}] No SteamConfig assigned; using placeholder identity (AppId 480).");
        }

        public static uint AppId => _config != null ? _config.AppId : 480u;
        public static string GameDescription => _config != null ? _config.GameDescription : "Name Of Game";
        public static string ModDir => _config != null ? _config.ModDir : "nameofgame";

        public static ulong LocalSteamId { get; internal set; }
        public static string LocalName { get; internal set; } = string.Empty;
        public static bool Initialized { get; internal set; }

#if UNITY_SERVER
        public static bool IsDedicated => SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;
#else
        public static bool IsDedicated => false;
#endif
    }

    internal static class SteamClientBootstrap
    {
        /// <summary>Fired when Steam overlay's "Join Game" or "Invite Accept" produces a connect request.</summary>
        public static event Action<string, string> OnJoinServerRequested;

        public static void Init()
        {
            if (SteamInformation.AppId == 480)
                Debug.LogWarning($"[{nameof(SteamClientBootstrap)}] Steam APPID 480 Please check the [SteamBootstrap.cs] code.");

            if (SteamClient.IsValid)
            {
                Debug.LogWarning($"[{nameof(SteamClientBootstrap)}] Already initialized.");
                return;
            }

            try
            {
                SteamClient.Init(SteamInformation.AppId, asyncCallbacks: true);

                SteamFriends.OnGameLobbyJoinRequested += HandleLobbyJoinRequested;
                SteamFriends.OnGameRichPresenceJoinRequested += HandleRichPresenceJoinRequested;

                SteamInformation.LocalSteamId = SteamClient.SteamId;
                SteamInformation.LocalName = SteamClient.Name;
                SteamInformation.Initialized = true;
                PrewarmSteamSockets();

                Debug.Log($"[{nameof(SteamClientBootstrap)}] Initialized. SteamId: {SteamClient.SteamId}, Name: {SteamClient.Name}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{nameof(SteamClientBootstrap)}] Failed to initialize: {ex.Message}");
            }
        }
        public static void Shutdown()
        {
            if (!SteamClient.IsValid) return;

            // Set flag first so anything that's still running sees Initialized=false
            // before Steam internals are actually torn down.
            SteamInformation.Initialized = false;
            SteamInformation.LocalName = string.Empty;
            SteamInformation.LocalSteamId = 0;

            SteamFriends.OnGameLobbyJoinRequested -= HandleLobbyJoinRequested;
            SteamFriends.OnGameRichPresenceJoinRequested -= HandleRichPresenceJoinRequested;

            SteamClient.Shutdown();
            Debug.Log($"[{nameof(SteamClientBootstrap)}] Shutdown.");
        }

        private static void PrewarmSteamSockets()
        {
            try
            {
                // Facepunch keeps SteamNetworkingSockets.Internal and ISteamNetworkingSockets.InitAuthentication
                // marked internal. We reach for them via reflection to signal Steam to begin
                // its certificate/identity setup before the user's first connection attempt.
                // Without this, the very first ConnectNormal/ConnectRelay call typically
                // disconnects before reaching the Connected state (cold-start handshake fail).
                var socketsType = typeof(SteamNetworkingSockets);
                var internalProp = socketsType.GetProperty(
                    "Internal",
                    BindingFlags.NonPublic | BindingFlags.Static);

                var instance = internalProp?.GetValue(null);
                if (instance == null)
                {
                    Debug.LogWarning($"[{nameof(SteamClientBootstrap)}] SteamNetworkingSockets.Internal not found - prewarm skipped.");
                    return;
                }

                var initAuth = instance.GetType().GetMethod(
                    "InitAuthentication",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (initAuth == null)
                {
                    Debug.LogWarning($"[{nameof(SteamClientBootstrap)}] InitAuthentication method not found - prewarm skipped.");
                    return;
                }

                var availability = initAuth.Invoke(instance, null);
                Debug.Log($"[{nameof(SteamClientBootstrap)}] Steam Sockets prewarm requested. Initial availability: {availability}");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[{nameof(SteamClientBootstrap)}] Prewarm failed (non-fatal, retry will cover): {ex.Message}");
            }
        }
        private static void HandleLobbyJoinRequested(Steamworks.Data.Lobby lobby, SteamId friend)
        {
            // TODO: Lobby data ile dedicated server lobby sistemi entegrasyonu yapilacak.
            // Su an lobby'nin owner'ina (friend) baglaniyoruz; gercek server adresi
            // lobby data'sindan okunmali.
            string address = friend.Value.ToString();
            Debug.Log($"[{nameof(SteamClientBootstrap)}] Lobby join requested by {friend} (lobby {lobby.Id}). Resolved address: {address}.");
            OnJoinServerRequested?.Invoke(address, string.Empty);
        }
        private static void HandleRichPresenceJoinRequested(Friend friend, string connectString)
        {
            // Rich presence "connect" string -> "ip:port" or steamId for direct connect.
            Debug.Log($"[{nameof(SteamClientBootstrap)}] Rich presence join from {friend.Name}: {connectString}");
            OnJoinServerRequested?.Invoke(connectString, string.Empty);
        }
    }

    internal static class SteamServerBootstrap
    {
        public static event Action OnReady;
        public static event Action<string> OnFailed;
        public static event Action OnDisconnected;

        public static void Init(ServerConfig config)
        {
            if (SteamInformation.AppId == 480)
                Debug.LogWarning($"[{nameof(SteamServerBootstrap)}] Steam APPID 480 Please check the [SteamBootstrap.cs] code.");

            if (SteamServer.IsValid)
            {
                Debug.LogWarning($"[{nameof(SteamServerBootstrap)}] Already initialized.");
                return;
            }

            ushort queryPort = (ushort)(config.Port + 1);
            var init = new SteamServerInit(SteamInformation.ModDir, SteamInformation.GameDescription)
            {
                GamePort = config.Port,
                QueryPort = queryPort,
                Secure = true,
                VersionString = Application.version
            };

            try
            {
                PicoShot.Localization.LocalizationManager.SetLanguage(PicoShot.Localization.LocalizationManager.DefaultLanguage);
                SteamServer.Init(SteamInformation.AppId, init, asyncCallbacks: true);

                SteamServer.ServerName = config.ServerName;
                SteamServer.Passworded = !string.IsNullOrEmpty(config.Password);
                SteamServer.DedicatedServer = SteamInformation.IsDedicated;
                SteamServer.MaxPlayers = Mathf.Max(1, config.MaxPlayers);
                SteamServer.MapName = string.IsNullOrEmpty(config.Scene) ? "default" : config.Scene;
                SteamServer.AdvertiseServer = false;

                SteamServer.OnSteamServersConnected += HandleServerConnected;
                SteamServer.OnSteamServersDisconnected += HandleServerDisconnected;
                SteamServer.OnSteamServerConnectFailure += HandleServerConnectFailure;

                NetworkState.Scene.OnLoaded += SyncMap;
                NetworkState.Server.OnStarted += SyncTags;

                SteamServer.LogOnAnonymous();
                Debug.Log($"[{nameof(SteamServerBootstrap)}] Logging on anonymously...");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{nameof(SteamServerBootstrap)}] Failed to initialize: {ex.Message}");
                OnFailed?.Invoke(ex.Message);
            }
        }
        public static void Shutdown()
        {
            if (!SteamServer.IsValid) return;

            SteamInformation.Initialized = false;
            SteamInformation.LocalSteamId = 0;

            SteamServer.OnSteamServersConnected -= HandleServerConnected;
            SteamServer.OnSteamServersDisconnected -= HandleServerDisconnected;
            SteamServer.OnSteamServerConnectFailure -= HandleServerConnectFailure;

            NetworkState.Scene.OnLoaded -= SyncMap;
            NetworkState.Server.OnStarted -= SyncTags;

            SteamServer.LogOff();
            SteamServer.Shutdown();
            Debug.Log($"[{nameof(SteamServerBootstrap)}] Shutdown.");
        }
        private static void SyncMap(string sceneId)
        {
            if (!SteamServer.IsValid) return;
            SteamServer.MapName = string.IsNullOrEmpty(sceneId) ? "default" : sceneId;
            SteamServer.AdvertiseServer = true; // server ready -> now visible on the master server
        }
        private static void SyncTags()
        {
            if (!SteamServer.IsValid) return;
            SteamServer.GameTags = BuildTags();
        }
        private static string BuildTags()
        {
            string tags = "v=" + Application.version;
            var keys = NetworkContentGate.GetRequiredKeys?.Invoke();
            if (keys != null && keys.Length > 0) tags += ",mod";
            return tags;
        }


        private static void HandleServerConnected()
        {
            SteamInformation.LocalSteamId = SteamServer.SteamId;
            SteamInformation.Initialized = true;
            Debug.Log($"[{nameof(SteamServerBootstrap)}] Connected to Steam. SteamId: {SteamServer.SteamId}");
            OnReady?.Invoke();
        }
        private static void HandleServerDisconnected(Result result)
        {
            Debug.LogWarning($"[{nameof(SteamServerBootstrap)}] Disconnected from Steam: {result}");
            OnDisconnected?.Invoke();
        }
        private static void HandleServerConnectFailure(Result result, bool stillRetrying)
        {
            string msg = $"Connect failure: {result} (retrying: {stillRetrying})";
            Debug.LogError($"[{nameof(SteamServerBootstrap)}] {msg}");

            if (!stillRetrying)
                OnFailed?.Invoke(msg);
        }
    }
}
