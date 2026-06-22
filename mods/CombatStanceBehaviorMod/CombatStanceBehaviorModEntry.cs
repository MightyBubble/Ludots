using CombatStanceBehaviorMod.Runtime;
using CombatStanceBehaviorMod.Triggers;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace CombatStanceBehaviorMod;

public sealed class CombatStanceBehaviorModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        CombatStanceComponentAuthoring.Register(context.ModId);
        context.Log("[CombatStanceBehaviorMod] Loaded");
        context.OnEvent(GameEvents.GameStart, new InstallCombatStanceBehaviorOnGameStartTrigger(context).ExecuteAsync);
    }

    public void OnUnload()
    {
    }
}
