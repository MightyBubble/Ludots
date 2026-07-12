using System;
using System.Diagnostics;

namespace Ludots.Core.MassNavigation.Runtime;

public sealed class MassNavigationTelemetry
{
    private const float TimingWeight = 0.18f;

    private long _controlTick;
    private long _commandTick;
    private long _simTick;
    private long _performerTick;
    private long _panelTick;

    public int CommandCountFrame { get; private set; }
    public int StructuralChangesFrame { get; private set; }
    public int StructuralChangeRevision { get; private set; }
    public int FlowReconcileCountFrame { get; private set; }
    public int FocusBudgetUpdatesFrame { get; private set; }
    public int SolverWindowMovesFrame { get; private set; }
    public float FrameMs { get; private set; }
    public float Fps { get; private set; }
    public float GroupTargetUpdateMs { get; private set; }
    public float FlowFieldRebuildMs { get; private set; }
    public float StepPrepMs { get; private set; }
    public float LocalSteeringMs { get; private set; }
    public float SimStepMs { get; private set; }
    public float HardResolveMs { get; private set; }
    public float EntitySyncMs { get; private set; }
    public float PerformerCommandMs { get; private set; }
    public float ControlHzObserved { get; private set; }
    public float CommandHzObserved { get; private set; }
    public float SimHzObserved { get; private set; }
    public float PerformerHzObserved { get; private set; }
    public float PanelHzObserved { get; private set; }
    public int CrowdInViewCount { get; private set; }
    public int CrowdSubmittedCount { get; private set; }
    public int ObstacleSubmittedCount { get; private set; }
    public int PerformerDroppedCount { get; private set; }
    public int StreamingWindowUpdatesFrame { get; private set; }
    public int FocusBudgetUpdatesTotal { get; private set; }
    public int SolverWindowMovesTotal { get; private set; }
    public int ScenarioSpawnCount { get; private set; }
    public int AuthoredRuntimeBindingRevision { get; private set; }

    public void BeginFrame(float dt)
    {
        CommandCountFrame = 0;
        StructuralChangesFrame = 0;
        FlowReconcileCountFrame = 0;
        StreamingWindowUpdatesFrame = 0;
        FocusBudgetUpdatesFrame = 0;
        SolverWindowMovesFrame = 0;
        FrameMs = dt > 0f ? dt * 1000f : 0f;
        Fps = FrameMs > 0.001f ? 1000f / FrameMs : 0f;
    }

    public void ObserveGroupTargetUpdate(double sampleMs) => GroupTargetUpdateMs = Smooth(GroupTargetUpdateMs, (float)sampleMs);
    public void ObserveFlowFieldRebuild(double sampleMs) => FlowFieldRebuildMs = Smooth(FlowFieldRebuildMs, (float)sampleMs);
    public void ObserveStepPrep(double sampleMs) => StepPrepMs = Smooth(StepPrepMs, (float)sampleMs);
    public void ObserveLocalSteering(double sampleMs) => LocalSteeringMs = Smooth(LocalSteeringMs, (float)sampleMs);
    public void ObserveSimStep(double sampleMs) => SimStepMs = Smooth(SimStepMs, (float)sampleMs);
    public void ObserveHardResolve(double sampleMs) => HardResolveMs = Smooth(HardResolveMs, (float)sampleMs);
    public void ObserveEntitySync(double sampleMs) => EntitySyncMs = Smooth(EntitySyncMs, (float)sampleMs);
    public void ObservePerformerCommand(double sampleMs) => PerformerCommandMs = Smooth(PerformerCommandMs, (float)sampleMs);

    public void ObservePerformerCoverage(
        int crowdInViewCount,
        int crowdSubmittedCount,
        int obstacleSubmittedCount,
        int performerDroppedCount)
    {
        CrowdInViewCount = Math.Max(0, crowdInViewCount);
        CrowdSubmittedCount = Math.Max(0, crowdSubmittedCount);
        ObstacleSubmittedCount = Math.Max(0, obstacleSubmittedCount);
        PerformerDroppedCount = Math.Max(0, performerDroppedCount);
    }

    public void ObserveControlTick() => ControlHzObserved = ObserveHz(ref _controlTick, ControlHzObserved);
    public void ObserveCommandTick() => CommandHzObserved = ObserveHz(ref _commandTick, CommandHzObserved);
    public void ObserveSimTick() => SimHzObserved = ObserveHz(ref _simTick, SimHzObserved);
    public void ObservePerformerTick() => PerformerHzObserved = ObserveHz(ref _performerTick, PerformerHzObserved);
    public void ObservePanelTick() => PanelHzObserved = ObserveHz(ref _panelTick, PanelHzObserved);

    public void MarkStructuralChange()
    {
        StructuralChangeRevision++;
        StructuralChangesFrame++;
    }

    public void MarkCommandApply() => CommandCountFrame++;
    public void MarkScenarioSpawned() => ScenarioSpawnCount++;
    public void MarkFlowReconcile() => FlowReconcileCountFrame++;
    public void MarkStreamingWindowUpdated() => StreamingWindowUpdatesFrame++;
    public void MarkFocusBudgetUpdated()
    {
        FocusBudgetUpdatesFrame++;
        FocusBudgetUpdatesTotal++;
    }

    public void MarkSolverWindowMoved()
    {
        SolverWindowMovesFrame++;
        SolverWindowMovesTotal++;
    }

    public void MarkAuthoredRuntimeBindingChanged() => AuthoredRuntimeBindingRevision++;

    private static float Smooth(float current, float sampleMs)
    {
        if (sampleMs < 0f)
        {
            sampleMs = 0f;
        }

        return current <= 0.001f
            ? sampleMs
            : (current * (1f - TimingWeight)) + (sampleMs * TimingWeight);
    }

    private static float ObserveHz(ref long lastTick, float current)
    {
        long now = Stopwatch.GetTimestamp();
        if (lastTick == 0)
        {
            lastTick = now;
            return current;
        }

        double elapsedTicks = now - lastTick;
        lastTick = now;
        if (elapsedTicks <= 0d)
        {
            return current;
        }

        float hz = (float)(Stopwatch.Frequency / elapsedTicks);
        return current <= 0.001f
            ? hz
            : (current * (1f - TimingWeight)) + (hz * TimingWeight);
    }
}
