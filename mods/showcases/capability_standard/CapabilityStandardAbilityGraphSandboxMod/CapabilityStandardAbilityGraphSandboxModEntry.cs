using System.Threading.Tasks;
using CapabilityStandardGraphBehaviorCommon;
using CapabilityStandardAbilityGraphSandboxMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace CapabilityStandardAbilityGraphSandboxMod;

public sealed class CapabilityStandardAbilityGraphSandboxModEntry : IMod
{
    public static readonly ServiceKey<GraphShowcaseMetrics> MetricsKey =
        new("CapabilityStandardAbilityGraphSandbox.Metrics");

    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardAbilityGraphSandboxMod] Loaded (Ability/Effect-graph-only showcase)");
        var runtime = new AbilityGraphSandboxRuntime();
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine? engine = ctx.GetEngine();
            if (engine == null) return Task.CompletedTask;
            engine.SetService(MetricsKey, runtime.Metrics);
            engine.RegisterSystem(new AbilityGraphSandboxSimulationSystem(engine, runtime), SystemGroup.PostMovement);
            return Task.CompletedTask;
        });
        context.OnEvent(GameEvents.MapLoaded, _ => { runtime.EnsureWorld(); return Task.CompletedTask; });
    }

    public void OnUnload() { }
}
