using System.Threading.Tasks;
using CapabilityStandardAbilityGraphSandboxMod.Runtime;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace CapabilityStandardAbilityGraphSandboxMod;

public sealed class CapabilityStandardAbilityGraphSandboxModEntry : IMod
{
    public static readonly ServiceKey<GraphShowcaseMetrics> MetricsKey =
        new("CapabilityStandardAbilityGraphSandbox.Metrics");

    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardAbilityGraphSandboxMod] Loaded (sandbox-owned Effect graphs)");
        var runtime = new AbilityGraphSandboxRuntime();
        runtime.BindStandaloneFromModAssets();
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine? engine = ctx.GetEngine();
            if (engine == null) return Task.CompletedTask;
            engine.SetService(MetricsKey, runtime.Metrics);
            runtime.BindStageVisuals(GraphOpsStageVisuals.FromEngine(engine));
            var debugDraw = new DebugDrawCommandBuffer();
            engine.SetService(CoreServiceKeys.DebugDrawCommandBuffer, debugDraw);
            ScreenOverlayBuffer overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
                ?? throw new InvalidOperationException("Ability Graph Sandbox requires ScreenOverlayBuffer.");
            engine.RegisterSystem(new AbilityGraphSandboxSimulationSystem(engine, runtime), SystemGroup.PostMovement);
            engine.RegisterPresentationSystem(new AbilityGraphSandboxPresentationSystem(runtime, debugDraw, overlay));
            return Task.CompletedTask;
        });
        context.OnEvent(GameEvents.MapLoaded, _ => { runtime.EnsureWorld(); return Task.CompletedTask; });
    }

    public void OnUnload() { }
}
