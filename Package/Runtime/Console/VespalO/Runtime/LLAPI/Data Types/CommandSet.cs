using JetBrains.Annotations;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;

namespace LMirman.VespaIO
{
	[PublicAPI]
	public class CommandSet
	{
		private readonly Dictionary<string, Command> lookup = new(32);
		private readonly List<Command> commands = new(32);
		private readonly CommandPropertiesComparer commandPropertiesComparer = new();
		private bool sortDirty;

		private List<Command> Commands
		{
			get
			{
				if (sortDirty)
				{
					commands.Clear();
					foreach (Command command in lookup.Values)
					{
						commands.Add(command);
					}

					commands.Sort(commandPropertiesComparer);
				}

				return commands;
			}
		}
		public IEnumerable<Command> AllCommands => Commands;
		public IEnumerable<KeyValuePair<string, Command>> AllDefinitions => lookup;

		public Dictionary<string, Command>.KeyCollection Keys => lookup.Keys;
		public Dictionary<string, Command>.ValueCollection Values => lookup.Values;

		public bool ContainsCommand(string key)
		{
			return lookup.ContainsKey(key.CleanseKey());
		}
		public bool TryGetCommand(string key, out Command command)
		{
			return lookup.TryGetValue(key.CleanseKey(), out command);
		}
		public Command GetCommand(string key, Command fallbackCommand = null)
		{
			return TryGetCommand(key.CleanseKey(), out Command command) ? command : fallbackCommand;
		}

		public void RegisterMethod(ICommandProperties properties, MethodInfo methodInfo)
		{
			string key = properties.Key.CleanseKey();
			if (TryGetCommand(key, out Command command))
			{
				command.AddMethod(methodInfo);
				command.SetAttributeProperties(properties);
				sortDirty = true;
			}
			else
			{
				command = new Command(properties, methodInfo);
				lookup.Add(key, command);
				sortDirty = true;
			}
		}
		public void UnregisterMethod(string key, MethodInfo methodInfo)
		{
			key = key.CleanseKey();
			if (TryGetCommand(key, out Command command))
			{
				command.RemoveMethod(methodInfo);

				if (!command.HasMethod)
				{
					lookup.Remove(key);
					sortDirty = true;
				}
			}
		}

		public void RegisterProperty(ICommandProperties properties, PropertyInfo propertyInfo)
		{
			string key = properties.Key.CleanseKey();
			if (TryGetCommand(key, out Command command))
			{
				command.SetPropertyTarget(propertyInfo);
				command.SetAttributeProperties(properties);
				sortDirty = true;
			}
			else
			{
				command = new Command(properties, propertyInfo);
				lookup.Add(key, command);
				sortDirty = true;
			}
		}
		public void RegisterField(ICommandProperties properties, FieldInfo fieldInfo)
		{
			string key = properties.Key.CleanseKey();
			if (TryGetCommand(key, out Command command))
			{
				command.SetFieldTarget(fieldInfo);
				command.SetAttributeProperties(properties);
				sortDirty = true;
			}
			else
			{
				command = new Command(properties, fieldInfo);
				lookup.Add(key, command);
				sortDirty = true;
			}
		}

		/// <summary>
		/// Unregister all command definitions for a particular key.
		/// </summary>
		/// <param name="key">The key of the command you would like to remove.</param>
		public void UnregisterCommand(string key)
		{
			key = key.CleanseKey();
			lookup.Remove(key);
			sortDirty = true;
		}
		public void UnregisterAllCommands()
		{
			lookup.Clear();
			sortDirty = true;
		}

		public IEnumerable<Command> GetPublicCommands()
		{
			return new PublicCommandEnumerable(Commands);
		}

		public class PublicCommandEnumerable : IEnumerable<Command>
		{
			private readonly PublicCommandEnumerator enumerator;
			private readonly List<Command> commands;

			public PublicCommandEnumerable(List<Command> commands)
			{
				this.commands = commands;
			}
			public IEnumerator<Command> GetEnumerator()
			{
				return new PublicCommandEnumerator(commands);
			}
			IEnumerator IEnumerable.GetEnumerator()
			{
				return GetEnumerator();
			}
		}
		public class PublicCommandEnumerator : IEnumerator<Command>
		{
			private readonly List<Command> commands;
			private int currentIndex;
			private Command currentCommand;

			public Command Current => currentCommand;
			object IEnumerator.Current => Current;

			public PublicCommandEnumerator(List<Command> commands)
			{
				this.commands = commands;
				currentIndex = -1;
				currentCommand = null;
			}

			public bool MoveNext()
			{
				while (++currentIndex < commands.Count)
				{
					Command evaluateCommand = commands[currentIndex];
					if (!evaluateCommand.Hidden)
					{
						currentCommand = evaluateCommand;
						return true;
					}
				}

				return false;
			}
			public void Reset()
			{
				currentIndex = -1;
			}
			public void Dispose() { }
		}
	}
}