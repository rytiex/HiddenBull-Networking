using HiddenBull.Networking.Data;
using PicoShot.Localization;
using UnityEngine;

using System.Collections.Generic;
using System;

namespace HiddenBull.Networking.Server
{
    internal static class ServerBanModeration
    {
        private static BanStorage _storage;

        public static event Action<ulong, TextNode> OnKickRequested;
        public static event Action<ClientBanInformation> OnPlayerBanned;
        public static event Action<ulong> OnPlayerUnbanned;

        /// <summary>
        /// Must be called before any moderation actions.
        /// Use BanStorage.CreatePersistent() for dedicated, BanStorage.CreateRuntime() for P2P/Host.
        /// </summary>
        public static void Initialize(BanStorage storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            Debug.Log($"[{nameof(ServerBanModeration)}] Initialized.");
        }
        private static void EnsureInitialized()
        {
            if (_storage == null)
                throw new InvalidOperationException($"{nameof(ServerBanModeration)} is not initialized. Call Initialize() first.");
        }

        private static bool IsHostConnection(ulong steamId)
        {
            // localConnection is non-null only in Host mode; null in Dedicated.
            return Mirror.NetworkServer.localConnection != null &&
                   Mirror.NetworkServer.localConnection.authenticationData is Data.ClientData data &&
                   data.SteamId == steamId;
        }
        public static IReadOnlyCollection<ClientBanInformation> GetAllBans()
        {
            EnsureInitialized();
            return _storage.GetAll();
        }
        public static bool IsBanned(ulong steamId, out ClientBanInformation banInfo)
        {
            EnsureInitialized();
            return _storage.IsBanned(steamId, out banInfo);
        }

        public static void Ban(ulong steamId, string playerName, string reason, DateTime? expiresAtUtc = null)
        {
            if (IsHostConnection(steamId))
            {
                Debug.LogWarning($"[{nameof(ServerBanModeration)}] Cannot ban the host ({steamId}). Use StopHost to leave.");
                return;
            }

            EnsureInitialized();

            var banInfo = new ClientBanInformation
            {
                SteamId = steamId,
                Name = playerName,
                Reason = reason,
                BanDateUtc = DateTime.UtcNow,
                ExpirationUtc = expiresAtUtc ?? DateTime.MaxValue
            };

            _storage.Add(banInfo);
            OnPlayerBanned?.Invoke(banInfo);

            TextNode message = banInfo.IsPermanent
                ? NetworkLocalizationMessages.Ban.Permanent(reason)
                : NetworkLocalizationMessages.Ban.Temporary(reason, banInfo.TimeRemaining);

            Kick(steamId, message);

            Debug.Log($"[{nameof(ServerBanModeration)}] Banned {playerName} ({steamId}). Reason: {reason}. Expires: {banInfo.ExpirationUtc}");
        }
        public static void Unban(ulong steamId)
        {
            EnsureInitialized();
            _storage.Remove(steamId);
            OnPlayerUnbanned?.Invoke(steamId);
            Debug.Log($"[{nameof(ServerBanModeration)}] Unbanned {steamId}.");
        }

        public static void Kick(ulong steamId, TextNode reason = default)
        {
            if (IsHostConnection(steamId))
            {
                Debug.LogWarning($"[{nameof(ServerBanModeration)}] Cannot kick the host ({steamId}). Use StopHost to leave.");
                return;
            }

            EnsureInitialized();

            if (reason.IsEmpty)
                reason = NetworkLocalizationMessages.Kick.Message;

            OnKickRequested?.Invoke(steamId, reason);
            Debug.Log($"[{nameof(ServerBanModeration)}] Kick requested for {steamId}. Reason: {reason}");
        }
    }

    internal static class ServerWhitelistModeration
    {
        private static WhitelistStorage _storage;

        /// <summary>
        /// True when the whitelist has at least one entry. List is the toggle —
        /// no separate enabled flag. To disable enforcement, clear the list or
        /// delete whitelist.json. Empty list = no enforcement (safe default).
        /// </summary>
        public static bool IsActive =>
            _storage != null && _storage.All.Count > 0;

        public static void Initialize(WhitelistStorage storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            Debug.Log($"[{nameof(ServerWhitelistModeration)}] Initialized. Entries: {_storage.All.Count}, Active: {IsActive}");
        }

        public static bool IsWhitelisted(ulong steamId) =>
            _storage != null && _storage.IsWhitelisted(steamId);
        public static IReadOnlyDictionary<ulong, WhitelistEntry> All =>
            _storage?.All ?? new Dictionary<ulong, WhitelistEntry>();

        /// <summary>
        /// Adds a player to the whitelist. If addedBy is provided and not already
        /// whitelisted, the adder is auto-added too (prevents self-lockout when
        /// an admin adds the first entry to an empty whitelist).
        /// </summary>
        public static void Add(
            ulong steamId, string name = "", string reason = "",
            ulong addedBy = 0, string addedByName = "")
        {
            if (_storage == null) return;

            _storage.Add(new WhitelistEntry
            {
                SteamId = steamId,
                Name = name,
                Reason = reason,
                AddedUtc = DateTime.UtcNow,

                AddedBy = addedBy,
                AddedByName = addedByName
            });
            Debug.Log($"[{nameof(ServerWhitelistModeration)}] Added {name} ({steamId}) by {addedByName} ({addedBy}). Reason: {reason}");

            if (addedBy != 0 && !_storage.IsWhitelisted(addedBy))
            {
                _storage.Add(new WhitelistEntry
                {
                    SteamId = addedBy,
                    Name = addedByName,
                    Reason = "Auto-added on first whitelist action",
                    AddedUtc = DateTime.UtcNow,

                    AddedBy = 0,
                    AddedByName = "system"
                });
                Debug.Log($"[{nameof(ServerWhitelistModeration)}] Auto-added initiator {addedByName} ({addedBy}) to prevent self-lockout.");
            }
        }
        public static void Remove(ulong steamId)
        {
            if (_storage == null) return;
            _storage.Remove(steamId);
            Debug.Log($"[{nameof(ServerWhitelistModeration)}] Removed {steamId}.");
        }
    }
}