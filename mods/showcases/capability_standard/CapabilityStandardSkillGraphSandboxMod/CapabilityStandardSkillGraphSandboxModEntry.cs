using System.Threading.Tasks;
using CapabilityStandardGraphBehaviorCommon;
using CapabilityStandardSkillGraphSandboxMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace CapabilityStandardSkillGraphSandboxMod;

public sealed class CapabilityStandardSkillGraphSandboxModEntry : IMod
{
    public static readonly ServiceKey<GraphShowcaseMetrics> MetricsKey =
        new("CapabilityStandardSkillGraphSandbox.Metrics");

    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardSkillGraphSandboxMod] Loaded (Skill-graph-only showcase)");
        var runtime = new SkillGraphSandboxRuntime();
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine? engine = ctx.GetEngine();
            if (engine == null) return Task.CompletedTask;
            engine.SetService(MetricsKey, runtime.Metrics);
            engine.RegisterSystem(new SkillGraphSandboxSimulationSystem(engine, runtime), SystemGroup.PostMovement);
            return Task.CompletedTask;
        });
        context.OnEvent(GameEvents.MapLoaded, _ => { runtime.EnsureWorld(); return Task.CompletedTask; });
    }

    public void OnUnload() { }
}
