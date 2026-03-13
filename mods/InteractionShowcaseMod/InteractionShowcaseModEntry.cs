using System.Threading.Tasks;
using InteractionShowcaseMod.Systems;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace InteractionShowcaseMod
{
    public sealed class InteractionShowcaseModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[InteractionShowcaseMod] Loaded");
            context.OnEvent(GameEvents.GameStart, ctx =>
            {
                var engine = ctx.GetEngine();
                if (engine == null)
                {
                    return Task.CompletedTask;
                }

                engine.RegisterSystem(new InteractionShowcaseAutoplaySystem(engine), SystemGroup.InputCollection);
                engine.RegisterPresentationSystem(new InteractionShowcaseGasEventTapSystem(engine));
                engine.RegisterPresentationSystem(new InteractionShowcaseOverlaySystem(engine));
                return Task.CompletedTask;
            });
        }

        public void OnUnload()
        {
        }
    }
}
