using System.Collections.Generic;
using Ludots.Core.Modding;

namespace CoreInputMod.ViewMode
{
    public static class ViewModeRegistrar
    {
        public static void RegisterFromVfs(IModContext ctx, Dictionary<string, object> globals, string? defaultModeId = null)
        {
            if (!globals.TryGetValue(ViewModeManager.GlobalKey, out var managerObj) || managerObj is not ViewModeManager manager)
            {
                ctx.Log($"[{ctx.ModId}] ViewModeManager not found in globals, skipping view mode registration.");
                return;
            }

            string uri = $"{ctx.ModId}:assets/viewmodes.json";
            try
            {
                using var stream = ctx.VFS.GetStream(uri);
                var modes = ViewModeLoader.LoadFromStream(stream);
                foreach (var mode in modes)
                {
                    manager.Register(mode);
                }

                ctx.Log($"[{ctx.ModId}] Registered {modes.Count} view modes.");
                if (manager.ActiveMode == null)
                {
                    if (!string.IsNullOrWhiteSpace(defaultModeId))
                    {
                        manager.SwitchTo(defaultModeId);
                    }
                    else if (modes.Count > 0)
                    {
                        manager.SwitchTo(modes[0].Id);
                    }
                }
            }
            catch (System.IO.FileNotFoundException)
            {
                ctx.Log($"[{ctx.ModId}] No viewmodes.json found, skipping.");
            }
        }
    }
}
