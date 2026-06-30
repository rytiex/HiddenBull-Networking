using HiddenBull.Networking.Data;
using UnityEngine;

using System.Collections.Generic;
using System;

namespace HiddenBull.Networking.Server
{
    internal sealed class ServerRateLimiter
    {
        public sealed class Config
        {
            /// <summary>Maximum failed attempts before the SteamID is temporarily blocked.</summary>
            public int MaxAttempts { get; set; } = 5;

            /// <summary>Minimum seconds required between connection attempts from the same SteamID.</summary>
            public float CooldownSeconds { get; set; } = 3f;

            /// <summary>Duration in seconds a SteamID remains blocked after exceeding MaxAttempts.</summary>
            public float BlockDurationSeconds { get; set; } = 60f;

            /// <summary>Seconds after which a SteamID's failed attempt record is cleared automatically.</summary>
            public float RecordExpirySeconds { get; set; } = 300f;
        }

        private sealed class AttemptRecord
        {
            public int FailedAttempts;
            public DateTime LastAttemptUtc;
            public DateTime BlockedUntilUtc;

            public bool IsBlocked => DateTime.UtcNow < BlockedUntilUtc;
            public bool IsExpired(float expirySeconds) =>
                (DateTime.UtcNow - LastAttemptUtc).TotalSeconds >= expirySeconds;
        }

        private readonly Config _config;
        private readonly Dictionary<ulong, AttemptRecord> _records = new();

        public ServerRateLimiter(Config config = null)
        {
            _config = config ?? new Config();
        }

        /// <summary>
        /// Checks whether the SteamID is allowed to attempt a connection.
        /// Does not mutate failure counters.
        /// </summary>
        public bool IsAllowed(ulong steamId, out PicoShot.Localization.TextNode rejectionReason)
        {
            rejectionReason = PicoShot.Localization.TextNode.Empty;

            CleanExpiredRecord(steamId);

            if (!_records.TryGetValue(steamId, out var record))
            {
                _records[steamId] = new AttemptRecord { LastAttemptUtc = DateTime.UtcNow };
                return true;
            }

            if (record.IsBlocked)
            {
                int remaining = (int)(record.BlockedUntilUtc - DateTime.UtcNow).TotalSeconds;
                rejectionReason = NetworkLocalizationMessages.RateLimit.Blocked(remaining);
                return false;
            }

            var secondsSinceLast = (DateTime.UtcNow - record.LastAttemptUtc).TotalSeconds;
            if (secondsSinceLast < _config.CooldownSeconds)
            {
                int remaining = (int)(_config.CooldownSeconds - secondsSinceLast) + 1;
                rejectionReason = NetworkLocalizationMessages.RateLimit.Cooldown(remaining);
                return false;
            }

            record.LastAttemptUtc = DateTime.UtcNow;
            return true;
        }

        /// <summary>Clears the failed attempt record for the SteamID.</summary>
        public void RecordSuccess(ulong steamId)
        {
            if (_records.Remove(steamId))
                Debug.Log($"[{nameof(ServerRateLimiter)}] Record cleared for SteamID {steamId} after successful connection.");
        }

        /// <summary>Increments the failed attempt counter and blocks if MaxAttempts is exceeded.</summary>
        public void RecordFailure(ulong steamId)
        {
            if (!_records.TryGetValue(steamId, out var record))
            {
                record = new AttemptRecord();
                _records[steamId] = record;
            }

            record.FailedAttempts++;
            record.LastAttemptUtc = DateTime.UtcNow;

            if (record.FailedAttempts >= _config.MaxAttempts)
            {
                record.BlockedUntilUtc = DateTime.UtcNow.AddSeconds(_config.BlockDurationSeconds);
                Debug.LogWarning($"[{nameof(ServerRateLimiter)}] SteamID {steamId} blocked for {_config.BlockDurationSeconds}s after {record.FailedAttempts} failed attempts.");
            }
        }

        private void CleanExpiredRecord(ulong steamId)
        {
            if (_records.TryGetValue(steamId, out var record) && record.IsExpired(_config.RecordExpirySeconds))
            {
                _records.Remove(steamId);
                Debug.Log($"[{nameof(ServerRateLimiter)}] Expired record cleared for SteamID {steamId}.");
            }
        }
    }
}