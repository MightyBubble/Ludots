using System.Threading.Tasks;
using CapabilityStandardAttachmentSettlementMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

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
            GameEngine engine = ctx.Get(CoreServiceKeys.Engine)
                ?? throw new InvalidOperationException("哨所静物开场需要引擎服务。");

            var state = new AttachmentSettlementDemoState();
            engine.SetService(DemoStateKey, state);
            ScreenOverlayBuffer overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
                ?? throw new InvalidOperationException("哨所静物需要 ScreenOverlayBuffer。");
            engine.RegisterSystem(new AttachmentSettlementDemoSystem(engine.World, state), SystemGroup.PostMovement);
            engine.RegisterPresentationSystem(new AttachmentSettlementPresentationSystem(state, overlay));
            return Task.CompletedTask;
        });
    }

    public void OnUnload()
    {
    }
}
