using GenreInfoShowcaseMod.Runtime;
using GenreInfoShowcaseMod.Triggers;
using Ludots.Core.Modding;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Scripting;

namespace GenreInfoShowcaseMod
{
    public sealed class GenreInfoShowcaseModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[GenreInfoShowcaseMod] Loaded");
            RegisterInsightAttributes();

            var runtime = new GenreInfoShowcaseRuntime();
            context.OnEvent(GameEvents.GameStart, new InstallGenreInfoShowcaseOnGameStartTrigger(context, runtime).ExecuteAsync);
            context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
            context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
            context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
        }

        public void OnUnload()
        {
        }

        private static void RegisterInsightAttributes()
        {
            AttributeRegistry.Register("Health");
            AttributeRegistry.Register("Gold");
            AttributeRegistry.Register("Production");
            AttributeRegistry.Register("TechProgress");
            AttributeRegistry.Register("FoodProduction");
            AttributeRegistry.Register("Energy");
            AttributeRegistry.Register("AttackSpeed");
            AttributeRegistry.Register("Shield");
        }
    }
}
