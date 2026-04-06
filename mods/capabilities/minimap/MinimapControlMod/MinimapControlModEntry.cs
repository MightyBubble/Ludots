using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using MinimapControlMod.Triggers;

namespace MinimapControlMod;

public sealed class MinimapControlModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[MinimapControlMod] Loaded.");
        context.OnEvent(GameEvents.GameStart, new InstallMinimapControlOnGameStartTrigger(context).ExecuteAsync);
    }

    public void OnUnload()
    {
    }
}
