using HiddenBull.Networking.Auth.Validators;
using HiddenBull.Networking.Auth;

using HiddenBull.Networking.Transport;
using HiddenBull.Networking.Server;
using HiddenBull.Networking.Data;

using System.Collections.Generic;
using Mirror;

namespace HiddenBull.Networking
{
    public sealed partial class NetworkSessionManager : NetworkManager
    {
        public static new NetworkSessionManager singleton =>
            NetworkManager.singleton as NetworkSessionManager;

        private HiddenBullTransport _transport;
        private SteamNetworkAuthenticator _authenticator;
        private ServerRateLimiter _rateLimiter;

        public override void Reset()
        {
            base.Reset();

            if (!TryGetComponent(out _transport))
                _transport = gameObject.AddComponent<HiddenBullTransport>();

            if (!TryGetComponent(out _authenticator))
                _authenticator = gameObject.AddComponent<SteamNetworkAuthenticator>();

            headlessStartMode = HeadlessStartOptions.DoNothing;
            dontDestroyOnLoad = false;
            editorAutoStart = false;
            autoCreatePlayer = false;

            transport = _transport;
            authenticator = _authenticator;
        }
        public override void Awake()
        {
            base.Awake();

            if (_transport == null) _transport = GetComponent<HiddenBullTransport>();
            if (_authenticator == null) _authenticator = GetComponent<SteamNetworkAuthenticator>();

            if (Steam.SteamInformation.IsDedicated && !TryGetComponent<Console.DedicatedServerConsole>(out _))
                gameObject.AddComponent<Console.DedicatedServerConsole>();

            transport = _transport;
            authenticator = _authenticator;

            autoCreatePlayer = false;
            onlineScene = string.Empty;
            offlineScene = string.Empty;
            networkSceneName = string.Empty;
        }
        public override void Update()
        {
            base.Update();
            NetworkState.Tick.Pump_Internal();
            PumpPings();
        }

        /// <summary>
        /// Starts the server with the given settings. Configures the authenticator,
        /// rate limiter, moderation, and transport mode before invoking Mirror's start.
        /// </summary>
        public void StartServer(ServerStartSettings settings)
        {
            bool dedicated = settings.StartMode == ServerStartMode.Dedicated;
            ServerBanModeration.Initialize(dedicated ? BanStorage.CreatePersistent() : BanStorage.CreateRuntime());
            ServerWhitelistModeration.Initialize(dedicated ? WhitelistStorage.CreatePersistent() : WhitelistStorage.CreateRuntime());
            ServerRoles.Initialize(dedicated ? RoleStorage.CreatePersistent() : RoleStorage.CreateRuntime(),
                dedicated ? AdminStorage.CreatePersistent() : AdminStorage.CreateRuntime());

            _rateLimiter = new ServerRateLimiter();

            var validators = BuildValidators(settings.Password, settings.TransportMode);
            _authenticator.ConfigureServer(settings.TransportMode, validators, _rateLimiter, IsSteamIdInUse);
            _transport.SetServerMode(settings.TransportMode, settings.Port);

            // Apply the configured simulation tick rate, then keep Mirror's network rate 1:1 with it.
            NetworkState.Tick.SetTickRate_Internal(settings.TickRate);
            sendRate = NetworkState.Tick.TickRate;
            maxConnections = UnityEngine.Mathf.Max(1, settings.MaxPlayers);

            NetworkState.Server.SetInfo_Internal(
                settings.ServerName, maxConnections,
                !string.IsNullOrEmpty(settings.Password), settings.TransportMode);

            if (settings.StartMode == ServerStartMode.Host)
                StartHost();
            else
                base.StartServer();
        }
        public new void StopServer()
        {
            if (mode == NetworkManagerMode.Host)
                StopHost();
            else
                base.StopServer();
        }
        public new void StopClient()
        {
            base.StopClient();
        }

        private List<IConnectionApprovalValidator> BuildValidators(string password, ServerTransportMode mode)
        {
            var validators = new List<IConnectionApprovalValidator>
            {
                new VersionValidator(),
                new BanValidator(steamId =>
                {
                    bool banned = ServerBanModeration.IsBanned(steamId, out var info);
                    return (banned, info);
                }),
                new WhitelistValidator(),
                new PasswordValidator(PasswordValidator.PasswordHash.Of(password))
            };

            // Duplicate check makes no sense in IP mode where SteamIDs get +N transformed.
            if (mode != ServerTransportMode.IP)
                validators.Add(new DuplicateConnectionValidator(IsSteamIdInUse));

            return validators;
        }
    }
}