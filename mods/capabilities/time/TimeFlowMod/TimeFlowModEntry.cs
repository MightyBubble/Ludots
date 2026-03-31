using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace TimeFlowMod;

public sealed class TimeFlowModEntry : IMod
{
    private const string InstalledKey = "TimeFlowMod.Installed";

    public void OnLoad(IModContext context)
    {
        context.Log("[TimeFlowMod] Loaded");
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine? engine = ctx.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            if (engine.GlobalContext.TryGetValue(InstalledKey, out object? installedObj) &&
                installedObj is bool installed &&
                installed)
            {
                return Task.CompletedTask;
            }

            TimeFlowConfig config = new TimeFlowConfigLoader(engine.ConfigPipeline).Load(
                engine.ConfigCatalog,
                engine.ConfigConflictReport);
            TimeFlowProfileRegistry registry = TimeFlowProfileRegistry.FromConfig(config);
            TimeFlowService service = new(engine, registry);

            engine.GlobalContext[InstalledKey] = true;
            engine.SetService(TimeFlowServiceKeys.Registry, registry);
            engine.SetService(TimeFlowServiceKeys.Service, service);

            context.Log($"[TimeFlowMod] Installed {registry.Count} time-flow profiles.");
            return Task.CompletedTask;
        });
    }

    public void OnUnload()
    {
    }
}
