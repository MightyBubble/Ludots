using System;
using System.Diagnostics;
using Arch.Core;

namespace MassNavWebParityMod.Runtime;

public sealed class MassNavSimulationRuntime
{
    private const float TimingWeight = 0.18f;

    private int[] _teamIds = new[] { 1, 2, 3, 4 };
    private Entity[] _selectionScratch = new Entity[256];
    private Entity[] _selectedEntities = Array.Empty<Entity>();
    private uint _selectionRevision;
    private bool _sceneResetRequested;
    private int _frameIndex;
    private int _nextSharedOrderId = 1;
    private long _selectionSyncTick;
    private long _controlTick;
    private long _commandTick;
    private long _commandDispatchTick;
    private long _simTick;
    private long _primitiveTick;
    private long _hudTick;
    private long _panelTick;

    public int SelectionSnapshotCountFrame { get; private set; }
    public int CommandCountFrame { get; private set; }
    public int StructuralChangesFrame { get; private set; }
    public int FlowReconcileCountFrame { get; private set; }
    public float FrameMs { get; private set; }
    public float Fps { get; private set; }
    public float SelectionSyncMs { get; private set; }
    public float CommandApplyMs { get; private set; }
    public float FormationTargetMs { get; private set; }
    public float FlowFieldRebuildMs { get; private set; }
    public float StepPrepMs { get; private set; }
    public float LocalSteeringMs { get; private set; }
    public float SimStepMs { get; private set; }
    public float HardResolveMs { get; private set; }
    public float EntitySyncMs { get; private set; }
    public float PrimitiveEmitMs { get; private set; }
    public float SelectionSyncHzObserved { get; private set; }
    public float ControlHzObserved { get; private set; }
    public float CommandHzObserved { get; private set; }
    public float CommandDispatchHzObserved { get; private set; }
    public float SimHzObserved { get; private set; }
    public float PrimitiveHzObserved { get; private set; }
    public float HudHzObserved { get; private set; }
    public float PanelHzObserved { get; private set; }
    public int CrowdInViewCount { get; private set; }
    public int CrowdSubmittedCount { get; private set; }
    public int ObstacleSubmittedCount { get; private set; }
    public int PrimitiveDroppedCount { get; private set; }
    public MassNavAgentState AgentState { get; } = new();
    public MassNavCommandRuntime Commands { get; } = new();
    public MassNavFlowTuning FlowTuning { get; } = new();
    public MassNavFormationRuntime FormationRuntime { get; }
    public MassNavGroupRuntime NavGroupRuntime { get; }
    public MassNavWebParitySimState WebParity { get; } = new();

    public int SelectedCount => _selectedEntities.Length;
    public uint SelectionRevision => _selectionRevision;
    public ReadOnlySpan<Entity> SelectedEntities => _selectedEntities;
    public ReadOnlySpan<int> TeamIds => _teamIds;
    public int TeamCount => _teamIds.Length;
    public int FrameIndex => _frameIndex;
    public int PendingCommandCount => Commands.PendingCommandCount;
    public int AgentsPerTeam { get; private set; } = 2_500;
    public int SelectedTeamId { get; private set; } = 1;
    public MassNavFormationMode FormationMode { get; private set; } = MassNavFormationMode.None;

    public MassNavSimulationRuntime()
    {
        FormationRuntime = new MassNavFormationRuntime();
        NavGroupRuntime = new MassNavGroupRuntime(FormationRuntime);
    }

    public void BeginFrame(float dt)
    {
        _frameIndex++;
        SelectionSnapshotCountFrame = 0;
        CommandCountFrame = 0;
        StructuralChangesFrame = 0;
        FlowReconcileCountFrame = 0;
        FrameMs = dt > 0f ? dt * 1000f : 0f;
        Fps = FrameMs > 0.001f ? 1000f / FrameMs : 0f;
    }

    public void ObserveSelectionSync(double sampleMs) => SelectionSyncMs = Smooth(SelectionSyncMs, (float)sampleMs);
    public void ObserveCommandApply(double sampleMs) => CommandApplyMs = Smooth(CommandApplyMs, (float)sampleMs);
    public void ObserveFormationTargets(double sampleMs) => FormationTargetMs = Smooth(FormationTargetMs, (float)sampleMs);
    public void ObserveFlowFieldRebuild(double sampleMs) => FlowFieldRebuildMs = Smooth(FlowFieldRebuildMs, (float)sampleMs);
    public void ObserveStepPrep(double sampleMs) => StepPrepMs = Smooth(StepPrepMs, (float)sampleMs);
    public void ObserveLocalSteering(double sampleMs) => LocalSteeringMs = Smooth(LocalSteeringMs, (float)sampleMs);
    public void ObserveSimStep(double sampleMs) => SimStepMs = Smooth(SimStepMs, (float)sampleMs);
    public void ObserveHardResolve(double sampleMs) => HardResolveMs = Smooth(HardResolveMs, (float)sampleMs);
    public void ObserveEntitySync(double sampleMs) => EntitySyncMs = Smooth(EntitySyncMs, (float)sampleMs);
    public void ObservePrimitiveEmit(double sampleMs) => PrimitiveEmitMs = Smooth(PrimitiveEmitMs, (float)sampleMs);

    public void ObservePrimitiveCoverage(int crowdInViewCount, int crowdSubmittedCount, int obstacleSubmittedCount, int primitiveDroppedCount)
    {
        CrowdInViewCount = Math.Max(0, crowdInViewCount);
        CrowdSubmittedCount = Math.Max(0, crowdSubmittedCount);
        ObstacleSubmittedCount = Math.Max(0, obstacleSubmittedCount);
        PrimitiveDroppedCount = Math.Max(0, primitiveDroppedCount);
    }

    public void ObserveSelectionSyncTick() => SelectionSyncHzObserved = ObserveHz(ref _selectionSyncTick, SelectionSyncHzObserved);
    public void ObserveControlTick() => ControlHzObserved = ObserveHz(ref _controlTick, ControlHzObserved);
    public void ObserveCommandTick() => CommandHzObserved = ObserveHz(ref _commandTick, CommandHzObserved);
    public void ObserveCommandDispatchTick() => CommandDispatchHzObserved = ObserveHz(ref _commandDispatchTick, CommandDispatchHzObserved);
    public void ObserveSimTick() => SimHzObserved = ObserveHz(ref _simTick, SimHzObserved);
    public void ObservePrimitiveTick() => PrimitiveHzObserved = ObserveHz(ref _primitiveTick, PrimitiveHzObserved);
    public void ObserveHudTick() => HudHzObserved = ObserveHz(ref _hudTick, HudHzObserved);
    public void ObservePanelTick() => PanelHzObserved = ObserveHz(ref _panelTick, PanelHzObserved);

    public Span<Entity> EnsureSelectionScratch(int required)
    {
        if (required > _selectionScratch.Length)
        {
            int next = _selectionScratch.Length;
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref _selectionScratch, next);
        }

        return _selectionScratch.AsSpan(0, required);
    }

    public void SetSelection(ReadOnlySpan<Entity> entities, uint revision)
    {
        if (_selectedEntities.Length != entities.Length)
        {
            _selectedEntities = new Entity[entities.Length];
        }

        entities.CopyTo(_selectedEntities);
        _selectionRevision = revision;
        SelectionSnapshotCountFrame++;
        WebParity.SetSelectedFlags(AgentState, _selectedEntities);
    }

    public void ClearSelection()
    {
        if (_selectedEntities.Length == 0)
        {
            WebParity.SetSelectedFlags(AgentState, ReadOnlySpan<Entity>.Empty);
            return;
        }

        _selectedEntities = Array.Empty<Entity>();
        _selectionRevision++;
        SelectionSnapshotCountFrame++;
        WebParity.SetSelectedFlags(AgentState, ReadOnlySpan<Entity>.Empty);
    }

    public void MarkStructuralChange()
    {
        StructuralChangesFrame++;
    }

    public void MarkCommandApply()
    {
        CommandCountFrame++;
    }

    public void MarkFlowReconcile()
    {
        FlowReconcileCountFrame++;
    }

    public void SetAgentsPerTeam(int agentsPerTeam)
    {
        int next = Math.Max(0, agentsPerTeam);
        if (AgentsPerTeam == next)
        {
            return;
        }

        AgentsPerTeam = next;
        RequestSceneReset();
    }

    public void SetSelectedTeam(int teamId)
    {
        if (Array.IndexOf(_teamIds, teamId) >= 0)
        {
            SelectedTeamId = teamId;
        }
    }

    public void ConfigureScenarioTeams(ReadOnlySpan<int> teamIds)
    {
        if (teamIds.Length <= 0)
        {
            _teamIds = Array.Empty<int>();
            SelectedTeamId = 0;
            return;
        }

        if (_teamIds.Length != teamIds.Length)
        {
            _teamIds = new int[teamIds.Length];
        }

        teamIds.CopyTo(_teamIds);
        if (Array.IndexOf(_teamIds, SelectedTeamId) < 0)
        {
            SelectedTeamId = _teamIds[0];
        }
    }

    public int AllocateSharedOrderId()
    {
        int next = _nextSharedOrderId++;
        if (next <= 0)
        {
            _nextSharedOrderId = 1;
            next = _nextSharedOrderId++;
        }

        return next;
    }

    public void CycleSelectedTeam()
    {
        int index = Array.IndexOf(_teamIds, SelectedTeamId);
        if (index < 0)
        {
            SelectedTeamId = _teamIds.Length > 0 ? _teamIds[0] : 0;
            return;
        }

        SelectedTeamId = _teamIds[(index + 1) % _teamIds.Length];
    }

    public void SetFormationMode(MassNavFormationMode mode)
    {
        FormationMode = mode;
    }

    public void RequestSceneReset()
    {
        _sceneResetRequested = true;
    }

    public bool ConsumeSceneResetRequest()
    {
        if (!_sceneResetRequested)
        {
            return false;
        }

        _sceneResetRequested = false;
        return true;
    }

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
