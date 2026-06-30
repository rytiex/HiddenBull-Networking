using HiddenBull.Networking.Server;
using HiddenBull.Networking.Data;
using HiddenBull.Networking;
using HiddenBull.Scenes;

using LMirman.VespaIO;

using System.Linq;
using System.Text;

namespace HiddenBull.Console.Commands
{
    /// <summary>
    /// Scene listing (local, informational) + server-authoritative scene change.
    /// The menu scene is intentionally excluded everywhere - it is only the disconnect target,
    /// not a hostable gameplay scene.
    /// </summary>
    public static class NetworkSceneCommands
    {
        // Local: every build ships the same SceneData, so this works on client and server alike.
        [VespaCommand("scenes", Name = "List Scenes", Description = "List all hostable scenes")]
        public static void Scenes()
        {
            var all = SceneUtils.All.Where(s => !s.IsMenu).ToList();
            if (all.Count == 0) { DevConsole.Log("No scenes found."); return; }

            var sb = new StringBuilder();
            sb.AppendLine($"------ Scenes ({all.Count}) ------");
            foreach (var s in all)
            {
                string role = s.IsDefault ? " [Default]" : string.Empty;
                sb.AppendLine($"{s.Id}{role}  -  {s.DisplayName}");
            }
            DevConsole.Log(sb.ToString());
        }

        // Server: changes the active scene for everyone. Requires the ServerConfig permission.
        [VespaCommand("changescene", Server = true, Name = "Change Scene", Description = "Change the active server scene. Usage: changescene <id>")]
        public static void ChangeScene(string sceneId)
        {
            if (!ServerRoles.Has(CommandContext.Actor, Permissions.ServerConfig))
            { CommandContext.Reply("You don't have permission to change the scene."); return; }

            if (NetworkSessionManager.singleton == null)
            { CommandContext.Reply("Server is not running."); return; }

            if (!SceneUtils.TryGet(sceneId, out var scene) || string.IsNullOrEmpty(scene.SceneName))
            { CommandContext.Reply($"Unknown scene '{sceneId}'. Use 'scenes' to list."); return; }

            if (scene.IsMenu)
            { CommandContext.Reply($"'{scene.Id}' is the menu scene and can't be hosted."); return; }

            if (!NetworkState.Scene.Change(scene.Id))
            { CommandContext.Reply($"Already on '{scene.DisplayName}'."); return; }

            // Persist only on a dedicated server (host/listen servers don't own the config file).
            if (Networking.Steam.SteamInformation.IsDedicated)
            {
                var config = ServerConfig.Load();
                config.Scene = scene.Id;
                config.Save();
            }

            CommandContext.Reply($"Changing scene to {scene.DisplayName} ({scene.Id}).");
        }

        // Scene ids for changescene autofill (menu excluded; SceneData is local in every build).
        [CommandAutofill("changescene")]
        private static AutofillValue ChangeSceneAutofill(AutofillBuilder b)
        {
            if (b.RelevantParameterIndex != 0) return null;
            var ids = SceneUtils.All.Where(s => !s.IsMenu).Select(s => s.Id);
            return b.CreateAutofillFromFirstMatch(ids, b.GetRelevantWordText().CleanseKey());
        }
    }
}