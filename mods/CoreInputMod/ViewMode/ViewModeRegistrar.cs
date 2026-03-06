using System.Collections.Generic;
using Ludots.Core.Modding;

namespace CoreInputMod.ViewMode
{
    /// <summary>
    /// Utility for mods to load viewmodes.json and register modes with ViewModeManager.
    /// Call from GameStart trigger after CoreInputMod is installed.
    /// </summary>
    public static class ViewModeRegistrar
    {
        public static void RegisterFromVfs(IModContext ctx, Dictionary<string, object> globals, string defaultModeId = null)
        {
            if (!globals.TryGetValue(ViewModeManager.GlobalKey, out var obj) || obj is not ViewModeManager manager)
            {
                ctx.Log($"[{ctx.ModId}] ViewModeManager not found in globals, skipping viewmode registration.");
                return;
            }

            string uri = $"{ctx.ModId}:assets/viewmodes.json";
            try
            {
                using var stream = ctx.VFS.GetStream(uri);
                var modes = ViewModeLoader.LoadFromStream(stream);
                foreach (var mode in modes)
                    manager.Register(mode);

                ctx.Log($"[{ctx.ModId}] Registered {modes.Count} view modes.");

                if (defaultModeId != null && manager.ActiveMode == null)
                    manager.SwitchTo(defaultModeId);
                else if (manager.ActiveMode == null && modes.Count > 0)
                    manager.SwitchTo(modes[0].Id);
            }
            catch (System.IO.FileNotFoundException)
            {
                ctx.Log($"[{ctx.ModId}] No viewmodes.json found, skipping.");
            }
        }
    }
}
