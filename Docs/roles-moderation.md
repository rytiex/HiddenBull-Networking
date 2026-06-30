# Roles & Moderation

The server-authoritative permission system and the moderation tools (kick, ban, whitelist). Role
**names** are replicated to clients (for "is admin" badges); levels and permission flags stay on the
server.

## Permissions

A `[Flags]` enum of what a role may **do**:

| Flag | Grants |
|---|---|
| `Kick` | Kick players (targeted — also needs the level rule). |
| `Ban` | Ban / unban (targeted). |
| `Whitelist` | Manage the whitelist. |
| `ServerConfig` | Change server settings. |
| `AssignRoles` | Add / remove roles (level rule stacks on top). |

A **role** (`RoleDefinition`) is a bundle of permissions plus a hierarchy **level** (`int`). An
**admin** (`AdminEntry`) maps a SteamId to a role by name.

## Checking permissions — `ServerRoles`

`ServerRoles` answers two independent questions, and combines them for targeted actions:

```csharp
ServerRoles.Has(steamId, Permissions.Kick);          // does the actor hold the flag?
ServerRoles.CanTarget(actor, target);                // does the actor outrank the target?
ServerRoles.CanActOn(actor, target, Permissions.Kick); // both — use this for kick/ban
ServerRoles.CanAssignRole(actor, "Moderator", target); // can't create a peer/superior admin
ServerRoles.CanRemoveRole(actor, target);
```

Resolution helpers: `GetRoleName`, `GetPermissions`, `GetLevel`, `TryGetRole`, plus `AllRoles` and
`AllAdmins`.

**Level rule:** targeted actions require the actor's level to be **strictly greater** than the
target's. Equal levels cannot touch each other. The **host** is always treated as `Owner` — max
level, all permissions, and untouchable. A dedicated server has no host, so its owner must be a real
`Owner` admin entry.

```csharp
// In your own Server=true command, gate actions with the same rules the built-ins use:
if (!ServerRoles.CanActOn(CommandContext.Actor, targetId, Permissions.Kick))
    CommandContext.Reply("Insufficient permission.");
```

> **`ServerRoles.ElevatedContext`** is a trusted-bypass flag used by the dedicated stdin console to
> skip all checks for one invocation. Its setter is internal — gameplay code never flips it.

## Moderation

Kick / ban / whitelist are **server-side, console-driven**. They are reached through the console
commands (see [Console](console.md)), not a public code API — this keeps a single, gated authority.

- **Bans** — permanent or timed; the kicked client receives a localized reason. Persisted to JSON on
  dedicated. The host can't be banned.
- **Whitelist** — *the list is the toggle*: a non-empty whitelist enforces; an empty one does not.
  Adding the first entry auto-adds the initiator to prevent self-lockout.

Connection-time enforcement (ban check, whitelist check, password, version, duplicate) runs in the
authenticator before a client is accepted.

## Storage

Chosen automatically by start mode:
- **Dedicated** → persistent JSON files (`roles.json`, `admins.json`, `bans.json`, `whitelist.json`)
  under the server's `config/` folder. Edit roles by hand; permissions serialize as readable names.
- **Host / P2P** → in-memory (runtime) storage.

## Replicated to clients

Clients receive only the assignable role **name** list:

```csharp
NetworkState.Roles.Names;   // e.g. ["Owner", "Admin", "Moderator"]
```

Use it for autofill and "is admin" badges (`PlayerInfo.IsAdmin` / `RoleName`). Levels and permission
flags are never sent to clients.
