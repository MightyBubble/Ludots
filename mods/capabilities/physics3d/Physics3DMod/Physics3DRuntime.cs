using System;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Physics3D;
using Ludots.Core.Scripting;

namespace Physics3DMod;

internal sealed class Physics3DRuntime : IDisposable
{
    private GameEngine? _engine;
    private Physics3DWorld? _world;
    private Physics3DSimulationSystem? _system;

    public Task EnsureInstalledAsync(ScriptContext context)
    {
        GameEngine engine = context.GetEngine()
            ?? throw new InvalidOperationException("Physics3DMod requires a live GameEngine.");
        if (_engine != null && !ReferenceEquals(_engine, engine))
        {
            throw new InvalidOperationException("Physics3DMod cannot attach one runtime to multiple GameEngine instances.");
        }

        if (_world != null || _system != null)
        {
            if (_world == null || _system == null)
            {
                throw new InvalidOperationException("Physics3DMod runtime is only partially installed.");
            }

            return Task.CompletedTask;
        }

        RequireServiceAbsent(engine, Physics3DServiceKeys.World);
        RequireServiceAbsent(engine, Physics3DServiceKeys.SimulationSystem);

        Physics3DWorldConfig config = new Physics3DWorldConfigLoader(engine.ConfigPipeline).Load(
            engine.ConfigCatalog,
            engine.ConfigConflictReport);
        int sourceFixedStepHz = FixedHzFromDeltaTime(Time.FixedDeltaTime);
        var world = new Physics3DWorld(config);
        var system = new Physics3DSimulationSystem(
            engine.World,
            world,
            sourceFixedStepHz,
            config.MaximumPhysicsStepsPerSourceTick);
        bool worldServiceSet = false;
        bool simulationSystemServiceSet = false;
        try
        {
            engine.RegisterSystem(system, SystemGroup.InputCollection);
            engine.SetService(Physics3DServiceKeys.World, (IPhysics3DWorld)world);
            worldServiceSet = true;
            engine.SetService(Physics3DServiceKeys.SimulationSystem, system);
            simulationSystemServiceSet = true;
        }
        catch
        {
            if (simulationSystemServiceSet)
            {
                engine.RemoveService(Physics3DServiceKeys.SimulationSystem);
            }

            if (worldServiceSet)
            {
                engine.RemoveService(Physics3DServiceKeys.World);
            }

            engine.UnregisterSystem(system, SystemGroup.InputCollection);
            world.Dispose();
            throw;
        }

        _engine = engine;
        _world = world;
        _system = system;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_engine == null)
        {
            if (_world != null || _system != null)
            {
                throw new InvalidOperationException("Physics3DMod runtime lost its owning GameEngine.");
            }

            return;
        }

        if (_world == null || _system == null)
        {
            throw new InvalidOperationException("Physics3DMod runtime is only partially installed.");
        }

        RequireOwnedService(_engine, Physics3DServiceKeys.World, (IPhysics3DWorld)_world);
        RequireOwnedService(_engine, Physics3DServiceKeys.SimulationSystem, _system);

        if (!_engine.UnregisterSystem(_system, SystemGroup.InputCollection))
        {
            throw new InvalidOperationException("Physics3DMod simulation system was not registered during unload.");
        }

        if (!_engine.RemoveService(Physics3DServiceKeys.SimulationSystem) ||
            !_engine.RemoveService(Physics3DServiceKeys.World))
        {
            throw new InvalidOperationException("Physics3DMod services were not registered during unload.");
        }

        _world.Dispose();
        _system = null;
        _world = null;
        _engine = null;
    }

    private static void RequireServiceAbsent<T>(GameEngine engine, ServiceKey<T> key)
    {
        if (engine.TryGetService(key, out _))
        {
            throw new InvalidOperationException($"Physics3DMod cannot install because '{key.Name}' is already registered.");
        }
    }

    private static void RequireOwnedService<T>(GameEngine engine, ServiceKey<T> key, T expected)
        where T : class
    {
        if (!engine.TryGetService(key, out T current) || !ReferenceEquals(current, expected))
        {
            throw new InvalidOperationException($"Physics3DMod no longer owns registered service '{key.Name}'.");
        }
    }

    private static int FixedHzFromDeltaTime(float deltaTime)
    {
        if (!(deltaTime > 0f) || !float.IsFinite(deltaTime))
        {
            throw new InvalidOperationException($"Engine fixed delta time '{deltaTime}' is invalid for Physics3D.");
        }

        int fixedHz = (int)MathF.Round(1f / deltaTime);
        if (fixedHz <= 0 || MathF.Abs((1f / fixedHz) - deltaTime) > 1e-5f)
        {
            throw new InvalidOperationException(
                $"Engine fixed delta time '{deltaTime}' is not representable as 1/integer Hz for Physics3D.");
        }

        return fixedHz;
    }
}
