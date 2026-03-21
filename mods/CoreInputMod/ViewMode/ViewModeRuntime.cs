using System;
using System.Collections.Generic;
using Ludots.Core.Scripting;

namespace CoreInputMod.ViewMode
{
    public static class ViewModeRuntime
    {
        public static bool TrySwitchTo(Dictionary<string, object> globals, string modeId)
        {
            if (globals == null ||
                string.IsNullOrWhiteSpace(modeId) ||
                !TryGetManager(globals, out object manager))
            {
                return false;
            }

            var switchTo = manager.GetType().GetMethod("SwitchTo", new[] { typeof(string) });
            if (switchTo == null)
            {
                return false;
            }

            return switchTo.Invoke(manager, new object[] { modeId }) as bool? ?? false;
        }

        public static bool TryClearActiveMode(Dictionary<string, object> globals)
        {
            if (globals == null || !TryGetManager(globals, out object manager))
            {
                return false;
            }

            var clear = manager.GetType().GetMethod("ClearActiveMode", Type.EmptyTypes);
            if (clear == null)
            {
                return false;
            }

            clear.Invoke(manager, null);
            return true;
        }

        public static bool TryGetActiveModeId(Dictionary<string, object> globals, out string modeId)
        {
            modeId = string.Empty;
            if (globals == null)
            {
                return false;
            }

            if (globals.TryGetValue(ViewModeManager.ActiveModeIdKey, out var activeModeObj) &&
                activeModeObj is string activeModeId &&
                !string.IsNullOrWhiteSpace(activeModeId))
            {
                modeId = activeModeId;
                return true;
            }

            if (!TryGetManager(globals, out object manager))
            {
                return false;
            }

            var activeModeProperty = manager.GetType().GetProperty("ActiveMode");
            object? activeMode = activeModeProperty?.GetValue(manager);
            if (activeMode == null)
            {
                return false;
            }

            var idProperty = activeMode.GetType().GetProperty("Id");
            if (idProperty?.GetValue(activeMode) is not string reflectedModeId ||
                string.IsNullOrWhiteSpace(reflectedModeId))
            {
                return false;
            }

            modeId = reflectedModeId;
            return true;
        }

        private static bool TryGetManager(Dictionary<string, object> globals, out object manager)
        {
            if (globals.TryGetValue(ViewModeManager.GlobalKey, out var managerObj) &&
                managerObj != null)
            {
                manager = managerObj;
                return true;
            }

            manager = null!;
            return false;
        }
    }
}
