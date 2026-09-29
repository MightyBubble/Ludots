using CoreInputMod.ViewMode;
using Ludots.Core.Scripting;

namespace CoreInputMod
{
    public static class CoreInputServiceKeys
    {
        public static readonly ServiceKey<bool> Installed = new("CoreInputMod.Installed");
        public static readonly ServiceKey<ViewModeManager> ViewModeManager = new(ViewMode.ViewModeManager.GlobalKey);
        public static readonly ServiceKey<string> ActiveViewModeId = new(ViewMode.ViewModeManager.ActiveModeIdKey);
    }
}
