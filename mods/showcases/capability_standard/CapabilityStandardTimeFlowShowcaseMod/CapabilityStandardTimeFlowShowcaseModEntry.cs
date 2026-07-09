using System;
using System.Threading.Tasks;
using CapabilityStandardTimeFlowShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Physics2D;
using Ludots.Core.Physics2D.Systems;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Scripting;

namespace CapabilityStandardTimeFlowShowcaseMod;

public sealed class CapabilityStandardTimeFlowShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardTimeFlowShowcaseMod] Loaded");
        var runtime = new CapabilityStandardTimeFlowShowcaseRuntime();

        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine? engine = ctx.GetEngine();
            if (engine != null)
            {
                engine.SetService(CoreServiceKeys.BenchmarkSceneController, runtime);
                var debugDrawBuffer = new DebugDrawCommandBuffer();
                engine.SetService(CoreServiceKeys.DebugDrawCommandBuffer, debugDrawBuffer);

                engine.RegisterSystem(new CapabilityStandardTimeFlowShowcaseSimulationSystem(engine, runtime), SystemGroup.PostMovement);
                if (engine.GetService(CoreServiceKeys.InputFrameConsumers) is not System.Collections.Generic.List<IInputFrameConsumer> inputConsumers)
                {
                    throw new InvalidOperationException("TimeFlow showcase shortcuts require InputFrameConsumers.");
                }

                if (!inputConsumers.Contains(runtime))
                {
                    inputConsumers.Add(runtime);
                }

                if (engine.GetService(CoreServiceKeys.Physics2DShapeStorage) is not ShapeDataStorage2D shapeStorage)
                {
                    throw new InvalidOperationException("TimeFlow showcase requires Physics2D shape storage.");
                }

                engine.RegisterPresentationSystem(new Physics2DDebugDrawSystem(engine.World, debugDrawBuffer, shapeStorage));
                engine.RegisterPresentationSystem(new CapabilityStandardTimeFlowShowcasePresentationSystem(engine, runtime, debugDrawBuffer));
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
