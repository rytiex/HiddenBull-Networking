using System;

namespace HiddenBull.Networking
{
    /// <summary>
    /// Optional chat filtering seams (both default null = no filtering):
    /// - Filter:       SERVER-side, one policy for everyone (links, admin word lists, ...).
    ///                 Runs in ServerChat before broadcast; null/empty result drops the message.
    /// - ClientFilter: CLIENT-side, applied to each received message before display
    ///                 (e.g. Steam's per-user profanity filter, respecting each player's own settings).
    /// </summary>
    public static class NetworkChatGate
    {
        /// <summary>Server: (senderSteamId, text) -> filtered text. Null/empty drops the message.</summary>
        public static Func<ulong, string, string> Filter;

        /// <summary>Client-side Steam per-user profanity filter, applied on receipt before display.
        /// Set internally by the Steam layer; not a public seam.</summary>
        internal static Func<ulong, string, string> SteamFilter;
    }
}