using PicoShot.Localization;

namespace HiddenBull.Networking.Data
{
    internal static class NetworkLocalizationMessages
    {
        public static class Auth
        {
            public static readonly TextNode InvalidPayload = TextNode.Localized("server.auth.invalidpayload");
            public static readonly TextNode PayloadValidationFailed = TextNode.Localized("server.auth.validationfailed");
            public static readonly TextNode SteamAuthFailed = TextNode.Localized("server.auth.steamfailed");
            public static readonly TextNode InvalidPassword = TextNode.Localized("server.auth.invalidpassword");
            public static readonly TextNode ContentFailed = TextNode.Localized("server.auth.contentfailed");
        }

        public static class Version
        {
            public static TextNode Mismatch(TextNode serverVer, TextNode clientVer) =>
                TextNode.Localized("server.version.mismatch", serverVer, clientVer);
        }

        public static class Ban
        {
            public static TextNode Permanent(TextNode reason) =>
                TextNode.Localized("server.ban.permanent", reason);

            public static TextNode Temporary(TextNode reason, System.TimeSpan timeRemaining) =>
                TextNode.Localized("server.ban.temporary", reason, ToReadable(timeRemaining));

            /// <summary>
            /// Human-readable representation of a duration, e.g.:
            ///   5d 3h 30m 0s -> "5 days 3 hours"
            ///   0d 3h 30m 0s -> "3 hours 30 minutes"
            ///   0d 0h 30m 5s -> "30 minutes"
            ///   0d 0h 0m 15s -> "15 seconds"
            /// Shows at most two units, largest first, to keep messages compact.
            /// </summary>
            public static TextNode ToReadable(System.TimeSpan span)
            {
                if (span.TotalSeconds < 1)
                    return TextNode.Localized("server.ban.temporary.second", $"{span.Seconds}");

                var parts = new System.Collections.Generic.List<TextNode>(4);

                if (span.Days > 0)
                    parts.Add(TextNode.Localized("server.ban.temporary.day", $"{span.Days}"));

                if (span.Hours > 0)
                    parts.Add(TextNode.Localized("server.ban.temporary.hour", $"{span.Hours}"));

                if (span.Minutes > 0 && parts.Count < 2)
                    parts.Add(TextNode.Localized("server.ban.temporary.minute", $"{span.Minutes}"));

                if (parts.Count == 0)
                    parts.Add(TextNode.Localized("server.ban.temporary.second", $"{span.Seconds}"));

                string pattern = string.Empty;
                for (int i = 0; i < parts.Count; ++i)
                    pattern += "{" + i.ToString() + "}" + (i != parts.Count - 1 ? " " : string.Empty);

                return TextNode.Formatted(pattern, parts.ToArray());
            }
        }

        public static class Kick
        {
            public static readonly TextNode Message = TextNode.Localized("server.kick");
        }

        public static class RateLimit
        {
            public static TextNode Blocked(int seconds) => TextNode.Localized("server.ratelimit.blocked", seconds.ToString());
            public static TextNode Cooldown(int seconds) => TextNode.Localized("server.ratelimit.cooldown", seconds.ToString());
        }

        public static class Validator
        {
            public static TextNode AlreadyConnected = TextNode.Localized("server.validator.alreadyconnected");
        }

        public static class Whitelist
        {
            public static readonly TextNode NotAllowed = TextNode.Localized("server.whitelist.notallowed");
        }
    }
}