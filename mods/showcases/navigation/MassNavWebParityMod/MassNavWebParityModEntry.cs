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
        var runtime = new MassNavWebParityRuntime(context, LoadConfig(context));
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

    private static MassNavWebParityConfig LoadConfig(IModContext context)
    {
        using var stream = context.GetResource($"{context.ModId}:assets/MassNavWebParityConfig.json");
        return MassNavWebParityConfig.Load(stream);
    }
}
