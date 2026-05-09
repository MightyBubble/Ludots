using System.Threading.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using MassNavWebParityMod.Runtime;

namespace MassNavWebParityMod;

public sealed class MassNavWebParityModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        MassNavWebParityComponentAuthoring.Register();
        var runtime = new MassNavWebParityRuntime(context);
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
