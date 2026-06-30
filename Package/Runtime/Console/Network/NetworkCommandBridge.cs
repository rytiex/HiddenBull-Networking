using HiddenBull.Networking.Server;
using HiddenBull.Networking.Steam;
using HiddenBull.Networking.Data;
using HiddenBull.Networking;

using LMirman.VespaIO;
using UnityEngine;
using Mirror;

namespace HiddenBull.Console.Commands
{
    /// <summary>
    /// Bridges VespaIO server-authoritative commands ([VespaCommand(..., Server = true)]) to
    /// the network layer. Client: forwards them to the server. Server/Host: executes them with
    /// the requesting player's identity bound (CommandContext) and routes output back.
    /// Non-server commands are untouched and run locally as usual.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkCommandBridge : MonoBehaviour
    {
        // Dedicated execution console: shares the global command set, always enabled so commands
        // can run on a headless server without touching the UI console's enabled state.
        private static LMirman.VespaIO.Console _exec;

        private void OnEnable()
        {
            _exec ??= new NativeConsole
            {
                CommandSet = LMirman.VespaIO.Commands.commandSet,
                AliasSet = Aliases.aliasSet,
                Enabled = true
            };

            DevConsole.console.ServerCommandRouter = Route;

            NetworkState.Server.OnStarted += RegisterServerHandler;
            NetworkState.Server.OnStopped += UnregisterServerHandler;
            NetworkState.Client.OnServerInfo += LogInfo;
            NetworkState.Client.OnServerWarning += LogWarning;
            if (NetworkState.IsServer) RegisterServerHandler();

            Debug.Log($"[{nameof(NetworkCommandBridge)}] Enabled.");
        }
        private void OnDisable()
        {
            if (DevConsole.console.ServerCommandRouter == Route)
                DevConsole.console.ServerCommandRouter = null;

            NetworkState.Server.OnStarted -= RegisterServerHandler;
            NetworkState.Server.OnStopped -= UnregisterServerHandler;
            NetworkState.Client.OnServerInfo -= LogInfo;
            NetworkState.Client.OnServerWarning -= LogWarning;
            UnregisterServerHandler();
        }

        // Client-side routing (the ServerCommandRouter seam)
        private static bool Route(string line, Command command)
        {
            // Authority is local (host or dedicated console): run here on behalf of the local player.
            if (NetworkState.IsServer)
            {
                ulong actor = SteamInformation.Initialized ? SteamInformation.LocalSteamId : 0UL;

                bool elevated = SteamInformation.IsDedicated;
                if (elevated) ServerRoles.ElevatedContext = true;
                try
                {
                    using (CommandContext.Begin(actor, message => DevConsole.Log(message)))
                        _exec.RunInvocation(new Invocation(line, LMirman.VespaIO.Commands.commandSet));
                }
                finally
                {
                    if (elevated) ServerRoles.ElevatedContext = false;
                }
                return true;
            }

            // Pure client: forward to the server for authoritative execution.
            if (NetworkState.Client.IsConnected)
            {
                NetworkClient.Send(new ClientCommandMessage { Line = line });
                return true;
            }

            DevConsole.Log("This command requires a server connection.", LMirman.VespaIO.Console.LogStyling.Warning);
            return true;
        }

        // Server-side execution
        private void RegisterServerHandler() =>
            NetworkServer.RegisterHandler<ClientCommandMessage>(OnClientCommand);   // requireAuthentication = true
        private void UnregisterServerHandler()
        {
            if (NetworkState.IsServer) NetworkServer.UnregisterHandler<ClientCommandMessage>();
        }

        // Clean console output for server notifications (command replies + announcements).
        private static void LogInfo(PicoShot.Localization.TextNode content) =>
            DevConsole.Log(content.ToString(), LMirman.VespaIO.Console.LogStyling.Info);
        private static void LogWarning(PicoShot.Localization.TextNode content) =>
            DevConsole.Log(content.ToString(), LMirman.VespaIO.Console.LogStyling.Warning);

        private static void OnClientCommand(NetworkConnectionToClient conn, ClientCommandMessage msg)
        {
            ulong actor = conn.authenticationData is ClientData data ? data.SteamId : 0UL;

            var invocation = new Invocation(msg.Line, LMirman.VespaIO.Commands.commandSet);

            if (invocation.validState != Invocation.ValidState.Valid)
            {
                NetworkSessionManager.singleton.SendNotification(conn, ServerMessageType.Info, "Unknown or invalid command.");
                return;
            }

            // Security: only commands explicitly marked Server may be run from a remote client.
            if (!invocation.command.Server)
            {
                NetworkSessionManager.singleton.SendNotification(conn, ServerMessageType.Info, "That command cannot be run remotely.");
                return;
            }

            using (CommandContext.Begin(actor,
                message => NetworkSessionManager.singleton.SendNotification(conn, ServerMessageType.Info, message)))
            {
                _exec.RunInvocation(invocation);
            }
        }
    }

    /// <summary>Client -> server request to run a server-authoritative console command line.</summary>
    internal struct ClientCommandMessage : NetworkMessage
    {
        public string Line;
    }
}