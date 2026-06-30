namespace HiddenBull.Networking.Data
{
    /// <summary>
    /// Lightweight, client-safe view of a connected player. Deliberately does
    /// NOT carry level or the permission flag set - clients only need name,
    /// identity, and the role label (for "is admin" badges / autofill).
    /// </summary>
    public struct PlayerInfo
    {
        public ulong SteamId;
        public string Name;
        public string RoleName;   // "" = regular player

        public readonly bool IsAdmin => !string.IsNullOrEmpty(RoleName);
    }

    /// <summary>
    /// Full roster snapshot pushed from the server to clients whenever the
    /// roster changes (join / leave / role change). Broadcast in full rather
    /// than as deltas - trivial at this player count, and no delta bugs.
    /// </summary>
    internal struct PlayerRosterMessage : Mirror.NetworkMessage
    {
        public PlayerInfo[] Players;
    }
}