using System;
using System.Collections.Generic;
using Arch.Core;
using CoreInputMod.ViewMode;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Mathematics;

namespace CoreInputMod
{
    public static class CoreInputRuntimeServices
    {
        public static bool TryGetEntitySelectionCallbacks(
            GameEngine engine,
            out List<Action<WorldCmInt2, Entity>> callbacks)
        {
            return engine.TryGetService(CoreInputServiceKeys.EntitySelectionCallbacks, out callbacks);
        }

        public static bool TryGetSelectionTriggeredCallbacks(
            GameEngine engine,
            out List<Action<SelectionRequest, WorldCmInt2>> callbacks)
        {
            return engine.TryGetService(CoreInputServiceKeys.SelectionTriggeredCallbacks, out callbacks);
        }

        public static bool TryGetViewModeManager(GameEngine engine, out ViewModeManager manager)
        {
            return engine.TryGetService(CoreInputServiceKeys.ViewModeManager, out manager);
        }

        public static ViewModeManager? GetViewModeManager(GameEngine engine)
        {
            return engine.GetService(CoreInputServiceKeys.ViewModeManager);
        }

        public static string? GetActiveViewModeId(GameEngine engine)
        {
            return engine.GetService(CoreInputServiceKeys.ActiveViewModeId);
        }
    }
}
