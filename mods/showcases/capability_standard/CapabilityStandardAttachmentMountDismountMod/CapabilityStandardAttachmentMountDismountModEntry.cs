using System.Threading.Tasks;
using CapabilityStandardAttachmentMountDismountMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

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
            GameEngine? engine = ctx.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            var state = new AttachmentMountDemoState();
            engine.SetService(DemoStateKey, state);
            var debugDraw = new DebugDrawCommandBuffer();
            engine.SetService(CoreServiceKeys.DebugDrawCommandBuffer, debugDraw);
            ScreenOverlayBuffer overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
                ?? throw new InvalidOperationException("Mount showcase requires ScreenOverlayBuffer.");
            engine.RegisterSystem(new AttachmentMountDemoSystem(engine, state), SystemGroup.InputCollection);
            engine.RegisterPresentationSystem(new AttachmentMountPresentationSystem(state, debugDraw, overlay));
            return Task.CompletedTask;
        });
    }

    public void OnUnload()
    {
    }
}
