using System.Threading.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using CapabilityStandardStaticPerformer30kMod.Runtime;
using CapabilityStandardStaticPerformer30kMod.Systems;

namespace CapabilityStandardStaticPerformer30kMod
{
    public sealed class CapabilityStandardStaticPerformer30kModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[CapabilityStandardStaticPerformer30kMod] Loaded");
            CapabilityStandardStaticPerformer30kComponentAuthoring.Register(context.ModId);
            var runtime = new CapabilityStandardStaticPerformer30kRuntime();

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
                        new CapabilityStandardStaticPerformer30kPresentationSystem(engine, runtime));
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
