using System;
using Ludots.Core.Engine;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Scripting;

namespace EntityCommandPanelMod.Runtime
{
    /// <summary>
    /// Hotkey text for a slot that declared an input binding. The active control scheme's
    /// contexts are the only lookup. A seat with no scheme is a load error, so an inactive
    /// scheme here is the same error.
    /// </summary>
    public static class EntityCommandPanelBindingKeys
    {
        public static string Resolve(GameEngine engine, string actionId, bool hasBindingInt, int bindingInt)
        {
            ArgumentNullException.ThrowIfNull(engine);
            ControlSchemeRuntime schemes = engine.GetService(CoreServiceKeys.ControlSchemeRuntime)
                ?? throw new InvalidOperationException(
                    "LUDOTS_INPUT_BINDING_KEY_SCHEME_RUNTIME_ABSENT: entity command panel key labels require ControlSchemeRuntime.");
            if (schemes.ActiveSchemeId == 0)
            {
                throw new InvalidOperationException(
                    "LUDOTS_INPUT_BINDING_KEY_SCHEME_INACTIVE: entity command panel key labels require an active control scheme.");
            }

            int? value = hasBindingInt ? bindingInt : null;
            return schemes.ResolveActiveBindingKey(actionId, value);
        }
    }
}
