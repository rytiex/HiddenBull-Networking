using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;

namespace LMirman.VespaIO
{
    /// <summary>
    /// Default commands that are built into the console and are useful in practically any project.
    /// </summary>
    public static class NativeCommands
    {
        private const int HelpPageLength = 10;

        [VespaCommand("quit", Name = "Quit Application", Description = "Closes the application", ManualPriority = 70)]
        public static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.ExitPlaymode();
#endif
            Application.Quit();
        }

#if !UNITY_SERVER
        [VespaCommand("clear", Name = "Clear Console History",
            Description =
                "Clears the entire console history including this command's execution. Usage is recommended when the history grows too large or the application freezes when logging occurs.",
            ManualPriority = 80)]
        public static void Clear()
        {
            DevConsole.console.Clear();
        }
#endif

        #region Help Commands
        [VespaCommand("help", Name = "Help Manual", Description = "Search for commands and get assistance with particular commands.", ManualPriority = 90)]
        public static void Help()
        {
            LogPage();
        }

        [VespaCommand("help")]
        public static void Help(int pageNum)
        {
            LogPage(pageNum);
        }

        [VespaCommand("help")]
        public static void Help(string query)
        {
            string value = query.ToLower();
            if (Commands.commandSet.TryGetCommand(value, out Command command))
            {
                DevConsole.Log(command.Guide);
            }
            else
            {
                PrintMatching(value);
            }
        }

        [CommandAutofill("help")]
        private static AutofillValue GetHelpAutofillValue(AutofillBuilder autofillBuilder)
        {
            if (autofillBuilder.RelevantParameterIndex != 0)
            {
                return null;
            }

            string relevantWord = autofillBuilder.GetRelevantWordText().CleanseKey();
            IEnumerable<string> commandKeys = Commands.commandSet.GetPublicCommands().Select(command => command.Key);
            return autofillBuilder.CreateAutofillFromFirstMatch(commandKeys, relevantWord);
        }

        private static int CountPages()
        {
            int count = 0;
            foreach (Command unused in Commands.commandSet.GetPublicCommands())
            {
                count++;
            }

            return Mathf.CeilToInt((float)count / HelpPageLength);
        }

        private static void LogPage(int page = 1)
        {
            int pageCount = CountPages();
            page = Mathf.Clamp(page, 1, pageCount);
            int remaining = HelpPageLength;
            int ignore = (page - 1) * HelpPageLength;
            DevConsole.Log($"========== Help: Page {page}/{pageCount} ==========");
            foreach (Command command in Commands.commandSet.GetPublicCommands())
            {
                if (remaining <= 0)
                    break;

                if (ignore <= 0)
                {
                    PrintLookup(command);
                    remaining--;
                }
                else
                    ignore--;
            }

            DevConsole.Log($"========== END OF PAGE {page}/{pageCount} ==========");
            DevConsole.Log("========== Use \"help {page #}\" for more ==========");
        }

        private static void PrintMatching(string key)
        {
            DevConsole.Log($"========== Commands Containing \"{key}\" ==========");
            foreach (Command command in Commands.commandSet.GetPublicCommands())
            {
                if (command.Key.Contains(key.CleanseKey()) || command.Name.ToLower().Contains(key.ToLower()))
                {
                    PrintLookup(command);
                }
            }
        }

        private static void PrintLookup(Command command)
        {
            DevConsole.Log($"- [{command.Key}] \"{command.Name}\"\n    - {command.Description}");
        }
        #endregion

#if !UNITY_SERVER
        #region Alias Commands
        [VespaCommand("alias", Name = "Set Alias", Description = "Set a particular alias definition")]
        public static void SetAlias(string alias, string value)
        {
            //Validate alias name
            alias = alias.CleanseKey();
            if (alias.Length == 0 || value.Length == 0)
            {
                DevConsole.Log("Your alias or value was empty.", Console.LogStyling.Notice);
                return;
            }

            // Set alias
            bool isNewAlias = Aliases.aliasSet.SetAlias(alias, value);
            Aliases.WriteToDisk();
            DevConsole.Log(isNewAlias
                ? $"<color=green>+</color> Added alias \"{alias}\" to represent \"{value}\""
                : $"<color=yellow>*</color> Modified alias \"{alias}\" to represent \"{value}\"");
        }

        [VespaCommand("alias_delete", Name = "Delete Alias", Description = "Delete a particular alias definition")]
        public static void DeleteAlias(string alias)
        {
            alias = alias.CleanseKey();
            bool didRemoveAlias = Aliases.aliasSet.RemoveAlias(alias);
            if (didRemoveAlias)
            {
                Aliases.WriteToDisk();
            }

            DevConsole.Log(didRemoveAlias
                ? $"<color=red>-</color> Removed alias \"{alias}\"."
                : $"<color=yellow>Warning:</color> Tried to remove alias \"{alias}\" but no such alias was found.");
        }

        [CommandAutofill("alias_view")]
        [CommandAutofill("alias_delete")]
        private static AutofillValue GetAliasAutofillValue(AutofillBuilder autofillBuilder)
        {
            if (autofillBuilder.RelevantParameterIndex != 0)
            {
                return null;
            }

            string relevantWord = autofillBuilder.GetRelevantWordText().CleanseKey();
            return autofillBuilder.CreateAutofillFromFirstMatch(Aliases.aliasSet.Keys, relevantWord);
        }

        [VespaCommand("alias_reset_all", Name = "Reset All Aliases", Description = "Reset all alias definitions")]
        public static void ResetAllAliasesWarning()
        {
            DevConsole.Log("This will remove <b>ALL</b> alias definitions!\nTo confirm alias reset please enter the following command: \"alias_reset_all CONFIRM\"", Console.LogStyling.Notice);
        }

        [VespaCommand("alias_reset_all", Name = "Reset All Aliases", Description = "Reset all alias definitions")]
        public static void ResetAllAliases(string confirmation)
        {
            if (confirmation == "CONFIRM")
            {
                Aliases.ResetAliasesAndFile();
                DevConsole.Log("<color=red>-</color> All aliases have been removed!");
            }
            else
            {
                ResetAllAliasesWarning();
            }
        }

        [VespaCommand("alias")]
        [VespaCommand("alias_view", Name = "View Alias", Description = "View the definition for a particular alias")]
        public static void ViewAlias(string alias)
        {
            alias = alias.CleanseKey();
            DevConsole.Log(Aliases.aliasSet.TryGetAlias(alias, out string definition)
                ? $"\"{alias}\" -> \"{definition}\""
                : $"<color=red>Error:</color> Tried to view alias \"{alias}\" but no such alias was found.");
        }

        [VespaCommand("alias_list", Name = "List Aliases", Description = "View list of all aliases that have been defined")]
        public static void ListAlias(string filter)
        {
            filter = filter.CleanseKey();
            DevConsole.Log($"--- Aliases Containing \"{filter}\" ---");
            foreach (KeyValuePair<string, string> alias in Aliases.aliasSet.AllAliases)
            {
                if (alias.Key.Contains(filter) || alias.Value.ToLower().Contains(filter))
                {
                    DevConsole.Log($"'{alias.Key}'  ->  '{alias.Value}'");
                }
            }
        }

        private const int AliasPageLength = 10;

        [VespaCommand("alias_list", Name = "List Aliases", Description = "View list of all aliases that have been defined")]
        public static void ListAlias(int pageNum = 0)
        {
            int pageCount = Mathf.Max(Mathf.CeilToInt((float)Aliases.aliasSet.AliasCount / AliasPageLength), 1);
            pageNum = Mathf.Clamp(pageNum, 1, pageCount);
            int remaining = AliasPageLength;
            int ignore = (pageNum - 1) * AliasPageLength;
            DevConsole.Log($"--- Aliases {pageNum}/{pageCount} ---");
            foreach (KeyValuePair<string, string> alias in Aliases.aliasSet.AllAliases)
            {
                //Stop if we have print out enough commands
                if (remaining <= 0)
                {
                    break;
                }

                if (ignore > 0)
                {
                    ignore--;
                }
                else
                {
                    DevConsole.Log($"'{alias.Key}'  ->  '{alias.Value}'");
                    remaining--;
                }
            }

            DevConsole.Log($"--- END OF PAGE {pageNum}/{pageCount} ---");
            DevConsole.Log("--- Use \"alias_list {page #}\" for more ---");
        }
        #endregion
#endif
    }
}