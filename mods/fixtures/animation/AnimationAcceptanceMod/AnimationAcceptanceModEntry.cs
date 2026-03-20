using System.Threading.Tasks;
using AnimationAcceptanceMod.Runtime;
using AnimationAcceptanceMod.Systems;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace AnimationAcceptanceMod
{
    public sealed class AnimationAcceptanceModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[AnimationAcceptanceMod] Loaded");
            var runtime = new AnimationAcceptanceRuntime();
            context.OnEvent(GameEvents.GameStart, scriptContext =>
            {
                var engine = scriptContext.GetEngine();
                if (engine != null)
                {
                    engine.SetService(AnimationAcceptanceServiceKeys.ControlState, new AnimationAcceptanceControlState());
                    engine.RegisterSystem(new AnimationAcceptancePrototypeSystem(engine), SystemGroup.InputCollection);
                    engine.RegisterPresentationSystem(new AnimationAcceptancePanelPresentationSystem(engine, runtime));
                }

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
