using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using OpenRaStanceBehaviorMod.Triggers;

namespace OpenRaStanceBehaviorMod;

public sealed class OpenRaStanceBehaviorModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[OpenRaStanceBehaviorMod] Loaded");
        context.OnEvent(GameEvents.GameStart, new InstallOpenRaStanceBehaviorOnGameStartTrigger(context).ExecuteAsync);
    }

    public void OnUnload()
    {
    }
}
