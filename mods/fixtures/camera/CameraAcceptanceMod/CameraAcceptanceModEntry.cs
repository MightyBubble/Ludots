using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using CameraAcceptanceMod.Systems;
using CoreInputMod.ViewMode;
using Ludots.Core.Input.Runtime;
using CameraAcceptanceMod.Runtime;
using System.Threading.Tasks;
using Ludots.Core.Engine;

namespace CameraAcceptanceMod
{
    public sealed class CameraAcceptanceModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[CameraAcceptanceMod] Loaded");
            var runtime = new CameraAcceptanceRuntime();
            context.OnEvent(GameEvents.GameStart, ctx =>
            {
                var engine = ctx.GetEngine();
                if (engine != null)
                {
                    ViewModeRegistrar.RegisterFromVfs(context, engine.GlobalContext, sourceModId: context.ModId, activateWhenUnset: false);
                    if (engine.GetService(CoreServiceKeys.InputHandler) is PlayerInputHandler input &&
                        input.HasContext(CameraAcceptanceIds.InputContextId))
                    {
                        input.PushContext(CameraAcceptanceIds.InputContextId);
                    }
                    engine.GlobalContext[CameraAcceptanceIds.ActiveBlendCameraIdKey] = CameraAcceptanceIds.BlendSmoothCameraId;
                    runtime.InstallSelectionCallbacks(engine);
                    engine.RegisterSystem(new CameraBlendAcceptanceSystem(engine), SystemGroup.InputCollection);
                    engine.RegisterSystem(new CameraStackAcceptanceSystem(engine), SystemGroup.InputCollection);
                    engine.RegisterPresentationSystem(new CameraAcceptancePanelPresentationSystem(engine, runtime));
                    engine.RegisterPresentationSystem(new CameraAcceptanceSelectionOverlaySystem(engine));
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
