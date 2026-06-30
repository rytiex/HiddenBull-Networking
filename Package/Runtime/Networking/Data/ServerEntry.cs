using System.Collections.Generic;

namespace HiddenBull.Networking.Data
{
    public enum ServerKind : byte { Unknown, Dedicated, P2P }

    /// <summary>
    /// Source-agnostic view of a discoverable server, produced by a browser source
    /// (Steam game-server list, Steam lobby, future LAN, ...). Reference type so the
    /// browser can update an entry in place (e.g. quick-refresh) by its Key.
    /// </summary>
    public sealed class ServerEntry
    {
        public string Name;
        public string Map;
        public int Players;
        public int MaxPlayers;
        public bool HasPassword;     // Locked
        public bool IsModded;
        public int PingMs;           // Latency (-1 = unknown, e.g. lobbies)
        public ServerKind Kind;      // Dedicated / P2P

        public string Address;       // ClientConnectSettings.Address (Steam: SteamId string)
        public ulong SteamId;
        public string SourceId;      // "steam", "steam-lobby", ...
        public bool OnInternet;      // discovered via the internet master-server list
        public bool OnLan;           // discovered via LAN broadcast
        public string Version;       // server's app version (for match indicator)

        public IReadOnlyDictionary<string, string> Extra;   // future columns; null until used
        public string Key => SteamId != 0 ? $"steam:{SteamId}" : $"{SourceId}:{Address}";
    }
}