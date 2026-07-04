using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using MassNavigationMod.Triggers;

namespace MassNavigationMod;

public sealed class MassNavigationModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[MassNavigationMod] Loaded MassNavigation assets and input bridge.");
        context.OnEvent(GameEvents.GameStart, new InstallMassNavigationInputOnGameStartTrigger(context).ExecuteAsync);
    }

    public void OnUnload()
    {
    }
}
