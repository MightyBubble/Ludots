using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CapabilityStandardPhysics3DShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Physics3D;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Scripting;

namespace CapabilityStandardPhysics3DShowcaseMod;

public sealed class CapabilityStandardPhysics3DShowcaseModEntry : IMod
{
    private Physics3DShowcaseRuntime? _runtime;
    private Physics3DShowcaseControlSystem? _controlSystem;
    private Physics3DShowcaseObservationSystem? _observationSystem;
    private Physics3DShowcasePresentationSystem? _presentationSystem;
    private GameEngine? _engine;

    public void OnLoad(IModContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_runtime != null)
        {
            throw new InvalidOperationException("CapabilityStandardPhysics3DShowcaseMod is already loaded.");
        }

        _runtime = new Physics3DShowcaseRuntime();
        context.OnEvent(GameEvents.MapLoaded, InstallSystemsAsync);
        context.OnEvent(GameEvents.MapResumed, InstallSystemsAsync);
        context.OnEvent(GameEvents.MapLoaded, _runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, _runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, _runtime.HandleMapUnloadedAsync);
        context.Log("[CapabilityStandardPhysics3DShowcaseMod] Registered the player-facing Physics3D Playground.");
    }

    public void OnUnload()
    {
        if (_engine != null)
        {
            if (_presentationSystem == null || !_engine.UnregisterPresentationSystem(_presentationSystem))
            {
                throw new InvalidOperationException("Physics3D showcase presentation system was not registered during unload.");
            }

            if (_observationSystem == null || !_engine.UnregisterSystem(_observationSystem, SystemGroup.PostMovement))
            {
                throw new InvalidOperationException("Physics3D showcase observation system was not registered during unload.");
            }

            if (_controlSystem == null || !_engine.UnregisterSystem(_controlSystem, SystemGroup.InputCollection))
            {
                throw new InvalidOperationException("Physics3D showcase control system was not registered during unload.");
            }

            if (_runtime == null ||
                !_engine.TryGetService(CoreServiceKeys.BenchmarkSceneController, out IBenchmarkSceneController controller) ||
                !ReferenceEquals(controller, _runtime))
            {
                throw new InvalidOperationException("Physics3D showcase no longer owns BenchmarkSceneController during unload.");
            }

            if (!_engine.RemoveService(CoreServiceKeys.BenchmarkSceneController))
            {
                throw new InvalidOperationException("Physics3D showcase failed to remove BenchmarkSceneController during unload.");
            }
        }

        _runtime?.Dispose();
        _presentationSystem = null;
        _observationSystem = null;
        _controlSystem = null;
        _runtime = null;
        _engine = null;
    }

    private Task InstallSystemsAsync(ScriptContext context)
    {
        GameEngine engine = context.GetEngine()
            ?? throw new InvalidOperationException("Physics3D showcase requires a live GameEngine during map activation.");
        Physics3DShowcaseRuntime runtime = _runtime
            ?? throw new InvalidOperationException("Physics3D showcase runtime is missing during map activation.");
        if (_engine != null || _controlSystem != null || _observationSystem != null || _presentationSystem != null)
        {
            if (_engine == null || _controlSystem == null || _observationSystem == null || _presentationSystem == null)
            {
                throw new InvalidOperationException("Physics3D showcase systems are only partially installed.");
            }

            if (!ReferenceEquals(_engine, engine))
            {
                throw new InvalidOperationException("Physics3D showcase systems cannot attach to multiple GameEngine instances.");
            }

            if (!engine.TryGetService(CoreServiceKeys.BenchmarkSceneController, out IBenchmarkSceneController controller) ||
                !ReferenceEquals(controller, runtime))
            {
                throw new InvalidOperationException(
                    "Physics3D showcase no longer owns BenchmarkSceneController during repeated map activation.");
            }

            return Task.CompletedTask;
        }

        if (engine.TryGetService(CoreServiceKeys.BenchmarkSceneController, out _))
        {
            throw new InvalidOperationException("Physics3D showcase requires exclusive BenchmarkSceneController ownership.");
        }

        var control = new Physics3DShowcaseControlSystem(runtime);
        var observation = new Physics3DShowcaseObservationSystem(runtime);
        var presentation = new Physics3DShowcasePresentationSystem(engine, runtime);
        bool serviceInstalled = false;
        bool controlRegistered = false;
        bool observationRegistered = false;
        bool presentationRegistered = false;
        try
        {
            engine.SetService(CoreServiceKeys.BenchmarkSceneController, (IBenchmarkSceneController)runtime);
            serviceInstalled = true;
            engine.InsertSystemBeforeRequired<Physics3DSimulationSystem>(control, SystemGroup.InputCollection);
            controlRegistered = true;
            engine.RegisterSystem(observation, SystemGroup.PostMovement);
            observationRegistered = true;
            engine.InsertPresentationSystemBefore<PresentationRequestFlushSystem>(presentation);
            presentationRegistered = true;
        }
        catch (Exception installException)
        {
            var rollbackFailures = new List<Exception>();
            TryRollback(
                presentationRegistered,
                () => engine.UnregisterPresentationSystem(presentation),
                "presentation system",
                rollbackFailures);
            TryRollback(
                observationRegistered,
                () => engine.UnregisterSystem(observation, SystemGroup.PostMovement),
                "observation system",
                rollbackFailures);
            TryRollback(
                controlRegistered,
                () => engine.UnregisterSystem(control, SystemGroup.InputCollection),
                "control system",
                rollbackFailures);

            if (serviceInstalled)
            {
                try
                {
                    if (!engine.TryGetService(CoreServiceKeys.BenchmarkSceneController, out IBenchmarkSceneController controller) ||
                        !ReferenceEquals(controller, runtime))
                    {
                        rollbackFailures.Add(new InvalidOperationException(
                            "Physics3D showcase lost BenchmarkSceneController ownership during install rollback."));
                    }
                    else if (!engine.RemoveService(CoreServiceKeys.BenchmarkSceneController))
                    {
                        rollbackFailures.Add(new InvalidOperationException(
                            "Physics3D showcase failed to remove BenchmarkSceneController during install rollback."));
                    }
                }
                catch (Exception rollbackException)
                {
                    rollbackFailures.Add(rollbackException);
                }
            }

            if (rollbackFailures.Count > 0)
            {
                rollbackFailures.Insert(0, installException);
                throw new AggregateException(
                    "Physics3D showcase installation failed and one or more rollback operations also failed.",
                    rollbackFailures);
            }

            throw;
        }

        _engine = engine;
        _controlSystem = control;
        _observationSystem = observation;
        _presentationSystem = presentation;
        return Task.CompletedTask;
    }

    private static void TryRollback(
        bool registered,
        Func<bool> unregister,
        string registrationName,
        List<Exception> failures)
    {
        if (!registered)
        {
            return;
        }

        try
        {
            if (!unregister())
            {
                failures.Add(new InvalidOperationException(
                    $"Physics3D showcase failed to unregister its {registrationName} during install rollback."));
            }
        }
        catch (Exception rollbackException)
        {
            failures.Add(rollbackException);
        }
    }
}
