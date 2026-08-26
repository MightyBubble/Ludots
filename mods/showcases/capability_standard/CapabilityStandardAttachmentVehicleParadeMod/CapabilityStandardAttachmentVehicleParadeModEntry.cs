using System.Threading.Tasks;
using CapabilityStandardAttachmentVehicleParadeMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardAttachmentVehicleParadeMod;

public sealed class CapabilityStandardAttachmentVehicleParadeModEntry : IMod
{
    public static readonly ServiceKey<AttachmentVehicleParadeDemoState> DemoStateKey =
        new("CapabilityStandardAttachmentVehicleParade.DemoState");

    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardAttachmentVehicleParadeMod] Loaded — 装甲阅兵");
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine engine = ctx.Get(CoreServiceKeys.Engine)
                ?? throw new InvalidOperationException("装甲阅兵开场需要引擎服务。");

            var state = new AttachmentVehicleParadeDemoState();
            engine.SetService(DemoStateKey, state);
            var debugDraw = new DebugDrawCommandBuffer();
            engine.SetService(CoreServiceKeys.DebugDrawCommandBuffer, debugDraw);
            ScreenOverlayBuffer overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
                ?? throw new InvalidOperationException("装甲阅兵需要 ScreenOverlayBuffer。");
            engine.RegisterSystem(new AttachmentVehicleParadeDemoSystem(engine.World, state), SystemGroup.InputCollection);
            engine.RegisterPresentationSystem(new AttachmentVehicleParadePresentationSystem(state, debugDraw, overlay));
            return Task.CompletedTask;
        });
    }

    public void OnUnload()
    {
    }
}
