using System.Threading.Tasks;
using CapabilityStandardGraphScoreShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace CapabilityStandardGraphScoreShowcaseMod;

public sealed class CapabilityStandardGraphScoreShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardGraphScoreShowcaseMod] Loaded");
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine? engine = ctx.Get(CoreServiceKeys.Engine);
            if (engine == null)
            {
                throw new InvalidOperationException(
                    "残血打分短剧开场需要引擎服务，缺了不能装字幕。");
            }

            if (!string.Equals(
                    engine.MergedConfig?.StartupMapId,
                    GraphScoreShowcaseContract.MapId,
                    StringComparison.Ordinal))
            {
                return Task.CompletedTask;
            }

            ScreenOverlayBuffer overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
                ?? throw new InvalidOperationException("残血打分短剧需要屏幕字幕缓冲。");
            engine.RegisterPresentationSystem(new GraphScoreShowcasePresentationSystem(engine, overlay));
            return Task.CompletedTask;
        });
    }

    public void OnUnload()
    {
    }
}
