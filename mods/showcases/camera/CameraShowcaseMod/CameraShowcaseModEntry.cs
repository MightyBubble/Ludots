using System.Threading.Tasks;
using CoreInputMod.ViewMode;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using CameraShowcaseMod.Runtime;
using Arch.Core;
using CameraShowcaseMod.Systems;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera.FollowTargets;
using Ludots.Core.Input.Selection;
using Ludots.Core.Systems;

namespace CameraShowcaseMod
{
    public sealed class CameraShowcaseModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[CameraShowcaseMod] Loaded");
            var runtime = new CameraShowcaseRuntime(context);

            context.OnEvent(GameEvents.GameStart, ctx =>
            {
                var engine = ctx.GetEngine();
                if (engine == null)
                {
                    return Task.CompletedTask;
                }

                ViewModeRegistrar.RegisterFromVfs(context, engine.GlobalContext, sourceModId: context.ModId, activateWhenUnset: false);
                var localPlayerTarget = new GlobalEntityFollowTarget(engine.World, engine.GlobalContext, CoreServiceKeys.LocalPlayerEntity.Name);
                var selectionTarget = new EntityResolverFollowTarget(engine.World, () =>
                    SelectionRuntime.TryGetPrimarySelected(engine.World, engine.GlobalContext, out var selected)
                        ? selected
                        : Entity.Null);
                engine.RegisterSystem(
                    new VirtualCameraFollowBindingSystem(
                        engine.GameSession.Camera,
                        new VirtualCameraFollowBinding(CameraShowcaseIds.FollowProfileId, localPlayerTarget),
                        new VirtualCameraFollowBinding(CameraShowcaseIds.TrackProfileId, selectionTarget),
                        new VirtualCameraFollowBinding(CameraShowcaseIds.FocusLockShotId, selectionTarget)),
                    SystemGroup.InputCollection);
                engine.RegisterPresentationSystem(new CameraShowcasePanelPresentationSystem(engine, runtime));
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
