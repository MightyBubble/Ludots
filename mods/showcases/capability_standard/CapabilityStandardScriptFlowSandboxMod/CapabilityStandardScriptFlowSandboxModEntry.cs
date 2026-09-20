using System.Threading.Tasks;
using CapabilityStandardGraphBehaviorCommon;
using CapabilityStandardScriptFlowSandboxMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardScriptFlowSandboxMod;

public sealed class CapabilityStandardScriptFlowSandboxModEntry : IMod
{
    public static readonly ServiceKey<GraphShowcaseMetrics> MetricsKey =
        new("CapabilityStandardScriptFlowSandbox.Metrics");

    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardScriptFlowSandboxMod] Loaded (Script from ActionLib)");
        var runtime = new ScriptFlowSandboxRuntime();
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine? engine = ctx.GetEngine();
            if (engine == null) return Task.CompletedTask;
            runtime.Bind(
                engine.GetService(CoreServiceKeys.GraphProgramRegistry),
                engine.GetService(CoreServiceKeys.GraphActionCatalog));
            engine.SetService(MetricsKey, runtime.Metrics);
            var debugDraw = new DebugDrawCommandBuffer();
            engine.SetService(CoreServiceKeys.DebugDrawCommandBuffer, debugDraw);
            engine.RegisterSystem(new ScriptFlowSandboxSimulationSystem(engine, runtime), SystemGroup.PostMovement);
            engine.RegisterPresentationSystem(new ScriptFlowSandboxPresentationSystem(runtime, debugDraw));
            return Task.CompletedTask;
        });
        context.OnEvent(GameEvents.MapLoaded, _ => { runtime.EnsureWorld(); return Task.CompletedTask; });
    }

    public void OnUnload() { }
}
