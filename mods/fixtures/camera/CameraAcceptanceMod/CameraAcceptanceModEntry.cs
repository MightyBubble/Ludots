using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using CameraAcceptanceMod.Systems;
using Arch.Core;
using CoreInputMod.ViewMode;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using CameraAcceptanceMod.Runtime;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera.FollowTargets;
using Ludots.Core.Systems;

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
                    var localPlayerTarget = new GlobalEntityFollowTarget(engine.World, engine.GlobalContext, CoreServiceKeys.LocalPlayerEntity.Name);
                    var selectionTarget = new EntityResolverFollowTarget(engine.World, () =>
                        SelectionRuntime.TryGetPrimarySelected(engine.World, engine.GlobalContext, out var selected)
                            ? selected
                            : Entity.Null);
                    engine.RegisterSystem(
                        new VirtualCameraFollowBindingSystem(
                            engine.GameSession.Camera,
                            new VirtualCameraFollowBinding(CameraAcceptanceIds.TpsCameraId, localPlayerTarget),
                            new VirtualCameraFollowBinding(CameraAcceptanceIds.FollowCloseCameraId, selectionTarget),
                            new VirtualCameraFollowBinding(CameraAcceptanceIds.FollowWideCameraId, selectionTarget)),
                        SystemGroup.InputCollection);
                    engine.RegisterSystem(new CameraAcceptanceProjectionSpawnSystem(engine), SystemGroup.InputCollection);
                    engine.RegisterSystem(new CameraAcceptanceFollowMoveSystem(engine), SystemGroup.InputCollection);
                    engine.RegisterSystem(new CameraBlendAcceptanceSystem(engine), SystemGroup.InputCollection);
                    engine.RegisterSystem(new CameraAcceptanceBlendClickSystem(engine), SystemGroup.InputCollection);
                    engine.RegisterSystem(new CameraStackAcceptanceSystem(engine), SystemGroup.InputCollection);
                    engine.RegisterPresentationSystem(new CameraAcceptanceFixtureHudSystem(engine));
                    engine.RegisterPresentationSystem(new CameraAcceptancePanelPresentationSystem(engine, runtime));
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
