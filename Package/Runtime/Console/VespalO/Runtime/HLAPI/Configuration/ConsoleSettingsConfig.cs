using UnityEngine;
using System;

namespace LMirman.VespaIO
{
	/// <summary>
	/// Stores configuration data for the <see cref="DevConsole"/>
	/// </summary>
	[Serializable]
	public class ConsoleSettingsConfig
	{
		[Header("Dev Console")]
		[Tooltip("The default state of the console enabled variable in the unity editor.")]
		public bool defaultConsoleEnableEditor = true;
		[Tooltip("The default state of the console enabled variable in a standalone build (i.e non-editor)")]
		public bool defaultConsoleEnableStandalone = true;
		[Tooltip("The method by which assemblies are picked for command selection")]
		public Commands.AssemblyFilter assemblyFilter = Commands.AssemblyFilter.Standard;

		[Header("Instantiate On Load")]
		[Tooltip("When true will automatically create an instance of the console when the game starts.")]
		public bool instantiateConsoleOnLoad = false;
		[Tooltip("Where is the console template stored? Must be a path inside a resources folder.")]
		public string consoleResourcePath = "Console/PREFAB_Console";

		public ConsoleSettingsConfig DeepCopy() => new()
		{
			defaultConsoleEnableEditor = defaultConsoleEnableEditor,
			defaultConsoleEnableStandalone = defaultConsoleEnableStandalone,
			assemblyFilter = assemblyFilter,
			instantiateConsoleOnLoad = instantiateConsoleOnLoad,
			consoleResourcePath = consoleResourcePath,
		};
	}
}