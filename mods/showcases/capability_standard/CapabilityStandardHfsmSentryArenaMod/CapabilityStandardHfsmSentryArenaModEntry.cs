using System.Threading.Tasks;
using CapabilityStandardGraphBehaviorCommon;
using CapabilityStandardHfsmSentryArenaMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace CapabilityStandardHfsmSentryArenaMod;

public sealed class CapabilityStandardHfsmSentryArenaModEntry : IMod
{
    public static readonly ServiceKey<GraphShowcaseMetrics> MetricsKey =
        new("CapabilityStandardHfsmSentryArena.Metrics");

    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardHfsmSentryArenaMod] Loaded (HFSM-only showcase)");
        var runtime = new HfsmSentryArenaRuntime();
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine? engine = ctx.GetEngine();
            if (engine == null) return Task.CompletedTask;
            engine.SetService(MetricsKey, runtime.Metrics);
            engine.RegisterSystem(new HfsmSentryArenaSimulationSystem(engine, runtime), SystemGroup.PostMovement);
            return Task.CompletedTask;
        });
        context.OnEvent(GameEvents.MapLoaded, _ => { runtime.EnsureWorld(); return Task.CompletedTask; });
    }

    public void OnUnload() { }
}
