using HiddenBull.Networking.Server;
using LMirman.VespaIO;
using System;

namespace HiddenBull.Console.Commands
{
    /// <summary>
    /// Ambient context for a server-authoritative command. Bound by the bridge for the
    /// duration of a single invocation. Command bodies read Actor (the requesting player's
    /// effective SteamId, NOT the server's) for permission checks and use Reply for output.
    /// Server commands always execute server-side; single-level, main-thread only.
    /// </summary>
    public static class CommandContext
    {
        /// <summary>Effective SteamId of the player who invoked the command. 0 when none.</summary>
        public static ulong Actor { get; private set; }

        /// <summary>True when invoked from the dedicated server console (no player identity).</summary>
        public static bool FromServerConsole => Actor == 0 || ServerRoles.ElevatedContext;

        private static Action<string> _replySink;

        /// <summary>Sends user-facing output back to whoever invoked the command.</summary>
        public static void Reply(string message)
        {
            if (_replySink != null) _replySink(message);
            else DevConsole.Log(message);
        }

        /// <summary>Binds the context for one invocation. Dispose (use a 'using' block) to clear.</summary>
        internal static Scope Begin(ulong actor, Action<string> replySink)
        {
            Actor = actor;
            _replySink = replySink;
            return new Scope();
        }

        private static void Clear()
        {
            Actor = 0;
            _replySink = null;
        }

        internal readonly struct Scope : IDisposable
        {
            public void Dispose() => Clear();
        }
    }
}