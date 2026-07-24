using System.Threading.Tasks;
using InteractionShowcaseMod.Runtime;
using InteractionShowcaseMod.Triggers;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace InteractionShowcaseMod
{
    public sealed class InteractionShowcaseModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[InteractionShowcaseMod] Loaded");

            var runtime = new InteractionShowcaseRuntime();
            var stressTelemetry = new InteractionShowcaseStressTelemetry();

            context.OnEvent(GameEvents.GameStart, new InstallInteractionShowcaseOnGameStartTrigger(context, runtime, stressTelemetry).ExecuteAsync);
            context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
            context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
            context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
        }

        public void OnUnload()
        {
        }
    }
}
