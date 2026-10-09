using System.Threading.Tasks;
using CrowdSimulationS4DeployMod.Runtime;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Fields;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace CrowdSimulationS4DeployMod;

/// <summary>
/// S4 部署演示交互层(F03-b):会话本体由 CrowdSimulationRuntime 按地图聚焦激活(纯数据,
/// 与对拍共用一条装载链);本 mod 只挂呈现——HUD 覆盖层、V 分层视图(全场视觉投影器注册表,
/// 与 S3 演示同一通道)、路线通道预览/线框/脏 tile 闪烁、交互输入队列(呈现只入队,
/// 仿真 tick 在 PostMovement 组消费,先于会话步进)。会话晚于 GameStart 激活时,
/// 呈现层逐帧取服务,空会话不写场。
/// </summary>
public sealed class CrowdSimulationS4DeployModEntry : IMod
{
    private S4DeployDemoRuntime? _runtime;

    public void OnLoad(IModContext context)
    {
        context.Log("[CrowdSimulationS4DeployMod] Loaded.");
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine? engine = ctx.GetEngine();
            if (engine == null) return Task.CompletedTask;

            _runtime = new S4DeployDemoRuntime(engine);

            var registry = engine.GetService(CoreServiceKeys.GlobalFieldVisualProjectorRegistry)
                ?? new GlobalFieldVisualProjectorRegistry();
            registry.Register(new S4DeployDemoViewProjector(
                _runtime,
                () => engine.TryGetService(CoreServiceKeys.CrowdSimulationSession, out var s) ? s : null));
            engine.SetService(CoreServiceKeys.GlobalFieldVisualProjectorRegistry, registry);

            RouteVisualBuffer routeVisuals = engine.GetService(CoreServiceKeys.RouteVisualBuffer) ?? new RouteVisualBuffer();
            engine.SetService(CoreServiceKeys.RouteVisualBuffer, routeVisuals);

            var input = engine.GetService(CoreServiceKeys.InputBackend)
                ?? throw new System.InvalidOperationException("CrowdSimulationS4DeployMod 需要 InputBackend。");
            var rays = engine.GetService(CoreServiceKeys.ScreenRayProvider)
                ?? throw new System.InvalidOperationException("CrowdSimulationS4DeployMod 需要 ScreenRayProvider。");
            ScreenOverlayBuffer overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
                ?? throw new System.InvalidOperationException("CrowdSimulationS4DeployMod 需要 ScreenOverlayBuffer。");

            // 现场指令要在本 tick 的会话步进前落地:会话已激活时锚点插到会话步进系统之前;
            // 会话未激活(启动图非 crowd 图)时步进系统不存在,组尾注册即可——那种局面下
            // 指令只会被丢弃,顺序不影响行为。
            var interaction = new S4DeployDemoInteractionSystem(_runtime);
            if (engine.TryGetService(CoreServiceKeys.CrowdSimulationSession, out _))
            {
                engine.InsertSystemBeforeRequired<CrowdSimulationSessionTickSystem>(interaction, SystemGroup.PostMovement);
            }
            else
            {
                engine.RegisterSystem(interaction, SystemGroup.PostMovement);
            }

            engine.RegisterPresentationSystem(new S4DeployDemoPresentationSystem(
                _runtime, routeVisuals, overlay, input, rays,
                () => engine.TryGetService(CoreServiceKeys.ContinuousHeightmap, out IContinuousHeightmap? hm) ? hm : null,
                () => engine.TryGetService(CoreServiceKeys.CrowdSimulationRuntime, out var r) ? r : null,
                () => engine.TryGetService(CoreServiceKeys.CrowdSimulationSession, out var s) ? s : null));
            return Task.CompletedTask;
        });
    }

    public void OnUnload()
    {
    }
}
