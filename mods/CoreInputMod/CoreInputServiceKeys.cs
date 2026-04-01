using System;
using System.Collections.Generic;
using Arch.Core;
using CoreInputMod.Triggers;
using CoreInputMod.ViewMode;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Mathematics;
using Ludots.Core.Scripting;

namespace CoreInputMod
{
    public static class CoreInputServiceKeys
    {
        public static readonly ServiceKey<bool> Installed = new("CoreInputMod.Installed");
        public static readonly ServiceKey<List<Action<WorldCmInt2, Entity>>> EntitySelectionCallbacks =
            new(InstallCoreInputOnGameStartTrigger.EntitySelectionCallbacksKey);
        public static readonly ServiceKey<List<Action<SelectionRequest, WorldCmInt2>>> SelectionTriggeredCallbacks =
            new(InstallCoreInputOnGameStartTrigger.SelectionTriggeredCallbacksKey);
        public static readonly ServiceKey<ViewModeManager> ViewModeManager = new(ViewMode.ViewModeManager.GlobalKey);
        public static readonly ServiceKey<string> ActiveViewModeId = new(ViewMode.ViewModeManager.ActiveModeIdKey);
    }
}
