using System.Threading.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace CapabilityStandardParticipantViewsMod;

public sealed class CapabilityStandardParticipantViewsModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardParticipantViewsMod] Loaded");
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
                engine.RemoveService(CoreServiceKeys.KnowledgeProjectionStore);
                engine.RemoveService(CoreServiceKeys.KnowledgeRelationCollectionProjector);
                engine.RemoveService(CoreServiceKeys.KnowledgeProjectionResolver);
            }

            return Task.CompletedTask;
        });
    }

    public void OnUnload()
    {
    }
}
