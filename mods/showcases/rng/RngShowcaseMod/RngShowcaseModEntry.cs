using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using RngShowcaseMod.Triggers;

namespace RngShowcaseMod;

public sealed class RngShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.OnEvent(GameEvents.GameStart, new InstallRngShowcaseOnGameStartTrigger(context).ExecuteAsync);
        context.Log("[RngShowcaseMod] Loaded.");
    }

    public void OnUnload()
    {
    }
}
