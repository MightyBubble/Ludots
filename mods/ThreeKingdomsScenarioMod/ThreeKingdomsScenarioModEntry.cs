using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using ThreeKingdomsScenarioMod.Runtime;
using ThreeKingdomsScenarioMod.Triggers;

namespace ThreeKingdomsScenarioMod;

public sealed class ThreeKingdomsScenarioModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[ThreeKingdomsScenarioMod] Loaded");
        ThreeKingdomsScenarioComponentAuthoring.Register();

        var runtime = new ThreeKingdomsScenarioRuntime();
        context.OnEvent(GameEvents.GameStart, new InstallThreeKingdomsScenarioOnGameStartTrigger(context, runtime).ExecuteAsync);
        context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
    }

    public void OnUnload()
    {
    }
}
