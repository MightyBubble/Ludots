using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.ParticipantVisibility;
using Ludots.Core.Scripting;

namespace CapabilityStandardParticipantViewsMod;

public sealed class CapabilityStandardParticipantViewsModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardParticipantViewsMod] Loaded");
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            var engine = ctx.GetEngine();
            if (engine != null &&
                !engine.GlobalContext.ContainsKey("CapabilityStandardParticipantViews.DynamicVisibilitySystemInstalled"))
            {
                IClock clock = engine.GetService(CoreServiceKeys.Clock)
                    ?? throw new System.InvalidOperationException("CapabilityStandardParticipantViewsMod requires Clock.");
                engine.RegisterSystem(
                    new DynamicParticipantVisibilitySystem(
                        engine.World,
                        () => engine.GetService(CoreServiceKeys.DynamicParticipantVisibilityPublisher),
                        clock),
                    SystemGroup.RuntimeEntityBinding);
                engine.GlobalContext["CapabilityStandardParticipantViews.DynamicVisibilitySystemInstalled"] = true;
            }

            return Task.CompletedTask;
        });
        context.OnEvent(GameEvents.MapLoaded, ctx =>
        {
            var engine = ctx.GetEngine();
            if (engine != null)
            {
                ParticipantViewKnowledgeShowcaseInstaller.Install(engine);
            }

            return Task.CompletedTask;
        });
        context.OnEvent(GameEvents.MapResumed, ctx =>
        {
            var engine = ctx.GetEngine();
            if (engine != null)
            {
                ParticipantViewKnowledgeShowcaseInstaller.Install(engine);
            }

            return Task.CompletedTask;
        });
        context.OnEvent(GameEvents.MapUnloaded, ctx =>
        {
            var engine = ctx.GetEngine();
            if (engine != null)
            {
                ParticipantViewKnowledgeShowcaseInstaller.Clear(engine);
            }

            return Task.CompletedTask;
        });
    }

    public void OnUnload()
    {
    }
}
