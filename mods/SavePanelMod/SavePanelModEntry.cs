using System.Threading.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using SavePanelMod.Runtime;
using SavePanelMod.Systems;
using SavePanelMod.UI;

namespace SavePanelMod;

public sealed class SavePanelModEntry : IMod
{
    private const string InstalledKey = "SavePanelMod.Installed";

    public static ServiceKey<SavePanelRuntime> RuntimeKey { get; } = new("SavePanelRuntime");

    public void OnLoad(IModContext context)
    {
        context.Log("[SavePanelMod] Loaded");
        context.OnEvent(GameEvents.GameStart, ctx => InstallAsync(context, ctx));
    }

    public void OnUnload()
    {
    }

    private static Task InstallAsync(IModContext modContext, ScriptContext context)
    {
        var engine = context.GetEngine();
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

        engine.GlobalContext[InstalledKey] = true;

        var runtime = new SavePanelRuntime();
        runtime.BindEngine(engine);
        var controller = new SavePanelController(runtime);
        engine.SetService(RuntimeKey, runtime);
        engine.RegisterPresentationSystem(new SavePanelPresentationSystem(engine, runtime, controller));

        modContext.Log("[SavePanelMod] Installed save panel (ShowPanel panel.save / F5).");
        return Task.CompletedTask;
    }
}
