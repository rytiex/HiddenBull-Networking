using HiddenBull.Networking.Server;
using HiddenBull.Networking.Data;
using PicoShot.Localization;
using System;

namespace HiddenBull.Networking.Auth
{
    internal interface IConnectionApprovalValidator
    {
        bool Validate(AuthRequestMessage request, out TextNode rejectionReason);
    }
}

namespace HiddenBull.Networking.Auth.Validators
{
    internal sealed class VersionValidator : IConnectionApprovalValidator
    {
        public bool Validate(AuthRequestMessage request, out TextNode rejectionReason)
        {
            if (request.GameVersion != UnityEngine.Application.version)
            {
                rejectionReason = NetworkLocalizationMessages.Version.Mismatch(UnityEngine.Application.version, request.GameVersion);
                return false;
            }

            rejectionReason = TextNode.Empty;
            return true;
        }
    }

    internal sealed class BanValidator : IConnectionApprovalValidator
    {
        private readonly Func<ulong, (bool isBanned, ClientBanInformation banInfo)> _banChecker;

        public BanValidator(Func<ulong, (bool, ClientBanInformation)> banChecker)
        {
            _banChecker = banChecker ?? throw new ArgumentNullException(nameof(banChecker));
        }

        public bool Validate(AuthRequestMessage request, out TextNode rejectionReason)
        {
            rejectionReason = TextNode.Empty;

            var (isBanned, banInfo) = _banChecker(request.SteamId);

            if (!isBanned)
                return true;

            rejectionReason = banInfo.IsPermanent
                ? NetworkLocalizationMessages.Ban.Permanent(banInfo.Reason)
                : NetworkLocalizationMessages.Ban.Temporary(banInfo.Reason, banInfo.TimeRemaining);

            return false;
        }
    }

    internal sealed class WhitelistValidator : IConnectionApprovalValidator
    {
        public bool Validate(AuthRequestMessage request, out TextNode rejectionReason)
        {
            rejectionReason = TextNode.Empty;

            // IsActive = list has entries. Bypasses when empty (safe default).
            if (!ServerWhitelistModeration.IsActive) return true;
            if (ServerWhitelistModeration.IsWhitelisted(request.SteamId)) return true;

            rejectionReason = NetworkLocalizationMessages.Whitelist.NotAllowed;
            return false;
        }
    }

    internal sealed class PasswordValidator : IConnectionApprovalValidator
    {
        private readonly string _expectedHash;

        public PasswordValidator(string expectedHash)
        {
            _expectedHash = expectedHash;
        }

        public bool Validate(AuthRequestMessage request, out TextNode rejectionReason)
        {
            rejectionReason = TextNode.Empty;

            if (string.IsNullOrEmpty(_expectedHash))
                return true;

            if (request.PasswordHash == _expectedHash)
                return true;

            rejectionReason = NetworkLocalizationMessages.Auth.InvalidPassword;
            return false;
        }

        internal static class PasswordHash
        {
            public static string Of(string password)
            {
                if (string.IsNullOrEmpty(password)) return string.Empty;
                using var sha = System.Security.Cryptography.SHA256.Create();
                var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
                var sb = new System.Text.StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }

    internal sealed class DuplicateConnectionValidator : IConnectionApprovalValidator
    {
        private readonly Func<ulong, bool> _isConnected;

        public DuplicateConnectionValidator(Func<ulong, bool> isConnected)
        {
            _isConnected = isConnected ?? throw new ArgumentNullException(nameof(isConnected));
        }

        public bool Validate(AuthRequestMessage request, out TextNode rejectionReason)
        {
            rejectionReason = TextNode.Empty;

            if (!_isConnected(request.SteamId))
                return true;

            rejectionReason = NetworkLocalizationMessages.Validator.AlreadyConnected;
            return false;
        }
    }
}