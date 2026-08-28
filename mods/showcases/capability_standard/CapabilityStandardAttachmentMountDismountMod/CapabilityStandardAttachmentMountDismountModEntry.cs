using System.Threading.Tasks;
using CapabilityStandardAttachmentMountDismountMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace CapabilityStandardAttachmentMountDismountMod;

public sealed class CapabilityStandardAttachmentMountDismountModEntry : IMod
{
    public static readonly ServiceKey<AttachmentMountDemoState> DemoStateKey =
        new("CapabilityStandardAttachmentMountDismount.DemoState");

    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardAttachmentMountDismountMod] Loaded — 乘员上下车");
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine engine = ctx.Get(CoreServiceKeys.Engine)
                ?? throw new InvalidOperationException("乘员上下车开场需要引擎服务。");

            var state = new AttachmentMountDemoState();
            engine.SetService(DemoStateKey, state);
            ScreenOverlayBuffer overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
                ?? throw new InvalidOperationException("乘员上下车需要 ScreenOverlayBuffer。");
            engine.RegisterSystem(new AttachmentMountDemoSystem(engine, state), SystemGroup.InputCollection);
            engine.RegisterPresentationSystem(new AttachmentMountPresentationSystem(state, overlay));
            return Task.CompletedTask;
        });
    }

    public void OnUnload()
    {
    }
}
