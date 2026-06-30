using HiddenBull.Networking;
using HiddenBull.Networking.Data;
using HiddenBull.Networking.Server;

using LMirman.VespaIO;
using Mirror;

using System;
using System.Linq;
using System.Text;

namespace HiddenBull.Console.Commands
{
    /// <summary>
    /// Server-authoritative admin commands. Each [VespaCommand(..., Server = true)] is routed to
    /// the server by NetworkCommandBridge and executed there with the requesting player's identity
    /// in CommandContext.Actor. Permission gating uses ServerRoles; output goes back via
    /// CommandContext.Reply. Target autofill reads the replicated roster (works on clients too);
    /// server-only autofills (bans, roles, whitelist) are guarded by !HiddenBull.Networking.NetworkState.IsServer.
    /// </summary>
    public static class NetworkAdminCommands
    {
        #region Players (local, informational)
        [VespaCommand("players", Name = "List Players", Description = "List all connected players")]
        public static void Players()
        {
            var all = NetworkState.Players.All;
            if (all.Count == 0) { DevConsole.Log("No players connected."); return; }

            var sb = new StringBuilder();
            sb.AppendLine($"------ Players ({all.Count}) ------");
            foreach (var p in all.Values)
            {
                string role = p.IsAdmin ? $" [{p.RoleName}]" : string.Empty;
                sb.AppendLine($"{p.Name}{role}  ({p.SteamId})");
            }
            DevConsole.Log(sb.ToString());
        }
        #endregion

        #region Kick / Ban
        [VespaCommand("kick", Server = true, Name = "Kick Player", Description = "Kick a player. Usage: kick <steamId> [reason]")]
        public static void Kick(ulong steamId) => Kick(steamId, string.Empty);

        [VespaCommand("kick", Server = true)]
        public static void Kick(ulong steamId, string reason)
        {
            if (!ServerRoles.CanActOn(CommandContext.Actor, steamId, Permissions.Kick))
            { CommandContext.Reply("You can't kick this player."); return; }

            if (string.IsNullOrWhiteSpace(reason)) ServerBanModeration.Kick(steamId);
            else ServerBanModeration.Kick(steamId, reason);
            CommandContext.Reply($"Kicked {steamId}.");
        }

        [VespaCommand("ban", Server = true, Name = "Ban Player", Description = "Permanently ban a player. Usage: ban <steamId> [reason]")]
        public static void Ban(ulong steamId) => Ban(steamId, string.Empty);

        [VespaCommand("ban", Server = true)]
        public static void Ban(ulong steamId, string reason)
        {
            if (!ServerRoles.CanActOn(CommandContext.Actor, steamId, Permissions.Ban))
            { CommandContext.Reply("You can't ban this player."); return; }

            string name = NetworkState.Players.TryGet(steamId, out var info) ? info.Name : string.Empty;
            ServerBanModeration.Ban(steamId, name, reason);
            CommandContext.Reply($"Banned {(string.IsNullOrEmpty(name) ? steamId.ToString() : name)} permanently.");
        }

        [VespaCommand("tempban", Server = true, Name = "Temp Ban Player", Description = "Ban for N minutes. Usage: tempban <steamId> <minutes> [reason]")]
        public static void TempBan(ulong steamId, int minutes) => TempBan(steamId, minutes, string.Empty);

        [VespaCommand("tempban", Server = true)]
        public static void TempBan(ulong steamId, int minutes, string reason)
        {
            if (!ServerRoles.CanActOn(CommandContext.Actor, steamId, Permissions.Ban))
            { CommandContext.Reply("You can't ban this player."); return; }
            if (minutes <= 0) { CommandContext.Reply("Minutes must be greater than zero."); return; }

            string name = NetworkState.Players.TryGet(steamId, out var info) ? info.Name : string.Empty;
            ServerBanModeration.Ban(steamId, name, reason, DateTime.UtcNow.AddMinutes(minutes));
            CommandContext.Reply($"Banned {(string.IsNullOrEmpty(name) ? steamId.ToString() : name)} for {minutes} minute(s).");
        }

        [VespaCommand("unban", Server = true, Name = "Unban Player", Description = "Remove a player's ban. Usage: unban <steamId>")]
        public static void Unban(ulong steamId)
        {
            if (!ServerRoles.Has(CommandContext.Actor, Permissions.Ban))
            { CommandContext.Reply("You don't have permission to unban."); return; }

            if (!ServerBanModeration.IsBanned(steamId, out _))
            { CommandContext.Reply($"{steamId} is not banned."); return; }

            ServerBanModeration.Unban(steamId);
            CommandContext.Reply($"Unbanned {steamId}.");
        }

        [VespaCommand("bans", Server = true, Name = "List Bans", Description = "List all active bans")]
        public static void Bans()
        {
            if (!ServerRoles.Has(CommandContext.Actor, Permissions.Ban))
            { CommandContext.Reply("You don't have permission to view bans."); return; }

            var bans = ServerBanModeration.GetAllBans();
            if (bans.Count == 0) { CommandContext.Reply("No active bans."); return; }

            var sb = new StringBuilder();
            sb.AppendLine($"------ Bans ({bans.Count}) ------");
            foreach (var b in bans)
            {
                string when = b.IsPermanent ? "permanent" : $"until {b.ExpirationUtc:u}";
                sb.AppendLine($"{(string.IsNullOrEmpty(b.Name) ? "?" : b.Name)} ({b.SteamId}) - {when} - {b.Reason}");
            }
            CommandContext.Reply(sb.ToString());
        }
        #endregion

        #region Whitelist
        [VespaCommand("whitelist", Server = true, Name = "List Whitelist", Description = "List whitelist entries")]
        public static void Whitelist()
        {
            if (!ServerRoles.Has(CommandContext.Actor, Permissions.Whitelist))
            { CommandContext.Reply("You don't have permission to view the whitelist."); return; }

            var all = ServerWhitelistModeration.All;
            var sb = new StringBuilder();
            sb.AppendLine($"------ Whitelist ({all.Count}, active: {ServerWhitelistModeration.IsActive}) ------");
            foreach (var e in all.Values)
                sb.AppendLine($"{(string.IsNullOrEmpty(e.Name) ? "?" : e.Name)} ({e.SteamId})");
            CommandContext.Reply(sb.ToString());
        }

        [VespaCommand("whitelist_add", Server = true, Name = "Whitelist Add", Description = "Add to whitelist. Usage: whitelist_add <steamId> [reason]")]
        public static void WhitelistAdd(ulong steamId) => WhitelistAdd(steamId, string.Empty);

        [VespaCommand("whitelist_add", Server = true)]
        public static void WhitelistAdd(ulong steamId, string reason)
        {
            if (!ServerRoles.Has(CommandContext.Actor, Permissions.Whitelist))
            { CommandContext.Reply("You don't have permission to manage the whitelist."); return; }

            string name = NetworkState.Players.TryGet(steamId, out var info) ? info.Name : string.Empty;
            ulong addedBy = CommandContext.FromServerConsole ? 0 : CommandContext.Actor;
            string actorName = NetworkState.Players.TryGet(addedBy, out var ai) ? ai.Name : string.Empty;
            ServerWhitelistModeration.Add(steamId, name, reason, addedBy, actorName);
            CommandContext.Reply($"Whitelisted {(string.IsNullOrEmpty(name) ? steamId.ToString() : name)}.");
        }

        [VespaCommand("whitelist_remove", Server = true, Name = "Whitelist Remove", Description = "Remove from whitelist. Usage: whitelist_remove <steamId>")]
        public static void WhitelistRemove(ulong steamId)
        {
            if (!ServerRoles.Has(CommandContext.Actor, Permissions.Whitelist))
            { CommandContext.Reply("You don't have permission to manage the whitelist."); return; }

            ServerWhitelistModeration.Remove(steamId);
            CommandContext.Reply($"Removed {steamId} from the whitelist.");
        }
        #endregion

        #region Roles
        [VespaCommand("roles", Server = true, Name = "List Roles", Description = "List available roles")]
        public static void Roles()
        {
            if (!ServerRoles.Has(CommandContext.Actor, Permissions.AssignRoles))
            { CommandContext.Reply("You don't have permission to view roles."); return; }

            var sb = new StringBuilder();
            sb.AppendLine("------ Roles ------");
            foreach (var kvp in ServerRoles.AllRoles)
                sb.AppendLine($"{kvp.Key}  (Level {kvp.Value.Level})  [{kvp.Value.Permissions}]");
            CommandContext.Reply(sb.ToString());
        }

        [VespaCommand("role_assign", Server = true, Name = "Assign Role", Description = "Assign a role. Usage: role_assign <steamId> <roleName>")]
        public static void RoleAssign(ulong steamId, string roleName)
        {
            if (!ServerRoles.CanAssignRole(CommandContext.Actor, roleName, steamId))
            { CommandContext.Reply("You can't assign that role to this player."); return; }

            string name = NetworkState.Players.TryGet(steamId, out var info) ? info.Name : string.Empty;
            ServerRoles.AssignRole(steamId, name, roleName);
            CommandContext.Reply($"Assigned role '{roleName}' to {(string.IsNullOrEmpty(name) ? steamId.ToString() : name)}.");
        }

        [VespaCommand("role_remove", Server = true, Name = "Remove Role", Description = "Remove a player's role. Usage: role_remove <steamId>")]
        public static void RoleRemove(ulong steamId)
        {
            if (!ServerRoles.CanRemoveRole(CommandContext.Actor, steamId))
            { CommandContext.Reply("You can't remove this player's role."); return; }

            ServerRoles.RemoveRole(steamId);
            CommandContext.Reply($"Removed role from {steamId}.");
        }
        #endregion

        #region Autofill
        // Connected-player target (param 0). Reads the replicated roster, so it works on clients too.
        [CommandAutofill("kick"), CommandAutofill("ban"), CommandAutofill("tempban"), CommandAutofill("whitelist_add")]
        private static AutofillValue TargetPlayerAutofill(AutofillBuilder b)
        {
            if (b.RelevantParameterIndex != 0) return null;
            ulong local = NetworkState.Players.Local.SteamId;
            var ids = NetworkState.Players.All.Values
                .Where(p => p.SteamId != local)
                .Select(p => p.SteamId.ToString());
            return b.CreateAutofillFromFirstMatch(ids, b.GetRelevantWordText().CleanseKey());
        }

        // role_assign: player (param 0) + role name (param 1). Roles are server-side data.
        [CommandAutofill("role_assign")]
        private static AutofillValue RoleAssignAutofill(AutofillBuilder b)
        {
            string word = b.GetRelevantWordText().CleanseKey();
            if (b.RelevantParameterIndex == 0)
            {
                ulong local = NetworkState.Players.Local.SteamId;
                var ids = NetworkState.Players.All.Values
                    .Where(p => p.SteamId != local)
                    .Select(p => p.SteamId.ToString());
                return b.CreateAutofillFromFirstMatch(ids, word);
            }

            if (b.RelevantParameterIndex == 1)
                return b.CreateAutofillFromFirstMatch(NetworkState.Roles.Names, word);

            return null;
        }

        // role_remove: only players that currently have a role.
        [CommandAutofill("role_remove")]
        private static AutofillValue AdminTargetAutofill(AutofillBuilder b)
        {
            if (b.RelevantParameterIndex != 0) return null;
            ulong local = NetworkState.Players.Local.SteamId;
            var ids = NetworkState.Players.All.Values
                .Where(p => p.IsAdmin && p.SteamId != local)
                .Select(p => p.SteamId.ToString());
            return b.CreateAutofillFromFirstMatch(ids, b.GetRelevantWordText().CleanseKey());
        }

        // Server-only sources (guarded): banned ids / whitelisted ids.
        [CommandAutofill("unban")]
        private static AutofillValue BannedAutofill(AutofillBuilder b)
        {
            if (b.RelevantParameterIndex != 0 || !NetworkState.IsServer) return null;
            var ids = ServerBanModeration.GetAllBans().Select(x => x.SteamId.ToString());
            return b.CreateAutofillFromFirstMatch(ids, b.GetRelevantWordText().CleanseKey());
        }

        [CommandAutofill("whitelist_remove")]
        private static AutofillValue WhitelistedAutofill(AutofillBuilder b)
        {
            if (b.RelevantParameterIndex != 0 || !NetworkServer.active) return null;
            var ids = ServerWhitelistModeration.All.Keys.Select(k => k.ToString());
            return b.CreateAutofillFromFirstMatch(ids, b.GetRelevantWordText().CleanseKey());
        }
        #endregion
    }
}