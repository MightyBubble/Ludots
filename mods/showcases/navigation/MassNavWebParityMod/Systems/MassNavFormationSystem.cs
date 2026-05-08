using System.Diagnostics;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Scripting;
using MassNavWebParityMod.Runtime;

namespace MassNavWebParityMod.Systems;

internal sealed class MassNavFormationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly MassNavSimulationRuntime _simulation;

    public MassNavFormationSystem(GameEngine engine, MassNavSimulationRuntime simulation)
    {
        _engine = engine;
        _simulation = simulation;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!MassNavWebParityIds.IsCurrentPlaygroundMap(_engine))
        {
            return;
        }

        ObserveCameraFocus();

        int stepsToRun = _simulation.CadenceScheduler.BeginFixedTick(dt);
        for (int stepIndex = 0; stepIndex < stepsToRun; stepIndex++)
        {
            MassNavCadenceStep step = _simulation.CadenceScheduler.NextSimulationStep();
            _simulation.ObserveSimTick();

            if (step.UpdateTargets)
            {
                long targetStart = Stopwatch.GetTimestamp();
                _simulation.NavGroupRuntime.UpdateTargets(
                    _simulation.WebParity,
                    _simulation.AgentState,
                    _simulation.SelectedEntities,
                    _simulation.FrameIndex);
                _simulation.ObserveFormationTargets((Stopwatch.GetTimestamp() - targetStart) * 1000.0 / Stopwatch.Frequency);
            }

            if (_simulation.WebParity.AdvanceFlowPipeline(
                    _simulation.FlowTuning,
                    step.RefreshFlow,
                    step.RefreshCrowd,
                    step.RefreshObstacles,
                    _simulation.ObserveFlowFieldRebuild))
            {
                _simulation.MarkFlowReconcile();
            }

            long start = Stopwatch.GetTimestamp();
            _simulation.WebParity.Step(
                step.SimulationDt,
                _simulation.NavGroupRuntime,
                step.RunHardResolve,
                _simulation.Cadence.HardResolveCandidateThresholdAgents,
                _simulation.ObserveStepPrep,
                _simulation.ObserveLocalSteering,
                _simulation.ObserveHardResolve);
            _simulation.ObserveSimStep((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);

            if (step.SyncEntities)
            {
                start = Stopwatch.GetTimestamp();
                _simulation.WebParity.SyncEntities(_engine.World, _simulation.AgentState);
                _simulation.ObserveEntitySync((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
            }
        }
    }

    private void ObserveCameraFocus()
    {
        var camera = _engine.GameSession.Camera.State;
        if (_engine.GetService(CoreServiceKeys.ViewController) is not IViewController view)
        {
            _simulation.ObserveCameraFocus(camera.TargetCm);
            return;
        }

        var extent = CameraViewportUtil.ComputeViewportExtent(
            camera.DistanceCm,
            camera.FovYDeg,
            camera.Pitch,
            view.AspectRatio);
        _simulation.ObserveCameraFocus(camera.TargetCm, extent.widthCm, extent.heightCm);
    }
}
