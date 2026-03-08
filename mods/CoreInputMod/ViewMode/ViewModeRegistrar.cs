using System;
using System.Collections.Generic;
using System.Text.Json;
using Ludots.Core.Modding;

namespace CoreInputMod.ViewMode
{
    public static class ViewModeRegistrar
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public static void RegisterFromVfs(IModContext ctx, Dictionary<string, object> globals, string? defaultModeId = null)
        {
            if (ctx == null || globals == null)
            {
                return;
            }

            if (!globals.TryGetValue(ViewModeManager.GlobalKey, out var managerObj) || managerObj is not ViewModeManager manager)
            {
                return;
            }

            try
            {
                using var stream = ctx.VFS.GetStream($"{ctx.ModId}:assets/viewmodes.json");
                var modes = JsonSerializer.Deserialize<List<ViewModeConfig>>(stream, JsonOptions);
                if (modes == null)
                {
                    return;
                }

                foreach (var mode in modes)
                {
                    if (mode != null)
                    {
                        manager.Register(mode);
                    }
                }

                if (!string.IsNullOrWhiteSpace(defaultModeId))
                {
                    manager.SwitchTo(defaultModeId);
                }
                else if (manager.Modes.Count > 0)
                {
                    manager.SwitchTo(manager.Modes[0].Id);
                }
            }
            catch
            {
            }
        }
    }
}
