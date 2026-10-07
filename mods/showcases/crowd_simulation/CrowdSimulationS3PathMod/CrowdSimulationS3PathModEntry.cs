using System.Threading.Tasks;
using CrowdSimulationS3PathMod.Runtime;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Fields;
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

            // 呈现走正式基建:流场经投影器注册表进全场视觉缓冲(方向图),
            // 折线与起终标记进路线视觉缓冲——两条都是 gameplay 通道,不是调试绘制
            var flowSource = new CrowdFlowFieldVisualSource
            {
                CellCount = _runtime.Nav.CellCount,
                CellSizeCm = _runtime.Config.NavCellSizeCm,
            };
            _runtime.FlowVisual = flowSource;
            var registry = engine.GetService(CoreServiceKeys.GlobalFieldVisualProjectorRegistry)
                ?? new GlobalFieldVisualProjectorRegistry();
            registry.Register(new CrowdFlowFieldVisualProjector(flowSource));
            engine.SetService(CoreServiceKeys.GlobalFieldVisualProjectorRegistry, registry);

            var routeVisuals = new RouteVisualBuffer();
            engine.SetService(CoreServiceKeys.RouteVisualBuffer, routeVisuals);

            ScreenOverlayBuffer overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
                ?? throw new System.InvalidOperationException("CrowdSimulationS3PathMod 需要 ScreenOverlayBuffer。");
            engine.RegisterSystem(new S3PathDemoSimulationSystem(_runtime), SystemGroup.PostMovement);
            engine.RegisterPresentationSystem(new S3PathDemoPresentationSystem(_runtime, routeVisuals, overlay));
            return Task.CompletedTask;
        });
    }

    public void OnUnload()
    {
        _runtime?.Dispose();
    }
}
