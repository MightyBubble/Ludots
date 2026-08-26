using System.Threading.Tasks;
using CapabilityStandardGraphFormalTextShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace CapabilityStandardGraphFormalTextShowcaseMod;

public sealed class CapabilityStandardGraphFormalTextShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardGraphFormalTextShowcaseMod] Loaded");
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine? engine = ctx.Get(CoreServiceKeys.Engine);
            if (engine == null)
            {
                throw new InvalidOperationException(
                    "拼句字幕短剧开场需要引擎服务，缺了不能装字幕。");
            }

            if (!string.Equals(
                    engine.MergedConfig?.StartupMapId,
                    GraphFormalTextShowcaseContract.MapId,
                    StringComparison.Ordinal))
            {
                return Task.CompletedTask;
            }

            ScreenOverlayBuffer overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
                ?? throw new InvalidOperationException("拼句字幕短剧需要屏幕字幕缓冲。");
            engine.RegisterPresentationSystem(new GraphFormalTextShowcasePresentationSystem(engine, overlay));
            return Task.CompletedTask;
        });
    }

    public void OnUnload()
    {
    }
}
