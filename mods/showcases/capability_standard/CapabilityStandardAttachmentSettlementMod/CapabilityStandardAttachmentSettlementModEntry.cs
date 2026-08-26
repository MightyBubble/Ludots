using System.Threading.Tasks;
using CapabilityStandardAttachmentSettlementMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardAttachmentSettlementMod;

public sealed class CapabilityStandardAttachmentSettlementModEntry : IMod
{
    public static readonly ServiceKey<AttachmentSettlementDemoState> DemoStateKey =
        new("CapabilityStandardAttachmentSettlement.DemoState");

    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardAttachmentSettlementMod] Loaded — 哨所静物");
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine? engine = ctx.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            var state = new AttachmentSettlementDemoState();
            engine.SetService(DemoStateKey, state);
            var debugDraw = new DebugDrawCommandBuffer();
            engine.SetService(CoreServiceKeys.DebugDrawCommandBuffer, debugDraw);
            ScreenOverlayBuffer overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
                ?? throw new InvalidOperationException("Settlement showcase requires ScreenOverlayBuffer.");
            engine.RegisterSystem(new AttachmentSettlementDemoSystem(engine.World, state), SystemGroup.PostMovement);
            engine.RegisterPresentationSystem(new AttachmentSettlementPresentationSystem(state, debugDraw, overlay));
            return Task.CompletedTask;
        });
    }

    public void OnUnload()
    {
    }
}
