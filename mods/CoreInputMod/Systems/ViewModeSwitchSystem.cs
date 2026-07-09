using System.Collections.Generic;
using Arch.System;
using CoreInputMod.ViewMode;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;

namespace CoreInputMod.Systems
{
    public sealed class ViewModeSwitchSystem : ISystem<float>
    {
        public const string ViewModeHudEnabledKey = "CoreInputMod.ViewModeHudEnabled";

        private const string NextAction = "ViewModeNext";
        private const string PrevAction = "ViewModePrev";

        private readonly Dictionary<string, object> _globals;

        public ViewModeSwitchSystem(Dictionary<string, object> globals)
        {
            _globals = globals;
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            if (!_globals.TryGetValue(ViewModeManager.GlobalKey, out var managerObj) || managerObj is not ViewModeManager manager)
            {
                return;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out var inputObj) || inputObj is not IInputActionReader input)
            {
                return;
            }

            if (input.PressedThisFrame(NextAction))
            {
                manager.SwitchNext();
            }
            else if (input.PressedThisFrame(PrevAction))
            {
                manager.SwitchPrev();
            }
            else
            {
                // Later-registered modes are typically more specialized showcase/mod overrides.
                // When multiple actions are driven by the same physical key in active contexts,
                // prefer the most recently registered mode instead of letting base capabilities
                // steal the switch.
                for (int i = manager.Modes.Count - 1; i >= 0; i--)
                {
                    var mode = manager.Modes[i];
                    if (!string.IsNullOrEmpty(mode.SwitchActionId) && input.PressedThisFrame(mode.SwitchActionId))
                    {
                        manager.SwitchTo(mode.Id);
                        break;
                    }
                }
            }
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
