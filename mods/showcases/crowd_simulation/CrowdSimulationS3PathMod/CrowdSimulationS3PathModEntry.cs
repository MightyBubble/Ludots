using System.Threading.Tasks;
using CrowdSimulationS3PathMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace CrowdSimulationS3PathMod;

public sealed class CrowdSimulationS3PathModEntry : IMod
{
    private S3PathDemoRuntime? _runtime;

    public void OnLoad(IModContext context)
    {
        context.Log("[CrowdSimulationS3PathMod] Loaded.");
        _runtime = S3PathDemoRuntime.Load(context);
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine? engine = ctx.GetEngine();
            if (engine == null || _runtime == null) return Task.CompletedTask;
            var debugDraw = new DebugDrawCommandBuffer();
            engine.SetService(CoreServiceKeys.DebugDrawCommandBuffer, debugDraw);
            ScreenOverlayBuffer overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
                ?? throw new System.InvalidOperationException("CrowdSimulationS3PathMod 需要 ScreenOverlayBuffer。");
            engine.RegisterSystem(new S3PathDemoSimulationSystem(_runtime), SystemGroup.PostMovement);
            engine.RegisterPresentationSystem(new S3PathDemoPresentationSystem(_runtime, debugDraw, overlay));
            return Task.CompletedTask;
        });
    }

    public void OnUnload()
    {
        _runtime?.Dispose();
    }
}
