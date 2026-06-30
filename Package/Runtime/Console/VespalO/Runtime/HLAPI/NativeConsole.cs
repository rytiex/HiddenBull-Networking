using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine;

namespace LMirman.VespaIO
{
	/// <summary>
	/// A standard implementation of <see cref="Console"/> that provides useful default functionality that isn't explicitly required.
	/// </summary>
	[PublicAPI]
	public class NativeConsole : Console
	{
		public readonly HashSet<string> autofillExclusions = new();

		private string virtualText;
		/// <summary>
		/// Virtual text is the internal input text that is considered the current user console input.
		/// This is particularly important when using autofill because autofill will <b>not</b> change the virtual text until the user inputs another character.
		/// </summary>
		public string VirtualText
		{
			get => virtualText;
			set
			{
				autofillExclusions.Clear();
				virtualText = value;
				UpdateNextAutofill();
			}
		}

		/// <summary>
		/// If <see cref="TryGetNextAutofillApplied"/> were to be invoked, this auto fill value will be input into <see cref="VirtualText"/> and another class 
		/// </summary>
		public AutofillValue NextAutofill { get; private set; }

		/// <summary>
		/// Run an invocation in the context of this console.
		/// </summary>
		/// <param name="invocation">The invocation to run on this console.</param>
		public override void RunInvocation(Invocation invocation) => base.RunInvocation(invocation);

		private void UpdateNextAutofill()
		{
			AutofillValue autofillValue = GetAutofillValue(virtualText, autofillExclusions);
			if (autofillValue == null && autofillExclusions.Count > 0)
			{
				autofillExclusions.Clear();
				autofillValue = GetAutofillValue(virtualText, autofillExclusions);
			}

			NextAutofill = autofillValue;
		}

		/// <summary>
		/// Attempts to get a new value for the console input with <see cref="NextAutofill"/> applied.
		/// </summary>
		/// <param name="newInputValue">The new input value for the console after the autofill is applied.</param>
		/// <returns>True if there was an autofill to apply, false if there was not.</returns>
		public bool TryGetNextAutofillApplied(out string newInputValue)
		{
			if (NextAutofill != null)
			{
				AutofillValue autofillValue = NextAutofill;
				autofillExclusions.Add(autofillValue.newWord);
				UpdateNextAutofill();
				newInputValue = $"{virtualText[..autofillValue.globalStartIndex]}{autofillValue.markupNewWord}";
				return true;
			}
			else
			{
				newInputValue = virtualText;
				return false;
			}
		}
	}
}