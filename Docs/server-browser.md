# Server Browser

A source-agnostic, merged server list reached through `NetworkState.Browser`. It aggregates several
discovery sources — Steam's dedicated game-server list, LAN, and P2P Steam lobbies — into one
de-duplicated list of `ServerEntry`. It is **session-independent**: a pre-connect tool that is not
cleared by `NetworkState.Clear()`.

## Reading the list

```csharp
NetworkState.Browser.RefreshAll();                 // start a full query
NetworkState.Browser.OnListChanged += Redraw;      // entries streamed in

foreach (var s in NetworkState.Browser.Servers)
    Debug.Log($"{s.Name}  {s.Players}/{s.MaxPlayers}  {s.PingMs} ms  v{s.Version}");
```

| Member | Description |
|---|---|
| `Servers` | `IReadOnlyList<ServerEntry>` — the merged result. |
| `IsRefreshing` | A query is in progress. |
| `RefreshAll()` | Clear and query everything from scratch. |
| `QuickRefresh()` | Re-query in place; servers that stop responding are dropped. |
| `Cancel()` | Cancel the running query. |
| `OnListChanged` | Fired as entries are added/updated/pruned. |
| `OnRefreshStateChanged` | `IsRefreshing` flipped. |

## ServerEntry

| Field | Meaning |
|---|---|
| `Name`, `Map` | Display name and scene id (resolve to a friendly name via `NetworkState.Scene.TryGet`). |
| `Players` / `MaxPlayers` | Current / capacity. |
| `HasPassword` | Locked. |
| `IsModded` | Server advertises required content (see [Seams](seams.md)). |
| `PingMs` | Latency in ms, or `-1` if unknown (e.g. a lobby before ping data is ready). |
| `Kind` | `Dedicated` or `P2P`. |
| `OnInternet` / `OnLan` | Which Steam source(s) found it (a server can be both). |
| `Version` | The server's app version — compare to `Application.version` to flag mismatches. |
| `Address` | What to pass to `ClientConnectSettings.Address` (a SteamId for Steam servers). |
| `Key` | Stable identity used to de-dup across sources. |

## Connecting

```csharp
var entry = NetworkState.Browser.Servers[i];
NetworkSessionManager.singleton.StartClient(new ClientConnectSettings {
    Address = entry.Address,        // SteamId for Steam servers
    Password = "..."                // if HasPassword
});
```

The transport routes a numeric SteamId to the Steam socket and an `ip:port` to direct IP
automatically.

## How discovery works

`SteamServerBrowser` (the Steam integration) registers **one** query with the browser; internally it
runs three look-ups in parallel and merges them by SteamId:

- **Dedicated** — Steam's game-server list (`ServerList.Internet`). `Kind = Dedicated`, `OnInternet`.
- **LAN** — Steam LAN discovery (`ServerList.LocalNetwork`). `Kind = Dedicated`, `OnLan`.
- **P2P** — Steam **lobbies** (`SteamMatchmaking.LobbyList`). `Kind = P2P`.

A dedicated server that is reachable both ways merges into a single entry with **both** `OnInternet`
and `OnLan` set (the lower ping wins).

> **Visibility:** a server only appears once it is **ready** (its scene has loaded). Dedicated
> servers must also have their **query port** (`GamePort + 1`, UDP) forwarded and a real Steam
> **App ID** to show in the Internet list.

## Advertising (host side)

When *this* peer hosts over Steam, `SteamServerBrowser` publishes a **Steam lobby** carrying the
server metadata (name, map, players, password, modded, version, host SteamId, ping location), so
other clients' P2P discovery can find it. It is created once the host's scene is ready and closed
when the server stops — all driven by `NetworkState` events, with no glue in the session manager.

## Adding your own source

Discovery is a delegate seam — register a query that streams `ServerEntry` values:

```csharp
NetworkState.Browser.AddSource(async (onFound, ct) =>
{
    // query your master server / file / etc.
    onFound(new ServerEntry { Name = "...", Address = "...", SteamId = id, /* ... */ });
});
```

See [Seams & Extensibility](seams.md).
