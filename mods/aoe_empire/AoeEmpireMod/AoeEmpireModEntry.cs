using AoeEmpireMod.Triggers;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace AoeEmpireMod;

public sealed class AoeEmpireModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[AoeEmpireMod] Loaded - 5 nations, 100 unit types.");
        context.OnEvent(GameEvents.GameStart, new InstallAoeEmpireOnGameStartTrigger(context).ExecuteAsync);
        context.OnEvent(GameEvents.MapLoaded, new AoeEmpireSetupOnMapLoadedTrigger(context).ExecuteAsync);
    }

    public void OnUnload()
    {
    }
}
