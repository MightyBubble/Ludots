using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using ControlPlaneProjectionShowcaseMod.Triggers;

namespace ControlPlaneProjectionShowcaseMod
{
    public sealed class ControlPlaneProjectionShowcaseModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.OnEvent(GameEvents.GameStart, new InstallControlPlaneProjectionShowcaseOnGameStartTrigger(context).ExecuteAsync);
        }

        public void OnUnload()
        {
        }
    }
}
