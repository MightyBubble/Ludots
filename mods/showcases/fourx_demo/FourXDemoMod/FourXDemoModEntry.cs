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

            TagRegistry.Register("Status.CanColonize");
            TagRegistry.Register("State.4X.Blockaded");
            TagRegistry.Register("State.4X.Razed");
            TagRegistry.Register("State.4X.Relationship.Trusted");
            TagRegistry.Register("State.4X.Relationship.TradePact");
            TagRegistry.Register("State.4X.Relationship.AtWar");

            context.Log("[FourXDemoMod] Loaded");
            context.OnEvent(GameEvents.GameStart, new InstallFourXDemoOnGameStartTrigger(context).ExecuteAsync);
            context.OnEvent(GameEvents.MapLoaded, new FourXSetupOnMapLoadedTrigger(context).ExecuteAsync);
        }

        public void OnUnload()
        {
        }
    }
}

