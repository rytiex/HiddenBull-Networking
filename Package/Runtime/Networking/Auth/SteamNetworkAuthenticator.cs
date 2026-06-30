using HiddenBull.Networking.Server;
using HiddenBull.Networking.Steam;
using HiddenBull.Networking.Data;
using Steamworks;

using UnityEngine;
using Mirror;

using System.Collections.Generic;
using System;

namespace HiddenBull.Networking.Auth
{
    /// <summary>
    /// Routes Steam auth API calls to the correct subsystem:
    ///   - Host mode (SteamClient.Init only)    -> SteamUser.*
    ///   - Dedicated mode (SteamServer.Init)    -> SteamServer.*
    ///
    /// Calling SteamServer.* in Host mode NREs on SteamServer.Internal (null).
    /// </summary>
    internal static class SteamAuthBridge
    {
        public static bool BeginAuthSession(byte[] ticket, SteamId steamId)
        {
            if (!SteamInformation.Initialized) return false;

            if (SteamInformation.IsDedicated)
                return SteamServer.BeginAuthSession(ticket, steamId);
            else
            {
                // SteamUser returns the enum directly; normalize to bool.
                var result = SteamUser.BeginAuthSession(ticket, steamId);
                return result == BeginAuthResult.OK;
            }
        }
        public static void EndAuthSession(SteamId steamId)
        {
            if (!SteamInformation.Initialized) return;

            if (SteamInformation.IsDedicated)
                SteamServer.EndSession(steamId);
            else
                SteamUser.EndAuthSession(steamId);
        }

        public static void SubscribeValidation(Action<SteamId, SteamId, AuthResponse> handler)
        {
            if (SteamInformation.IsDedicated)
                SteamServer.OnValidateAuthTicketResponse += handler;
            else
                SteamUser.OnValidateAuthTicketResponse += handler;
        }
        public static void UnsubscribeValidation(Action<SteamId, SteamId, AuthResponse> handler)
        {
            if (SteamInformation.IsDedicated)
                SteamServer.OnValidateAuthTicketResponse -= handler;
            else
                SteamUser.OnValidateAuthTicketResponse -= handler;
        }
    }
    internal sealed class SteamNetworkAuthenticator : NetworkAuthenticator
    {
        private const float AuthTimeoutSeconds = 10f;
        public const float ContentTimeoutSeconds = 600f;   // generous cap so a stuck download frees the slot

        private ServerTransportMode _transportMode;
        private IReadOnlyList<IConnectionApprovalValidator> _validators;
        private ServerRateLimiter _rateLimiter;
        private Func<ulong, bool> _isSteamIdInUse;

        private readonly HashSet<int> _awaitingContent = new();
        private readonly Dictionary<int, Coroutine> _authTimeouts = new();
        private readonly Dictionary<ulong, NetworkConnectionToClient> _pendingAuthSessions = new();

        #region Configuration
        /// <summary>
        /// Must be called before StartServer. Wires the validator chain,
        /// rate limiter, and session lookup used by IP-mode identity transform.
        /// </summary>
        public void ConfigureServer(
            ServerTransportMode mode, IReadOnlyList<IConnectionApprovalValidator> validators,
            ServerRateLimiter rateLimiter, Func<ulong, bool> isSteamIdInUse)
        {
            _transportMode = mode;
            _validators = validators ?? throw new ArgumentNullException(nameof(validators));
            _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
            _isSteamIdInUse = isSteamIdInUse ?? throw new ArgumentNullException(nameof(isSteamIdInUse));
        }

        /// <summary>
        /// Sets the password for the next client connection attempt.
        /// </summary>
        public void SetSessionPassword(string password) => ClientAuth.SetPassword(password);
        #endregion

        #region Server
        public override void OnStartServer()
        {
            NetworkServer.RegisterHandler<AuthRequestMessage>(OnAuthRequestReceived, false);
            NetworkServer.RegisterHandler<ContentReadyMessage>(OnContentReady, false);
            NetworkServer.RegisterHandler<ContentProgressMessage>(OnContentProgress, false);
            NetworkServer.OnDisconnectedEvent += OnConnectionDropped;
            SteamAuthBridge.SubscribeValidation(OnSteamAuthValidated);
        }
        public override void OnStopServer()
        {
            foreach (var co in _authTimeouts.Values)
                if (co != null) StopCoroutine(co);
            _authTimeouts.Clear();
            _awaitingContent.Clear();

            NetworkServer.UnregisterHandler<AuthRequestMessage>();
            NetworkServer.UnregisterHandler<ContentReadyMessage>();
            NetworkServer.UnregisterHandler<ContentProgressMessage>();
            NetworkServer.OnDisconnectedEvent -= OnConnectionDropped;
            SteamAuthBridge.UnsubscribeValidation(OnSteamAuthValidated);
            _pendingAuthSessions.Clear();
        }
        public override void OnServerAuthenticate(NetworkConnectionToClient conn)
        {
            if (conn is LocalConnectionToClient)
            {
                HandleHostConnection(conn);
                return;
            }

            // Start timeout - if client doesn't send AuthRequestMessage in time, drop it
            _authTimeouts[conn.connectionId] = StartCoroutine(AuthTimeoutCoroutine(conn));
        }
        private System.Collections.IEnumerator AuthTimeoutCoroutine(NetworkConnectionToClient conn)
        {
            yield return new WaitForSeconds(AuthTimeoutSeconds);

            _authTimeouts.Remove(conn.connectionId);

            if (conn != null && !conn.isAuthenticated && NetworkServer.connections.ContainsKey(conn.connectionId))
            {
                Debug.LogWarning($"[{nameof(SteamNetworkAuthenticator)}] Connection {conn.connectionId} did not authenticate within {AuthTimeoutSeconds}s. Dropping.");
                conn.Disconnect();
            }
        }
        private void ClearAuthTimeout(NetworkConnectionToClient conn)
        {
            if (conn == null) return;
            if (_authTimeouts.TryGetValue(conn.connectionId, out var co))
            {
                if (co != null) StopCoroutine(co);
                _authTimeouts.Remove(conn.connectionId);
            }
        }
        private void OnContentProgress(NetworkConnectionToClient conn, ContentProgressMessage msg)
        {
            if (!_awaitingContent.Contains(conn.connectionId)) return;
            if (_authTimeouts.TryGetValue(conn.connectionId, out var co) && co != null) StopCoroutine(co);
            _authTimeouts[conn.connectionId] = StartCoroutine(ContentTimeoutCoroutine(conn));
        }

        private void HandleHostConnection(NetworkConnectionToClient conn)
        {
            ulong steamId = Steam.SteamInformation.Initialized ? Steam.SteamInformation.LocalSteamId : 0UL;
            string playerName = Steam.SteamInformation.Initialized ? Steam.SteamInformation.LocalName : "Host";

            conn.authenticationData = new ClientData(conn.connectionId, steamId, playerName);
            Debug.Log($"[{nameof(SteamNetworkAuthenticator)}] Host connection accepted ({playerName}, SteamId: {steamId}).");
            ServerAccept(conn);
        }
        private void OnAuthRequestReceived(NetworkConnectionToClient conn, AuthRequestMessage request)
        {
            // Defense in depth: a buggy or malicious client could resend AuthRequest
            // after being authenticated. Ignore those.
            if (conn.isAuthenticated)
            {
                Debug.LogWarning($"[{nameof(SteamNetworkAuthenticator)}] AuthRequest from already-authenticated connection {conn.connectionId}, ignoring.");
                return;
            }

            if (!request.IsValid)
            {
                RejectWith(conn, NetworkLocalizationMessages.Auth.InvalidPayload);
                return;
            }

            // Rate-limit gate. This is flow control, NOT an auth failure: it must never
            // feed RecordFailure, or a blocked/cooldown client's retries would keep
            // re-extending their own block (the failure counter is unbounded and a
            // re-block resets BlockedUntilUtc).
            if (!_rateLimiter.IsAllowed(request.SteamId, out PicoShot.Localization.TextNode rateLimitReason))
            {
                Debug.LogWarning($"[{nameof(SteamNetworkAuthenticator)}] Rate limited {request.SteamId}: {rateLimitReason}");
                RejectWith(conn, rateLimitReason);
                return;
            }

            // Validator chain runs over the original SteamID (before any transform).
            foreach (var validator in _validators)
            {
                if (!validator.Validate(request, out PicoShot.Localization.TextNode reason))
                {
                    // Real auth rejection (version/ban/whitelist/password/duplicate) feeds
                    // the rate limiter. Rate-limit's own gate rejection does NOT reach here.
                    _rateLimiter.RecordFailure(request.SteamId);
                    Debug.LogWarning($"[{nameof(SteamNetworkAuthenticator)}] Validator {validator.GetType().Name} rejected {request.SteamId}: {reason}");
                    RejectWith(conn, reason);
                    return;
                }
            }

            // IP mode: skip Steam ticket validation, apply identity transform, accept.
            if (_transportMode == ServerTransportMode.IP)
            {
                ulong effectiveSteamId = ResolveUniqueSteamId(request.SteamId);
                conn.authenticationData = new ClientData(conn.connectionId, effectiveSteamId, request.PlayerName);
                _rateLimiter.RecordSuccess(request.SteamId);
                Debug.Log($"[{nameof(SteamNetworkAuthenticator)}] IP mode auth accepted: {request.PlayerName} (original: {request.SteamId}, effective: {effectiveSteamId}).");
                AcceptWith(conn);
                return;
            }

            // Steam mode: begin auth session and wait for OnValidateAuthTicketResponse.
            // Defensive EndSession: clears any lingering session from a crashed/abrupt
            // disconnect where OnConnectionDropped didn't run. Belt-and-suspenders.
            SteamAuthBridge.EndAuthSession(request.SteamId);
            if (!SteamAuthBridge.BeginAuthSession(request.AuthTicket, request.SteamId))
            {
                _rateLimiter.RecordFailure(request.SteamId);
                Debug.LogWarning($"[{nameof(SteamNetworkAuthenticator)}] BeginAuthSession failed for SteamID {request.SteamId} (ticket bytes: {request.AuthTicket?.Length ?? 0}).");
                RejectWith(conn, NetworkLocalizationMessages.Auth.SteamAuthFailed);
                return;
            }

            conn.authenticationData = new ClientData(conn.connectionId, request.SteamId, request.PlayerName);
            _pendingAuthSessions[request.SteamId] = conn;
            Debug.Log($"[{nameof(SteamNetworkAuthenticator)}] Steam auth pending for {request.PlayerName} (SteamId: {request.SteamId}).");
        }
        private void OnSteamAuthValidated(SteamId steamId, SteamId ownerSteamId, AuthResponse response)
        {
            if (!_pendingAuthSessions.TryGetValue(steamId, out var conn))
            {
                // Already cleaned up (OnConnectionDropped won the race).
                SteamAuthBridge.EndAuthSession(steamId);
                return;
            }
            _pendingAuthSessions.Remove(steamId);

            // Defensive: connection may have died between BeginAuthSession and now.
            // Mirror removes from NetworkServer.connections on transport disconnect, so
            // its absence here means the conn is dead and we must NOT proceed to Accept.
            if (conn == null || !NetworkServer.connections.ContainsKey(conn.connectionId))
            {
                SteamAuthBridge.EndAuthSession(steamId);
                Debug.Log($"[{nameof(SteamNetworkAuthenticator)}] Steam auth result for {steamId} arrived after disconnect; discarded.");
                return;
            }

            if (response == AuthResponse.OK)
            {
                _rateLimiter.RecordSuccess(steamId);
                AcceptWith(conn);
                Debug.Log($"[{nameof(SteamNetworkAuthenticator)}] Steam auth validated for {steamId}.");
            }
            else
            {
                SteamAuthBridge.EndAuthSession(steamId);
                _rateLimiter.RecordFailure(steamId);

                Debug.LogWarning($"[{nameof(SteamNetworkAuthenticator)}] Steam ticket rejected for {steamId}: {response}");
                RejectWith(conn, NetworkLocalizationMessages.Auth.SteamAuthFailed);
            }
        }
        private ulong ResolveUniqueSteamId(ulong baseId)
        {
            ulong candidate = baseId;
            while (_isSteamIdInUse(candidate))
                candidate++;
            return candidate;
        }

        private void OnConnectionDropped(NetworkConnectionToClient conn)
        {
            ClearAuthTimeout(conn);
            _awaitingContent.Remove(conn.connectionId);

            // 1. Pending Steam auth: still mid-validation when connection dropped.
            SteamId? pendingId = null;
            foreach (var kvp in _pendingAuthSessions)
            {
                if (kvp.Value == conn) { pendingId = kvp.Key; break; }
            }

            if (pendingId.HasValue)
            {
                SteamAuthBridge.EndAuthSession(pendingId.Value);
                _pendingAuthSessions.Remove(pendingId.Value);
                Debug.Log($"[{nameof(SteamNetworkAuthenticator)}] Ended pending Steam auth session for {pendingId.Value} on disconnect.");
                return;
            }

            // 2. Authenticated Steam client: BeginAuthSession was OK'd and the entry
            // was removed from _pendingAuthSessions in OnSteamAuthValidated. Steam
            // doesn't auto-EndSession on connection drop — without this, the next
            // BeginAuthSession for the same SteamID returns DuplicateRequest.
            if (_transportMode != ServerTransportMode.IP &&
                conn is not LocalConnectionToClient &&
                conn.authenticationData is ClientData data &&
                data.SteamId != 0)
            {
                SteamAuthBridge.EndAuthSession(data.SteamId);
                Debug.Log($"[{nameof(SteamNetworkAuthenticator)}] Ended Steam auth session for {data.SteamId} on disconnect.");
            }
        }

        private void AcceptWith(NetworkConnectionToClient conn)
        {
            ClearAuthTimeout(conn);

            string[] required = NetworkContentGate.GetRequiredKeys?.Invoke() ?? System.Array.Empty<string>();
            conn.Send(new AuthResponseMessage
            {
                Success = true,
                Reason = PicoShot.Localization.TextNode.Empty,
                RequiredContent = required
            });

            if (required.Length == 0)
            {
                ServerAccept(conn);   // nothing to download -> join immediately
                return;
            }

            // Password/auth already passed; wait for the client to download+mount required content
            // before joining (GMod-style). No short auth timeout here (content comes from Steam,
            // out-of-band, can take a while); a long safety cap frees a stuck slot.
            _awaitingContent.Add(conn.connectionId);
            _authTimeouts[conn.connectionId] = StartCoroutine(ContentTimeoutCoroutine(conn));
        }
        private System.Collections.IEnumerator ContentTimeoutCoroutine(NetworkConnectionToClient conn)
        {
            yield return new WaitForSeconds(ContentTimeoutSeconds);
            _authTimeouts.Remove(conn.connectionId);
            _awaitingContent.Remove(conn.connectionId);
            if (conn != null && !conn.isAuthenticated && NetworkServer.connections.ContainsKey(conn.connectionId))
            {
                Debug.LogWarning($"[{nameof(SteamNetworkAuthenticator)}] Connection {conn.connectionId} did not finish content within {ContentTimeoutSeconds}s. Dropping.");
                conn.Disconnect();
            }
        }
        private void OnContentReady(NetworkConnectionToClient conn, ContentReadyMessage msg)
        {
            if (conn.isAuthenticated) return;
            if (!_awaitingContent.Remove(conn.connectionId)) return;   // not awaiting / already handled
            ClearAuthTimeout(conn);

            if (msg.Success)
            {
                Debug.Log($"[{nameof(SteamNetworkAuthenticator)}] Content ready for connection {conn.connectionId}; accepting.");
                ServerAccept(conn);
            }
            else
            {
                Debug.LogWarning($"[{nameof(SteamNetworkAuthenticator)}] Content prep failed for connection {conn.connectionId}.");
                RejectWith(conn, NetworkLocalizationMessages.Auth.ContentFailed);
            }
        }

        private void RejectWith(NetworkConnectionToClient conn, PicoShot.Localization.TextNode reason)
        {
            ClearAuthTimeout(conn);
            conn.Send(new AuthResponseMessage
            {
                Success = false,
                Reason = reason.IsEmpty ? PicoShot.Localization.TextNode.Empty : reason
            });
            StartCoroutine(DelayedReject(conn));
        }
        private System.Collections.IEnumerator DelayedReject(NetworkConnectionToClient conn)
        {
            // Wait long enough for the AuthResponseMessage to be batched and flushed
            // through ServerLateUpdate so the client receives the reason before disconnect.
            yield return new WaitForSeconds(.1f);

            if (conn != null && conn.isReady == false && conn.isAuthenticated == false)
                ServerReject(conn);
        }
        #endregion

        #region Client
        public override void OnStartClient()
        {
            NetworkClient.RegisterHandler<AuthResponseMessage>(OnAuthResponseReceived, false);
        }
        public override void OnStopClient()
        {
            NetworkClient.UnregisterHandler<AuthResponseMessage>();
            ClientAuth.OnDisconnected();
        }

        public override async void OnClientAuthenticate()
        {
            // Host mode: client uses LocalConnectionToServer. The server has already
            // accepted this connection via the LocalConnectionToClient branch in
            // OnServerAuthenticate. No auth request needed.
            if (NetworkClient.connection is LocalConnectionToServer)
            {
                Debug.Log($"[{nameof(SteamNetworkAuthenticator)}] Host client self-accepting.");
                ClientAccept();
                return;
            }

            try
            {
                var request = await ClientAuth.PrepareAsync();
                NetworkClient.Send(request);
                Debug.Log($"[{nameof(SteamNetworkAuthenticator)}] Auth request sent.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{nameof(SteamNetworkAuthenticator)}] Failed to prepare auth: {ex.Message}");
                ClientReject();
            }
        }
        private async void OnAuthResponseReceived(AuthResponseMessage response)
        {
            if (!response.Success)
            {
                Debug.LogWarning($"[{nameof(SteamNetworkAuthenticator)}] Authentication rejected: {response.Reason}");
                ClientReject();
                return;
            }

            // No content system on this client -> just accept.
            if (NetworkContentGate.PrepareAsync == null)
            {
                Debug.Log($"[{nameof(SteamNetworkAuthenticator)}] Authenticated.");
                ClientAccept();
                return;
            }

            // Always prepare — even with an empty required set — so the client ENTERS the session
            // content context and unmounts menu-only content it had loaded.
            var keys = response.RequiredContent ?? Array.Empty<string>();
            Debug.Log($"[{nameof(SteamNetworkAuthenticator)}] Authenticated; entering session with {keys.Length} required addon(s)...");

            float lastPing = 0f;
            void Report(float p)
            {
                NetworkState.Client.RaiseContentProgress_Internal(p);
                if (NetworkClient.active && Time.realtimeSinceStartup - lastPing > 2f)
                { lastPing = Time.realtimeSinceStartup; NetworkClient.Send(new ContentProgressMessage { Progress = p }); }
            }

            bool ok;
            try { ok = await NetworkContentGate.PrepareAsync(keys, Report); }
            catch (Exception ex) { Debug.LogError($"[{nameof(SteamNetworkAuthenticator)}] Content prepare failed: {ex.Message}"); ok = false; }

            if (!NetworkClient.active) return;
            NetworkClient.Send(new ContentReadyMessage { Success = ok });
            if (ok) ClientAccept(); else ClientReject();
        }
        #endregion
    }
}