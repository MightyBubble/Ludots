using System;
using System.Threading.Tasks;
using CapabilityStandardPhysics2DShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Physics2D;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Physics2D.Systems;
using Ludots.Core.Scripting;

namespace CapabilityStandardPhysics2DShowcaseMod;

public sealed class CapabilityStandardPhysics2DShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardPhysics2DShowcaseMod] Loaded");
        var runtime = new CapabilityStandardPhysics2DShowcaseRuntime();

        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine? engine = ctx.GetEngine();
            if (engine != null)
            {
                engine.SetService(CoreServiceKeys.BenchmarkSceneController, runtime);
                var debugDrawBuffer = new DebugDrawCommandBuffer();
                engine.SetService(CoreServiceKeys.DebugDrawCommandBuffer, debugDrawBuffer);
                engine.RegisterSystem(new CapabilityStandardPhysics2DShowcaseControlSystem(engine, runtime), SystemGroup.InputCollection);
                if (engine.GetService(CoreServiceKeys.Physics2DShapeStorage) is not ShapeDataStorage2D shapeStorage)
                {
                    throw new InvalidOperationException("CapabilityStandardPhysics2DShowcaseMod requires Physics2D shape storage.");
                }

                engine.RegisterPresentationSystem(new Physics2DDebugDrawSystem(engine.World, debugDrawBuffer, shapeStorage));
                engine.RegisterPresentationSystem(new CapabilityStandardPhysics2DShowcasePresentationSystem(engine, runtime));
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
