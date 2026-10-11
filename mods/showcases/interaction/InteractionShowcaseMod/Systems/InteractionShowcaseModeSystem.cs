using System.Collections.Generic;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using InteractionShowcaseMod;
using InteractionShowcaseMod.Runtime;

namespace InteractionShowcaseMod.Systems
{
    /// <summary>
    /// Polls the showcase-bound mode keys (F1-F5) and applies the corresponding cast mode via
    /// the active input order mapping; the Action key also swaps the camera to the follow profile.
    /// </summary>
    internal sealed class InteractionShowcaseModeSystem : ISystem<float>
    {
        private readonly Dictionary<string, object> _globals;

        public InteractionShowcaseModeSystem(Dictionary<string, object> globals)
        {
            _globals = globals;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float t) { }
        public void AfterUpdate(in float t) { }
        public void Dispose() { }

        public void Update(in float t)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out var inputObj) ||
                inputObj is not IInputActionReader input)
            {
                return;
            }

            string? modeId = null;
            if (input.PressedThisFrame(InteractionShowcaseIds.WowModeActionId))
            {
                modeId = InteractionShowcaseIds.WowModeId;
            }
            else if (input.PressedThisFrame(InteractionShowcaseIds.LolModeActionId))
            {
                modeId = InteractionShowcaseIds.LolModeId;
            }
            else if (input.PressedThisFrame(InteractionShowcaseIds.Sc2ModeActionId))
            {
                modeId = InteractionShowcaseIds.Sc2ModeId;
            }
            else if (input.PressedThisFrame(InteractionShowcaseIds.IndicatorModeActionId))
            {
                modeId = InteractionShowcaseIds.IndicatorModeId;
            }
            else if (input.PressedThisFrame(InteractionShowcaseIds.ActionModeActionId))
            {
                modeId = InteractionShowcaseIds.ActionModeId;
            }

            if (modeId != null &&
                _globals.TryGetValue(CoreServiceKeys.Engine.Name, out var engineObj) &&
                engineObj is GameEngine engine)
            {
                InteractionShowCastModes.TrySetActive(engine, modeId);
            }
        }
    }
}
