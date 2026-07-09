using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using CameraAcceptanceMod.Systems;
using CoreInputMod.ViewMode;
using Ludots.Core.Input.Runtime;
using CameraAcceptanceMod.Runtime;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Input.Systems;
using Ludots.Core.Systems;

namespace CameraAcceptanceMod
{
    public sealed class CameraAcceptanceModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[CameraAcceptanceMod] Loaded");
            CameraAcceptanceHotpathTerrainGenerator.EnsureGenerated(context);
            var runtime = new CameraAcceptanceRuntime();
            context.OnEvent(GameEvents.GameStart, ctx =>
            {
                var engine = ctx.GetEngine();
                if (engine != null)
                {
                    ViewModeRegistrar.RegisterFromVfs(context, engine.GlobalContext, sourceModId: context.ModId, activateWhenUnset: false);
                    engine.SetService(CameraAcceptanceServiceKeys.DiagnosticsState, new CameraAcceptanceDiagnosticsState());
                    CameraAcceptanceRuntime.InitializeProjectionSpawnCount(engine);
                    engine.GlobalContext[CameraAcceptanceIds.ActiveBlendCameraIdKey] = CameraAcceptanceIds.BlendSmoothCameraId;
                    runtime.InstallSelectionCallbacks(engine);
                    var inputOwnership = new CameraAcceptanceInputOwnershipSystem(engine);
                    engine.InsertSystemBeforeRequired<AuthoritativeInputSnapshotSystem>(inputOwnership, SystemGroup.InputCollection);
                    if (engine.GetService(CoreServiceKeys.InputFrameConsumers) is System.Collections.Generic.List<IInputFrameConsumer> consumers)
                    {
                        consumers.Add(inputOwnership);
                    }
                    engine.RegisterSystem(new CameraAcceptanceLocalAvatarMoveSystem(engine), SystemGroup.InputCollection);
                    engine.RegisterSystem(new CameraAcceptanceDiagnosticsToggleSystem(engine), SystemGroup.InputCollection);
                    engine.RegisterSystem(new CameraAcceptanceProjectionSpawnControlSystem(engine), SystemGroup.InputCollection);
                    engine.RegisterSystem(new CameraBlendAcceptanceSystem(engine), SystemGroup.InputCollection);
                    engine.RegisterSystem(new CameraStackAcceptanceSystem(engine), SystemGroup.InputCollection);
                    engine.RegisterPresentationSystem(new CameraAcceptancePanelPresentationSystem(engine, runtime));
                    engine.RegisterPresentationSystem(new CameraAcceptanceProjectionBoundsOverlaySystem(engine));
                    engine.RegisterPresentationSystem(new CameraAcceptanceHotpathLaneSystem(engine));
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
