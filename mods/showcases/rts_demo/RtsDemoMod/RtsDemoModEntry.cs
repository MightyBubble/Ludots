using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using RtsDemoMod.Triggers;

namespace RtsDemoMod
{
    public sealed class RtsDemoModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            // AttributeRegistry freezes when engine init completes; these gameplay attribute names
            // are this mod's template data vocabulary, so register them before the freeze.
            foreach (string attribute in new[] { "Credits", "Gas", "Health", "Lumber", "Minerals", "MoveSpeed", "Shield" })
            {
                Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.Register(attribute);
            }

            context.Log("[RtsDemoMod] Loaded");
            context.OnEvent(GameEvents.GameStart, new InstallRtsDemoOnGameStartTrigger(context).ExecuteAsync);
            context.OnEvent(GameEvents.MapLoaded, new RtsSetupOnMapLoadedTrigger(context).ExecuteAsync);
        }

        public void OnUnload()
        {
        }
    }
}
