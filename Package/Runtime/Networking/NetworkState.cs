using HiddenBull.Networking.Server;
using HiddenBull.Networking.Data;
using HiddenBull.Scenes;

using PicoShot.Localization;
using UnityEngine;
using Mirror;

using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace HiddenBull.Networking
{
    /// <summary>
    /// The single public facade for network session state. PRESENTATION + READ layer:
    /// gameplay, UI, autofill, and the console talk only to NetworkState, never to
    /// NetworkSessionManager / ServerRoles directly.
    ///
    /// Grouped by perspective:
    ///   - NetworkState.Server  : server-authoritative lifecycle + connection events (carry ClientData).
    ///   - NetworkState.Client  : this client's own connection + server notifications.
    ///   - NetworkState.Players : shared roster (replicated to clients), admins, local player.
    ///
    /// Forward-only: NetworkState holds NO logic. NetworkSessionManager is the sole driver
    /// that raises these events; other systems plug in the same way as they come online.
    /// </summary>
    public static class NetworkState
    {
        public static bool IsServer => NetworkServer.active;
        public static bool IsClient => NetworkClient.active;
        public static bool IsHost => NetworkServer.active && NetworkClient.active;

        /// <summary>Full reset on server/client teardown.</summary>
        public static void Clear()
        {
            Players.Clear_Internal();
            Client.Reset_Internal();
            Roles.Clear_Internal();
            Tick.Reset_Internal();
            Scene.Reset_Internal();
            Communication.Reset_Internal();
            Server.ResetInfo_Internal();
        }

        // Server perspective
        public static class Server
        {
            // PUBLIC
            public static event Action OnStarted;
            public static event Action OnStopped;
            public static event Action<ClientData> OnClientConnected;
            public static event Action<ClientData> OnClientDisconnected;

            public static string Name { get; private set; } = string.Empty;
            public static int MaxPlayers { get; private set; }
            public static bool HasPassword { get; private set; }
            public static ServerTransportMode Transport { get; private set; }


            // INTERNALS
            internal static void RaiseStarted_Internal() => OnStarted?.Invoke();
            internal static void RaiseStopped_Internal() => OnStopped?.Invoke();
            internal static void RaiseClientConnected_Internal(ClientData data) => OnClientConnected?.Invoke(data);
            internal static void RaiseClientDisconnected_Internal(ClientData data) => OnClientDisconnected?.Invoke(data);

            internal static void SetInfo_Internal(string name, int maxPlayers, bool hasPassword, ServerTransportMode transport)
            { Name = name ?? string.Empty; MaxPlayers = maxPlayers; HasPassword = hasPassword; Transport = transport; }
            internal static void ResetInfo_Internal()
            { Name = string.Empty; MaxPlayers = 0; HasPassword = false; Transport = default; }
        }

        // Client perspective
        public static class Client
        {
            // PUBLIC
            public static bool IsConnecting { get; private set; }

            /// <summary>True once fully connected (Mirror's isConnected), not merely connecting.</summary>
            public static bool IsConnected => NetworkClient.isConnected;

            public static event Action OnConnected;
            public static event Action OnDisconnected;

            public static event Action<TextNode> OnServerInfo;
            public static event Action<TextNode> OnServerWarning;
            public static event Action<TextNode> OnDisconnectReason;

            public static event Action<float> OnContentProgress;   // client: required-content prepare progress 0..1

            // INTERNALS
            internal static void SetConnecting_Internal(bool value) => IsConnecting = value;
            internal static void RaiseConnected_Internal() { IsConnecting = false; OnConnected?.Invoke(); }
            internal static void RaiseDisconnected_Internal() { IsConnecting = false; OnDisconnected?.Invoke(); }
            internal static void RaiseContentProgress_Internal(float p) => OnContentProgress?.Invoke(p);

            internal static void RaiseServerInfo_Internal(TextNode content) => OnServerInfo?.Invoke(content);
            internal static void RaiseServerWarning_Internal(TextNode content) => OnServerWarning?.Invoke(content);
            internal static void RaiseDisconnectReason_Internal(TextNode content) => OnDisconnectReason?.Invoke(content);

            internal static void Reset_Internal() => IsConnecting = false;
        }

        // Scene: catalog + active scene + server scene control. The single facade for scenes.
        public static class Scene
        {
            /// <summary>The scene this peer last loaded (server: its scene; client: the joined scene).</summary>
            public static string Current { get; private set; }

            /// <summary>Fires when this peer finishes loading a scene (once per load; host = the server load).</summary>
            public static event Action<string> OnLoaded;

            // catalog / read (every build ships the same SceneData) -> SceneUtils
            public static IReadOnlyCollection<SceneData> All => SceneUtils.All;
            public static SceneData Default => SceneUtils.Default;
            public static SceneData Menu => SceneUtils.Menu;

            public static SceneData Resolve(string id) => SceneUtils.Resolve(id);
            public static bool TryGet(string id, out SceneData data) => SceneUtils.TryGet(id, out data);
            public static void Reload() => SceneUtils.Reload();

            /// <summary>Server: change the active scene at runtime (loads it + re-syncs clients).
            /// Returns false if already on it / not an active server.</summary>
            public static bool Change(string sceneId) =>
                NetworkServer.active && NetworkSessionManager.singleton != null
                && NetworkSessionManager.singleton.SetServerScene(sceneId);

            internal static void RaiseLoaded_Internal(string sceneId) { Current = sceneId; OnLoaded?.Invoke(sceneId); }
            internal static void Reset_Internal() => Current = null;
        }

        // Shared roster & presentation
        public static class Players
        {
            // PRIVATE
            private static readonly Dictionary<ulong, PlayerInfo> _players = new();
            private static readonly Dictionary<ulong, int> _pings = new();
            private static bool _hasSynced;


            // PUBLIC
            public static IReadOnlyDictionary<ulong, PlayerInfo> All => _players;
            public static PlayerInfo Local { get; private set; }

            public static event Action<PlayerInfo> OnJoined;
            public static event Action<PlayerInfo> OnLeft;
            public static event Action<PlayerInfo> OnRoleChanged;
            public static event Action OnRosterChanged;
            public static event Action OnPingsUpdated;
            public static event Action OnSynced;   // client only: initial snapshot received

            public static IEnumerable<PlayerInfo> Admins
            {
                get
                {
                    foreach (var player in _players.Values)
                        if (player.IsAdmin)
                            yield return player;
                }
            }
            public static bool TryGet(ulong steamId, out PlayerInfo info) =>
                _players.TryGetValue(steamId, out info);
            public static int GetPing(ulong steamId) =>
                _pings.TryGetValue(steamId, out var ms) ? ms : -1;


            // INTERNALS
            internal static void SetLocal_Internal(PlayerInfo info)
            {
                // Prefer the authoritative roster entry if it already exists (carries the
                // resolved role, e.g. host = Owner). Otherwise use the basic info; a pure
                // client's initial roster sync will then fill in the role via Diff.
                Local = _players.TryGetValue(info.SteamId, out var existing) ? existing : info;
            }
            internal static void ServerApply_Internal(IReadOnlyList<PlayerInfo> snapshot)
            {
                Diff_Internal(snapshot, suppressJoinEvents: false);
                _hasSynced = true;
                OnRosterChanged?.Invoke();
            }
            internal static void ClientApply_Internal(IReadOnlyList<PlayerInfo> snapshot)
            {
                bool initial = !_hasSynced;
                Diff_Internal(snapshot, suppressJoinEvents: initial);
                _hasSynced = true;

                if (initial) OnSynced?.Invoke();
                OnRosterChanged?.Invoke();
            }
            internal static void ApplyPings_Internal(PingEntry[] entries)
            {
                _pings.Clear();
                if (entries != null)
                    foreach (var e in entries) _pings[e.SteamId] = e.PingMs;
                OnPingsUpdated?.Invoke();
            }
            private static void Diff_Internal(IReadOnlyList<PlayerInfo> snapshot, bool suppressJoinEvents)
            {
                var incoming = new Dictionary<ulong, PlayerInfo>(snapshot.Count);
                foreach (var player in snapshot)
                    incoming[player.SteamId] = player;

                var left = new List<PlayerInfo>();
                foreach (var kvp in _players)
                    if (!incoming.ContainsKey(kvp.Key))
                        left.Add(kvp.Value);
                foreach (var player in left)
                    _players.Remove(player.SteamId);

                var joined = new List<PlayerInfo>();
                var roleChanged = new List<PlayerInfo>();
                foreach (var player in snapshot)
                {
                    if (!_players.TryGetValue(player.SteamId, out var existing))
                    {
                        _players[player.SteamId] = player;
                        joined.Add(player);
                    }
                    else if (existing.RoleName != player.RoleName || existing.Name != player.Name)
                    {
                        bool roleDiff = existing.RoleName != player.RoleName;
                        _players[player.SteamId] = player;
                        if (roleDiff) roleChanged.Add(player);
                    }
                }

                if (Local.SteamId != 0 && incoming.TryGetValue(Local.SteamId, out var me))
                    Local = me;

                foreach (var player in left) OnLeft?.Invoke(player);
                if (!suppressJoinEvents)
                    foreach (var player in joined) OnJoined?.Invoke(player);
                foreach (var player in roleChanged) OnRoleChanged?.Invoke(player);
            }
            internal static void Clear_Internal()
            {
                _hasSynced = false;
                _pings.Clear();

                if (_players.Count == 0 && Local.SteamId == 0)
                    return;

                _players.Clear();
                Local = default;
                OnRosterChanged?.Invoke();
            }
        }

        // Communication: shared channels, text & voice. The single facade for all comms.
        public static class Communication
        {
            // STRUCTS
            public readonly struct ChatEntry
            {
                public readonly ulong SenderSteamId;
                public readonly string SenderName;
                public readonly string Channel;
                public readonly string Text;
                public readonly bool IsWhisper;
                public ChatEntry(ulong senderSteamId, string senderName, string channel, string text, bool isWhisper)
                { SenderSteamId = senderSteamId; SenderName = senderName; Channel = channel; Text = text; IsWhisper = isWhisper; }
            }
            public readonly struct ChannelInfo
            {
                public readonly string Key;
                public readonly string Label;
                public readonly string ColorHex;   // RRGGBB (rich-text ready)
                public ChannelInfo(string key, string label, string colorHex) { Key = key; Label = label; ColorHex = colorHex; }
                public Color Color => ColorUtility.TryParseHtmlString("#" + ColorHex, out var c) ? c : Color.white;
            }

            // Channels (shared by text & voice)
            private static string[] _myChannels = Array.Empty<string>();
            private static readonly Dictionary<string, ChannelInfo> _catalog = new();

            public static IReadOnlyList<string> MyChannels => _myChannels;
            public static IReadOnlyDictionary<string, ChannelInfo> Channels => _catalog;

            public static event Action OnChannelsChanged;   // your membership changed
            public static event Action OnCatalogChanged;    // channel styles changed

            /// <summary>Display info for a channel; falls back to UPPERCASE key + white if undefined.</summary>
            public static ChannelInfo GetChannel(string key) =>
                !string.IsNullOrEmpty(key) && _catalog.TryGetValue(key, out var info)
                    ? info : new ChannelInfo(key, key?.ToUpperInvariant() ?? "", "FFFFFF");

            // Server channel ops (no-op off the server) -> ServerChat engine.
            public static void Join(ulong steamId, string channel, bool leaveOthers = true)
            { if (NetworkServer.active) ServerChat.Join(steamId, channel, leaveOthers); }
            public static void Leave(ulong steamId, string channel)
            { if (NetworkServer.active) ServerChat.Leave(steamId, channel); }
            public static void RemoveFromAll(ulong steamId)
            { if (NetworkServer.active) ServerChat.RemoveAll(steamId); }
            public static void DefineChannel(string key, string label, Color color)
            { if (NetworkServer.active) ServerChat.DefineChannel(key, label, color); }
            public static void UndefineChannel(string key)
            { if (NetworkServer.active) ServerChat.UndefineChannel(key); }
            public static void ClearChannels()
            { if (NetworkServer.active) ServerChat.ClearChannels(); }

            internal static void SetChannels_Internal(string[] channels)
            { _myChannels = channels ?? Array.Empty<string>(); OnChannelsChanged?.Invoke(); }
            internal static void SetCatalog_Internal(ChannelStyleData[] data)
            {
                _catalog.Clear();
                if (data != null) foreach (var d in data) _catalog[d.Key] = new ChannelInfo(d.Key, d.Label, d.ColorHex);
                OnCatalogChanged?.Invoke();
            }
            internal static void Reset_Internal() { _myChannels = System.Array.Empty<string>(); _catalog.Clear(); }

            // Text chat
            public static class Text
            {
                public static event Action<ChatEntry> OnReceived;

                /// <summary>Send to a channel you belong to (e.g. "all", "team:red").</summary>
                public static void Send(string channel, string text)
                {
                    if (!NetworkClient.active || string.IsNullOrEmpty(channel) || string.IsNullOrWhiteSpace(text)) return;
                    NetworkClient.Send(new ChatSendMessage { Channel = channel, Target = 0, Text = text });
                }

                /// <summary>Whisper directly to a SteamId.</summary>
                public static void Whisper(ulong target, string text)
                {
                    if (!NetworkClient.active || target == 0 || string.IsNullOrWhiteSpace(text)) return;
                    NetworkClient.Send(new ChatSendMessage { Channel = string.Empty, Target = target, Text = text });
                }

                /// <summary>Server: send a System message to a channel.</summary>
                public static void Broadcast(string channel, string text)
                { if (NetworkServer.active) ServerChat.ServerBroadcast(channel, text); }

                internal static void RaiseReceived_Internal(ChatMessage msg)
                {
                    string text = ApplyFilter && NetworkChatGate.ClientFilter != null
                        ? NetworkChatGate.ClientFilter(msg.SenderSteamId, msg.Text)
                        : msg.Text;

                    string name = msg.SenderSteamId == 0
                        ? "System"
                        : (Players.TryGet(msg.SenderSteamId, out var p) ? p.Name : msg.SenderSteamId.ToString());

                    OnReceived?.Invoke(new ChatEntry(msg.SenderSteamId, name, msg.Channel, text, msg.IsWhisper));
                }
                private static bool ApplyFilter => true;   // NOTE: bind to a settings toggle
            }
        }

        // Replicated catalog of assignable role names (labels only; no levels/permissions sent to clients).
        public static class Roles
        {
            private static IReadOnlyList<string> _names = Array.Empty<string>();

            // PUBLICS
            public static IReadOnlyList<string> Names => _names;

            // INTERNALS
            internal static void SetCatalog_Internal(string[] names) => _names = names ?? Array.Empty<string>();
            internal static void Clear_Internal() => _names = Array.Empty<string>();
        }

        // Server-authoritative fixed-rate clock. CurrentTick is DERIVED from Mirror's synchronized
        // NetworkTime so server and clients agree on the same tick number without an explicit tick
        // broadcast. Monotonic (never rewinds on clock jitter); catch-up is capped so a hitch or a
        // late join snaps forward instead of replaying thousands of ticks.
        public static class Tick
        {
            // PUBLICS
            public const int DefaultTickRate = 30;
            private const double MaxCatchUpSeconds = 0.25;   // rate-independent catch-up window

            public static int TickRate { get; private set; } = DefaultTickRate;
            public static long CurrentTick { get; private set; }
            public static double TickInterval => TickRate > 0 ? 1.0 / TickRate : 0.0;

            public static event Action<long> OnTick;

            // INTERNALS
            internal static void SetTickRate_Internal(int rate)
            {
                TickRate = rate > 0 ? rate : DefaultTickRate;
                Time.fixedDeltaTime = (float)TickInterval;
            }
            internal static void Pump_Internal()
            {
                if (!NetworkServer.active && !NetworkClient.active) return;
                if (TickRate <= 0) return;

                long target = (long)(NetworkTime.time / TickInterval);
                if (target <= CurrentTick) return;   // not yet, or clock regressed -> wait (monotonic)

                long behind = target - CurrentTick;
                long maxCatchUp = (long)Math.Ceiling(MaxCatchUpSeconds * TickRate);
                if (behind > maxCatchUp)
                {
                    // Late join or hitch: snap instead of replaying every intermediate tick.
                    CurrentTick = target;
                    OnTick?.Invoke(CurrentTick);
                    return;
                }

                for (long i = 0; i < behind; i++)
                {
                    CurrentTick++;
                    OnTick?.Invoke(CurrentTick);
                }
            }
            internal static void Reset_Internal()
            {
                CurrentTick = 0;
                TickRate = DefaultTickRate;
                Time.fixedDeltaTime = (float)TickInterval;
            }
        }

        // Server browser: aggregated, source-agnostic discovery. Sources (Steam server list,
        // Steam lobby, future LAN) register a query delegate; the facade fans out, de-dups by
        // ServerEntry.Key, and exposes one read surface. Session-independent (a pre-connect tool;
        // intentionally NOT touched by Clear()).
        public static class Browser
        {
            public delegate Task ServerQuery(Action<ServerEntry> onFound, CancellationToken ct);

            private static readonly HashSet<string> _seen = new();
            private static readonly List<ServerQuery> _sources = new();
            private static readonly Dictionary<string, ServerEntry> _byKey = new();
            private static List<ServerEntry> _snapshot = new();

            private static bool _dirty;
            private static CancellationTokenSource _cts;

            public static IReadOnlyList<ServerEntry> Servers
            {
                get
                {
                    if (_dirty) { _snapshot = new List<ServerEntry>(_byKey.Values); _dirty = false; }
                    return _snapshot;
                }
            }
            public static bool IsRefreshing { get; private set; }

            public static event Action OnListChanged;
            public static event Action OnRefreshStateChanged;

            public static void AddSource(ServerQuery query)
            { if (query != null && !_sources.Contains(query)) _sources.Add(query); }
            public static void RemoveSource(ServerQuery query) => _sources.Remove(query);

            public static void RefreshAll() => Run(clear: true);
            public static void QuickRefresh() => Run(clear: false);
            public static void Cancel() => _cts?.Cancel();

            private static async void Run(bool clear)
            {
                _cts?.Cancel();
                var cts = new CancellationTokenSource();
                _cts = cts;
                var ct = cts.Token;

                _seen.Clear();
                if (clear) { _byKey.Clear(); _dirty = true; OnListChanged?.Invoke(); }
                SetRefreshing(true);
                try
                {
                    var tasks = new List<Task>(_sources.Count);
                    foreach (var source in _sources)
                    {
                        var q = source;
                        tasks.Add(q(OnFound, ct));
                    }
                    await Task.WhenAll(tasks);
                    if (_cts == cts) Prune();   // drop servers that didn't respond this round
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Debug.LogError($"[NetworkState.Browser] Refresh failed: {ex.Message}"); }
                finally { if (_cts == cts) SetRefreshing(false); }   // only the current run clears the flag
            }
            private static void OnFound(ServerEntry entry)
            {
                if (entry == null || string.IsNullOrEmpty(entry.Key)) return;
                _seen.Add(entry.Key);
                _byKey[entry.Key] = entry;
                _dirty = true;
                OnListChanged?.Invoke();
            }
            private static void Prune()
            {
                List<string> stale = null;
                foreach (var key in _byKey.Keys)
                    if (!_seen.Contains(key)) (stale ??= new()).Add(key);
                if (stale == null) return;
                foreach (var key in stale) _byKey.Remove(key);
                _dirty = true;
                OnListChanged?.Invoke();
            }
            private static void SetRefreshing(bool value)
            {
                if (IsRefreshing == value) return;
                IsRefreshing = value;
                OnRefreshStateChanged?.Invoke();
            }
        }
    }
}