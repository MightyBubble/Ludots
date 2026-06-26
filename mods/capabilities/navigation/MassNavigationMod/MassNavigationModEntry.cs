using System.Threading.Tasks;
using Ludots.Core.Layers;
using Ludots.Core.MassCrowd.Runtime;
using Ludots.Core.MassCrowd;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using MassNavigationMod.Systems;
using MassNavigationMod.UI;

namespace MassNavigationMod;

public sealed class MassNavigationModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        LayerRegistry.Register(MassNavigationLayerNames.Agent);

        var runtime = new MassNavigationRuntime(context);
        var panelPresenter = new MassNavigationPanelPresenter();
        bool panelSystemInstalled = false;
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            if (ctx.GetEngine() is { } engine)
            {
                if (!panelSystemInstalled)
                {
                    engine.RegisterPresentationSystem(new MassNavigationPanelPresentationSystem(engine, panelPresenter));
                    panelSystemInstalled = true;
                }
            }

            return Task.CompletedTask;
        });
        context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapSuspended, ctx =>
        {
            if (ctx.GetEngine() is { } engine)
            {
                panelPresenter.ClearPanelIfOwned(engine);
            }

            return runtime.HandleMapSuspendedAsync(ctx);
        });
        context.OnEvent(GameEvents.MapUnloaded, ctx =>
        {
            if (ctx.GetEngine() is { } engine)
            {
                panelPresenter.ClearPanelIfOwned(engine);
            }

            return runtime.HandleMapUnloadedAsync(ctx);
        });
    }

    public void OnUnload()
    {
    }
}
