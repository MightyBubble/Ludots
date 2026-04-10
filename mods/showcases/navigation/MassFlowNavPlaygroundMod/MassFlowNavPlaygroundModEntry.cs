using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using MassFlowNavPlaygroundMod.Runtime;
using MassFlowNavPlaygroundMod.Triggers;

namespace MassFlowNavPlaygroundMod
{
    public sealed class MassFlowNavPlaygroundModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            var runtime = new MassFlowNavPlaygroundRuntime();
            context.OnEvent(GameEvents.GameStart, new InstallMassFlowNavPlaygroundOnGameStartTrigger(context, runtime).ExecuteAsync);
            context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
            context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
            context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
        }

        public void OnUnload()
        {
        }
    }
}
