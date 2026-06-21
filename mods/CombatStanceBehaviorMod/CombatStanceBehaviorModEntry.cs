using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using CombatStanceBehaviorMod.Triggers;

namespace CombatStanceBehaviorMod;

public sealed class CombatStanceBehaviorModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[CombatStanceBehaviorMod] Loaded");
        context.OnEvent(GameEvents.GameStart, new InstallCombatStanceBehaviorOnGameStartTrigger(context).ExecuteAsync);
    }

    public void OnUnload()
    {
    }
}
