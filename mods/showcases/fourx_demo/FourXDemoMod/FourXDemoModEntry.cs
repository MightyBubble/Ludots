using FourXDemoMod.Triggers;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace FourXDemoMod
{
    public sealed class FourXDemoModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            AttributeRegistry.Register("Health");
            AttributeRegistry.Register("Production");
            AttributeRegistry.Register("Gold");
            AttributeRegistry.Register("TechProgress");
            AttributeRegistry.Register("FoodProduction");

            context.Log("[FourXDemoMod] Loaded");
            context.OnEvent(GameEvents.GameStart, new InstallFourXDemoOnGameStartTrigger(context).ExecuteAsync);
            context.OnEvent(GameEvents.MapLoaded, new FourXSetupOnMapLoadedTrigger(context).ExecuteAsync);
        }

        public void OnUnload()
        {
        }
    }
}
