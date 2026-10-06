using CoreInputMod.ViewMode;
using Ludots.Core.Engine;

namespace CoreInputMod
{
    public static class CoreInputRuntimeServices
    {
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
