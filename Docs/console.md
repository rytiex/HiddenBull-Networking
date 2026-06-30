# Console & Commands

The framework ships a developer console (a modified **VespaIO**) wired to networking, plus a
headless console for dedicated servers. Commands are ordinary `[VespaCommand]` static methods;
networking adds **server-authoritative** routing on top.

## Writing a command

```csharp
using LMirman.VespaIO;

public static class MyCommands
{
    [VespaCommand("hello", "Prints a greeting")]
    public static void Hello() => DevConsole.Log("Hi!");
}
```

Run it from the in-game console (open with the `DeveloperConsoleActive` hotkey — see
[Setup](../README.md)) or the dedicated server's stdin.

> Command classes must stay **public** so VespaIO's reflection can discover them.

## Server-authoritative commands

Mark a command `Server = true` to make it run **on the server**, with the requesting player's
identity bound. Read `CommandContext.Actor` to know who invoked it and gate with `ServerRoles`:

```csharp
using HiddenBull.Console.Commands;   // CommandContext
using HiddenBull.Networking.Server;  // ServerRoles
using HiddenBull.Networking.Data;    // Permissions

[VespaCommand("givegold", "Grant gold to a player", Server = true)]
public static void GiveGold(ulong steamId, int amount)
{
    // Actor is the requesting player (0 = dedicated console / elevated).
    if (!ServerRoles.Has(CommandContext.Actor, Permissions.AssignRoles))
    {
        CommandContext.Reply("Insufficient permission.");
        return;
    }

    Economy.Grant(steamId, amount);                  // your game logic
    CommandContext.Reply($"Gave {amount} gold to {steamId}.");
}
```

> **Kick, ban, whitelist, scene change, and chat** ship as built-in server commands — you don't
> reimplement them (the moderation engines are internal). You write *your own* game commands and
> gate them with `ServerRoles`.

Routing, handled by `NetworkCommandBridge`:

- **Pure client** → the command line is forwarded to the server (`ClientCommandMessage`); the server
  validates it is actually marked `Server`, executes it with `CommandContext` bound to the sender,
  and routes output back to that client.
- **Host / dedicated** → executes locally with the local player (or the elevated console) as actor.
- **Non-`Server` commands** are untouched and run locally as usual.

Security: only commands explicitly marked `Server` may be invoked by a remote client.

## CommandContext

Inside a server command, `CommandContext` tells you who is acting and lets you reply to them:

- `CommandContext.Actor` — the invoking player's SteamId (0 for the local/console authority).
- `CommandContext.Reply(message)` — send output back to the invoker (the client, or the local
  console).

Pair `Actor` with `ServerRoles` checks (see [Roles & Moderation](roles-moderation.md)) to gate
actions.

## Dedicated server console

On a headless build the framework attaches a console that reads stdin and runs commands with full
authority (the dedicated equivalent of the host's `Owner` bypass — it sets
`ServerRoles.ElevatedContext` around the call). This is how an operator administers a dedicated
server without a UI.
