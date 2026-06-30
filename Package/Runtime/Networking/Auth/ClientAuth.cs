using HiddenBull.Networking.Data;
using UnityEngine;

using Steamworks.Data;
using Steamworks;

using HiddenBull.Networking.Auth.Validators;
using System.Threading.Tasks;
using System;

namespace HiddenBull.Networking.Auth
{
    internal static class ClientAuth
    {
        private static AuthTicket _activeTicket;
        private static string _passwordHash = string.Empty;

        public static void SetPassword(string password) =>
            _passwordHash = PasswordValidator.PasswordHash.Of(password);

        /// <summary>
        /// Retrieves a Steam auth ticket and returns a ready-to-send auth request.
        /// The caller is responsible for sending this through Mirror's NetworkClient.
        /// </summary>
        public static async Task<AuthRequestMessage> PrepareAsync()
        {
            if (!Steam.SteamInformation.Initialized)
                throw new InvalidOperationException("Steam client is not initialized.");

            DisposeTicket();

            _activeTicket = await SteamUser.GetAuthSessionTicketAsync(NetIdentity.LocalHost);

            if (_activeTicket == null || _activeTicket.Data == null || _activeTicket.Data.Length == 0)
                throw new Exception("Failed to retrieve Steam auth ticket.");

            var request = new AuthRequestMessage
            {
                SteamId = Steam.SteamInformation.LocalSteamId,
                AuthTicket = _activeTicket.Data,
                PlayerName = Steam.SteamInformation.LocalName,
                PasswordHash = _passwordHash,
                GameVersion = Application.version
            };

            if (!request.IsValid)
                throw new Exception("Auth request is invalid after construction.");

            Debug.Log($"[{nameof(ClientAuth)}] Auth request prepared for {request.PlayerName} (SteamId: {request.SteamId})");
            return request;
        }

        /// <summary>
        /// Should be called when the connection is dropped or reset.
        /// Disposes the active Steam ticket and clears auth state.
        /// </summary>
        public static void OnDisconnected()
        {
            DisposeTicket();
            _passwordHash = string.Empty;
            Debug.Log($"[{nameof(ClientAuth)}] Auth data cleared on disconnect.");
        }

        private static void DisposeTicket()
        {
            if (_activeTicket == null)
                return;

            try
            {
                if (Steam.SteamInformation.Initialized)
                {
                    _activeTicket.Dispose();
                    Debug.Log($"[{nameof(ClientAuth)}] Previous auth ticket disposed.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[{nameof(ClientAuth)}] Ticket dispose suppressed (Steam shutdown race): {ex.Message}");
            }
            finally
            {
                _activeTicket = null;
            }
        }
    }
}