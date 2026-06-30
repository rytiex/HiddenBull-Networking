using HiddenBull.Networking.Steam;
using HiddenBull.Networking;
using LMirman.VespaIO;
using UnityEngine;

using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.Threading;

namespace HiddenBull.Console
{
    /// <summary>
    /// Headless command input for a dedicated server. Reads stdin on a background thread and
    /// runs each line on the main thread through the VespaIO console; console output is mirrored
    /// to stdout so the operator sees results. Self-disables on any non-dedicated build.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class DedicatedServerConsole : MonoBehaviour
    {
        private static readonly Regex RichText = new("<[^>]+>", RegexOptions.Compiled);
        private readonly ConcurrentQueue<string> _queue = new();

        private Thread _thread;
        private volatile bool _running;
        private bool _announcedReady;
        private int _emitted;

        private void Awake()
        {
            if (!SteamInformation.IsDedicated)
            {
                Destroy(this);
                return;
            }

            DevConsole.console.Enabled = true;
            _emitted = DevConsole.console.GetOutputLog().Length;
            DevConsole.console.OutputUpdate += OnOutputUpdate;

            _running = true;
            _thread = new Thread(ReadLoop) { IsBackground = true, Name = "DedicatedServerStdin" };
            _thread.Start();

            System.Console.WriteLine("[DedicatedServerConsole] Starting... commands available once the server is ready.");
        }
        private void Update()
        {
            bool ready = NetworkState.IsServer;
            if (ready && !_announcedReady)
            {
                _announcedReady = true;
                System.Console.WriteLine("[DedicatedServerConsole] Ready. Type commands, e.g. 'players', 'kick <steamId>'.");
            }

            while (_queue.TryDequeue(out string line))
            {
                if (!ready)
                {
                    System.Console.WriteLine("[DedicatedServerConsole] Server is still starting; command ignored.");
                    continue;
                }
                DevConsole.console.RunInput(line);
            }
        }
        private void OnDestroy()
        {
            _running = false;
            if (SteamInformation.IsDedicated)
                DevConsole.console.OutputUpdate -= OnOutputUpdate;
            // The stdin thread is a blocked background thread; it ends with the process.
        }

        private void ReadLoop()
        {
            while (_running)
            {
                string line;
                try { line = System.Console.ReadLine(); }
                catch { break; }

                if (line == null) break;                       // stdin closed (EOF)
                if (!string.IsNullOrWhiteSpace(line)) _queue.Enqueue(line);
            }
        }

        // Mirror new DevConsole output to stdout (rich-text tags stripped).
        private void OnOutputUpdate()
        {
            string log = DevConsole.console.GetOutputLog();
            if (log.Length <= _emitted) { _emitted = log.Length; return; }   // buffer was trimmed

            string delta = log[_emitted..];
            _emitted = log.Length;
            System.Console.Write(RichText.Replace(delta, string.Empty));
        }
    }
}