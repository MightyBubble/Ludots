using System.Threading.Tasks;
using SuperweaponContextShowcaseMod.Runtime;
using SuperweaponContextShowcaseMod.Systems;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace SuperweaponContextShowcaseMod
{
    public sealed class SuperweaponContextShowcaseModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[SuperweaponContextShowcaseMod] Loaded");
            var runtime = new SuperweaponContextShowcaseRuntime();

            context.OnEvent(GameEvents.GameStart, ctx =>
            {
                ctx.Set(SuperweaponContextShowcaseIds.RuntimeStateServiceKey, runtime.State);
                var engine = ctx.GetEngine();
                if (engine != null)
                {
                    engine.GlobalContext[SuperweaponContextShowcaseIds.RuntimeStateServiceKey] = runtime.State;
                    engine.RegisterSystem(new SuperweaponContextShowcaseInputSystem(engine, runtime), SystemGroup.InputCollection);
                    engine.RegisterSystem(new SuperweaponContextShowcaseSystem(engine, runtime), SystemGroup.AbilityActivation);
                    engine.RegisterPresentationSystem(new SuperweaponContextShowcasePresentationSystem(engine, runtime));
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
