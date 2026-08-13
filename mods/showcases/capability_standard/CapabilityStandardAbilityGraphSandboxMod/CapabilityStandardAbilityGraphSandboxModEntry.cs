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
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine? engine = ctx.GetEngine();
            if (engine == null) return Task.CompletedTask;
            runtime.BindStandaloneFromModAssets();
            engine.SetService(MetricsKey, runtime.Metrics);
            var debugDraw = new DebugDrawCommandBuffer();
            engine.SetService(CoreServiceKeys.DebugDrawCommandBuffer, debugDraw);
            engine.TryGetService(CoreServiceKeys.ScreenOverlayBuffer, out ScreenOverlayBuffer overlay);
            engine.RegisterSystem(new AbilityGraphSandboxSimulationSystem(engine, runtime), SystemGroup.PostMovement);
            engine.RegisterPresentationSystem(new AbilityGraphSandboxPresentationSystem(runtime, debugDraw, overlay));
            return Task.CompletedTask;
        });
        context.OnEvent(GameEvents.MapLoaded, _ => { runtime.EnsureWorld(); return Task.CompletedTask; });
    }

    public void OnUnload() { }
}
