# Seams & Extensibility

The framework keeps a small public surface; everything else is `internal`. You extend it through a
handful of **one-way seams** — static delegates the framework *invokes* but never depends on. Assign
them at startup; **clear them to null on teardown** (they are mutable statics that survive editor
play-mode restarts with domain reload off).

## NetworkSceneGate — how scenes load

The network layer only deals with scene **names**; it never loads scenes itself. Provide a custom
loader (loading screen, addressables, bundles) — or leave it null to fall back to `SceneManager`.

```csharp
NetworkSceneGate.LoadSceneAsync = async sceneName =>
{
    await ShowLoadingScreen();
    await Addressables.LoadSceneAsync(sceneName).Task;
};
```

## NetworkChatGate — chat filtering

Server-side message policy (see [Chat](chat.md)):

```csharp
NetworkChatGate.Filter = (senderSteamId, text) =>
    profanity.Clean(text);     // return null/empty to drop the message
```

(The client-side Steam profanity filter is set internally; toggle it via
`NetworkState.Communication.Text.ApplySteamFilter`.)

## NetworkContentGate — optional content/mod gating

Lets an **optional** content layer gate connections on content readiness, without the network core
ever naming a content type. Dormant by default (both delegates null = no-op).

```csharp
// Server: which content keys a joining client must have.
NetworkContentGate.GetRequiredKeys = () => activeMods.Keys;

// Client: download + mount them (out-of-band), reporting progress 0..1.
NetworkContentGate.PrepareAsync = async (keys, progress) =>
{
    return await mods.DownloadAndMount(keys, progress);
};
```

When set, a server advertises as **modded** in the [Server Browser](server-browser.md), the
authenticator holds a joining client until its content is ready, and `NetworkState.Client.OnContentProgress`
reports progress. Remove the layer by clearing both delegates — no surgery on the auth flow.

## Browser sources

The [Server Browser](server-browser.md) discovers servers through registered query delegates. Add
your own (a master server, a friends list, a file) without touching the browser:

```csharp
NetworkState.Browser.AddSource(async (onFound, ct) =>
{
    foreach (var s in await MyMasterServer.Query(ct))
        onFound(new ServerEntry { Name = s.name, Address = s.address, /* ... */ });
});

// on teardown
NetworkState.Browser.RemoveSource(myQuery);
```

## Why seams

This is the whole point of the architecture: the core never references the optional layer — it only
invokes a delegate. So content, custom scene loading, chat policy, and extra discovery sources can be
added or removed by **assigning one delegate**, not by editing framework code.
