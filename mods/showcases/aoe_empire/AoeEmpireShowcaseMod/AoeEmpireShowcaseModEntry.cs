using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using System.Threading.Tasks;

namespace AoeEmpireShowcaseMod;

public sealed class AoeEmpireShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine? engine = ctx.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            engine.RegisterSystem(new AoeEmpireKnowledgeProjectionSystem(engine), SystemGroup.InputCollection);
            context.Log("[AoeEmpireShowcaseMod] Loaded - AoE empire browser showcase root.");
            return Task.CompletedTask;
        });
    }

    public void OnUnload()
    {
    }
}
