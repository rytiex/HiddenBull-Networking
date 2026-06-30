using HiddenBull.Networking.Steam;

namespace HiddenBull.Networking.Data
{
    public sealed class ClientConnectSettings
    {
        /// <summary>SteamID string for Steam P2P, or "ip:port" for direct IP.</summary>
        public string Address { get; set; } = "127.0.0.1:27015";
        public string Password { get; set; } = string.Empty;
    }

    public sealed class ServerStartSettings
    {
        public string ServerName { get; set; } = $"{SteamInformation.GameDescription} Server";
        public string Password { get; set; } = string.Empty;
        public ushort Port { get; set; } = 27015;
        public int TickRate { get; set; } = NetworkState.Tick.DefaultTickRate;
        public int MaxPlayers { get; set; } = 16;

        public ServerStartMode StartMode { get; set; } = ServerStartMode.Host;
        public ServerTransportMode TransportMode { get; set; } = ServerTransportMode.Steam;

        /// <summary>
        /// Creates settings for a dedicated server from a ServerConfig file.
        /// StartMode and TransportMode are set automatically.
        /// </summary>
        internal static ServerStartSettings FromConfig(ServerConfig config) => Create(
            serverName: config.ServerName,
            password: config.Password,
            port: config.Port,
            tickRate: config.TickRate,
            maxPlayers: config.MaxPlayers,
            startMode: ServerStartMode.Dedicated,
            transportMode: ServerTransportMode.Both);
        public static ServerStartSettings Create(
            string serverName = null,
            string password = null,
            ushort port = 27015,
            int tickRate = 0, // 0 <= DefaultTickRate
            int maxPlayers = 16,
            ServerStartMode startMode = ServerStartMode.Host,
            ServerTransportMode transportMode = ServerTransportMode.Steam) => new()
            {
                ServerName = string.IsNullOrEmpty(serverName) ? $"{SteamInformation.GameDescription} Server" : serverName,
                Password = string.IsNullOrEmpty(password) ? string.Empty : password,
                Port = port,
                TickRate = tickRate <= 0 ? NetworkState.Tick.DefaultTickRate : tickRate,
                MaxPlayers = maxPlayers,
                StartMode = startMode,
                TransportMode = transportMode
            };
    }
}

public enum ServerStartMode
{
    Host,
    Dedicated
}
public enum ServerTransportMode
{
    Steam,
    IP,
    Both
}