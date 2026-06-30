using PicoShot.Localization;
using Mirror;

namespace HiddenBull.Networking.Data
{
    internal struct AuthRequestMessage : NetworkMessage
    {
        public ulong SteamId;
        public byte[] AuthTicket;
        public string PlayerName;
        public string PasswordHash;
        public string GameVersion;

        public readonly bool IsValid =>
            SteamId != 0 &&
            AuthTicket != null &&
            AuthTicket.Length > 0 &&
            !string.IsNullOrEmpty(PlayerName);
    }
    internal struct AuthResponseMessage : NetworkMessage
    {
        public bool Success;
        public TextNode Reason;
        public string[] RequiredContent;   // keys the client must mount before joining (empty = none)
    }

    public enum ServerMessageType : byte
    {
        Info,        // general info / announcement, no action
        Warning,     // warning, no forced action
        Disconnect   // reason for an imminent disconnect (kick/ban)
    }
    internal struct ServerNotificationMessage : NetworkMessage
    {
        public ServerMessageType Type;
        public TextNode Content;
    }

    internal struct RoleCatalogMessage : NetworkMessage
    {
        public string[] RoleNames;
    }

    internal struct TickConfigMessage : NetworkMessage
    {
        public int TickRate;
    }

    internal struct ContentReadyMessage : NetworkMessage
    {
        public bool Success;               // client finished preparing required content
    }
    internal struct ContentProgressMessage : NetworkMessage 
    {
        public float Progress;
    }

    internal struct SceneLoadMessage : NetworkMessage
    {
        public string SceneId;
    }
    internal struct SceneReadyMessage : NetworkMessage { }

    internal struct ChatSendMessage : NetworkMessage   // client intent
    {
        public string Channel;   // target channel; ignored when Target != 0
        public ulong Target;     // != 0 => whisper to this SteamId
        public string Text;
    }
    internal struct ChatMessage : NetworkMessage
    {
        public ulong SenderSteamId;   // 0 = server/system; name resolved client-side
        public string Channel;
        public string Text;
        public bool IsWhisper;
    }
    internal struct ChannelStyleData       // one channel's server-defined display style
    {
        public string Key;
        public string Label;
        public string ColorHex;           // RRGGBB
    }
    internal struct ChannelCatalogMessage : NetworkMessage   // server -> clients: full style catalog
    {
        public ChannelStyleData[] Channels;
    }
    internal struct ChannelMembershipMessage : NetworkMessage   // a client's own channel membership
    {
        public string[] Channels;
    }

    internal struct PingEntry
    {
        public ulong SteamId;
        public ushort PingMs;     // round-trip in ms (clamped), ushort = max ~65 s
    }
    internal struct PingSnapshotMessage : NetworkMessage
    {
        public PingEntry[] Pings;
    }
}