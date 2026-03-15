using EntityInfoPanelsMod.Triggers;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace EntityInfoPanelsMod;

public sealed class EntityInfoPanelsModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[EntityInfoPanelsMod] Loaded.");
        context.OnEvent(GameEvents.GameStart, new InstallEntityInfoPanelsOnGameStartTrigger(context).ExecuteAsync);
    }

    public void OnUnload()
    {
    }
}
