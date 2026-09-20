using System.Threading.Tasks;
using CapabilityStandardLiveSkillWorkbenchShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardLiveSkillWorkbenchShowcaseMod;

/// <summary>
/// Hot-apply demo hosted on Champion Skill Showcase map/cast pipeline.
/// </summary>
public sealed class CapabilityStandardLiveSkillWorkbenchShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardLiveSkillWorkbenchShowcaseMod] Loaded — champion-style fire→ice hot-apply demo");
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine? engine = ctx.GetEngine();
            if (engine == null) return Task.CompletedTask;
            DebugDrawCommandBuffer debugDraw =
                engine.GetService(CoreServiceKeys.DebugDrawCommandBuffer) ?? new DebugDrawCommandBuffer();
            engine.SetService(CoreServiceKeys.DebugDrawCommandBuffer, debugDraw);
            engine.RegisterSystem(new LswChampionHotApplyDemoSystem(engine), SystemGroup.InputCollection);
            context.Log("[CapabilityStandardLiveSkillWorkbenchShowcaseMod] LswChampionHotApplyDemoSystem registered.");
            return Task.CompletedTask;
        });
    }

    public void OnUnload() { }
}
