using HiddenBull.Networking.Data;
using Steamworks.Data;
using Steamworks;
using UnityEngine;

using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace HiddenBull.Networking.Steam
{
    /// <summary>
    /// The single Steam-side integration point for the server browser. Covers BOTH server
    /// kinds and BOTH directions:
    ///   - Discovery (read):    dedicated via Steam's game-server list + P2P via Steam lobbies,
    ///                          merged into NetworkState.Browser through one registered source.
    ///   - Advertising (write): when THIS peer hosts (P2P), publishes a Steam lobby carrying the
    ///                          server metadata so other clients' P2P discovery can find it.
    /// To add a field for either kind, edit the matching map/write helper here - one place.
    /// </summary>
    internal static class SteamServerBrowser
    {
        private const float QueryTimeoutSeconds = 10f;

        private static class Keys   // lobby data keys
        {
            public const string Game = "game";
            public const string Name = "name";
            public const string Map = "map";
            public const string Password = "pw";       // "1"/"0"
            public const string Modded = "mod";        // "1"/"0"
            public const string Players = "players";
            public const string Host = "host";         // host SteamId (connect target)
            public const string PingLoc = "ploc";      // host's Steam ping location (for P2P ping)
            public const string Version = "ver";
        }

        private static NetworkState.Browser.ServerQuery _source;
        private static Lobby? _lobby;
        private static bool _enabled;
        private static bool _creatingLobby;

        public static void Enable()
        {
            if (_enabled) return;
            _enabled = true;
            if (SteamClient.IsValid) SteamNetworkingUtils.InitRelayNetworkAccess();

            _source = QueryAsync;
            NetworkState.Browser.AddSource(_source);

            NetworkState.Server.OnStopped += HandleServerStopped;
            NetworkState.Players.OnRosterChanged += WriteLobbyData;
            NetworkState.Scene.OnLoaded += HandleSceneReady;
        }
        public static void Disable()
        {
            if (!_enabled) return;
            _enabled = false;

            NetworkState.Browser.RemoveSource(_source);
            _source = null;

            NetworkState.Server.OnStopped -= HandleServerStopped;
            NetworkState.Players.OnRosterChanged -= WriteLobbyData;
            NetworkState.Scene.OnLoaded -= HandleSceneReady;

            CloseLobby();
        }

        // Discovery (read)
        private static async Task QueryAsync(Action<ServerEntry> onFound, CancellationToken ct)
        {
            if (!SteamClient.IsValid) return;

            var dedicated = new Dictionary<ulong, ServerEntry>();   // per-round merge of internet + LAN

            void ReportDedicated(ServerInfo info, bool lan)
            {
                if (ct.IsCancellationRequested) return;
                if (!dedicated.TryGetValue(info.SteamId, out var e))
                {
                    e = MapServer(info);
                    dedicated[info.SteamId] = e;
                }
                if (lan) e.OnLan = true; else e.OnInternet = true;
                if (info.Ping >= 0 && (e.PingMs < 0 || info.Ping < e.PingMs)) e.PingMs = info.Ping;  // keep the lower (local) ping
                onFound(e);
            }

            await Task.WhenAll(
                QueryDedicatedAsync(i => ReportDedicated(i, false), ct),
                QueryLanAsync(i => ReportDedicated(i, true), ct),
                QueryLobbiesAsync(onFound, ct));
        }
        private static async Task QueryDedicatedAsync(Action<ServerInfo> onInfo, CancellationToken ct)
        {
            using var list = new Steamworks.ServerList.Internet();
            using var reg = ct.Register(list.Cancel);
            list.OnResponsiveServer += info => { if (!ct.IsCancellationRequested) onInfo(info); };
            await list.RunQueryAsync(QueryTimeoutSeconds);
        }
        private static async Task QueryLanAsync(Action<ServerInfo> onInfo, CancellationToken ct)
        {
            using var list = new Steamworks.ServerList.LocalNetwork();
            using var reg = ct.Register(list.Cancel);
            list.OnResponsiveServer += info => { if (!ct.IsCancellationRequested) onInfo(info); };
            await list.RunQueryAsync(QueryTimeoutSeconds);
        }
        private static async Task QueryLobbiesAsync(Action<ServerEntry> onFound, CancellationToken ct)
        {
            await SteamNetworkingUtils.WaitForPingDataAsync();   // enables EstimatePingTo for P2P entries

            var lobbies = await SteamMatchmaking.LobbyList
                .WithKeyValue(Keys.Game, SteamInformation.ModDir)
                .FilterDistanceWorldwide()
                .RequestAsync();

            if (lobbies == null) return;
            foreach (var lobby in lobbies)
            {
                if (ct.IsCancellationRequested) return;
                onFound(MapLobby(lobby));
            }
        }

        private static ServerEntry MapServer(ServerInfo s) => new()
        {
            Name = s.Name,
            Map = s.Map,
            Players = s.Players,
            MaxPlayers = s.MaxPlayers,
            HasPassword = s.Passworded,
            PingMs = s.Ping,
            IsModded = ParseModdedTag(s.Tags),
            Version = ParseVersionTag(s.Tags),
            Kind = ServerKind.Dedicated,
            SteamId = s.SteamId,
            Address = s.SteamId.ToString(),
            SourceId = "steam",
        };
        private static ServerEntry MapLobby(Lobby lobby)
        {
            ulong host = ulong.TryParse(lobby.GetData(Keys.Host), out var h) ? h : lobby.Owner.Id.Value;
            return new ServerEntry
            {
                Name = Fallback(lobby.GetData(Keys.Name), lobby.Owner.Name),
                Map = lobby.GetData(Keys.Map),
                Players = ParseInt(lobby.GetData(Keys.Players), lobby.MemberCount),
                MaxPlayers = lobby.MaxMembers,
                HasPassword = lobby.GetData(Keys.Password) == "1",
                IsModded = lobby.GetData(Keys.Modded) == "1",
                Version = lobby.GetData(Keys.Version),
                PingMs = EstimateLobbyPing(lobby.GetData(Keys.PingLoc)),
                Kind = ServerKind.P2P,
                SteamId = host,
                Address = host.ToString(),
                SourceId = "steam-lobby",
            };
        }

        // Advertising (write)
        private static async void HandleSceneReady(string sceneId)
        {
            if (_lobby != null) { WriteLobbyData(); return; }   // later scene change -> just refresh
            if (_creatingLobby) return;

            // Only a P2P host advertises, and only once the scene is loaded (server ready).
            if (!NetworkState.IsHost || !SteamClient.IsValid) return;
            var transport = NetworkState.Server.Transport;
            if (transport != ServerTransportMode.Steam && transport != ServerTransportMode.Both) return;

            _creatingLobby = true;
            int max = Mathf.Clamp(NetworkState.Server.MaxPlayers, 1, 250);
            var created = await SteamMatchmaking.CreateLobbyAsync(max);
            _creatingLobby = false;

            if (created == null) { Debug.LogWarning($"[{nameof(SteamServerBrowser)}] Lobby creation failed."); return; }
            var lobby = created.Value;
            if (!NetworkState.IsHost) { lobby.Leave(); return; }   // stopped during the await

            lobby.SetPublic();
            lobby.SetJoinable(true);
            lobby.SetData(Keys.Game, SteamInformation.ModDir);
            lobby.SetData(Keys.Host, SteamInformation.LocalSteamId.ToString());
            _lobby = lobby;
            WriteLobbyData();
            Debug.Log($"[{nameof(SteamServerBrowser)}] Advertising lobby {lobby.Id} (server ready).");
        }
        private static void HandleServerStopped() => CloseLobby();

        private static void WriteLobbyData()
        {
            if (_lobby == null || !SteamClient.IsValid) return;
            try
            {
                var lobby = _lobby.Value;
                lobby.SetData(Keys.Name, NetworkState.Server.Name ?? string.Empty);
                lobby.SetData(Keys.Map, NetworkState.Scene.Current ?? string.Empty);
                lobby.SetData(Keys.Password, NetworkState.Server.HasPassword ? "1" : "0");
                lobby.SetData(Keys.Modded, IsLocalModded() ? "1" : "0");
                lobby.SetData(Keys.Players, NetworkState.Players.All.Count.ToString());
                lobby.SetData(Keys.Version, Application.version);

                var ploc = SteamNetworkingUtils.LocalPingLocation;
                if (ploc.HasValue) lobby.SetData(Keys.PingLoc, ploc.Value.ToString());
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[{nameof(SteamServerBrowser)}] Lobby data write skipped: {ex.Message}");
            }
        }
        private static void CloseLobby()
        {
            _creatingLobby = false;
            if (_lobby == null) return;
            _lobby.Value.Leave();
            _lobby = null;
        }

        // helpers
        private static bool IsLocalModded()
        {
            var keys = NetworkContentGate.GetRequiredKeys?.Invoke();
            return keys != null && keys.Length > 0;
        }
        private static int EstimateLobbyPing(string ploc)
        {
            if (string.IsNullOrEmpty(ploc)) return -1;

            var loc = NetPingLocation.TryParseFromString(ploc);
            if (!loc.HasValue) return -1;

            int ms = SteamNetworkingUtils.EstimatePingTo(loc.Value);
            return ms < 0 ? -1 : ms;
        }
        private static string ParseVersionTag(string[] tags)
        {
            if (tags == null) return string.Empty;
            foreach (var t in tags)
                if (!string.IsNullOrEmpty(t) && t.StartsWith("v=")) return t[2..];
            return string.Empty;
        }
        private static bool ParseModdedTag(string[] tags)
        {
            if (tags == null) return false;
            foreach (var t in tags)
                if (t == "mod") return true;
            return false;
        }
        private static int ParseInt(string s, int fallback) =>
            int.TryParse(s, out var v) ? v : fallback;
        private static string Fallback(string primary, string secondary) =>
            string.IsNullOrEmpty(primary) ? secondary : primary;
    }
}