using System.Threading.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using PerformerBlacksmithShowcaseMod.Runtime;
using PerformerBlacksmithShowcaseMod.Systems;

namespace PerformerBlacksmithShowcaseMod
{
    public sealed class PerformerBlacksmithShowcaseModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[PerformerBlacksmithShowcaseMod] Loaded");
            PerformerBlacksmithShowcaseComponentAuthoring.Register(context.ModId);
            var runtime = new PerformerBlacksmithShowcaseRuntime();

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
                        new PerformerBlacksmithShowcasePresentationSystem(engine, runtime));
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
