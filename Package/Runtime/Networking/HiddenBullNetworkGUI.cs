using System.Collections.Generic;
using HiddenBull.Networking.Data;
using HiddenBull.Scenes;
using UnityEngine;

namespace HiddenBull.Networking
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Network/HiddenBull Network GUI")]
    [RequireComponent(typeof(NetworkSessionManager))]
    public sealed class HiddenBullNetworkGUI : MonoBehaviour
    {
#if !UNITY_SERVER
        [Header("Layout")]
        public int offsetX = 10;
        public int offsetY = 10;
        public int width = 500;

        private enum StartTab { Host, Join }
        private StartTab _tab = StartTab.Host;

        private int _sceneIndex;
        private SceneData[] _hostScenes;

        // Server form
        private string _serverName;
        private string _serverPassword = string.Empty;
        private string _portText = "27015";
        private string _maxPlayersText = "16";
        private ServerTransportMode _transportMode = ServerTransportMode.Steam;

        // Client form
        private string _clientAddress = "46.1.103.182:27015";
        private string _clientPassword = string.Empty;

        private Vector2 _clientListScroll;
        private GUIStyle _richText;
        private GUIStyle _smallText;
        private float _contentProgress = -1f;

        // Browser form
        private enum ServerFilter { All, Dedicated, P2P }
        private ServerFilter _serverFilter = ServerFilter.All;
        private Vector2 _browserScroll;
        private string _selectedKey;
        private readonly List<ServerEntry> _view = new();

        // Chat form
        private readonly List<string> _chatLog = new();
        private Vector2 _chatScroll;
        private string _chatInput = string.Empty;
        private string _chatChannel = "all";
        private bool _chatOpen;
        private bool _chatUnread;

        private NetworkSessionManager _manager;

        private void Awake() => _manager = GetComponent<NetworkSessionManager>();
        private void Start() => _serverName = $"{Steam.SteamInformation.GameDescription} Server";
        private void OnGUI()
        {
            _richText ??= new GUIStyle(GUI.skin.label) { richText = true }; _richText.normal.textColor = Color.white;
            _smallText ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 10 }; _smallText.normal.textColor = Color.white;

            GUILayout.BeginArea(new Rect(offsetX, offsetY, width, Screen.height - offsetY * 2));

            GUILayout.Label("<b><size=14>HiddenBull Network</size></b>", _richText);
            GUILayout.Space(6);

            if (NetworkState.Client.IsConnecting)
                DrawConnecting();
            else if (NetworkState.IsClient || NetworkState.IsServer)
                DrawRunning();
            else
                DrawStartControls();

            GUILayout.EndArea();
        }
        private void OnEnable()
        {
            NetworkState.Client.OnContentProgress += OnContentProgress;
            NetworkState.Communication.Text.OnReceived += OnChat;
            NetworkState.Client.OnDisconnected += ClearChat;
        }
        private void OnDisable()
        {
            NetworkState.Client.OnContentProgress -= OnContentProgress;
            NetworkState.Communication.Text.OnReceived -= OnChat;
            NetworkState.Client.OnDisconnected -= ClearChat;
        }

        private void OnContentProgress(float p) => _contentProgress = p;
        private void OnChat(NetworkState.Communication.ChatEntry e)
        {
            var info = NetworkState.Communication.GetChannel(e.IsWhisper ? "whisper" : e.Channel);
            string who = e.SenderSteamId == 0 ? "System" : e.SenderName;

            _chatLog.Add($"<color=#{info.ColorHex}>[{info.Label}]</color> <b>{who}</b>: {e.Text}");
            if (_chatLog.Count > 100) _chatLog.RemoveAt(0);
            _chatScroll.y = float.MaxValue;
            if (!_chatOpen) _chatUnread = true;
        }
        private void ClearChat()
        {
            _chatLog.Clear();
            _chatUnread = false;
            _chatChannel = "all";
        }

        private void DrawStartControls()
        {
            var newTab = (StartTab)GUILayout.SelectionGrid((int)_tab, new[] { "Host", "Join" }, 2, "Button", GUILayout.Height(26));
            if (newTab != _tab)
            {
                if (_tab == StartTab.Join) NetworkState.Browser.Cancel();
                _tab = newTab;
                if (_tab == StartTab.Join) NetworkState.Browser.RefreshAll();
            }

            GUILayout.Space(6);
            GUILayout.BeginVertical(GUI.skin.box);
            if (_tab == StartTab.Host) DrawHostPanel();
            else DrawJoinPanel();
            GUILayout.EndVertical();
        }
        private void DrawHostPanel()
        {
            GUILayout.Label("<b>Create Server</b>", _richText);

            // Transport
            GUILayout.BeginHorizontal();
            GUILayout.Label("Transport:", GUILayout.Width(90));
            bool wantsSteam = GUILayout.Toggle(_transportMode == ServerTransportMode.Steam, "Steam", "Button");
            bool wantsIP = GUILayout.Toggle(_transportMode == ServerTransportMode.IP, "IP", "Button");
            if (wantsSteam && _transportMode != ServerTransportMode.Steam) _transportMode = ServerTransportMode.Steam;
            else if (wantsIP && _transportMode != ServerTransportMode.IP) _transportMode = ServerTransportMode.IP;
            GUILayout.EndHorizontal();

            // Name
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name:", GUILayout.Width(90));
            _serverName = GUILayout.TextField(_serverName);
            GUILayout.EndHorizontal();

            // Password
            GUILayout.BeginHorizontal();
            GUILayout.Label("Password:", GUILayout.Width(90));
            _serverPassword = GUILayout.TextField(_serverPassword);
            GUILayout.EndHorizontal();

            // Max players
            GUILayout.BeginHorizontal();
            GUILayout.Label("Max Players:", GUILayout.Width(90));
            _maxPlayersText = GUILayout.TextField(_maxPlayersText);
            GUILayout.EndHorizontal();

            // Map selector
            DrawMapSelector();

            // Port (IP only)
            if (_transportMode == ServerTransportMode.IP)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Port:", GUILayout.Width(90));
                _portText = GUILayout.TextField(_portText);
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(4);
            if (GUILayout.Button("Start Host", GUILayout.Height(32)))
                StartServerFromForm();
        }
        private void DrawJoinPanel()
        {
            GUILayout.Label("<b>Servers</b>", _richText);

            _serverFilter = (ServerFilter)GUILayout.SelectionGrid((int)_serverFilter,
                new[] { "All", "Dedicated", "P2P" }, 3, "Button", GUILayout.Height(22));

            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label("<size=9><b>ML</b></size>", _richText, GUILayout.Width(24));
            GUILayout.Label("<size=9><b>Name</b></size>", _richText, GUILayout.ExpandWidth(true));
            GUILayout.Label("<size=9><b>Map</b></size>", _richText, GUILayout.Width(84));
            GUILayout.Label("<size=9><b>Players</b></size>", _richText, GUILayout.Width(52));
            GUILayout.Label("<size=9><b>Ping</b></size>", _richText, GUILayout.Width(46));
            GUILayout.Label("<size=9><b>Ver</b></size>", _richText, GUILayout.Width(58));
            GUILayout.EndHorizontal();

            var view = BuildView(NetworkState.Browser.Servers);   // filtered + sorted by ping

            _browserScroll = GUILayout.BeginScrollView(_browserScroll, GUILayout.Height(300));
            foreach (var s in view) DrawServerRow(s);
            if (view.Count == 0)
                GUILayout.Label(NetworkState.Browser.IsRefreshing ? "<i>Searching...</i>" : "<i>No servers found.</i>", _richText);
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Quick Refresh", GUILayout.Height(24))) NetworkState.Browser.QuickRefresh();
            if (GUILayout.Button("Refresh All", GUILayout.Height(24))) NetworkState.Browser.RefreshAll();
            GUILayout.EndHorizontal();
            if (NetworkState.Browser.IsRefreshing) GUILayout.Label("<size=10>Refreshing...</size>", _smallText);

            GUILayout.Space(6);
            var selected = FindSelected(view);
            GUI.enabled = selected != null;
            if (GUILayout.Button(selected != null ? $"Connect to {selected.Name}" : "Connect (select a server)", GUILayout.Height(28)))
                ConnectTo(selected);
            GUI.enabled = true;

            GUILayout.Space(6);
            GUILayout.Label("<size=10><b>Direct connect</b></size>", _smallText);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Address:", GUILayout.Width(64));
            _clientAddress = GUILayout.TextField(_clientAddress);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Password:", GUILayout.Width(64));
            _clientPassword = GUILayout.TextField(_clientPassword);
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Connect (direct)", GUILayout.Height(24)))
                StartClientFromForm();
        }
        private void DrawMapSelector()
        {
            var scenes = HostScenes();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Map:", GUILayout.Width(90));
            if (scenes.Length == 0)
            {
                GUILayout.Label("<i>no scenes</i>", _richText);
            }
            else
            {
                _sceneIndex = Mathf.Clamp(_sceneIndex, 0, scenes.Length - 1);
                if (GUILayout.Button("<", GUILayout.Width(26))) _sceneIndex = (_sceneIndex - 1 + scenes.Length) % scenes.Length;
                GUILayout.Label(scenes[_sceneIndex].DisplayName, _richText, GUILayout.ExpandWidth(true));
                if (GUILayout.Button(">", GUILayout.Width(26))) _sceneIndex = (_sceneIndex + 1) % scenes.Length;
                if (GUILayout.Button("R", GUILayout.Width(26))) { Scenes.SceneUtils.Reload(); _hostScenes = null; }   // re-scan + refresh
            }
            GUILayout.EndHorizontal();
        }
        private SceneData[] HostScenes()
        {
            if (_hostScenes == null)
            {
                var list = new List<SceneData>();
                foreach (var s in SceneUtils.All)
                    if (!s.IsMenu) list.Add(s);   // you host gameplay scenes, not the menu
                _hostScenes = list.ToArray();
            }
            return _hostScenes;
        }

        private void DrawConnecting()
        {
            GUILayout.Label($"<b>CONNECTING</b>  →  {_manager.networkAddress}", _richText);
            GUILayout.Space(8);

            if (_contentProgress >= 0f)
            {
                GUILayout.Label($"Downloading content...  {(int)(_contentProgress * 100)}%", _smallText);
                var bg = GUILayoutUtility.GetRect(width - 20, 16);
                GUI.Box(bg, GUIContent.none);
                var c = GUI.color;
                GUI.color = new Color(0.4f, 0.8f, 1f);
                GUI.Box(new Rect(bg.x, bg.y, bg.width * Mathf.Clamp01(_contentProgress), bg.height), GUIContent.none);
                GUI.color = c;
                GUILayout.Space(6);
            }

            if (GUILayout.Button("Cancel", GUILayout.Height(28)))
                _manager.StopClient();
        }

        private void DrawRunning()
        {
            DrawStatusHeader();
            DrawRoster();
            DrawChat();
            GUILayout.Space(8);
            DrawStopButton();
        }
        private void DrawStatusHeader()
        {
            string label;
            if (NetworkState.IsHost)
                label = "<b>HOST</b> (Server + Client)";
            else if (NetworkState.IsServer)
                label = "<b>SERVER</b>";
            else if (NetworkState.IsClient)
                label = $"<b>CLIENT</b> {_manager.networkAddress}";
            else
                label = "<b>UNKNOWN</b>";

            GUILayout.Label(label, _richText);

            if (NetworkState.IsServer && Mirror.Transport.active != null)
            {
                var uri = Mirror.Transport.active.ServerUri();
                if (uri != null) GUILayout.Label($"Address: {uri}", _smallText);
            }

            GUILayout.Label($"Tick: {NetworkState.Tick.CurrentTick}  @  {NetworkState.Tick.TickRate} Hz", _smallText);
        }
        private void DrawChat()
        {
            GUILayout.Space(8);

            var prev = GUI.color;
            GUI.color = (!_chatOpen && _chatUnread) ? Color.yellow : Color.white;
            if (GUILayout.Button(_chatOpen ? "Close Chat" : "Open Chat", GUILayout.Height(24)))
            {
                _chatOpen = !_chatOpen;
                if (_chatOpen) _chatUnread = false;   // opening clears the unread flag
            }
            GUI.color = prev;

            if (!_chatOpen) return;   // collapsed -> draw nothing else (the optimization)

            _chatScroll = GUILayout.BeginScrollView(_chatScroll, GUILayout.Height(160));
            foreach (var line in _chatLog) GUILayout.Label(line, _richText);
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            var channels = NetworkState.Communication.MyChannels;
            if (channels.Count == 0) _chatChannel = "all";
            else if (IndexOfChannel(channels, _chatChannel) < 0) _chatChannel = channels[0];

            if (GUILayout.Button(NetworkState.Communication.GetChannel(_chatChannel).Label, GUILayout.Width(110)) && channels.Count > 0)
            {
                int i = IndexOfChannel(channels, _chatChannel);
                _chatChannel = channels[(i + 1) % channels.Count];
            }

            GUI.SetNextControlName("ChatInput");
            _chatInput = GUILayout.TextField(_chatInput);

            bool enter = Event.current.type == EventType.KeyDown
                       && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                       && GUI.GetNameOfFocusedControl() == "ChatInput";
            bool send = GUILayout.Button("Send", GUILayout.Width(60)) || enter;
            GUILayout.EndHorizontal();

            if (send && !string.IsNullOrWhiteSpace(_chatInput))
            {
                NetworkState.Communication.Text.Send(string.IsNullOrWhiteSpace(_chatChannel) ? "all" : _chatChannel, _chatInput);
                _chatInput = string.Empty;
                if (enter) Event.current.Use();
                GUI.FocusControl("ChatInput");
            }
        }
        private static int IndexOfChannel(IReadOnlyList<string> channels, string channel)
        {
            for (int i = 0; i < channels.Count; i++) if (channels[i] == channel) return i;
            return -1;
        }

        private void DrawRoster()
        {
            // Information-only, identical for host and client: both read the shared roster facade.
            var players = NetworkState.Players.All;

            GUILayout.Space(8);
            GUILayout.Label($"<b>Players: {players.Count}</b>", _richText);

            _clientListScroll = GUILayout.BeginScrollView(_clientListScroll, GUILayout.MaxHeight(220));
            foreach (var player in players.Values)
            {
                GUILayout.BeginHorizontal(GUI.skin.box);

                string roleBadge = player.IsAdmin ? $"  <color=#4ea1ff>[{player.RoleName}]</color>" : string.Empty;
                GUILayout.Label($"<b>{player.Name}</b>{roleBadge}\n<size=10>{player.SteamId}</size>", _richText);

                GUILayout.FlexibleSpace();
                int ping = NetworkState.Players.GetPing(player.SteamId);
                GUILayout.Label(FormatPing(ping), _richText, GUILayout.Width(64));

                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }
        private void DrawStopButton()
        {
            if (NetworkState.IsHost)
            {
                if (GUILayout.Button("Stop Host", GUILayout.Height(32)))
                    _manager.StopServer();
            }
            else if (NetworkState.IsServer)
            {
                if (GUILayout.Button("Stop Server", GUILayout.Height(32)))
                    _manager.StopServer();
            }
            else if (NetworkState.IsClient)
            {
                if (GUILayout.Button("Stop Client", GUILayout.Height(32)))
                    _manager.StopClient();
            }
        }

        private void StartServerFromForm()
        {
            ushort.TryParse(_portText, out ushort port);
            if (port == 0) port = 27015;

            if (!int.TryParse(_maxPlayersText, out int maxPlayers) || maxPlayers < 1)
                maxPlayers = 16;

            var scenes = HostScenes();
            string sceneId = scenes.Length > 0 ? scenes[Mathf.Clamp(_sceneIndex, 0, scenes.Length - 1)].Id : string.Empty;
            _manager.SetServerScene(sceneId);   // empty -> default fallback; OnStartServer loads it

            _manager.StartServer(ServerStartSettings.Create(
                serverName: _serverName, password: _serverPassword, port: port,
                maxPlayers: maxPlayers,
                startMode: ServerStartMode.Host,
                transportMode: _transportMode));
        }
        private void StartClientFromForm()
        {
            _contentProgress = -1f;
            _manager.StartClient(new ClientConnectSettings
            {
                Address = _clientAddress,
                Password = _clientPassword
            });
        }

        // Helpers
        private void DrawServerRow(ServerEntry s)
        {
            bool isSelected = s.Key == _selectedKey;
            var prevBg = GUI.backgroundColor;
            if (isSelected) GUI.backgroundColor = new Color(0.30f, 0.50f, 0.80f);

            GUILayout.BeginHorizontal(GUI.skin.box);
            string m = s.IsModded ? "<color=#c080ff>M</color>" : "<color=#444444>-</color>";
            string l = s.HasPassword ? "<color=#e0c020>L</color>" : "<color=#444444>-</color>";
            GUILayout.Label($"{m}{l}", _richText, GUILayout.Width(24));

            string kind;
            if (s.Kind == ServerKind.P2P)
                kind = " <size=9><color=#60ff90>[P2P]</color></size>";
            else
            {
                kind = "";
                if (s.OnInternet) kind += " <size=9><color=#60c0ff>[D]</color></size>";
                if (s.OnLan) kind += " <size=9><color=#ffb060>[LAN]</color></size>";
                if (kind.Length == 0) kind = " <size=9><color=#60c0ff>[D]</color></size>";
            }
            GUILayout.Label($"<b>{s.Name}</b>{kind}", _richText, GUILayout.ExpandWidth(true));
            GUILayout.Label(MapLabel(s.Map), _smallText, GUILayout.Width(84));
            GUILayout.Label($"{s.Players}/{s.MaxPlayers}", _smallText, GUILayout.Width(52));
            GUILayout.Label(FormatPing(s.PingMs), _smallText, GUILayout.Width(46));
            GUILayout.Label(VersionLabel(s.Version), _smallText, GUILayout.Width(58));
            GUILayout.EndHorizontal();
            GUI.backgroundColor = prevBg;

            var rect = GUILayoutUtility.GetLastRect();
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                _selectedKey = s.Key;
                Event.current.Use();
            }
        }
        private ServerEntry FindSelected(List<ServerEntry> view)
        {
            if (string.IsNullOrEmpty(_selectedKey)) return null;
            foreach (var s in view) if (s.Key == _selectedKey) return s;
            return null;
        }
        private List<ServerEntry> BuildView(IReadOnlyList<ServerEntry> servers)
        {
            _view.Clear();
            foreach (var s in servers) if (PassesFilter(s)) _view.Add(s);
            string localVer = Application.version;
            _view.Sort((a, b) => Compare(a, b, localVer));
            return _view;
        }
        private void ConnectTo(ServerEntry s)
        {
            if (s == null) return;
            _contentProgress = -1f;
            _manager.StartClient(new ClientConnectSettings { Address = s.Address, Password = _clientPassword });
        }

        private static int Compare(ServerEntry a, ServerEntry b, string localVersion)
        {
            // Fixed rule (filter-independent): incompatible/unknown versions always sink to the bottom.
            bool ca = a.Version == localVersion;
            bool cb = b.Version == localVersion;
            if (ca != cb) return ca ? -1 : 1;

            // within a group: ascending ping (unknown ping last)
            int pa = a.PingMs < 0 ? int.MaxValue : a.PingMs;
            int pb = b.PingMs < 0 ? int.MaxValue : b.PingMs;
            if (pa != pb) return pa.CompareTo(pb);
            return string.CompareOrdinal(a.Key, b.Key);
        }
        private static string MapLabel(string mapId)
        {
            if (string.IsNullOrEmpty(mapId)) return "-";
            return NetworkState.Scene.TryGet(mapId, out var data) && !string.IsNullOrEmpty(data.DisplayName)
                ? data.DisplayName : mapId;
        }
        private static string VersionLabel(string version)
        {
            if (string.IsNullOrEmpty(version)) return "<color=#888888>?</color>";
            string hex = version == Application.version ? "57d957" : "e05050";
            return $"<color=#{hex}>{version}</color>";   // "0.0.3" yeşil/kırmızı, bilinmiyorsa "?"
        }
        private bool PassesFilter(ServerEntry s) => _serverFilter switch
        {
            ServerFilter.Dedicated => s.Kind == ServerKind.Dedicated,
            ServerFilter.P2P => s.Kind == ServerKind.P2P,
            _ => true,
        };
        private static string FormatPing(int ms)
        {
            if (ms < 0) return "<color=#888888>—</color>";
            string hex = ms < 80 ? "57d957" : ms < 160 ? "e0c020" : "e05050";
            return $"<color=#{hex}>{ms} ms</color>";
        }
#endif
    }
}