using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using CoreInputMod.Triggers;

namespace CoreInputMod
{
    public sealed class CoreInputModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[CoreInputMod] Loaded — generic input (click-select, GAS selection/input)");
            context.OnEvent(GameEvents.GameStart, new InstallCoreInputOnGameStartTrigger(context).ExecuteAsync);
        }

        public void OnUnload() { }
    }
}
