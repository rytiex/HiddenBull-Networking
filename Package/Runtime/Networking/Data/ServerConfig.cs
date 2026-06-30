using UnityEngine;
using System.IO;
using System;

namespace HiddenBull.Networking.Data
{
    [Serializable]
    internal sealed class ServerConfig
    {
        private static readonly string ConfigDirectory = Path.Combine(Application.dataPath, "..", "config");
        private static readonly string FilePath = Path.Combine(ConfigDirectory, "server.json");

        public string ServerName { get; set; } = $"{Steam.SteamInformation.GameDescription} Server";
        public string Password { get; set; } = string.Empty;
        public ushort Port { get; set; } = 27015;
        public int TickRate { get; set; } = NetworkState.Tick.DefaultTickRate;
        public int MaxPlayers { get; set; } = 16;
        public string Scene { get; set; } = string.Empty;

        public static ServerConfig Load()
        {
            try
            {
                Directory.CreateDirectory(ConfigDirectory);

                if (!File.Exists(FilePath))
                {
                    var defaults = new ServerConfig();
                    defaults.Save();
                    Debug.Log($"[{nameof(ServerConfig)}] No config found, created default at {FilePath}");
                    return defaults;
                }

                string json = File.ReadAllText(FilePath);
                var config = NetworkJson.FromJson<ServerConfig>(json) ?? new ServerConfig();
                Debug.Log($"[{nameof(ServerConfig)}] Loaded from {FilePath}");
                return config;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{nameof(ServerConfig)}] Failed to load: {ex.Message}. Using defaults.");
                return new ServerConfig();
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigDirectory);
                File.WriteAllText(FilePath, NetworkJson.ToJson(this));
                Debug.Log($"[{nameof(ServerConfig)}] Saved to {FilePath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{nameof(ServerConfig)}] Failed to save: {ex.Message}");
            }
        }
    }
}