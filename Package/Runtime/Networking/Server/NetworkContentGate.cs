using System.Threading.Tasks;
using System;

namespace HiddenBull.Networking
{
    /// <summary>
    /// Extension seam that lets an OPTIONAL content/mod layer gate connections on content
    /// readiness, without the network core ever depending on that layer. A content layer
    /// (if one exists) assigns these delegates at startup; the authenticator only invokes them.
    ///
    /// The coupling is deliberately one-way: this assembly never names a content type, only
    /// these two delegates. That is exactly what lets the whole content system be added or
    /// removed without touching networking code.
    ///
    /// DORMANT BY DESIGN: with no content layer wired, both delegates stay null and the gate
    /// is a pure no-op. The authenticator reads "no gate" as "accept immediately"
    /// (see SteamNetworkAuthenticator.AcceptWith and OnAuthResponseReceived), and the matching
    /// content-await flow there (ContentReady/ContentProgress handling, ContentTimeout) never
    /// triggers because a client with no content layer never reports content. Keeping this in
    /// place costs nothing and preserves the re-integration path: plugging content back in is
    /// "assign these two delegates", not "re-surgery the auth flow".
    ///
    /// CONTRACT: whoever assigns these MUST clear them back to null on teardown. They are
    /// mutable statics; in the editor with domain reload disabled they survive play-mode
    /// restarts, so a stale closure would otherwise linger across sessions.
    /// </summary>
    public static class NetworkContentGate
    {
        /// <summary>Server: keys a joining client must mount before being accepted. Null/empty = no gate.</summary>
        public static Func<string[]> GetRequiredKeys;

        /// <summary>Client: download + mount the given keys (Steam, out-of-band). Returns success. progress 0..1.</summary>
        public static Func<string[], Action<float>, Task<bool>> PrepareAsync;
    }
}