using System.Collections.Generic;
using System.Linq;
using System.IO;
using System;

using UnityEngine;

namespace HiddenBull.Networking.Data
{
    /// <summary>
    /// JSON-backed persistent dictionary storage.
    /// Auto-creates an empty file on first run; saves on every mutation.
    /// Used by BanStorage, WhitelistStorage, RoleStorage, AdminStorage,
    /// and any future keyed config.
    /// </summary>
    internal sealed class PersistentJsonStore<TKey, TValue>
    {
        private readonly string _filePath;
        private Dictionary<TKey, TValue> _entries = new();

        public PersistentJsonStore(string fileName)
        {
            _filePath = Path.Combine(Application.dataPath, "..", "config", fileName);

            if (File.Exists(_filePath))
                Load();
            else
                Save();   // create empty file so admin can see/edit it
        }
        public IReadOnlyDictionary<TKey, TValue> All => _entries;

        public bool Contains(TKey key) => _entries.ContainsKey(key);
        public bool TryGet(TKey key, out TValue value) =>
            _entries.TryGetValue(key, out value);

        public void Set(TKey key, TValue value)
        {
            _entries[key] = value;
            Save();
        }
        public bool Remove(TKey key)
        {
            bool removed = _entries.Remove(key);
            if (removed) Save();
            return removed;
        }

        private void Load()
        {
            try
            {
                string json = File.ReadAllText(_filePath);
                _entries = NetworkJson.FromJson<Dictionary<TKey, TValue>>(json) ?? new();
                Debug.Log($"[{nameof(PersistentJsonStore<TKey, TValue>)}<{typeof(TValue).Name}>] Loaded {_entries.Count} entries from {_filePath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{nameof(PersistentJsonStore<TKey, TValue>)}<{typeof(TValue).Name}>] Failed to load: {ex.Message}");
                _entries = new();
            }
        }
        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath));
                string json = NetworkJson.ToJson(_entries);
                if (json != null)
                    File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{nameof(PersistentJsonStore<TKey, TValue>)}<{typeof(TValue).Name}>] Failed to save: {ex.Message}");
            }
        }
    }

    internal abstract class BanStorage
    {
        public static BanStorage CreatePersistent() => new Persistent();
        public static BanStorage CreateRuntime() => new Runtime();

        protected abstract bool TryGet(ulong steamId, out ClientBanInformation info);
        public abstract void Add(ClientBanInformation banInfo);
        public abstract void Remove(ulong steamId);
        public abstract IReadOnlyCollection<ClientBanInformation> GetAll();

        /// <summary>
        /// Returns true if the SteamID has an active ban. Expired temporary bans
        /// are automatically removed on access - IsBanned doubles as cleanup.
        /// </summary>
        public bool IsBanned(ulong steamId, out ClientBanInformation banInfo)
        {
            if (!TryGet(steamId, out banInfo))
                return false;

            if (banInfo.IsExpired)
            {
                Remove(steamId);
                return false;
            }
            return true;
        }

        private sealed class Runtime : BanStorage
        {
            private readonly Dictionary<ulong, ClientBanInformation> _entries = new();

            protected override bool TryGet(ulong steamId, out ClientBanInformation info) =>
                _entries.TryGetValue(steamId, out info);

            public override void Add(ClientBanInformation banInfo) =>
                _entries[banInfo.SteamId] = banInfo;

            public override void Remove(ulong steamId) =>
                _entries.Remove(steamId);

            public override IReadOnlyCollection<ClientBanInformation> GetAll() =>
                _entries.Values;
        }
        private sealed class Persistent : BanStorage
        {
            private readonly PersistentJsonStore<ulong, ClientBanInformation> _store = new("bans.json");

            protected override bool TryGet(ulong steamId, out ClientBanInformation info) =>
                _store.TryGet(steamId, out info);

            public override void Add(ClientBanInformation banInfo) =>
                _store.Set(banInfo.SteamId, banInfo);

            public override void Remove(ulong steamId) =>
                _store.Remove(steamId);

            public override IReadOnlyCollection<ClientBanInformation> GetAll() =>
                _store.All.Values.ToArray();
        }
    }

    internal abstract class WhitelistStorage
    {
        public static WhitelistStorage CreatePersistent() => new Persistent();
        public static WhitelistStorage CreateRuntime() => new Runtime();

        public abstract bool IsWhitelisted(ulong steamId);
        public abstract void Add(WhitelistEntry entry);
        public abstract void Remove(ulong steamId);
        public abstract IReadOnlyDictionary<ulong, WhitelistEntry> All { get; }

        private sealed class Runtime : WhitelistStorage
        {
            private readonly Dictionary<ulong, WhitelistEntry> _entries = new();

            public override bool IsWhitelisted(ulong steamId) => _entries.ContainsKey(steamId);
            public override void Add(WhitelistEntry entry) => _entries[entry.SteamId] = entry;
            public override void Remove(ulong steamId) => _entries.Remove(steamId);
            public override IReadOnlyDictionary<ulong, WhitelistEntry> All => _entries;
        }
        private sealed class Persistent : WhitelistStorage
        {
            private readonly PersistentJsonStore<ulong, WhitelistEntry> _store = new("whitelist.json");

            public override bool IsWhitelisted(ulong steamId) => _store.Contains(steamId);
            public override void Add(WhitelistEntry entry) => _store.Set(entry.SteamId, entry);
            public override void Remove(ulong steamId) => _store.Remove(steamId);
            public override IReadOnlyDictionary<ulong, WhitelistEntry> All => _store.All;
        }
    }

    internal abstract class RoleStorage
    {
        public static RoleStorage CreatePersistent() => new Persistent();
        public static RoleStorage CreateRuntime() => new Runtime();

        public abstract bool TryGet(string roleName, out RoleDefinition definition);
        public abstract IReadOnlyDictionary<string, RoleDefinition> All { get; }

        protected static Dictionary<string, RoleDefinition> Defaults() => new()
        {
            ["Owner"] = new RoleDefinition
            {
                Permissions = PermissionsUtil.All,
                Level = 100
            },
            ["Admin"] = new RoleDefinition
            {
                Permissions = Permissions.Kick | Permissions.Ban | Permissions.Whitelist,
                Level = 50
            },
            ["Moderator"] = new RoleDefinition
            {
                Permissions = Permissions.Kick,
                Level = 20
            }
        };

        private sealed class Runtime : RoleStorage
        {
            private readonly Dictionary<string, RoleDefinition> _entries = Defaults();

            public override bool TryGet(string roleName, out RoleDefinition definition) =>
                _entries.TryGetValue(roleName, out definition);

            public override IReadOnlyDictionary<string, RoleDefinition> All => _entries;
        }
        private sealed class Persistent : RoleStorage
        {
            private readonly PersistentJsonStore<string, RoleDefinition> _store = new("roles.json");

            public Persistent()
            {
                if (_store.All.Count == 0)
                {
                    foreach (var kvp in Defaults())
                        _store.Set(kvp.Key, kvp.Value);
                    Debug.Log($"[{nameof(RoleStorage)}] Seeded default roles into roles.json.");
                }
            }

            public override bool TryGet(string roleName, out RoleDefinition definition) =>
                _store.TryGet(roleName, out definition);

            public override IReadOnlyDictionary<string, RoleDefinition> All => _store.All;
        }
    }

    internal abstract class AdminStorage
    {
        public static AdminStorage CreatePersistent() => new Persistent();
        public static AdminStorage CreateRuntime() => new Runtime();

        public abstract bool TryGet(ulong steamId, out AdminEntry entry);
        public abstract void Set(AdminEntry entry);
        public abstract void Remove(ulong steamId);
        public abstract IReadOnlyDictionary<ulong, AdminEntry> All { get; }

        private sealed class Runtime : AdminStorage
        {
            private readonly Dictionary<ulong, AdminEntry> _entries = new();

            public override bool TryGet(ulong steamId, out AdminEntry entry) =>
                _entries.TryGetValue(steamId, out entry);

            public override void Set(AdminEntry entry) => _entries[entry.SteamId] = entry;
            public override void Remove(ulong steamId) => _entries.Remove(steamId);
            public override IReadOnlyDictionary<ulong, AdminEntry> All => _entries;
        }
        private sealed class Persistent : AdminStorage
        {
            private readonly PersistentJsonStore<ulong, AdminEntry> _store = new("admins.json");

            public override bool TryGet(ulong steamId, out AdminEntry entry) =>
                _store.TryGet(steamId, out entry);

            public override void Set(AdminEntry entry) => _store.Set(entry.SteamId, entry);
            public override void Remove(ulong steamId) => _store.Remove(steamId);
            public override IReadOnlyDictionary<ulong, AdminEntry> All => _store.All;
        }
    }
}
