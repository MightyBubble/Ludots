using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using DesertStrikeShowcaseMod.Runtime;
using DesertStrikeShowcaseMod.Triggers;

namespace DesertStrikeShowcaseMod
{
    public sealed class DesertStrikeShowcaseModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            DesertStrikeComponentAuthoring.Register(context.ModId);
            DesertStrikeAttributeSetup.EnsureRegistered();
            context.Log("[DesertStrikeShowcaseMod] Loaded");
            context.OnEvent(GameEvents.GameStart, new InstallDesertStrikeOnGameStartTrigger(context).ExecuteAsync);
            context.OnEvent(GameEvents.MapLoaded, new DesertStrikeSetupOnMapLoadedTrigger(context).ExecuteAsync);
        }

        public void OnUnload()
        {
        }
    }
}
