# Getting Started

HiddenBull Networking is a Mirror + Steam (Facepunch) server framework. You reach everything through
one facade — `NetworkState` — and the implementation stays internal.

## 1. Install

See the [README](../README.md): add the package via its git URL, make sure **Mirror**,
**Facepunch.Steamworks**, and **PicoShot.Localization** are in your project, set the project-wide
**Input Actions**, and import the **Demo Setup** sample (strongly recommended).

## 2. Boot

The `BOOT_GameInitializer` prefab (in the demo) self-loads from a `Resources/` folder before any
scene and persists across scenes. It carries:
- `NetworkSessionManager` — the Mirror `NetworkManager` driver,
- `SteamLifecycle` — Steam client/server init (reads your `SteamConfig`),
- the console and the debug GUI.

Fill in your real Steam identity on the `DATA_SteamConfig` asset (App ID / Mod Dir / Game
Description).

## 3. Host or join

With the demo GUI:
- **Host** — pick a transport, map, and max players, then start.
- **Join** — open the server browser to discover dedicated / LAN / P2P servers, or type a direct
  address.

In code:

```csharp
var mgr = NetworkSessionManager.singleton;

// host
mgr.StartServer(ServerStartSettings.Create(
    serverName: "My Server", maxPlayers: 16,
    startMode: ServerStartMode.Host, transportMode: ServerTransportMode.Steam));

// join
mgr.StartClient(new ClientConnectSettings { Address = "<steamid-or-ip:port>" });
```

## 4. React to state

Everything reads from `NetworkState`:

```csharp
NetworkState.Server.OnClientConnected += data => SpawnPlayer(data);
NetworkState.Players.OnRosterChanged  += RebuildScoreboard;
NetworkState.Communication.Text.OnReceived += e => AppendChat(e.SenderName, e.Text);
NetworkState.Tick.OnTick += tick => StepSimulation(tick);
```

> The framework never spawns players — do that yourself in `NetworkState.Server.OnClientConnected`
> via `NetworkServer.AddPlayerForConnection(...)`.

## Where to go next

- **[NetworkState](network-state.md)** — the full facade reference (start here).
- **[Server Browser](server-browser.md)** — discovery, advertising, `ServerEntry`.
- **[Chat](chat.md)** — channels, whispers, filtering.
- **[Roles & Moderation](roles-moderation.md)** — permissions, hierarchy, bans, whitelist.
- **[Console & Commands](console.md)** — server-authoritative commands.
- **[Seams & Extensibility](seams.md)** — the official extension points.
