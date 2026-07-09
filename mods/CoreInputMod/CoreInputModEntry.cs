using CoreInputMod.Triggers;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace CoreInputMod
{
    public sealed class CoreInputModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[CoreInputMod] Loaded - generic command-source and GAS input");
            context.OnEvent(GameEvents.GameStart, new InstallCoreInputOnGameStartTrigger(context).ExecuteAsync);
        }

        public void OnUnload() { }
    }
}
