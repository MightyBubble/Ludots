using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using NarrativeFrontendMod.Triggers;

namespace NarrativeFrontendMod;

public sealed class NarrativeFrontendModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[NarrativeFrontendMod] Loaded.");
        context.OnEvent(GameEvents.GameStart, new InstallNarrativeFrontendOnGameStartTrigger(context).ExecuteAsync);
    }

    public void OnUnload()
    {
    }
}
