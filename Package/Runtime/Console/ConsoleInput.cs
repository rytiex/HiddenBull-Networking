using System.Collections.Generic;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace HiddenBull.Console
{
    /// <summary>
    /// Minimal Input System helper for the console's named hotkeys. Resolves actions by name from
    /// the project's global Input Actions asset (InputSystem.actions), enabling and caching them.
    /// Replaces the former XUtils.XInput dependency (identical FindAction + WasPressed/Released).
    /// </summary>
    internal static class ConsoleInput
    {
#if ENABLE_INPUT_SYSTEM
        private static readonly Dictionary<string, InputAction> _cache = new();

        private static InputAction Resolve(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_cache.TryGetValue(name, out var cached)) return cached;

            var action = InputSystem.actions != null ? InputSystem.actions.FindAction(name) : null;
            if (action == null)
            {
                Debug.LogWarning($"[ConsoleInput] Action '{name}' not found.");
                return null;
            }
            if (!action.enabled) action.Enable();
            _cache[name] = action;
            return action;
        }

        public static bool GetButtonDown(string name)
        {
            var action = Resolve(name);
            return action != null && action.WasPressedThisFrame();
        }
        public static bool GetButtonUp(string name)
        {
            var action = Resolve(name);
            return action != null && action.WasReleasedThisFrame();
        }
#else
        public static bool GetButtonDown(string name) => false;
        public static bool GetButtonUp(string name) => false;
#endif
    }
}