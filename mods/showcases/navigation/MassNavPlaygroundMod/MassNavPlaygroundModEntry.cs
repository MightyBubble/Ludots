using System.Threading.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using MassNavPlaygroundMod.Runtime;

namespace MassNavPlaygroundMod;

public sealed class MassNavPlaygroundModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        var runtime = new MassNavPlaygroundRuntime(context);
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
        context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
    }

    public void OnUnload()
    {
    }
}
