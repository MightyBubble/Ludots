using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using StaticObstaclePhysicsShowcaseMod.Runtime;

namespace StaticObstaclePhysicsShowcaseMod;

public sealed class StaticObstaclePhysicsShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[StaticObstaclePhysicsShowcaseMod] Loaded");
        AttributeRegistry.Register("Durability");
        var runtime = new StaticObstaclePhysicsShowcaseRuntime();
        context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
    }

    public void OnUnload()
    {
    }
}
