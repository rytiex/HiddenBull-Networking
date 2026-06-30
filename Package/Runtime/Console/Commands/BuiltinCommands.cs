using LMirman.VespaIO;

namespace HiddenBull.Console.Commands
{
    public static class BuiltinCommands
    {
#if !UNITY_SERVER

        #region Connection (local, client-side)
        [VespaCommand("connect", Name = "Connect", Description = "Connect to a server. Usage: connect <address> [password]")]
        public static void Connect(string address) => Connect(address, string.Empty);

        [VespaCommand("connect", Name = "Connect")]
        public static void Connect(string address, string password)
        {
            if (HiddenBull.Networking.NetworkState.IsServer)
            { DevConsole.Log("Already hosting; stop the server first.", LMirman.VespaIO.Console.LogStyling.Warning); return; }
            if (HiddenBull.Networking.NetworkState.IsClient)
            { DevConsole.Log("Already connected or connecting; disconnect first.", LMirman.VespaIO.Console.LogStyling.Warning); return; }
            if (HiddenBull.Networking.NetworkSessionManager.singleton == null)
            { DevConsole.Log("NetworkSessionManager not found.", LMirman.VespaIO.Console.LogStyling.Error); return; }
            if (string.IsNullOrWhiteSpace(address))
            { DevConsole.Log("Address was empty.", LMirman.VespaIO.Console.LogStyling.Notice); return; }

            HiddenBull.Networking.NetworkSessionManager.singleton.StartClient(new 
                HiddenBull.Networking.Data.ClientConnectSettings { Address = address, Password = password });

            DevConsole.Log($"Connecting to {address}...");
        }

        [VespaCommand("disconnect", Name = "Disconnect", Description = "Disconnect from the current server (or stop hosting)")]
        public static void Disconnect()
        {
            if (HiddenBull.Networking.NetworkSessionManager.singleton == null)
            { DevConsole.Log("NetworkSessionManager not found.", LMirman.VespaIO.Console.LogStyling.Error); return; }

            if (HiddenBull.Networking.NetworkState.IsServer)
            {
                HiddenBull.Networking.NetworkSessionManager.singleton.StopServer(); // handles host (StopHost) and dedicated
                DevConsole.Log("Server stopped.");
            }
            else if (HiddenBull.Networking.NetworkState.IsClient)
            {
                HiddenBull.Networking.NetworkSessionManager.singleton.StopClient();
                DevConsole.Log("Disconnected.");
            }
            else
            {
                DevConsole.Log("Not connected.", LMirman.VespaIO.Console.LogStyling.Notice);
            }
        }
        #endregion

#endif
    }
}