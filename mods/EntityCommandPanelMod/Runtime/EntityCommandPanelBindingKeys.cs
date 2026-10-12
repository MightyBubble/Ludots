using System;
using System.Collections.Generic;
using Ludots.Core.Engine;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Scripting;

namespace EntityCommandPanelMod.Runtime
{
    /// <summary>
    /// Hotkey text for a slot that declared an input binding. The active control scheme's
    /// contexts win; a game with no scheme uses <c>startupInputContexts</c>.
    /// </summary>
    public static class EntityCommandPanelBindingKeys
    {
        public static string Resolve(GameEngine engine, string actionId, bool hasBindingInt, int bindingInt)
        {
            ArgumentNullException.ThrowIfNull(engine);
            ControlSchemeRuntime schemes = engine.GetService(CoreServiceKeys.ControlSchemeRuntime)
                ?? throw new InvalidOperationException(
                    "LUDOTS_INPUT_BINDING_KEY_SCHEME_RUNTIME_ABSENT: entity command panel key labels require ControlSchemeRuntime.");
            int? value = hasBindingInt ? bindingInt : null;
            if (schemes.ActiveSchemeId != 0)
            {
                return schemes.ResolveActiveBindingKey(actionId, value);
            }

            IReadOnlyList<string> startup = engine.MergedConfig.StartupInputContexts;
            if (startup.Count == 0)
            {
                throw new InvalidOperationException(
                    "LUDOTS_INPUT_BINDING_KEY_SCHEME_INACTIVE: no control scheme is active and startupInputContexts is empty.");
            }

            return schemes.ResolveBindingKey(startup, "startupInputContexts", actionId, value);
        }
    }
}
