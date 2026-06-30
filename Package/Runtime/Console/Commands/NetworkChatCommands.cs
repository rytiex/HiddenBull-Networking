using HiddenBull.Networking.Server;
using HiddenBull.Networking;
using LMirman.VespaIO;

using System.Collections.Generic;
using System.Linq;

namespace HiddenBull.Console.Commands
{
    /// <summary>Chat via console. A player's "say" is normal chat as them; the dedicated server
    /// console (no player identity) sends as System. Consistent on both sides.</summary>
    public static class NetworkChatCommands
    {
        [VespaCommand("say", Server = true, Name = "Say", Description = "Send a chat message. Usage: say <text>  (multi-word: say \"hi all\")")]
        public static void Say(string text) => SayTo(ServerChat.GlobalChannel, text);

        [VespaCommand("say", Server = true, Name = "Say to channel", Description = "Send to a channel. Usage: say <channel> <text>")]
        public static void Say(string channel, string text) => SayTo(channel, text);

        private static void SayTo(string channel, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) { CommandContext.Reply("Nothing to say."); return; }

            if (CommandContext.FromServerConsole)   // dedicated server console -> system message, any channel
            {
                if (channel != ServerChat.GlobalChannel && !ServerChat.ChannelExists(channel))
                { CommandContext.Reply($"Unknown channel '{channel}'."); return; }
                ServerChat.ServerBroadcast(channel, text);
                CommandContext.Reply($"Sent to '{channel}'.");
            }
            else if (ServerChat.SendFromPlayer(CommandContext.Actor, channel, text))   // a player -> chat as them
            {
                CommandContext.Reply($"Sent to '{channel}'.");
            }
            else
            {
                CommandContext.Reply($"Couldn't send (not in '{channel}', empty, or rate-limited).");
            }
        }


        [VespaCommand("whisper", Server = true, Name = "Whisper", Description = "Private message a player. Usage: whisper <steamId> <text>")]
        public static void Whisper(ulong target, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) { CommandContext.Reply("Nothing to say."); return; }

            bool ok = CommandContext.FromServerConsole
                ? ServerChat.SystemWhisper(target, text)                          // console -> System whisper
                : ServerChat.WhisperFromPlayer(CommandContext.Actor, target, text); // a player -> as them

            CommandContext.Reply(ok ? $"Whispered {target}." : "Couldn't whisper (offline, empty, or rate-limited).");
        }


        // Channel autofill: server console has all channels; a player only knows its own (replicated).
        [CommandAutofill("say")]
        private static AutofillValue SayChannelAutofill(AutofillBuilder b)
        {
            if (b.RelevantParameterIndex != 0) return null;
            IEnumerable<string> channels = NetworkState.IsClient ? NetworkState.Communication.MyChannels : ServerChat.ChannelKeys;
            return b.CreateAutofillFromFirstMatch(channels, b.GetRelevantWordText().CleanseKey());
        }

        // Target autofill: connected players (from the replicated roster).
        [CommandAutofill("whisper")]
        private static AutofillValue WhisperTargetAutofill(AutofillBuilder b)
        {
            if (b.RelevantParameterIndex != 0) return null;
            ulong local = NetworkState.Players.Local.SteamId;
            var ids = NetworkState.Players.All.Values.Where(p => p.SteamId != local).Select(p => p.SteamId.ToString());
            return b.CreateAutofillFromFirstMatch(ids, b.GetRelevantWordText().CleanseKey());
        }
    }
}