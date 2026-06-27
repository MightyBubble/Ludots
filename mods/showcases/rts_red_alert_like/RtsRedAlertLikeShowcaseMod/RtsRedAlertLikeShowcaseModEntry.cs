using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using RtsRedAlertLikeShowcaseMod.Systems;
using System.Threading.Tasks;
using Ludots.Core.Engine;

namespace RtsRedAlertLikeShowcaseMod;

public sealed class RtsRedAlertLikeShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            var engine = ctx.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            engine.RegisterSystem(new RtsRedAlertKnowledgeProjectionSystem(engine), SystemGroup.InputCollection);
            engine.RegisterPresentationSystem(new RtsRedAlertSelectionFeedbackPresentationSystem(engine));
            return Task.CompletedTask;
        });

        context.Log("[RtsRedAlertLikeShowcaseMod] Loaded - Red Alert style production showcase root.");
    }

    public void OnUnload()
    {
    }
}
