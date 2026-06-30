using HiddenBull.Networking.Data;
using UnityEngine;

using System.Collections.Generic;
using System;

namespace HiddenBull.Networking.Server
{
    /// <summary>
    /// Central permission authority. Resolves a SteamID to its role, then
    /// answers two independent questions:
    ///   - "Has"      : does the actor have a permission flag? (what they can do)
    ///   - "CanTarget": does the actor outrank the target by level? (who they can do it to)
    /// CanActOn combines both for targeted actions (kick/ban).
    ///
    /// The host is always treated as Owner (max level, all permissions) and is
    /// untouchable, mirroring the existing IsHostConnection guard in moderation.
    /// </summary>
    public static class ServerRoles
    {
        private static RoleStorage _roles;
        private static AdminStorage _admins;

        public static event Action<ulong, string> OnRoleAssigned;
        public static event Action<ulong> OnRoleRemoved;

        /// <summary>
        /// When true, the current synchronous action is performed by a trusted local authority
        /// (the dedicated server's stdin console) and bypasses all permission/level checks — the
        /// dedicated equivalent of the host's Owner bypass. Set and cleared around one invocation.
        /// </summary>
        public static bool ElevatedContext { get; internal set; }

        public const string OwnerRoleName = "Owner";

        /// <summary>
        /// Must be called before any permission checks.
        /// Use Persistent storage for dedicated, Runtime for P2P/Host.
        /// </summary>
        internal static void Initialize(RoleStorage roles, AdminStorage admins)
        {
            _roles = roles ?? throw new ArgumentNullException(nameof(roles));
            _admins = admins ?? throw new ArgumentNullException(nameof(admins));
            Debug.Log($"[{nameof(ServerRoles)}] Initialized. Roles: {_roles.All.Count}, Admins: {_admins.All.Count}");
        }
        private static void EnsureInitialized()
        {
            if (_roles == null || _admins == null)
                throw new InvalidOperationException($"{nameof(ServerRoles)} is not initialized. Call Initialize() first.");
        }

        private static bool IsHost(ulong steamId)
        {
            // localConnection is non-null only in Host mode; null in Dedicated.
            return Mirror.NetworkServer.localConnection != null &&
                   Mirror.NetworkServer.localConnection.authenticationData is ClientData data &&
                   data.SteamId == steamId;
        }

        #region Resolution
        /// <summary>Resolves a player's role, if any. False for regular players.</summary>
        public static bool TryGetRole(ulong steamId, out string roleName, out RoleDefinition definition)
        {
            roleName = null;
            definition = default;

            if (_admins == null || _roles == null)
                return false;
            if (!_admins.TryGet(steamId, out var entry))
                return false;
            if (!_roles.TryGet(entry.RoleName, out definition))
            {
                Debug.LogWarning($"[{nameof(ServerRoles)}] Admin {steamId} references unknown role '{entry.RoleName}'. Treating as no role.");
                return false;
            }

            roleName = entry.RoleName;
            return true;
        }

        /// <summary>
        /// Display role name. The host is labelled Owner even without an admin entry,
        /// matching its GetPermissions/GetLevel bypass. Dedicated has no host, so its
        /// owner must be a real "Owner" admin entry.
        /// </summary>
        public static string GetRoleName(ulong steamId)
        {
            if (IsHost(steamId)) return OwnerRoleName;
            return TryGetRole(steamId, out var name, out _) ? name : string.Empty;
        }

        public static Permissions GetPermissions(ulong steamId)
        {
            if (IsHost(steamId)) return PermissionsUtil.All;
            return TryGetRole(steamId, out _, out var def) ? def.Permissions : Permissions.None;
        }
        public static int GetLevel(ulong steamId)
        {
            if (IsHost(steamId)) return int.MaxValue;
            return TryGetRole(steamId, out _, out var def) ? def.Level : 0;
        }
        #endregion

        #region Checks
        /// <summary>Does the actor hold the given permission flag?</summary>
        public static bool Has(ulong steamId, Permissions permission)
        {
            if (ElevatedContext) return true;
            return (GetPermissions(steamId) & permission) == permission;
        }

        /// <summary>
        /// Can the actor act on the target by hierarchy? Strict: actor level must
        /// be greater than target level. Equal levels cannot touch each other.
        /// The host is untouchable; the host can touch anyone.
        /// </summary>
        public static bool CanTarget(ulong actor, ulong target)
        {
            if (ElevatedContext) return true;
            if (IsHost(target)) return false;
            if (IsHost(actor)) return true;
            return GetLevel(actor) > GetLevel(target);
        }

        /// <summary>Targeted action gate: actor needs the flag AND must outrank the target.</summary>
        public static bool CanActOn(ulong actor, ulong target, Permissions permission) =>
            Has(actor, permission) && CanTarget(actor, target);

        /// <summary>
        /// Assignment gate. Requires the AssignRoles flag AND the level rule:
        ///   - the role being granted must be below the actor's level, and
        ///   - the target must currently be below the actor's level.
        /// This makes it impossible to create a peer/superior admin.
        /// </summary>
        public static bool CanAssignRole(ulong actor, string roleName, ulong target)
        {
            EnsureInitialized();

            if (!_roles.TryGet(roleName, out var role))
                return false;

            if (ElevatedContext) 
                return true;

            if (!Has(actor, Permissions.AssignRoles))
                return false;

            int actorLevel = GetLevel(actor);
            if (role.Level >= actorLevel) return false;
            if (GetLevel(target) >= actorLevel) return false;

            return true;
        }

        /// <summary>
        /// Removal gate. Requires the AssignRoles flag AND that the actor outranks
        /// the target, so a peer/superior's role can't be stripped.
        /// </summary>
        public static bool CanRemoveRole(ulong actor, ulong target)
        {
            if (!Has(actor, Permissions.AssignRoles))
                return false;
            return CanTarget(actor, target);
        }
        #endregion

        #region Mutation
        /// <summary>
        /// Raw assignment - no hierarchy check here. Callers that act on behalf of
        /// a player (e.g. future in-game commands) must gate with CanAssignRole first.
        /// Host/console assignment is trusted.
        /// </summary>
        internal static void AssignRole(ulong steamId, string name, string roleName)
        {
            EnsureInitialized();

            if (!_roles.TryGet(roleName, out _))
            {
                Debug.LogWarning($"[{nameof(ServerRoles)}] Cannot assign unknown role '{roleName}' to {steamId}.");
                return;
            }

            _admins.Set(new AdminEntry { SteamId = steamId, Name = name, RoleName = roleName });
            OnRoleAssigned?.Invoke(steamId, roleName);
            Debug.Log($"[{nameof(ServerRoles)}] Assigned role '{roleName}' to {name} ({steamId}).");
        }
        /// <summary>
        /// Raw removal - no hierarchy check here. Callers acting on behalf of a
        /// player must gate with CanRemoveRole first. Host/console removal is trusted.
        /// </summary>
        internal static void RemoveRole(ulong steamId)
        {
            EnsureInitialized();
            _admins.Remove(steamId);
            OnRoleRemoved?.Invoke(steamId);
            Debug.Log($"[{nameof(ServerRoles)}] Removed role from {steamId}.");
        }
        #endregion

        public static IReadOnlyDictionary<string, RoleDefinition> AllRoles =>
            _roles?.All ?? new Dictionary<string, RoleDefinition>();
        public static IReadOnlyDictionary<ulong, AdminEntry> AllAdmins =>
            _admins?.All ?? new Dictionary<ulong, AdminEntry>();
    }
}