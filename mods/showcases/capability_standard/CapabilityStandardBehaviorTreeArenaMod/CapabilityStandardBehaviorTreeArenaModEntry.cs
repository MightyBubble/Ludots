using System.Threading.Tasks;
using CapabilityStandardBehaviorTreeArenaMod.Runtime;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace CapabilityStandardBehaviorTreeArenaMod;

public sealed class CapabilityStandardBehaviorTreeArenaModEntry : IMod
{
    public static readonly ServiceKey<GraphShowcaseMetrics> MetricsKey =
        new("CapabilityStandardBehaviorTreeArena.Metrics");

    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardBehaviorTreeArenaMod] Loaded (BT-only showcase)");
        var runtime = new BehaviorTreeArenaRuntime();
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine? engine = ctx.GetEngine();
            if (engine == null) return Task.CompletedTask;
            engine.SetService(MetricsKey, runtime.Metrics);
            engine.RegisterSystem(new BehaviorTreeArenaSimulationSystem(engine, runtime), SystemGroup.PostMovement);
            return Task.CompletedTask;
        });
        context.OnEvent(GameEvents.MapLoaded, _ =>
        {
            runtime.EnsureWorld();
            return Task.CompletedTask;
        });
    }

    public void OnUnload()
    {
    }
}
