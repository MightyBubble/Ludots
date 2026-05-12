using System.Threading.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using MassNavigationMod.Runtime;

namespace MassNavigationMod;

public sealed class MassNavigationModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        MassNavigationComponentAuthoring.Register(context.ModId);
        var runtime = new MassNavigationRuntime(context);
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            if (ctx.GetEngine() is { } engine)
            {
                runtime.EnsureSystemsInstalled(engine);
            }

            return Task.CompletedTask;
        });
        context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapSuspended, runtime.HandleMapSuspendedAsync);
        context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
    }

    public void OnUnload()
    {
    }
}

