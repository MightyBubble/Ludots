using System;
using System.Collections.Generic;
using System.Reflection;
using Ludots.Core.Modding;

namespace CoreInputMod.ViewMode
{
    public static class ViewModeRegistrar
    {
        public static void RegisterFromVfs(
            IModContext ctx,
            Dictionary<string, object> globals,
            string? defaultModeId = null,
            string? sourceModId = null,
            bool activateWhenUnset = true)
        {
            if (!globals.TryGetValue(ViewModeManager.GlobalKey, out var managerObj) || managerObj == null)
            {
                ctx.Log($"[{ctx.ModId}] ViewModeManager not found in globals, skipping view mode registration.");
                return;
            }

            string sourceId = string.IsNullOrWhiteSpace(sourceModId) ? ctx.ModId : sourceModId;
            string uri = $"{sourceId}:assets/viewmodes.json";
            try
            {
                using var stream = ctx.VFS.GetStream(uri);
                var modes = ViewModeLoader.LoadFromStream(stream);
                if (!TryResolveManagerContract(managerObj, out var registerMethod, out var switchToMethod, out var activeModeProperty))
                {
                    ctx.Log($"[{ctx.ModId}] ViewModeManager contract mismatch, skipping view mode registration.");
                    return;
                }

                foreach (var mode in modes)
                {
                    var targetMode = CloneMode(mode, registerMethod.GetParameters()[0].ParameterType);
                    registerMethod.Invoke(managerObj, new[] { targetMode });
                }

                ctx.Log($"[{ctx.ModId}] Registered {modes.Count} view modes from {sourceId}.");
                if (activateWhenUnset && activeModeProperty.GetValue(managerObj) == null)
                {
                    if (!string.IsNullOrWhiteSpace(defaultModeId))
                    {
                        switchToMethod.Invoke(managerObj, new object[] { defaultModeId });
                    }
                    else if (modes.Count > 0)
                    {
                        switchToMethod.Invoke(managerObj, new object[] { modes[0].Id });
                    }
                }
            }
            catch (System.IO.FileNotFoundException)
            {
                ctx.Log($"[{ctx.ModId}] No viewmodes.json found, skipping.");
            }
        }

        private static bool TryResolveManagerContract(
            object manager,
            out MethodInfo registerMethod,
            out MethodInfo switchToMethod,
            out PropertyInfo activeModeProperty)
        {
            var managerType = manager.GetType();
            var register = managerType.GetMethod("Register", BindingFlags.Instance | BindingFlags.Public);
            var switchTo = managerType.GetMethod("SwitchTo", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(string) }, null);
            var activeMode = managerType.GetProperty("ActiveMode", BindingFlags.Instance | BindingFlags.Public);

            if (register == null || register.GetParameters().Length != 1 || switchTo == null || activeMode == null)
            {
                registerMethod = null!;
                switchToMethod = null!;
                activeModeProperty = null!;
                return false;
            }

            registerMethod = register;
            switchToMethod = switchTo;
            activeModeProperty = activeMode;
            return true;
        }

        private static object CloneMode(ViewModeConfig source, Type targetType)
        {
            var target = Activator.CreateInstance(targetType)
                ?? throw new InvalidOperationException($"Failed to create {targetType.FullName}.");
            var sourceType = typeof(ViewModeConfig);
            var targetProperties = targetType.GetProperties(BindingFlags.Instance | BindingFlags.Public);

            for (int i = 0; i < targetProperties.Length; i++)
            {
                var targetProperty = targetProperties[i];
                if (!targetProperty.CanWrite)
                {
                    continue;
                }

                var sourceProperty = sourceType.GetProperty(targetProperty.Name, BindingFlags.Instance | BindingFlags.Public);
                if (sourceProperty == null || !sourceProperty.CanRead)
                {
                    continue;
                }

                if (!targetProperty.PropertyType.IsAssignableFrom(sourceProperty.PropertyType))
                {
                    continue;
                }

                targetProperty.SetValue(target, sourceProperty.GetValue(source));
            }

            return target;
        }
    }
}
