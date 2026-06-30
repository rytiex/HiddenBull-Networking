# NetworkState — the facade

`NetworkState` is the **single public entry point** to the whole framework. Gameplay, UI, and the
console read state and subscribe to events here — never directly on the engines, transport, or
`NetworkSessionManager`. It is a static class grouped into sub-classes by *perspective*; it holds
**no logic** of its own (it forwards to the driver and engines, and raises events they trigger).

```csharp
using HiddenBull.Networking;
```

## Top level

| Member | Description |
|---|---|
| `NetworkState.IsServer` | `true` if a server is active (dedicated or host). |
| `NetworkState.IsClient` | `true` if a client is active (pure client or host). |
| `NetworkState.IsHost`   | `true` if this peer is both server and client. |
| `NetworkState.Clear()`  | Full state reset on teardown. Called by the driver — you rarely call this. |

---

## NetworkState.Server
Server-authoritative lifecycle and the running server's metadata. Only meaningful while `IsServer`.

**Events**
- `OnStarted` / `OnStopped` — the server began / ended.
- `OnClientConnected(ClientData)` / `OnClientDisconnected(ClientData)` — a player became *ready*
  (announced) / left. `ClientData` carries `ConnectionId`, `SteamId`, `PlayerName`.

**Read**
- `Name`, `MaxPlayers`, `HasPassword`, `Transport` — set when the server starts.

```csharp
NetworkState.Server.OnClientConnected += data =>
    Debug.Log($"{data.PlayerName} joined ({data.SteamId})");
```

> The framework never spawns players. Spawn yours in `OnClientConnected` via
> `NetworkServer.AddPlayerForConnection(...)`.

---

## NetworkState.Client
This peer's own connection and notifications from the server.

**Read**
- `IsConnecting` — handshake in progress.
- `IsConnected` — fully connected.

**Events**
- `OnConnected` / `OnDisconnected`.
- `OnServerInfo(TextNode)` / `OnServerWarning(TextNode)` — server announcements.
- `OnDisconnectReason(TextNode)` — why you're about to be dropped (kick/ban).
- `OnContentProgress(float)` — optional content-download progress, 0..1.

```csharp
NetworkState.Client.OnDisconnectReason += reason => ShowPopup(reason.ToString());
```

---

## NetworkState.Players
The replicated roster, plus per-player ping and the local player.

**Read**
- `All` — `IReadOnlyDictionary<ulong, PlayerInfo>` keyed by SteamId.
- `Local` — this client's `PlayerInfo`.
- `Admins` — players whose `RoleName` is non-empty.
- `TryGet(steamId, out PlayerInfo)`.
- `GetPing(steamId)` — last measured round-trip in ms, or `-1` if unknown.

**Events**
- `OnJoined` / `OnLeft` / `OnRoleChanged(PlayerInfo)` — granular roster changes.
- `OnRosterChanged` — any roster change (coarse).
- `OnPingsUpdated` — a new ping snapshot arrived (~every 0.5 s).
- `OnSynced` — client only: the initial roster snapshot was received (fires once after connect,
  without flooding `OnJoined`).

```csharp
NetworkState.Players.OnRosterChanged += RebuildPlayerList;
int ping = NetworkState.Players.GetPing(steamId);
```

---

## NetworkState.Scene
The single facade for scenes: a shared catalog plus the active scene and server-side scene control.
Every build ships the same `SceneData` catalog (see [Server Browser](server-browser.md) for how the
map name travels).

**Read**
- `Current` — the scene this peer last loaded.
- `All`, `Default`, `Menu` — the `SceneData` catalog.
- `Resolve(id)` / `TryGet(id, out SceneData)` — look up a scene.
- `Reload()` — re-scan the catalog.

**Event**
- `OnLoaded(string sceneId)` — this peer finished loading a scene.

**Command (server)**
- `Change(sceneId)` — switch the active scene at runtime (loads it and re-syncs clients). Returns
  `false` if already on it or not an active server.

```csharp
NetworkState.Scene.OnLoaded += id => Debug.Log($"Now on {id}");
NetworkState.Scene.Change("level_arena");   // server only
```

---

## NetworkState.Communication
Shared channels and text chat. Channels are opaque string keys (`"all"`, `"team:red"`, ...).

**Read**
- `MyChannels` — channels you currently belong to.
- `Channels` — the styled channel catalog (`ChannelInfo`: `Key`, `Label`, `Color`).
- `GetChannel(key)` — display info, falling back to the UPPERCASE key + white.

**Events**
- `OnChannelsChanged` — your membership changed.
- `OnCatalogChanged` — channel styles changed.

**Server channel ops** (no-op off the server):
`Join`, `Leave`, `RemoveFromAll`, `DefineChannel`, `UndefineChannel`, `ClearChannels`.

### NetworkState.Communication.Text
- `OnReceived(ChatEntry)` — a message arrived (`SenderSteamId`, `SenderName`, `Channel`, `Text`,
  `IsWhisper`). Sender `0` is "System".
- `Send(channel, text)` — send to a channel you belong to.
- `Whisper(targetSteamId, text)` — direct message.
- `Broadcast(channel, text)` — server: send a System message.
- `ApplySteamFilter` *(bool, default true)* — toggle the client-side Steam per-user profanity
  filter applied to incoming messages. Bind this to a user setting.

```csharp
NetworkState.Communication.Text.OnReceived += e => AppendChat(e.SenderName, e.Text);
NetworkState.Communication.Text.Send("all", "gg");
```

See [Chat](chat.md) for the full model.

---

## NetworkState.Roles
The replicated list of assignable role **names** (labels only — clients never receive levels or
permission flags).

- `Names` — `IReadOnlyList<string>`.

For permission checks and assignment, see [Roles & Moderation](roles-moderation.md).

---

## NetworkState.Tick
A server-authoritative fixed-rate clock, **derived** from Mirror's synchronized `NetworkTime` so
server and clients agree on the same tick number without an explicit broadcast. Monotonic (never
rewinds on clock jitter); catch-up is capped so a hitch or a late join snaps forward instead of
replaying thousands of ticks.

- `TickRate` (default 30), `CurrentTick`, `TickInterval`.
- `OnTick(long tick)` — fires once per tick on both server and clients.

```csharp
NetworkState.Tick.OnTick += tick => { /* fixed-rate simulation step */ };
```

> Setting the tick rate also drives Unity's `Time.fixedDeltaTime`, so `FixedUpdate`/physics run at
> the network tick rate on every peer. Reset to Unity's default on teardown.

---

## NetworkState.Browser
Aggregated, source-agnostic server discovery. **Session-independent** (a pre-connect tool; not
cleared by `Clear()`).

- `Servers` — `IReadOnlyList<ServerEntry>` (the merged result).
- `IsRefreshing`.
- `RefreshAll()` — full query from scratch.
- `QuickRefresh()` — re-query and update in place (drops servers that stop responding).
- `Cancel()`.
- `AddSource(query)` / `RemoveSource(query)` — register a discovery source (delegate seam).
- `OnListChanged` / `OnRefreshStateChanged`.

```csharp
NetworkState.Browser.RefreshAll();
foreach (var s in NetworkState.Browser.Servers)
    Debug.Log($"{s.Name}  {s.Players}/{s.MaxPlayers}  {s.PingMs}ms");
```

Full details in [Server Browser](server-browser.md).

---

## Conventions

- **Read/subscribe only.** Consumers read state and subscribe to events on `NetworkState`. The
  driver (`NetworkSessionManager`) is the sole thing that raises them.
- **Commands forward to the driver/engines** and are no-ops when they don't apply (e.g. server
  channel ops off the server).
- **Everything else is internal.** Engines, messages, storage, validators, transport, and the Steam
  bootstraps are not part of the public API — see [Seams & Extensibility](seams.md) for the official
  extension points.
```