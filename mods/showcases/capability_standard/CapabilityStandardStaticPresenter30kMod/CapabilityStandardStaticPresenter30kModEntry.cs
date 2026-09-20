using System.Threading.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using CapabilityStandardStaticPresenter30kMod.Runtime;
using CapabilityStandardStaticPresenter30kMod.Systems;

namespace CapabilityStandardStaticPresenter30kMod
{
    public sealed class CapabilityStandardStaticPresenter30kModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[CapabilityStandardStaticPresenter30kMod] Loaded");
            CapabilityStandardStaticPresenter30kComponentAuthoring.Register(context.ModId);
            var runtime = new CapabilityStandardStaticPresenter30kRuntime();

            context.OnEvent(GameEvents.GameStart, ctx =>
            {
                var engine = ctx.GetEngine();
                if (engine != null)
                {
                    engine.SetService(CoreServiceKeys.BenchmarkSceneController, runtime);
                    engine.RegisterSystem(
                        new DynamicWorkerCrowdMovementSystem(engine),
                        SystemGroup.PostMovement);
                    engine.RegisterSystem(
                        new MinimapMarkerBallMovementSystem(engine),
                        SystemGroup.PostMovement);
                    engine.RegisterPresentationSystem(
                        new CapabilityStandardStaticPresenter30kPresentationSystem(engine, runtime));
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
