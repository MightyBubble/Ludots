using System.Threading.Tasks;
using CoreInputMod.ViewMode;
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
            context.OnEvent(GameEvents.GameStart, ctx =>
            {
                var engine = ctx.GetEngine();
                if (engine == null)
                {
                    return Task.CompletedTask;
                }

                ViewModeRegistrar.RegisterFromVfs(
                    context,
                    engine.GlobalContext,
                    defaultModeId: null,
                    sourceModId: context.ModId,
                    activateWhenUnset: false);
                return Task.CompletedTask;
            });
            context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
            context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
            context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
        }

        public void OnUnload()
        {
        }
    }
}
