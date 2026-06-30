# Chat

Channel-subscription chat reached through `NetworkState.Communication`. Players are members of
opaque string **channels** (`"all"`, `"team:red"`, ...). A message to a channel reaches its members;
a **whisper** reaches one SteamId. The framework owns the global channel (`"all"`) and relays
everything centrally, so anti-spam and filtering stay in one place — the network core never knows
what a "team" is.

## Sending & receiving (client)

```csharp
NetworkState.Communication.Text.OnReceived += e =>
    AppendLine($"[{e.Channel}] {e.SenderName}: {e.Text}");

NetworkState.Communication.Text.Send("all", "gg");        // to a channel you belong to
NetworkState.Communication.Text.Whisper(targetSteamId, "hi");
```

A `ChatEntry` carries `SenderSteamId` (0 = System), `SenderName`, `Channel`, `Text`, `IsWhisper`.

## Channels

```csharp
NetworkState.Communication.MyChannels;        // channels you're in
NetworkState.Communication.Channels;          // styled catalog (ChannelInfo: Key, Label, Color)
NetworkState.Communication.GetChannel("all"); // display info, with a sensible fallback

NetworkState.Communication.OnChannelsChanged += RefreshChannelTabs;  // your membership changed
NetworkState.Communication.OnCatalogChanged  += RefreshChannelStyles; // styles changed
```

## Server-side operations

These are no-ops off the server. Use them to manage non-global channels (e.g. teams):

```csharp
NetworkState.Communication.Join(steamId, "team:red");   // leaveOthers=true → clean team switch
NetworkState.Communication.Leave(steamId, "team:red");
NetworkState.Communication.RemoveFromAll(steamId);      // on disconnect

NetworkState.Communication.DefineChannel("team:red", "RED", Color.red);  // style + replicate
NetworkState.Communication.UndefineChannel("team:red");
NetworkState.Communication.ClearChannels();             // back to lobby (keeps "all")

NetworkState.Communication.Text.Broadcast("all", "Server restarting in 5 min");  // System message
```

`Join` with `leaveOthers: true` (default) removes the player from every other channel except `"all"`
— a clean team switch. Pass `false` for additive membership. The global `"all"` channel is
framework-managed and cannot be left or removed.

## Built-in safety

Applied server-side to every relayed message:
- **Anti-spam** — a per-sender cooldown (1 s).
- **Length cap** — messages are trimmed to 256 chars.
- **Membership** — you can only send to a channel you belong to.

## Filtering

Two independent filter seams (see [Seams](seams.md)):

- **`NetworkChatGate.Filter`** *(server-side, public seam)* — `(senderSteamId, text) → text`. One
  policy for everyone (links, word lists). Runs before broadcast; returning null/empty **drops** the
  message.
- **Steam profanity filter** *(client-side)* — applied to each received message before display,
  respecting each player's own Steam setting. Toggle it with
  `NetworkState.Communication.Text.ApplySteamFilter` (bind to a user setting).

```csharp
NetworkChatGate.Filter = (sender, text) =>
    text.Contains("http") ? null : text;     // drop links

NetworkState.Communication.Text.ApplySteamFilter = userSettings.censorChat;
```
