namespace HiddenBull.Networking.Data
{
    public sealed class ClientData
    {
        public int ConnectionId { get; }
        public ulong SteamId { get; }
        public string PlayerName { get; }

        public ClientData(int connectionId, ulong steamId, string playerName)
        {
            ConnectionId = connectionId;
            SteamId = steamId;
            PlayerName = playerName;
        }
    }
}