using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using ConfigurableDataSchemaSharedMod.Runtime;
using ConfigurableDataSchemaSharedMod.Systems;

namespace ConfigurableDataSchemaSharedMod;

public sealed class ConfigurableDataSchemaSharedModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        var runtime = new ConfigurableDataSchemaRuntime();

        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            if (ctx.Get(CoreServiceKeys.Engine) is not GameEngine engine)
            {
                return Task.CompletedTask;
            }

            if (engine.GlobalContext.TryGetValue(ConfigurableDataSchemaIds.InstalledKey, out object? installedObj) &&
                installedObj is bool installed &&
                installed)
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext[ConfigurableDataSchemaIds.InstalledKey] = true;
            engine.GlobalContext[ConfigurableDataSchemaIds.RuntimeServiceKey] = runtime;
            engine.RegisterPresentationSystem(new ConfigurableDataSchemaPresentationSystem(engine, runtime));
            context.Log("[ConfigurableDataSchemaSharedMod] workbench systems registered.");
            return Task.CompletedTask;
        });

        context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
    }

    public void OnUnload()
    {
    }
}
