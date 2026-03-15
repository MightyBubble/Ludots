using System.Threading.Tasks;
using EntityCommandPanelShowcaseMod.Runtime;
using EntityCommandPanelShowcaseMod.Systems;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace EntityCommandPanelShowcaseMod
{
    public sealed class EntityCommandPanelShowcaseModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[EntityCommandPanelShowcaseMod] Loaded");
            var runtime = new EntityCommandPanelShowcaseRuntime();

            context.OnEvent(GameEvents.GameStart, ctx =>
            {
                var engine = ctx.GetEngine();
                if (engine != null)
                {
                    engine.RegisterPresentationSystem(new EntityCommandPanelShowcasePresentationSystem(engine, runtime));
                }

                return Task.CompletedTask;
            });
            context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
            context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
            context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
        }

        public void OnUnload()
        {
        }
    }
}
