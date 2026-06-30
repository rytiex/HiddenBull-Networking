using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using System;

namespace HiddenBull.Networking.Data
{
    [Serializable]
    internal struct ClientBanInformation
    {
        public string Name;
        public ulong SteamId;
        public string Reason;
        public DateTime BanDateUtc;
        public DateTime ExpirationUtc;

        [JsonIgnore]
        public readonly bool IsPermanent => ExpirationUtc == DateTime.MaxValue;

        [JsonIgnore]
        public readonly bool IsExpired => !IsPermanent && DateTime.UtcNow >= ExpirationUtc;

        [JsonIgnore]
        public readonly TimeSpan TimeRemaining => IsPermanent ? TimeSpan.MaxValue : ExpirationUtc - DateTime.UtcNow;
    }

    [Serializable]
    internal struct WhitelistEntry
    {
        public ulong SteamId;
        public string Name;
        public DateTime AddedUtc;
        public string Reason;

        public ulong AddedBy;
        public string AddedByName;
    }

    /// <summary>
    /// What a role is allowed to DO. Bitflags internally (fast AND checks),
    /// serialized as readable names (e.g. "Kick, Ban, Whitelist") so an admin
    /// editing roles.json by hand understands it at a glance.
    ///
    /// Targeted actions (Kick, Ban) additionally require a level check via
    /// ServerRoles.CanTarget. Non-targeted ones (Whitelist, ServerConfig) only
    /// require the flag. AssignRoles covers BOTH adding and removing roles, and
    /// the level rule stacks on top (you can only grant/remove below your level).
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    [Flags]
    public enum Permissions : uint
    {
        None = 0,
        Kick = 1 << 0,
        Ban = 1 << 1,   // ban + unban
        Whitelist = 1 << 2,   // manage the whitelist
        ServerConfig = 1 << 3,   // change server settings
        AssignRoles = 1 << 4    // add + remove roles (level rule stacks on top)
    }

    /// <summary>
    /// Helpers for the Permissions flag set.
    /// </summary>
    internal static class PermissionsUtil
    {
        /// <summary>
        /// Every defined permission OR'd together. Computed via reflection so new
        /// enum values are automatically included wherever "all permissions" is
        /// needed (the Owner role default, the host bypass) without editing those
        /// spots. None contributes nothing.
        /// </summary>
        public static readonly Permissions All = ComputeAll();

        private static Permissions ComputeAll()
        {
            Permissions all = Permissions.None;
            foreach (Permissions value in Enum.GetValues(typeof(Permissions)))
                all |= value;
            return all;
        }
    }

    /// <summary>
    /// A named bundle of permissions + a hierarchy level.
    /// Keyed by name in roles.json (the dictionary key IS the role name).
    /// </summary>
    [Serializable]
    public struct RoleDefinition
    {
        public Permissions Permissions;
        public int Level;
    }

    /// <summary>
    /// Maps a player (SteamID) to a role by name. Stored in admins.json.
    /// </summary>
    [Serializable]
    public struct AdminEntry
    {
        public ulong SteamId;
        public string Name;
        public string RoleName;
    }
}