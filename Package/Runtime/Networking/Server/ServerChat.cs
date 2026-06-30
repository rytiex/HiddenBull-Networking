using HiddenBull.Networking.Data;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

namespace HiddenBull.Networking.Server
{
    /// <summary>
    /// Channel-subscription chat. Players are members of opaque string channels ("all", "team:red", ...).
    /// A message to a channel reaches its members; a whisper reaches one SteamId. The game manages
    /// non-global channels via Join/Leave; the framework owns "all" and relays everything (so mute/
    /// spam/filter stay centralized). The network core never knows what a "team" is.
    /// </summary>
    internal static class ServerChat
    {
        public const string GlobalChannel = "all";
        private const int MaxMessageLength = 256;
        private const float SpamCooldownSeconds = 1f;

        private static readonly Dictionary<string, HashSet<ulong>> _channels = new();
        private static readonly Dictionary<ulong, float> _lastSent = new();

        private static readonly Dictionary<string, (string label, string hex)> _styles = new();
        public static bool ChannelExists(string channel) => _channels.ContainsKey(channel);
        public static IEnumerable<string> ChannelKeys => _channels.Keys;

        // membership
        /// <summary>Join a channel. leaveOthers (default) first removes the player from every other
        /// channel except the global one - a clean team switch. Pass false for additive membership.</summary>
        public static void Join(ulong steamId, string channel, bool leaveOthers = true)
        {
            if (string.IsNullOrEmpty(channel)) return;

            if (leaveOthers)
                foreach (var kv in _channels)
                    if (kv.Key != GlobalChannel && kv.Key != channel) kv.Value.Remove(steamId);

            if (!_channels.TryGetValue(channel, out var set)) { set = new HashSet<ulong>(); _channels[channel] = set; }
            set.Add(steamId);

            PruneEmpty();
            NotifyChannels(steamId);
        }
        public static void Leave(ulong steamId, string channel)
        {
            if (string.IsNullOrEmpty(channel) || channel == GlobalChannel) return;   // global is framework-managed
            if (_channels.TryGetValue(channel, out var set) && set.Remove(steamId))
            { PruneEmpty(); NotifyChannels(steamId); }
        }
        /// <summary>Drop a (disconnecting) player from every channel. No client notify - they are gone.</summary>
        public static void RemoveAll(ulong steamId)
        {
            foreach (var set in _channels.Values) set.Remove(steamId);
            _lastSent.Remove(steamId);
            PruneEmpty();
        }
        public static bool IsMember(ulong steamId, string channel) =>
            _channels.TryGetValue(channel, out var set) && set.Contains(steamId);

        public static void Clear() { _channels.Clear(); _lastSent.Clear(); _styles.Clear(); }

        // server-originated (system) message
        public static void ServerBroadcast(string channel, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var message = new ChatMessage { SenderSteamId = 0, Channel = channel, Text = text };
            if (string.IsNullOrEmpty(channel) || channel == GlobalChannel)
                foreach (var conn in NetworkServer.connections.Values) conn.Send(message);
            else
                RelayToChannel(channel, message);
        }

        // client send handler
        public static void HandleClientSend(NetworkConnectionToClient conn, ChatSendMessage msg)
            => TrySend(conn, msg.Channel, msg.Target, msg.Text);

        /// <summary>Relays a command-issued message AS the given player (membership, anti-spam, filter
        /// all apply, exactly like normal chat). False if not connected / dropped.</summary>
        public static bool SendFromPlayer(ulong steamId, string channel, string text)
            => TryGetConnection(steamId, out var conn) && TrySend(conn, channel, 0, text);
        private static bool TrySend(NetworkConnectionToClient conn, string channel, ulong target, string text)
        {
            if (conn == null || conn.authenticationData is not ClientData sender) return false;

            float now = Time.unscaledTime;
            if (_lastSent.TryGetValue(sender.SteamId, out var last) && now - last < SpamCooldownSeconds) return false;

            text = text?.Trim();
            if (string.IsNullOrEmpty(text)) return false;
            if (text.Length > MaxMessageLength) text = text.Substring(0, MaxMessageLength);

            if (NetworkChatGate.Filter != null)
            {
                text = NetworkChatGate.Filter(sender.SteamId, text);
                if (string.IsNullOrEmpty(text)) return false;
            }

            _lastSent[sender.SteamId] = now;

            if (target != 0)   // whisper
            {
                var dm = new ChatMessage { SenderSteamId = sender.SteamId, Channel = "whisper", Text = text, IsWhisper = true };
                if (TryGetConnection(target, out var t)) t.Send(dm);
                conn.Send(dm);
                return true;
            }

            if (string.IsNullOrEmpty(channel) || !IsMember(sender.SteamId, channel)) return false;
            RelayToChannel(channel, new ChatMessage { SenderSteamId = sender.SteamId, Channel = channel, Text = text });
            return true;
        }

        // catalog / lifecycle
        /// <summary>Defines/updates a channel's display style. 'all' may be styled but never removed.</summary>
        public static void DefineChannel(string key, string label, Color color)
        {
            if (string.IsNullOrEmpty(key)) return;
            _styles[key] = (label, ColorUtility.ToHtmlStringRGB(color));
            BroadcastCatalog();
        }
        /// <summary>Removes a channel entirely (style + members). No-op for 'all'.</summary>
        public static void UndefineChannel(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (RemoveChannel(key)) BroadcastCatalog();
        }
        /// <summary>Removes every channel except 'all' (style + members) - the "back to lobby" reset.</summary>
        public static void ClearChannels()
        {
            var keys = new List<string>();
            foreach (var k in _styles.Keys) if (k != GlobalChannel) keys.Add(k);
            foreach (var k in _channels.Keys) if (k != GlobalChannel && !keys.Contains(k)) keys.Add(k);

            bool changed = false;
            foreach (var k in keys) changed |= RemoveChannel(k);
            if (changed) BroadcastCatalog();
        }
        private static bool RemoveChannel(string key)   // no broadcast; 'all' protected
        {
            if (key == GlobalChannel) return false;
            bool changed = _styles.Remove(key);
            if (_channels.TryGetValue(key, out var members))
            {
                var ids = new List<ulong>(members);
                _channels.Remove(key);
                foreach (var id in ids) NotifyChannels(id);   // their MyChannels shrank
                changed = true;
            }
            return changed;
        }

        // replication
        private static ChannelCatalogMessage BuildCatalog()
        {
            var arr = new ChannelStyleData[_styles.Count];
            int i = 0;
            foreach (var kv in _styles)
                arr[i++] = new ChannelStyleData { Key = kv.Key, Label = kv.Value.label, ColorHex = kv.Value.hex };
            return new ChannelCatalogMessage { Channels = arr };
        }
        private static void BroadcastCatalog()
        {
            var msg = BuildCatalog();
            foreach (var conn in NetworkServer.connections.Values) conn.Send(msg);
        }

        /// <summary>Sends the current catalog to one connection (called when a client becomes ready).</summary>
        public static void SendCatalogTo(NetworkConnectionToClient conn) => conn.Send(BuildCatalog());

        // helpers
        private static void RelayToChannel(string channel, ChatMessage message)
        {
            if (!_channels.TryGetValue(channel, out var members)) return;
            foreach (var conn in NetworkServer.connections.Values)
                if (conn.authenticationData is ClientData d && members.Contains(d.SteamId))
                    conn.Send(message);
        }
        private static void NotifyChannels(ulong steamId)
        {
            if (!TryGetConnection(steamId, out var conn)) return;
            var list = new List<string>();
            foreach (var kv in _channels) if (kv.Value.Contains(steamId)) list.Add(kv.Key);
            conn.Send(new ChannelMembershipMessage { Channels = list.ToArray() });
        }
        private static bool TryGetConnection(ulong steamId, out NetworkConnectionToClient conn)
        {
            foreach (var c in NetworkServer.connections.Values)
                if (c.authenticationData is ClientData d && d.SteamId == steamId) { conn = c; return true; }
            conn = null; return false;
        }
        private static void PruneEmpty()
        {
            List<string> empty = null;
            foreach (var kv in _channels)
                if (kv.Value.Count == 0 && kv.Key != GlobalChannel) (empty ??= new()).Add(kv.Key);
            if (empty != null) foreach (var k in empty) _channels.Remove(k);
        }

        public static bool WhisperFromPlayer(ulong senderSteamId, ulong target, string text)
            => TryGetConnection(senderSteamId, out var conn) && TrySend(conn, null, target, text);
        public static bool SystemWhisper(ulong target, string text)
        {
            if (string.IsNullOrEmpty(text) || !TryGetConnection(target, out var conn)) return false;
            conn.Send(new ChatMessage { SenderSteamId = 0, Channel = "whisper", Text = text, IsWhisper = true });
            return true;
        }
    }
}